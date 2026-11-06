using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal static class ChatTests
{
    [STAThread] static int Main(string[] args)
    {
        try
        {
            Application.EnableVisualStyles();
            if (args.Length > 1 && args[1] == "--preview-window")
                using (var window = new PetChatWindow(args[0], true)) Application.Run(window);
            else Run(args[0]);
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    sealed class Mock : HttpMessageHandler
    {
        internal Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Send;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { return Send(request, token); }
    }
    static HttpResponseMessage Json(string text) { return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") }; }
    static void Check(bool value, string message) { if (!value) throw new Exception("AI chat: " + message); }
    static object Get(object instance, string name) { return instance.GetType().GetField(name, Private).GetValue(instance); }
    static void Invoke(object instance, string name) { instance.GetType().GetMethod(name, Private).Invoke(instance, new object[] {null, EventArgs.Empty}); }
    static KeyEventArgs Enter(PetChatWindow form)
    {
        var key = new KeyEventArgs(Keys.Enter);
        typeof(PetChatWindow).GetMethod("InputKeyDown", Private).Invoke(form, new object[] {Get(form, "input"), key});
        return key;
    }
    static void Pump(Func<bool> done)
    {
        var until = DateTime.UtcNow.AddSeconds(8);
        while (!done() && DateTime.UtcNow < until) { Application.DoEvents(); Thread.Sleep(10); }
        Check(done(), "UI operation must finish without blocking its message loop");
    }
    internal static void Run(string root)
    {
        string temporary = Path.Combine(Path.GetTempPath(), "DesktopPet.ChatTests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            Settings(temporary); ProviderSettings(temporary); Segmenter(); Task.Run(() => Api()).GetAwaiter().GetResult(); Offline(temporary); Bubbles(root); Window(temporary);
        }
        finally
        {
            string resolved = Path.GetFullPath(temporary), tempRoot = Path.GetFullPath(Path.GetTempPath());
            if (!resolved.StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(resolved).StartsWith("DesktopPet.ChatTests.", StringComparison.Ordinal)) throw new Exception("Unexpected temporary test path");
            Directory.Delete(resolved, true);
        }
        Console.WriteLine("PASS: encrypted AI settings, endpoint/key isolation, API contracts, bounded context, Enter-to-send bubble, IME input, errors/cancellation and text-only lifecycle. No cloud or microphone used.");
    }
    static void Settings(string root)
    {
        string path = Path.Combine(root, "ai_chat_config.json");
        var s = new PetChatSettings { ApiKey = "test-key-never-plaintext", AudioApiKey = "separate-audio-secret", VoiceThreshold = 900, ReadReplies = false };
        PetChatSettingsStore.Save(path, s);
        string body = File.ReadAllText(path);
        Check(!body.Contains(s.ApiKey) && !body.Contains(s.AudioApiKey), "keys must not be stored as plaintext");
        var loaded = PetChatSettingsStore.Load(path);
        Check(loaded.ApiKey == s.ApiKey && loaded.AudioApiKey == s.AudioApiKey && !loaded.ReadReplies && loaded.VoiceThreshold == 900, "encrypted settings round trip");
        s.Model = "custom-model"; PetChatSettingsStore.Save(path, s); Check(File.Exists(path + ".bak"), "atomic replacement preserves backup");
        s.AudioBaseUrl = "https://voice.example/v1"; Check(s.EffectiveAudioKey == s.AudioApiKey, "separate voice endpoint uses only its own key");
        s.AudioApiKey = ""; Check(s.EffectiveAudioKey == "", "never leak chat key to separate endpoint");
        Check(PetChatSettings.Endpoint("https://example.com/proxy/v1/", "chat/completions").AbsolutePath == "/proxy/v1/chat/completions", "preserve base path");
        Check(PetChatSettings.Endpoint("http://127.0.0.1:1234/v1", "chat/completions").IsLoopback, "local HTTP support");
        foreach (string invalid in new[] {"http://example.com/v1", "file:///C:/key", "https://user:password@example.com/v1", "https://example.com/v1?key=secret", "https://example.com/#key"})
        {
            bool blocked = false; try { PetChatSettings.Endpoint(invalid, "chat/completions"); } catch (InvalidOperationException) { blocked = true; }
            Check(blocked, "reject unsafe or malformed base address");
        }
        File.WriteAllText(path, "null"); bool failed = false; try { PetChatSettingsStore.Load(path); } catch (InvalidOperationException) { failed = true; }
        Check(failed && File.ReadAllText(path) == "null", "invalid settings cannot be silently replaced");
        File.Delete(path);
        var history = new PetChatHistory();
        for (int i = 0; i < 50; i++) history.Commit("question " + i, "reply " + i);
        var messages = history.Request("persona", "next"); Check(messages.Count == 22 && messages[1].content == "question 40", "retain ten completed turns in order");
        history.Clear(); Check(history.Request("persona", "next").Count == 2, "new conversation clears context");
    }
    static byte[] Pcm(short amplitude)
    {
        var frame = new byte[3200];
        for (int i = 0; i < frame.Length; i += 2) { frame[i] = (byte)(amplitude & 255); frame[i + 1] = (byte)((amplitude >> 8) & 255); }
        return frame;
    }
    static void ProviderSettings(string root)
    {
        string path = Path.Combine(root, "provider-settings.json");
        var initial = new PetChatSettings { ApiKey = "original-chat-key", Model = "my-custom-openai-model", AudioBaseUrl = "https://voice.example/v1", AudioApiKey = "voice-key" };
        using (var editor = new PetChatSettingsWindow(initial, path))
        {
            var provider = (ComboBox)Get(editor, "provider"); var address = (TextBox)Get(editor, "address");
            var model = (ComboBox)Get(editor, "model"); var key = (TextBox)Get(editor, "key");
            Check(provider.SelectedIndex == 1 && model.Text == initial.Model && key.Text == initial.ApiKey, "opening existing settings preserves custom model and key");
            provider.SelectedIndex = 2;
            Check(address.Text == "https://api.deepseek.com" && model.Text == "deepseek-flash" && key.Text == "", "DeepSeek preset supplies current endpoint/model and clears the previous service key");
            key.Text = "deepseek-key"; model.SelectedIndex = 1;
            Check(model.Text == "deepseek-v4-pro", "DeepSeek model choice is usable");
            provider.SelectedIndex = 3;
            Check(address.Text == "https://open.bigmodel.cn/api/paas/v4" && model.Text == "glm-5.3" && key.Text == "", "GLM preset has its own endpoint and credential");
            model.Text = "glm-private-model"; key.Text = "glm-key";
            Invoke(editor, "Save");
            Check(editor.Saved != null, "provider settings save successfully");
        }
        var saved = PetChatSettingsStore.Load(path);
        Check(saved.ApiKey == "glm-key" && saved.Model == "glm-private-model" && saved.AudioBaseUrl == initial.AudioBaseUrl && saved.AudioApiKey == initial.AudioApiKey && saved.ReadReplies, "provider save preserves independent audio configuration and custom model");
        using (var reopened = new PetChatSettingsWindow(saved, path))
        {
            var provider = (ComboBox)Get(reopened, "provider");
            Check(provider.SelectedIndex == 3 && ((ComboBox)Get(reopened, "model")).Text == saved.Model, "saved provider is detected without resetting the model");
            provider.SelectedIndex = 0;
            Check(((TextBox)Get(reopened, "key")).Text == saved.ApiKey, "switching to manual editing preserves the current service");
            ((TextBox)Get(reopened, "address")).Text = "https://another.example/v1";
            Check(((TextBox)Get(reopened, "key")).Text == "", "manual endpoint changes also clear the old service key");
        }
        using (var editor = new PetChatSettingsWindow(new PetChatSettings(), path))
        {
            ((ComboBox)Get(editor, "provider")).SelectedIndex = 2;
            Check(!((CheckBox)Get(editor, "read")).Checked, "chat-only preset disables inherited reply speech until audio is configured");
        }
        saved.AudioBaseUrl = ""; saved.Validate(false);
        bool rejected = false; try { saved.Validate(true); } catch (InvalidOperationException e) { rejected = e.Message.Contains("单独配置语音服务"); }
        Check(rejected, "chat-only endpoint gives an actionable audio configuration error before making a request");
    }
    static void Segmenter()
    {
        var detector = new PetVoiceSegmenter(650, 1000);
        for (int i = 0; i < 1000; i++) Check(detector.Feed(Pcm(0)) == null && !detector.HasSpeech, "silence must never upload audio");
        detector.Feed(Pcm(2000)); for (int i = 0; i < 11; i++) Check(detector.Feed(Pcm(0)) == null, "brief clicks are rejected");
        for (int i = 0; i < 5; i++) Check(detector.Feed(Pcm(-2000)) == null, "signed PCM speech accumulates");
        for (int i = 0; i < 9; i++) Check(detector.Feed(Pcm(0)) == null, "short gaps do not end turn");
        byte[] wav = detector.Feed(Pcm(0)); Check(wav != null, "silence ends a genuine utterance");
        byte[] format, samples; PetVoicePlayback.ReadWav(wav, out format, out samples);
        Check(BitConverter.ToInt32(format, 4) == 16000 && samples.Length > 16000, "PCM WAV format and captured content");
        byte[] streamed = PetVoiceSegmenter.ToWav(Pcm(0));
        for (int i = 40; i < 44; i++) streamed[i] = 255;
        PetVoicePlayback.ReadWav(streamed, out format, out samples);
        Check(samples.Length == 3200, "streamed WAV with unknown data length");
        byte[] bounded = null; for (int i = 0; i < 220 && bounded == null; i++) bounded = detector.Feed(Pcm(2000));
        Check(bounded != null && bounded.Length <= 640044, "continuous speech bounded to twenty seconds");
    }
    static async Task Api()
    {
        foreach (int providerIndex in new[] { 2, 3 })
        {
            var preset = PetChatProvider.Presets[providerIndex];
            var configured = new PetChatSettings { BaseUrl = preset.BaseUrl, Model = preset.Models[0], ApiKey = "provider-specific-key" };
            using (var api = new PetChatApi(configured, new Mock { Send = async (request, token) =>
            {
                string body = await request.Content.ReadAsStringAsync();
                Check(request.RequestUri == new Uri(preset.BaseUrl + "/chat/completions") && request.Headers.Authorization.Parameter == configured.ApiKey, "provider uses its official path and corresponding key");
                Check(body.Contains("\"model\":\"" + configured.Model + "\"") && body.Contains("\"stream\":false"), "provider receives the selected model through the compatible protocol");
                return Json("{\"choices\":[{\"message\":{\"reasoning_content\":\"internal reasoning\",\"content\":\"你好呀\"}}]}");
            }}))
                Check(await api.ChatAsync(new PetChatHistory().Request("persona", "你好"), CancellationToken.None) == "你好呀", "DeepSeek/GLM final answer is parsed without reading reasoning as dialogue");
        }
        var s = new PetChatSettings { BaseUrl = "https://chat.example/proxy/v1", ApiKey = "chat-secret", AudioBaseUrl = "https://audio.example/v1", AudioApiKey = "audio-secret" };
        int count = 0;
        var mock = new Mock { Send = async (request, token) =>
        {
            string body = await request.Content.ReadAsStringAsync(); count++;
            if (request.RequestUri.Host == "chat.example")
            {
                Check(request.Headers.Authorization.Parameter == "chat-secret", "chat authentication");
                Check(request.RequestUri.AbsolutePath == "/proxy/v1/chat/completions", "chat path");
                Check(body.Contains("你好") && body.Contains("system"), "UTF8 message/personality payload");
                return Json("{\"choices\":[{\"message\":{\"content\":\"你好，骑士在这里。\"}}]}");
            }
            Check(request.Headers.Authorization.Parameter == "audio-secret", "audio authentication is isolated");
            if (request.RequestUri.AbsolutePath.EndsWith("transcriptions"))
            {
                Check(request.Content is MultipartFormDataContent && body.Contains("speech.wav") && body.Contains("whisper-1"), "STT multipart file/model");
                return Json("{\"text\":\"你好\"}");
            }
            Check(body.Contains("\"response_format\":\"wav\"") && body.Contains("nova"), "TTS format and voice");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(PetVoiceSegmenter.ToWav(Pcm(0))) };
        }};
        using (var api = new PetChatApi(s, mock))
        {
            var history = new PetChatHistory();
            Check(await api.ChatAsync(history.Request("personality", "你好"), CancellationToken.None) == "你好，骑士在这里。", "chat response parsing");
            Check(await api.TranscribeAsync(PetVoiceSegmenter.ToWav(Pcm(1000)), CancellationToken.None) == "你好", "STT response");
            Check((await api.SpeechAsync("你好", CancellationToken.None)).Length > 44 && count == 3, "TTS bytes");
        }
        using (var api = new PetChatApi(s, new Mock { Send = (request, token) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("chat-secret echoed by provider") }) }))
        {
            bool safe = false; try { await api.ChatAsync(new PetChatHistory().Request("p", "q"), CancellationToken.None); } catch (InvalidOperationException e) { safe = e.Message.Contains("401") && !e.Message.Contains("chat-secret"); }
            Check(safe, "HTTP errors identify failure without disclosing body/secrets");
        }
        using (var api = new PetChatApi(s, new Mock { Send = (request, token) => Task.FromResult(Json("{\"choices\":[]}")) }))
        {
            bool failed = false; try { await api.ChatAsync(new PetChatHistory().Request("p", "q"), CancellationToken.None); } catch (InvalidOperationException) { failed = true; }
            Check(failed, "malformed response rejected");
        }
        using (var cancellation = new CancellationTokenSource())
        using (var api = new PetChatApi(s, new Mock { Send = async (request, token) => { await Task.Delay(30000, token); return Json("{}"); } }))
        {
            var pending = api.ChatAsync(new PetChatHistory().Request("p", "q"), cancellation.Token); cancellation.Cancel();
            bool cancelled = false; try { await pending; } catch (OperationCanceledException) { cancelled = true; } Check(cancelled, "cancellation interrupts pending request");
        }
    }
    static void Offline(string root)
    {
        var local = new PetLocalDialogue(); var morning = new DateTime(2026, 11, 6, 8, 0, 0);
        Check(local.Greeting("rest", morning).Contains("歇") && local.Greeting("music", morning).Contains("节奏") &&
            local.Greeting("typing", morning).Contains("敲字") && local.Greeting("idle", morning).Contains("早"), "scene and time select distinct local greetings");
        string tired = local.Reply("有点累了", "idle", morning);
        Check(tired.Contains("喝口水") || tired.Contains("休息"), "fatigue has a local response");
        Check(local.Reply("听歌", "idle", morning).Contains("歌"), "music has a local response");
        var mood = new PetLocalDialogue().Reply("心情不好", "idle", morning);
        Check(mood.Contains("难熬") || mood.Contains("我在听"), "the low-mood preset receives a matching comforting reply");
        string path = Path.Combine(root, "ai_chat_config.json"); File.Delete(path);
        using (var form = new PetChatWindow(root, false, "music"))
        {
            form.CreateApi = s => { throw new Exception("Offline chat must not create a network client"); };
            Check(!form.ShowInTaskbar && form.FormBorderStyle == FormBorderStyle.None, "chat is a borderless taskbar-hidden bubble");
            Check(form.LastReply.Contains("节奏"), "initial local greeting uses the clicked scene");
            ((TextBox)Get(form, "input")).Text = "工作加油";
            Check(Enter(form).SuppressKeyPress, "plain Enter sends and suppresses a newline");
            Check(form.LastReply == "我真的很想拥有你，但是你却没有牵住我的手" && !form.Visible, "unconfigured chat uses the requested fixed reply and hides the input");
            var area = new Rectangle(-1280, 0, 1280, 900);
            foreach (var pet in new[] {new Rectangle(-400, 350, 300, 400), new Rectangle(-1280, 0, 300, 400), new Rectangle(-1100, 500, 300, 400)})
            { form.PlaceBeside(pet, area); Check(area.Contains(form.Bounds), "bubble stays on a monitor with negative coordinates, including both edges"); }
            form.PlaceBeside(new Rectangle(1000, 500, 300, 400), new Rectangle(0, 0, 1920, 1080));
            Check(form.Right < 1000, "bubble prefers the pet's left side");
            form.PlaceBeside(new Rectangle(0, 200, 300, 400), new Rectangle(0, 0, 1920, 1080));
            Check(form.Left > 300, "left edge moves bubble to the pet's right");
        }
        Check(!File.Exists(path), "local dialogue does not create API settings");
    }
    static void Bubbles(string root)
    {
        using (var bubble = new PetSpeechBubble())
        {
            bubble.Say("短回复", 0); Check(bubble.Visible && bubble.Hit(new PointF(30, 30)), "speech bubble participates in the pet's hit testing");
            Check(!bubble.Advance(5) && bubble.Opacity == 1, "reply stays fully visible during the five-second idle reading period");
            Check(bubble.Advance(6) && bubble.Visible && Math.Abs(bubble.Opacity - .5f) < .01f, "reply fades smoothly after five idle seconds");
            using (var image = new Bitmap(352, 128))
            using (var graphics = Graphics.FromImage(image))
            {
                bubble.Draw(graphics);
                Check(image.GetPixel(30, 30).A >= 125 && image.GetPixel(30, 30).A <= 130, "the painted bubble actually uses the fade opacity");
            }
            bubble.Touch(6);
            Check(bubble.Opacity == 1 && !bubble.Advance(11), "interaction restores full opacity and restarts the idle timer");
            Check(bubble.Advance(12) && bubble.Advance(13) && !bubble.Visible && !bubble.Hit(new PointF(30, 30)), "completed fade removes drawing and hit testing");
            bubble.Say("新的回复", 20);
            Check(bubble.Visible && bubble.Opacity == 1 && !bubble.Advance(25), "a new reply restores a fresh fully visible bubble");
            string text = "第一行。\n" + new String('长', 220) + "😀" + new String('尾', 50);
            bubble.Say(text, 0); Check(bubble.PageCount > 1, "long replies paginate inside the character bubble");
            var pages = new List<string>();
            for (int i = 0; i < bubble.PageCount; i++) { pages.Add(bubble.CurrentPage); bubble.Turn(1); }
            Check(String.Join("", pages.ToArray()) == text && bubble.PageIndex == 0, "paging preserves all text, line breaks and emoji");
            Check(bubble.Advance(6) && bubble.Opacity < 1, "multi-page replies also fade after inactivity");
            bubble.Touch(6);
            bubble.Click(new PointF(200, 90)); Check(bubble.PageIndex == 1, "clicking speech advances its page");
            Check(!bubble.Advance(11) && bubble.Opacity == 1, "page interaction restarts the reading period");
            bubble.Click(new PointF(326, 18)); Check(!bubble.Visible && !bubble.Hit(new PointF(30, 30)), "closing speech restores click through");
        }
        using (var pet = new KeyboardPetWindow(root, true))
        {
            ((System.Windows.Forms.Timer)Get(pet, "timer")).Stop();
            int windows = Application.OpenForms.Count;
            pet.Say("小骑士在这里陪你。");
            Check(Application.OpenForms.Count == windows && Get(pet, "chatWindow") == null, "reply uses the existing pet surface without opening a chat or speech form");
            foreach (int size in new[] {252, 336, 420})
            {
                typeof(KeyboardPetWindow).GetMethod("SetSpriteSize", Private).Invoke(pet, new object[] {size});
                float scale = size / 336f;
                using (var image = new Bitmap(pet.Width, pet.Height))
                using (var graphics = Graphics.FromImage(image))
                {
                    pet.DrawScene(graphics);
                    var point = new Point((int)(30 * scale), (int)(30 * scale));
                    Check(image.GetPixel(point.X, point.Y).A > 240 && pet.Hit(point), "speech is drawn and clickable in the actual pet canvas at every size");
                    Check(!pet.scene.CurrentKeyFrame.IntersectsWith(PetSpeechBubble.Bounds), "live key display has a separate area while the pet speaks");
                }
            }
            var bubble = (PetSpeechBubble)Get(pet, "speechBubble");
            typeof(KeyboardPetWindow).GetMethod("OnMouseDown", Private).Invoke(pet, new object[] {new MouseEventArgs(MouseButtons.Left, 1, 30 * 420 / 336, 30 * 420 / 336, 0)});
            Check(!bubble.Visible && Get(pet, "chatWindow") == null, "clicking a reply dismisses it without triggering a new input popup");
        }
    }
    static void Window(string root)
    {
        // A saved voice preference must never make the simple chat use audio.
        PetChatSettingsStore.Save(Path.Combine(root, "ai_chat_config.json"), new PetChatSettings { ApiKey = "mock-key", ReadReplies = true });
        int chatRequests = 0;
        bool fail = false, block = false;
        var started = new TaskCompletionSource<bool>();
        using (var form = new PetChatWindow(root))
        {
            form.CreateApi = s => new PetChatApi(s, new Mock { Send = async (request, token) =>
            {
                Check(request.RequestUri.AbsolutePath.EndsWith("completions"), "simple chat never calls audio endpoints");
                chatRequests++;
                if (fail) throw new HttpRequestException("mock connection failure");
                if (block) { started.TrySetResult(true); await Task.Delay(30000, token); }
                return Json("{\"choices\":[{\"message\":{\"content\":\"很高兴和你聊天。\"}}]}");
            }});
            form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-30000, -30000); form.Show(); Application.DoEvents();
            string delivered = null; form.ReplyReady += text => delivered = text;
            Check(form.Height < 120 && form.Controls.Count == 1 && form.Controls[0] is PetBubbleInput, "conversation contains only a compact bubble input");
            Check(form.Controls[0].Controls.Count == 1 && form.Controls[0].Controls[0].Controls.Count == 2, "input surface contains only the textbox and placeholder, with no action buttons");
            var draft = (TextBox)Get(form, "input"); draft.Text = "未发送的草稿";
            form.SayRandomOpening();
            Check(chatRequests == 0 && !String.IsNullOrEmpty(form.LastReply) && form.Visible, "random opening stays local and keeps the input open");
            Check(delivered == form.LastReply, "fixed reply is delivered to the character");
            for (int i = 0; i < 60; i++)
            {
                string previous = form.LastReply; form.SayRandomOpening();
                Check(form.LastReply != previous, "consecutive click openings never repeat");
            }
            Check(draft.Text == "未发送的草稿" && draft.Visible, "random openings preserve the visible draft");
            Check(((PetChatHistory)Get(form, "history")).Request("p", "q").Count == 2, "automatic openings never fabricate user turns");

            draft.Text = "   "; string beforeEmpty = form.LastReply; Enter(form);
            Check(chatRequests == 0 && form.Visible && form.LastReply == beforeEmpty, "blank input sends nothing and keeps the bubble open");
            draft.Text = "中文输入";
            var imeStart = Message.Create(draft.Handle, 0x010D, IntPtr.Zero, IntPtr.Zero);
            typeof(PetChatInput).GetMethod("WndProc", Private).Invoke(draft, new object[] {imeStart});
            Check(!Enter(form).SuppressKeyPress && chatRequests == 0 && draft.Text == "中文输入", "Enter during IME composition does not send or consume candidate selection");
            var imeEnd = Message.Create(draft.Handle, 0x010E, IntPtr.Zero, IntPtr.Zero);
            typeof(PetChatInput).GetMethod("WndProc", Private).Invoke(draft, new object[] {imeEnd});
            Check(Enter(form).SuppressKeyPress, "plain Enter submits after composition finishes");
            Pump(() => !form.Busy);
            Check(chatRequests == 1 && form.LastReply.Contains("很高兴") && delivered == form.LastReply && !form.Visible, "Enter sends text, closes the input and delivers the bubble reply without audio");

            fail = true; form.Show(); draft.Text = "失败后保留"; Enter(form); Pump(() => !form.Busy);
            Check(form.Visible && draft.Enabled && draft.Text == "失败后保留" && form.LastReply.Contains("输入已保留"), "failed request restores the draft and reports the error in the character bubble");
            fail = false; block = true; Enter(form); Pump(() => started.Task.IsCompleted);
            string waiting = form.LastReply; form.SayRandomOpening(); Enter(form);
            Check(form.Busy && !draft.Enabled && form.LastReply == waiting && chatRequests == 3, "pending request blocks duplicate Enter and random openings");
            form.DismissInput(); Check(!form.Visible && form.Busy, "dismissing input leaves the pending text response running");
            form.Close(); Pump(() => form.IsDisposed);
        }
        using (var preview = new PetChatWindow(root, true))
        {
            preview.CreateApi = s => { throw new Exception("Preview must not connect"); };
            ((TextBox)Get(preview, "input")).Text = "preview"; Enter(preview);
            Check(!((Control)Get(preview, "input")).Enabled && !preview.Busy, "preview cannot submit text");
        }
        string path = Path.Combine(root, "ai_chat_config.json"); File.WriteAllText(path, "null");
        using (var broken = new PetChatWindow(root))
        {
            ((TextBox)Get(broken, "input")).Text = "你好"; Enter(broken);
            Check(((Control)Get(broken, "input")).Enabled && broken.LastReply == "我真的很想拥有你，但是你却没有牵住我的手" && File.ReadAllText(path) == "null", "damaged settings allow local chat without overwriting the file");
        }
    }
}
