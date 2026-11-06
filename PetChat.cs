using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

// Local lines use only the pet's animation state and the text explicitly sent.
// They never read desktop content or create an API/microphone client.
internal sealed class PetLocalDialogue
{
    int turn;
    readonly Random random = new Random();
    int lastOpening = -1;
    static readonly string[] openings = {
        "你来啦，刚好想你了。今天有什么想和我分享的？",
        "让我抱一下嘛。忙也好，闲也好，都想陪你待一会儿。",
        "被你点到啦，嘿嘿。是不是也有一点想我？",
        "亲爱的，今天过得怎么样？我想听你慢慢说。",
        "想听你分享一件小事，什么都行，你说的我都想听。",
        "先把这一小会儿留给我们，好不好？想陪你说说话。",
        "今天有没有好好照顾自己呀？也分一点时间给我嘛。",
        "我在呢。想聊天就说，想安静待着，我也陪你。",
        "给你一个抱抱。希望这一刻，你能稍微放松一点。",
        "想和你一起听首歌。有没有一首想分享给我的？",
        "刚刚还在想，要是你来找我就好了。你就来啦～",
        "见到你就开心一点了。来，跟我聊两句嘛。"
    };
    internal string RandomOpening()
    {
        // Draw from all other lines so consecutive clicks never repeat a line.
        int index = random.Next(openings.Length - (lastOpening < 0 ? 0 : 1));
        if (lastOpening >= 0 && index >= lastOpening) index++;
        lastOpening = index;
        return openings[index];
    }
    internal string Greeting(string scene, DateTime now)
    {
        switch (scene)
        {
            case "rest": return "你来啦，刚好想你了。靠过来，我们一起歇一会儿好不好？";
            case "music": return "想和你一起跟着节奏晃一晃。挑首你喜欢的歌给我听嘛～";
            case "typing": return "敲字辛苦啦，想你了。忙完这一小段，陪我说说话好不好？";
            case "relieved": return "忙完一段啦？抱抱，先松松肩膀，陪我歇一会儿吧。";
        }
        return now.Hour < 6 || now.Hour >= 23 ? "这么晚还没睡呀，抱抱。再陪你聊一会儿，然后早点睡，好不好？" :
            now.Hour < 11 ? "早呀，亲爱的。今天也想第一个和你说早安，记得吃早餐哦。" :
            now.Hour < 14 ? "到饭点啦，吃过了吗？想陪你慢慢吃顿饭，听你说说上午的事。" :
            now.Hour < 18 ? "下午好呀，有没有一点想我？过来陪我聊两句嘛。" : "你来啦，今天过得怎么样？开心的、不开心的，我都想听。";
    }
    internal string Reply(string text, string scene, DateTime now)
    {
        string s = text.ToLowerInvariant();
        if (Has(s, "累", "困", "休息", "晚安", "睡"))
            return Pick("辛苦啦，抱抱你。先喝口水，靠过来歇一会儿，我陪着你。", "今天已经很努力了，心疼你。休息一会儿吧，不用急着打起精神。");
        if (Has(s, "难过", "烦", "焦虑", "不开心", "心情不好", "压力"))
            return Pick("今天很难熬吧，抱抱你。在我面前不用硬撑，想说的话我都听。", "我在听呢，亲爱的。想跟我吐槽，还是想让我安静陪一会儿？都可以。");
        if (Has(s, "音乐", "听歌", "唱", "歌", "music"))
            return Pick("好呀，想和你一起听歌。今天听你喜欢的，下一首让我选好不好？", "把你最近单曲循环的歌分享给我嘛，想多了解一点你的喜好～");
        if (Has(s, "工作", "学习", "代码", "赶稿", "加油", "打字"))
            return Pick("一起加油呀，亲爱的。先完成一件小事就很棒了，忙完来找我，给你一个大大的抱抱。", "我陪你呀，慢慢来。认真忙一会儿，也要记得歇歇眼睛，我会心疼的。");
        if (Has(s, "你好", "早", "晚上好", "hello", "hi")) return Greeting(scene, now);
        if (Has(s, "谢谢", "喜欢", "可爱", "摸摸"))
            return Pick("我也喜欢陪着你呀。被你这样惦记着，我会忍不住开心的。", "那再让我多陪你一会儿嘛。今天的小开心，也想分给你一半～");
        if (Has(s, "再见", "拜拜")) return "好呀，待会儿见。先忙你的，想我了就来找我，亲一下～";
        return Pick(Greeting(scene, now), "我在呢，想听你说说今天的小事。刚刚有没有什么让你开心一点？", "想陪你多聊一会儿。聊聊心情、分享一首歌，或者就这样待着，都好呀。");
    }
    static bool Has(string text, params string[] words)
    { foreach (string word in words) if (text.Contains(word)) return true; return false; }
    string Pick(params string[] lines) { return lines[turn++ % lines.Length]; }
}

internal sealed class PetChatProvider
{
    internal readonly string Name, BaseUrl;
    internal readonly string[] Models;
    internal readonly bool SeparateAudio;
    internal PetChatProvider(string name, string baseUrl, bool separateAudio, params string[] models)
    { Name = name; BaseUrl = baseUrl; SeparateAudio = separateAudio; Models = models; }
    public override string ToString() { return Name; }
    internal static readonly PetChatProvider[] Presets = {
        new PetChatProvider("自定义（OpenAI 兼容）", "", false),
        new PetChatProvider("OpenAI", "https://api.openai.com/v1", false, "gpt-4o-mini"),
        new PetChatProvider("DeepSeek", "https://api.deepseek.com", true, "deepseek-flash", "deepseek-v4-pro"),
        new PetChatProvider("GLM（智谱）", "https://open.bigmodel.cn/api/paas/v4", true, "glm-5.3", "glm-4.7")
    };
    internal static bool SameEndpoint(string a, string b)
    { return String.Equals((a ?? "").Trim().TrimEnd('/'), (b ?? "").Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase); }
    internal static int Detect(string address)
    {
        for (int i = 1; i < Presets.Length; i++) if (SameEndpoint(address, Presets[i].BaseUrl)) return i;
        if (SameEndpoint(address, "https://api.deepseek.com/v1")) return 2;
        return 0;
    }
}

internal sealed class PetChatSettings
{
    internal string BaseUrl = "https://api.openai.com/v1", ApiKey = "", Model = "gpt-4o-mini";
    internal string AudioBaseUrl = "", AudioApiKey = "", TranscriptionModel = "whisper-1", SpeechModel = "tts-1", Voice = "nova";
    internal string Personality = "你是一位温柔、俏皮、有自己想法的 AI 女友，以第一人称‘我’和用户自然交谈。像恋人日常聊天一样关心、倾听、分享心情，可以适度撒娇，偶尔称呼‘亲爱的’，不要每句都重复昵称或说教。不要以骑士、守卫或任务执行者的身份自称，不使用站岗、补给、出征等角色台词。用自然亲昵的中文，通常简短回答，适合语音交流。不要使用舞台动作或复杂 Markdown。不假装能看到屏幕或执行电脑操作；你只能收到用户主动发送的文字和语音转写。";
    internal int VoiceThreshold = 650, SilenceMilliseconds = 1000;
    internal bool ReadReplies = true;
    internal string EffectiveAudioBase { get { return String.IsNullOrWhiteSpace(AudioBaseUrl) ? BaseUrl : AudioBaseUrl; } }
    // Never forward the chat credential to a separately configured audio service.
    internal string EffectiveAudioKey { get { return String.IsNullOrWhiteSpace(AudioBaseUrl) ? ApiKey : AudioApiKey; } }
    internal PetChatSettings Copy() { return (PetChatSettings)MemberwiseClone(); }
    internal static Uri Endpoint(string address, string resource)
    {
        Uri uri;
        if (!Uri.TryCreate((address ?? "").Trim().TrimEnd('/') + "/", UriKind.Absolute, out uri) ||
            (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)) ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new InvalidOperationException("API 地址须为 HTTPS 基础地址（如 https://api.openai.com/v1）；本机服务可使用 HTTP。不要包含密钥、查询参数或完整接口路径。");
        return new Uri(uri, resource);
    }
    internal void Validate(bool audio)
    {
        Endpoint(BaseUrl, "chat/completions");
        if (String.IsNullOrWhiteSpace(Model)) throw new InvalidOperationException("请填写聊天模型名称。");
        if ((ApiKey ?? "").IndexOfAny(new[] {'\r', '\n'}) >= 0 || (AudioApiKey ?? "").IndexOfAny(new[] {'\r', '\n'}) >= 0)
            throw new InvalidOperationException("API Key 不能包含换行。");
        if (audio)
        {
            if (String.IsNullOrWhiteSpace(AudioBaseUrl) && PetChatProvider.Presets[PetChatProvider.Detect(BaseUrl)].SeparateAudio)
                throw new InvalidOperationException("当前聊天服务需要单独配置语音服务。请在“语音接口”填写兼容的语音地址和对应 Key，或关闭“朗读回复”进行文字聊天。");
            Endpoint(EffectiveAudioBase, "audio/transcriptions");
            if (String.IsNullOrWhiteSpace(TranscriptionModel) || String.IsNullOrWhiteSpace(SpeechModel) || String.IsNullOrWhiteSpace(Voice))
                throw new InvalidOperationException("请填写语音识别模型、语音合成模型与音色。服务须支持 audio/transcriptions 和 audio/speech（WAV）。");
        }
    }
}

internal static class PetChatSettingsStore
{
    static string Protect(string value)
    {
        return String.IsNullOrEmpty(value) ? "" : Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(value), null, DataProtectionScope.CurrentUser));
    }
    static string Unprotect(string value)
    {
        return String.IsNullOrEmpty(value) ? "" : Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), null, DataProtectionScope.CurrentUser));
    }
    static string Str(IDictionary<string, object> data, string key, string fallback)
    { object value; return data.TryGetValue(key, out value) && value is string ? (string)value : fallback; }
    internal static PetChatSettings Load(string path)
    {
        var s = new PetChatSettings();
        if (!File.Exists(path)) return s;
        try
        {
            var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8));
            if (d == null) throw new FormatException();
            s.BaseUrl = Str(d, "baseUrl", s.BaseUrl); s.Model = Str(d, "model", s.Model);
            s.ApiKey = Unprotect(Str(d, "protectedApiKey", "")); s.AudioApiKey = Unprotect(Str(d, "protectedAudioApiKey", ""));
            s.AudioBaseUrl = Str(d, "audioBaseUrl", ""); s.TranscriptionModel = Str(d, "transcriptionModel", s.TranscriptionModel);
            s.SpeechModel = Str(d, "speechModel", s.SpeechModel); s.Voice = Str(d, "voice", s.Voice);
            s.Personality = Str(d, "personality", s.Personality);
            object value;
            if (d.TryGetValue("readReplies", out value) && value is bool) s.ReadReplies = (bool)value;
            if (d.TryGetValue("voiceThreshold", out value) && value is int) s.VoiceThreshold = Math.Max(100, Math.Min(5000, (int)value));
            if (d.TryGetValue("silenceMilliseconds", out value) && value is int) s.SilenceMilliseconds = Math.Max(600, Math.Min(2500, (int)value));
            return s;
        }
        catch (Exception e)
        {
            if (!(e is IOException || e is UnauthorizedAccessException || e is ArgumentException || e is InvalidOperationException || e is CryptographicException || e is FormatException)) throw;
            throw new InvalidOperationException("无法读取 AI 配置或解密密钥。请检查文件，或在原 Windows 用户下打开。配置文件未被覆盖。", e);
        }
    }
    internal static void Save(string path, PetChatSettings s)
    {
        s.Validate(!String.IsNullOrWhiteSpace(s.AudioBaseUrl));
        var d = new Dictionary<string, object> {
            {"baseUrl", s.BaseUrl}, {"model", s.Model}, {"protectedApiKey", Protect(s.ApiKey)},
            {"audioBaseUrl", s.AudioBaseUrl}, {"protectedAudioApiKey", Protect(s.AudioApiKey)},
            {"transcriptionModel", s.TranscriptionModel}, {"speechModel", s.SpeechModel}, {"voice", s.Voice},
            {"personality", s.Personality}, {"readReplies", s.ReadReplies},
            {"voiceThreshold", s.VoiceThreshold}, {"silenceMilliseconds", s.SilenceMilliseconds}
        };
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, new JavaScriptSerializer().Serialize(d), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

internal sealed class PetChatMessage
{
    public string role { get; set; }
    public string content { get; set; }
    internal PetChatMessage(string role, string content) { this.role = role; this.content = content; }
}

internal sealed class PetChatHistory
{
    readonly List<PetChatMessage> messages = new List<PetChatMessage>();
    internal void Clear() { messages.Clear(); }
    internal List<PetChatMessage> Request(string personality, string user)
    {
        var result = new List<PetChatMessage>();
        result.Add(new PetChatMessage("system", personality)); result.AddRange(messages);
        result.Add(new PetChatMessage("user", user)); return result;
    }
    internal void Commit(string user, string reply)
    {
        messages.Add(new PetChatMessage("user", user)); messages.Add(new PetChatMessage("assistant", reply));
        while (messages.Count > 20 || Size() > 48000) messages.RemoveRange(0, 2);
    }
    int Size() { int length = 0; foreach (var m in messages) length += m.content.Length; return length; }
}

internal sealed class PetChatApi : IDisposable
{
    readonly HttpClient client;
    readonly PetChatSettings settings;
    internal PetChatApi(PetChatSettings settings, HttpMessageHandler handler = null)
    {
        this.settings = settings.Copy();
        client = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false });
        client.Timeout = Timeout.InfiniteTimeSpan;
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
    }
    internal async Task<string> ChatAsync(List<PetChatMessage> messages, CancellationToken token)
    {
        settings.Validate(false);
        using (var content = Json(new { model = settings.Model, messages = messages, stream = false }))
        {
            byte[] response = await Send(settings.BaseUrl, "chat/completions", settings.ApiKey, content, 2 * 1024 * 1024, token).ConfigureAwait(false);
            try
            {
                var data = Parse(response);
                var choices = (IList)data["choices"];
                if (choices.Count == 0) throw new FormatException();
                var message = (Dictionary<string, object>)((Dictionary<string, object>)choices[0])["message"];
                string reply = message["content"] as string;
                if (String.IsNullOrWhiteSpace(reply) || reply.Length > 24000) throw new FormatException();
                return reply.Trim();
            }
            catch (Exception e) { if (e is KeyNotFoundException || e is InvalidCastException || e is IndexOutOfRangeException || e is FormatException) throw new InvalidOperationException("聊天接口未返回有效的 choices[0].message.content 文本。请检查接口兼容性和模型。", e); throw; }
        }
    }
    internal async Task<string> TranscribeAsync(byte[] wav, CancellationToken token)
    {
        settings.Validate(true);
        using (var form = new MultipartFormDataContent())
        {
            form.Add(new StringContent(settings.TranscriptionModel), "model");
            form.Add(new StringContent("json"), "response_format");
            var file = new ByteArrayContent(wav); file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav"); form.Add(file, "file", "speech.wav");
            var data = Parse(await Send(settings.EffectiveAudioBase, "audio/transcriptions", settings.EffectiveAudioKey, form, 1024 * 1024, token).ConfigureAwait(false));
            object value; if (!data.TryGetValue("text", out value) || !(value is string)) throw new InvalidOperationException("语音识别接口未返回 text。请检查语音服务配置。");
            string text = ((string)value).Trim();
            if (text.Length > 8000) throw new InvalidOperationException("语音转写过长，请分段说话。");
            return text;
        }
    }
    internal async Task<byte[]> SpeechAsync(string text, CancellationToken token)
    {
        settings.Validate(true);
        using (var content = Json(new { model = settings.SpeechModel, voice = settings.Voice, input = text, response_format = "wav" }))
        {
            byte[] bytes = await Send(settings.EffectiveAudioBase, "audio/speech", settings.EffectiveAudioKey, content, 16 * 1024 * 1024, token).ConfigureAwait(false);
            if (bytes.Length < 44 || Encoding.ASCII.GetString(bytes, 0, 4) != "RIFF" || Encoding.ASCII.GetString(bytes, 8, 4) != "WAVE")
                throw new InvalidOperationException("语音合成服务须返回 WAV 音频，请检查模型和接口兼容性。");
            return bytes;
        }
    }
    static StringContent Json(object value) { return new StringContent(new JavaScriptSerializer().Serialize(value), Encoding.UTF8, "application/json"); }
    static Dictionary<string, object> Parse(byte[] bytes)
    {
        try
        {
            var data = new JavaScriptSerializer { MaxJsonLength = 2 * 1024 * 1024 }.Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(bytes));
            if (data == null) throw new ArgumentException();
            return data;
        }
        catch (Exception e) { if (!(e is ArgumentException || e is InvalidOperationException)) throw; throw new InvalidOperationException("接口返回了无效的 JSON。请检查 API 基础地址。", e); }
    }
    async Task<byte[]> Send(string address, string resource, string key, HttpContent content, int limit, CancellationToken token)
    {
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
        using (var request = new HttpRequestMessage(HttpMethod.Post, PetChatSettings.Endpoint(address, resource)))
        {
            deadline.CancelAfter(TimeSpan.FromSeconds(90)); request.Content = content;
            if (!String.IsNullOrWhiteSpace(key)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key.Trim());
            try
            {
                using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        int status = (int)response.StatusCode;
                        string hint = status == 401 || status == 403 ? "密钥无效或无模型权限" : status == 404 ? "基础地址、模型或该语音接口不存在" : status == 429 ? "请求过频或额度不足" : status >= 500 ? "服务暂时不可用" : "请求被拒绝，请检查模型与接口参数";
                        // Provider error bodies can echo credentials or user content. Do not display or log them.
                        throw new InvalidOperationException(resource + "：HTTP " + status + "，" + hint + "。未自动重试。");
                    }
                    using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var output = new MemoryStream())
                    {
                        var buffer = new byte[8192]; int count;
                        while ((count = await stream.ReadAsync(buffer, 0, buffer.Length, deadline.Token).ConfigureAwait(false)) > 0)
                        {
                            if (output.Length + count > limit) throw new InvalidOperationException("接口响应过大，已停止接收。");
                            output.Write(buffer, 0, count);
                        }
                        return output.ToArray();
                    }
                }
            }
            catch (OperationCanceledException) { if (token.IsCancellationRequested) throw; throw new InvalidOperationException("接口请求超过 90 秒，请稍后重试。"); }
            catch (HttpRequestException) { throw new InvalidOperationException("无法连接 AI 服务，请检查网络、API 地址和 HTTPS 证书。"); }
        }
    }
    public void Dispose() { client.Dispose(); }
}
