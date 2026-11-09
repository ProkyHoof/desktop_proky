using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;

internal static class AssistantTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool CreateHardLink(string name, string target, IntPtr reserved);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    sealed class Mock : HttpMessageHandler
    {
        internal Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Send;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) { return Send(request, token); }
    }
    static void Check(bool value, string text) { if (!value) throw new Exception("Assistant: " + text); }
    static void Denied(Action action, string text)
    {
        bool failed = false;
        try { action(); } catch (Exception error) { if (!AssistantRunner.IsExpected(error)) throw; failed = true; }
        Check(failed, text);
    }
    static Dictionary<string, object> Args(params string[] pairs)
    { var result = new Dictionary<string, object>(); for (int i = 0; i < pairs.Length; i += 2) result.Add(pairs[i], pairs[i + 1]); return result; }
    static HttpResponseMessage Json(object value)
    { return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(AssistantJson.Encode(value), Encoding.UTF8, "application/json") }; }
    static object Call(string id, string name, params string[] args)
    { return new { id = id, type = "function", function = new { name = name, arguments = AssistantJson.Encode(Args(args)) } }; }
    static HttpResponseMessage Calls(params object[] calls)
    { return Json(new { choices = new[] { new { message = new { role = "assistant", content = (string)null, reasoning_content = "test-reasoning-kept-only-in-memory", tool_calls = calls }, finish_reason = "tool_calls" } } }); }
    static HttpResponseMessage Answer(string text)
    { return Json(new { choices = new[] { new { message = new { role = "assistant", content = text }, finish_reason = "stop" } } }); }
    static PetChatSettings Credentials()
    { return new PetChatSettings { BaseUrl = "https://api.deepseek.com", ApiKey = "test-secret-not-for-logs", Model = "deepseek-flash" }; }
    internal static void Run(string assetsRoot, string testRoot, string preview)
    {
        SetProcessDPIAware();
        var safe = new AssistantPaths(testRoot); safe.CreateDirectory(".");
        var config = new AssistantConfiguration { Workspace = Path.Combine(testRoot, "workspace"), ExportDirectory = Path.Combine(testRoot, "delivery") };
        Paths(testRoot); Tools(config); Task.Run(() => Api(config)).GetAwaiter().GetResult();
        Window(assetsRoot, Path.Combine(testRoot, "ui-data"), preview);
        Conversation(Path.Combine(testRoot, "conversation"));
        Console.WriteLine("PASS: path boundary/retention, DOCX/export, shared AI settings and live reload, configured endpoint/key/model, provider-specific options, tool loop/errors/cancellation and UI lifecycle. No live API used.");
        Console.WriteLine("Test files retained: " + testRoot);
    }
    static void Paths(string root)
    {
        foreach (string path in new[] { @"C:\test", @"c:\Users\x", @"\\localhost\D$\x", @"\\?\D:\x", "D:relative", @"D:\folder\..\escape", @"D:\folder\x:stream", @"D:\folder\NUL.txt", @"D:\folder\trailing. ", @"D:\folder\%TEMP%", @"D:\PROGRA~1\x" })
            Denied(() => new AssistantPaths(path), "deny root " + path);
        var cTarget = AssistantPaths.ForUserTarget(@"C:\DesktopPet-Final-Target");
        Check(cTarget.Resolve("final.docx") == @"C:\DesktopPet-Final-Target\final.docx", "C is permitted for an explicit final target without creating test files there");
        Denied(() => cTarget.Resolve("../internal-cache"), "C target scope still prevents traversal");
        var paths = new AssistantPaths(Path.Combine(root, "paths")); paths.CreateDirectory(".");
        foreach (string path in new[] { @"C:\outside.txt", @"D:\outside.txt", "../outside.txt", "sub/../../outside.txt", "file.txt:ads", "sub/NUL.txt", "sub/name.", @"\outside", "sub/%APPDATA%/x" })
            Denied(() => paths.Resolve(path), "deny relative " + path);
        paths.WriteNew("nested/中文.txt", Encoding.UTF8.GetBytes("内容"));
        Check(Encoding.UTF8.GetString(paths.Read("nested/中文.txt", 100)) == "内容", "UTF8 and nested directory round trip");
        Denied(() => paths.WriteNew("nested/中文.txt", new byte[0]), "no overwrite");
        string target = paths.Resolve("nested/中文.txt"), hard = Path.Combine(paths.Root, "hard.txt");
        Check(CreateHardLink(hard, target, IntPtr.Zero), "create test hard link");
        Denied(() => paths.Read("hard.txt", 100), "deny hard-link reads");
        File.Delete(hard);
        string sibling = Path.Combine(root, "outside"); Directory.CreateDirectory(sibling);
        string junction = Path.Combine(paths.Root, "junction");
        var start = new System.Diagnostics.ProcessStartInfo {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
            Arguments = "/d /c mklink /J \"" + junction + "\" \"" + sibling + "\"",
            WorkingDirectory = paths.Root, CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        using (var process = System.Diagnostics.Process.Start(start))
        { process.StandardOutput.ReadToEnd(); process.StandardError.ReadToEnd(); process.WaitForExit(); Check(process.ExitCode == 0, "create test junction"); }
        Denied(() => paths.Resolve("junction/out.txt"), "deny junction descendants");
        Denied(() => new AssistantPaths(junction), "deny junction roots");
        Directory.Delete(junction); // Remove only the junction, never recurse into its target.
        Check(Directory.Exists(sibling), "junction target retained");
    }
    static void Tools(AssistantConfiguration config)
    {
        var task = new AssistantTask(config, "offline tool tests"); var tools = new AssistantTools(task, config.ExportDirectory);
        tools.Execute("write_text", Args("path", "scripts/build.ps1", "content", "throw 'MUST NOT RUN'"), CancellationToken.None);
        tools.Execute("write_text", Args("path", "temp/keep.txt", "content", "retain me"), CancellationToken.None);
        tools.Execute("create_document", Args("path", "output/计划.docx", "title", "A&B <标题>", "body", "第一段\n第二段 <内容>"), CancellationToken.None);
        byte[] bytes = task.Files.Read("output/计划.docx", 1024 * 1024);
        using (var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
        {
            Check(zip.GetEntry("[Content_Types].xml") != null && zip.GetEntry("_rels/.rels") != null, "DOCX package parts");
            using (var reader = new StreamReader(zip.GetEntry("word/document.xml").Open()))
            { var xml = new XmlDocument(); xml.LoadXml(reader.ReadToEnd()); Check(xml.InnerText.Contains("A&B <标题>") && xml.InnerText.Contains("第二段 <内容>"), "valid XML and escaped document content"); }
        }
        tools.Execute("copy_file", Args("source", "output/计划.docx", "destination", "project/计划.docx"), CancellationToken.None);
        tools.Execute("export_file", Args("source", "output/计划.docx", "destination", "中文/计划.docx"), CancellationToken.None);
        tools.Execute("export_file", Args("source", "output/计划.docx", "destination", "中文/计划.docx"), CancellationToken.None);
        Check(File.Exists(Path.Combine(config.ExportDirectory, "中文", "计划_002.docx")), "export version prevents overwrite");
        foreach (string name in new[] { "run_shell", "powershell", "delete_file", "open_app", "run_python" })
            Denied(() => tools.Execute(name, Args(), CancellationToken.None), "no unconfined tool " + name);
        Denied(() => tools.Execute("write_text", Args("path", "../records/attack.json", "content", "x"), CancellationToken.None), "cannot overwrite records");
        Denied(() => tools.Execute("write_text", Args("path", "output/test.exe", "content", "x"), CancellationToken.None), "no executable generation");
        Denied(() => tools.Execute("export_file", Args("source", "output/计划.docx", "destination", @"C:\Users\test\Desktop\计划.docx"), CancellationToken.None), "cannot export to C desktop");
        new AssistantTools(task, @"C:\DesktopPet-Final-Target");
        var cConfig = new AssistantConfiguration { Workspace = config.Workspace, ExportDirectory = @"C:\DesktopPet-Final-Target" };
        cConfig.Validate();
        string cTargetConfig = Path.Combine(config.Workspace, "c-target-settings");
        cConfig.Save(cTargetConfig);
        Check(AssistantConfiguration.Load(cTargetConfig).ExportDirectory == @"C:\DesktopPet-Final-Target", "C final target settings round-trip while configuration and workspace stay off C");
        Denied(() => new AssistantConfiguration { Workspace = @"C:\DesktopPet-Internal-Forbidden", ExportDirectory = config.ExportDirectory }.Validate(), "C remains forbidden for internal workspace even when final targets allow it");
        string desktop = Path.Combine(config.Workspace, "simulated-final-desktop");
        var desktopTools = new AssistantTools(task, config.ExportDirectory, () => desktop);
        desktopTools.Execute("deliver_to_desktop", Args("source", "output/计划.docx", "name", "最终计划.docx"), CancellationToken.None);
        Check(Directory.GetFiles(desktop).Length == 1 && Directory.GetDirectories(desktop).Length == 0 && File.ReadAllBytes(Path.Combine(desktop, "最终计划.docx")).SequenceEqual(bytes), "desktop receives only the final file, no scripts/temp/log/staging files");
        Denied(() => desktopTools.Execute("deliver_to_desktop", Args("source", "temp/keep.txt", "name", "keep.txt"), CancellationToken.None), "intermediate files cannot be delivered as desktop output");
        Denied(() => desktopTools.Execute("export_file", Args("source", "scripts/build.ps1", "destination", "build.ps1"), CancellationToken.None), "execution scripts stay in non-C workspace");
        Check(Path.GetPathRoot(task.Directory) != @"C:\" && Path.GetPathRoot(task.Files.Root) != @"C:\", "all task engineering/records roots remain off C");
        var noExport = new AssistantTools(task, "");
        Denied(() => noExport.Execute("export_file", Args("source", "output/计划.docx", "destination", "计划.docx"), CancellationToken.None), "unconfigured destination blocked");
        task.Finish("failed", "intentional retention test");
        Check(File.Exists(task.Files.Resolve("scripts/build.ps1")) && File.Exists(task.Files.Resolve("temp/keep.txt")) && File.Exists(Path.Combine(task.Directory, "result.json")), "failure retains script, temporary file and result");
        string data = Path.Combine(config.Workspace, "configuration"); config.Save(data);
        Check(AssistantConfiguration.Load(data).Workspace == new AssistantPaths(config.Workspace).Root, "configuration round trip");
        config.Save(data); Check(Directory.GetFiles(Path.Combine(data, "assistant"), "paths-backup-*.json").Length == 1, "configuration backup retained");
    }
    static async Task Api(AssistantConfiguration config)
    {
        int turn = 0;
        var mock = new Mock { Send = async (request, token) => {
            Check(request.RequestUri.AbsoluteUri == "https://api.deepseek.com/chat/completions", "official endpoint only");
            Check(request.Headers.Authorization.Parameter == Credentials().ApiKey, "key is HTTP header only");
            string body = await request.Content.ReadAsStringAsync();
            Check(!body.Contains(Credentials().ApiKey) && body.Contains("tools") && body.Contains("thinking"), "no key in model messages");
            if (++turn == 1) return Calls(Call("one", "write_text", "path", "scripts/retain.py", "content", "# saved, never executed"), Call("two", "create_document", "path", "output/任务.docx", "title", "任务", "body", "测试正文"));
            Check(body.Contains("test-reasoning-kept-only-in-memory") && body.Contains("tool_call_id"), "reasoning_content and tool results carried back");
            return Answer("已经生成文档。");
        } };
        var runner = new AssistantRunner(); string answer = await runner.Run(config, Credentials(), "写文档", _ => { }, CancellationToken.None, mock);
        Check(turn == 2 && answer.Contains("实际生成文件") && File.Exists(Path.Combine(runner.Task.Directory, "result.json")), "tool loop completes with real output paths");
        foreach (string file in Directory.GetFiles(runner.Task.Directory, "*.json", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file);
            Check(!text.Contains(Credentials().ApiKey) && !text.Contains("test-reasoning-kept-only-in-memory"), "keys and private reasoning not logged");
        }
        var denied = new AssistantRunner();
        var invalid = new Mock { Send = (request, token) => Task.FromResult(Calls(Call(Guid.NewGuid().ToString("N"), "write_text", "path", @"C:\escape.txt", "content", "deny"))) };
        bool rejected = false;
        try { await denied.Run(config, Credentials(), "拒绝路径测试", _ => { }, CancellationToken.None, invalid); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected && File.ReadAllText(Path.Combine(denied.Task.Directory, "result.json")).Contains("failed") && Directory.GetFiles(Path.Combine(denied.Task.Directory, "records")).Length == 6, "three policy denials stop and retain records");
        using (var cancel = new CancellationTokenSource())
        {
            int step = 0; var cancelled = new AssistantRunner();
            var slow = new Mock { Send = async (request, token) => {
                if (++step == 1) return Calls(Call("keep", "write_text", "path", "temp/cancelled.txt", "content", "kept after cancellation"));
                cancel.Cancel(); await Task.Delay(10000, token); return Answer("unreachable");
            } };
            bool stopped = false;
            try { await cancelled.Run(config, Credentials(), "取消测试", _ => { }, cancel.Token, slow); } catch (OperationCanceledException) { stopped = true; }
            Check(stopped && File.Exists(cancelled.Task.Files.Resolve("temp/cancelled.txt")) && File.ReadAllText(Path.Combine(cancelled.Task.Directory, "result.json")).Contains("cancelled"), "cancellation retains partial file and marks result");
        }
        var custom = new PetChatSettings { BaseUrl = "https://example.com/proxy/v1", ApiKey = "custom-service-key", Model = "my-selected-model" };
        int customTurns = 0;
        var customRunner = new AssistantRunner();
        await customRunner.Run(config, custom, "列出任务文件", _ => { }, CancellationToken.None,
            new Mock { Send = async (request, token) => {
                Check(request.RequestUri.AbsoluteUri == "https://example.com/proxy/v1/chat/completions" && request.Headers.Authorization.Parameter == "custom-service-key",
                    "use the configured endpoint path and that provider's credential");
                var body = AssistantJson.Decode(await request.Content.ReadAsStringAsync());
                Check((string)body["model"] == "my-selected-model" && !body.ContainsKey("thinking"), "use the selected model without leaking DeepSeek-specific options");
                // Even if the caller changes its settings object during execution,
                // the active task keeps the initial endpoint/key/model snapshot.
                custom.BaseUrl = "https://changed.example/v1"; custom.Model = "changed-model"; custom.ApiKey = "changed-key";
                return ++customTurns == 1 ? Calls(Call("custom", "list_files", "path", ".")) : Answer("已列出文件。");
            } });
        Check(customTurns == 2, "custom provider supports the complete tool loop");
        var invalidEndpoint = Credentials(); invalidEndpoint.BaseUrl = "http://example.com";
        Denied(() => new AssistantChatApi(invalidEndpoint), "remote plaintext HTTP remains blocked");
        var local = new PetChatSettings { BaseUrl = "http://127.0.0.1:12345/v1", ApiKey = "", Model = "local-tools-model" };
        using (var localApi = new AssistantChatApi(local, new Mock { Send = (request, token) => {
            Check(request.Headers.Authorization == null && request.RequestUri.IsLoopback, "keyless local compatible endpoint");
            return Task.FromResult(Answer("本地连接成功。"));
        } })) await localApi.Turn(new List<object>(), CancellationToken.None);
        bool unsupported = false; int attempts = 0;
        try { await new AssistantRunner().Run(config, Credentials(), "不支持工具测试", _ => { }, CancellationToken.None,
            new Mock { Send = (request, token) => { attempts++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)); } }); }
        catch (InvalidOperationException error) { unsupported = error.Message.Contains("工具调用") && error.Message.Contains("AI 接口设置"); }
        Check(unsupported && attempts == 1, "unsupported tool/API parameters report settings guidance without switching providers or retrying");
        var apiError = new AssistantRunner(); bool failed = false;
        try { await apiError.Run(config, Credentials(), "HTTP失败测试", _ => { }, CancellationToken.None,
            new Mock { Send = (request, token) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent(Credentials().ApiKey) }) }); }
        catch (InvalidOperationException error) { failed = !error.Message.Contains(Credentials().ApiKey); }
        Check(failed && File.Exists(Path.Combine(apiError.Task.Directory, "result.json")), "provider error body never exposed, failure recorded");
        var duplicate = new AssistantRunner(); bool repeated = false;
        try { await duplicate.Run(config, Credentials(), "重复调用测试", _ => { }, CancellationToken.None,
            new Mock { Send = (request, token) => Task.FromResult(Calls(Call("same", "write_text", "path", "temp/once.txt", "content", "one"), Call("same", "write_text", "path", "temp/twice.txt", "content", "two"))) }); }
        catch (InvalidOperationException) { repeated = true; }
        Check(repeated && File.Exists(duplicate.Task.Files.Resolve("temp/once.txt")) && !File.Exists(duplicate.Task.Files.Resolve("temp/twice.txt")), "duplicate tool id never executes twice");
        var limited = new AssistantRunner(); bool capped = false;
        try { await limited.Run(config, Credentials(), "调用上限测试", _ => { }, CancellationToken.None,
            new Mock { Send = (request, token) => Task.FromResult(Calls(Enumerable.Range(0, 33).Select(i => Call("limit-" + i, "list_files", "path", ".")).ToArray())) }); }
        catch (InvalidOperationException) { capped = true; }
        Check(capped && Directory.GetFiles(Path.Combine(limited.Task.Directory, "records")).Length == 64, "32-call budget enforces a bounded execution loop");
    }
    static object Field(object target, string name) { return target.GetType().GetField(name, Private).GetValue(target); }
    static void Window(string assetsRoot, string data, string preview)
    {
        Directory.CreateDirectory(data);
        string shared = Path.Combine(data, "ai_chat_config.json");
        var selected = new PetChatSettings { BaseUrl = "https://configured.example/v1", ApiKey = "shared-current-key", Model = "selected-model" };
        PetChatSettingsStore.Save(shared, selected);
        var paths = new AssistantPaths(data); paths.CreateDirectory("assistant");
        PetChatSettingsStore.Save(paths.Resolve("assistant/deepseek.json"), Credentials());
        paths.WriteNew("assistant/paths.json", AssistantJson.Bytes(new { workspace = Path.Combine(data, "assistant", "workspace"), exportDirectory = "", model = "obsolete-model", thinking = true }));
        string legacyKey = File.ReadAllText(paths.Resolve("assistant/deepseek.json"));
        var sample = new AssistantTask(AssistantConfiguration.Load(data), "聊天中的操作记录示例");
        var sampleTools = new AssistantTools(sample, "");
        sampleTools.Execute("write_text", Args("path", "scripts/example.ps1", "content", "# retained source only"), CancellationToken.None);
        sampleTools.Execute("write_text", Args("path", "temp/example.txt", "content", "retained temporary file"), CancellationToken.None);
        sample.Finish("completed", "示例操作完成，文件已保留。");
        using (var window = new PetAssistantWindow(data))
        {
            window.Show(); Application.DoEvents();
            Check(window.Text == "电脑助手设置与记录" && ((Label)Field(window, "currentModel")).Text == selected.Model, "configuration/history module uses shared model");
            var loaded = (PetChatSettings)typeof(PetAssistantWindow).GetMethod("Credentials", Private).Invoke(window, null);
            Check(loaded.BaseUrl == selected.BaseUrl && loaded.ApiKey == selected.ApiKey, "assistant loads the complete shared settings");
            string unchanged = File.ReadAllText(shared);
            typeof(PetAssistantWindow).GetMethod("SaveSettings", Private).Invoke(window, new object[] { null, EventArgs.Empty });
            Check(File.ReadAllText(shared) == unchanged && File.ReadAllText(paths.Resolve("assistant/deepseek.json")) == legacyKey, "saving assistant paths does not rewrite shared or legacy credentials");
            var savedPaths = AssistantJson.Decode(File.ReadAllText(paths.Resolve("assistant/paths.json")));
            Check(!savedPaths.ContainsKey("model") && !savedPaths.ContainsKey("thinking"), "assistant persists paths only");
            selected.BaseUrl = "https://replacement.example/custom/v1"; selected.Model = "updated-model"; selected.ApiKey = "replacement-key";
            PetChatSettingsStore.Save(shared, selected);
            typeof(PetAssistantWindow).GetMethod("RefreshApiSettings", Private).Invoke(window, null);
            loaded = (PetChatSettings)typeof(PetAssistantWindow).GetMethod("Credentials", Private).Invoke(window, null);
            Check(loaded.Model == selected.Model && loaded.ApiKey == selected.ApiKey && ((Label)Field(window, "currentApi")).Text == selected.BaseUrl,
                "already-open assistant refreshes shared endpoint/key/model without stale credentials");
            ((ListBox)Field(window, "history")).SelectedIndex = 0;
            typeof(PetAssistantWindow).GetMethod("InspectHistory", Private).Invoke(window, null);
            string task = (string)Field(window, "lastDirectory");
            Check(File.Exists(Path.Combine(task, "files", "scripts", "example.ps1")) && File.Exists(Path.Combine(task, "files", "temp", "example.txt")), "UI local demo retains script and temp without API");
            Check(((ListBox)Field(window, "history")).Items.Count == 1 && !Descendants(window).Any(c => c.Text == "开始任务"), "history view does not introduce a separate execution entry");
            if (!String.IsNullOrEmpty(preview))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(preview));
                using (var bitmap = new Bitmap(window.Width, window.Height)) { window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(preview, ImageFormat.Png); }
            }
            var workspace = (TextBox)Field(window, "workspace"); var delivery = (TextBox)Field(window, "delivery");
            var chooseWorkspace = (KnightButton)Field(window, "chooseWorkspace"); var chooseDelivery = (KnightButton)Field(window, "chooseDelivery");
            string originalWorkspace = workspace.Text, originalDelivery = delivery.Text;
            string originalConfig = File.ReadAllText(paths.Resolve("assistant/paths.json"));
            string picked = Path.Combine(data, "chosen-project"); int selections = 0;
            window.SelectDirectory = (description, initial) => { selections++; Check(Directory.Exists(initial), "folder browser starts at an existing parent"); return picked; };
            chooseWorkspace.PerformClick();
            Check(selections == 1 && workspace.Text == picked, "workspace browse button fills the selected directory");
            window.SelectDirectory = (description, initial) => null;
            chooseWorkspace.PerformClick(); Check(workspace.Text == picked, "cancelling the folder browser preserves the selection");
            window.SelectDirectory = (description, initial) => @"C:\DesktopPet-Internal-Forbidden";
            chooseWorkspace.PerformClick();
            Check(workspace.Text == picked && ((Label)Field(window, "state")).Text.Contains("目录未更改"), "browse cannot bypass internal C-drive restriction");
            window.SelectDirectory = (description, initial) => @"C:\DesktopPet-Final-Target";
            chooseDelivery.PerformClick();
            Check(delivery.Text == @"C:\DesktopPet-Final-Target", "delivery browser accepts C final-target directories");
            Check(File.ReadAllText(paths.Resolve("assistant/paths.json")) == originalConfig, "browsing changes fields only until Save Paths is pressed");
            workspace.Text = originalWorkspace; delivery.Text = originalDelivery;
            var root = (TextBox)Field(window, "workspace"); root.Text = @"C:\NotAllowed";
            typeof(PetAssistantWindow).GetMethod("SaveSettings", Private).Invoke(window, new object[] { null, EventArgs.Empty });
            Check(((Label)Field(window, "state")).Text.Contains("未保存"), "UI rejects C settings");
            window.Close();
        }
        string empty = Path.Combine(data, "not-configured");
        Denied(() => AssistantSettings.LoadChat(empty), "missing shared settings never fall back to a private provider profile");
        using (var unconfigured = new PetAssistantWindow(empty))
        {
            Check(((Label)Field(unconfigured, "currentModel")).Text.Contains("AI 接口设置"), "unconfigured assistant points to the existing AI settings");
            typeof(PetAssistantWindow).GetMethod("SaveSettings", Private).Invoke(unconfigured, new object[] { null, EventArgs.Empty });
            Check(File.Exists(Path.Combine(empty, "assistant", "paths.json")) && !File.Exists(Path.Combine(empty, "ai_chat_config.json")), "path settings can be saved without creating or changing AI configuration");
        }
        int edits = 0;
        using (var linked = new PetAssistantWindow(data, false, () => {
            edits++; selected.Model = "model-from-existing-settings-editor"; PetChatSettingsStore.Save(shared, selected);
        }))
        {
            typeof(PetAssistantWindow).GetMethod("ConfigureApi", Private).Invoke(linked, new object[] { null, EventArgs.Empty });
            Check(edits == 1 && ((Label)Field(linked, "currentModel")).Text == selected.Model, "settings button reuses the existing AI editor and immediately refreshes its result");
        }
        using (var pet = new KeyboardPetWindow(assetsRoot, true, dataRoot:data))
        {
            var menu = (PetContextMenu)Field(pet, "menu");
            var entry = menu.Items.Commands().Single(i => i.Text.Contains("电脑助手"));
            Check(entry.Text == "电脑助手设置与记录…", "assistant menu is settings and history");
            entry.PerformClick(); var window = (PetAssistantWindow)Field(pet, "assistantWindow");
            Check(window.Visible && !window.Modal && pet.Enabled && !((KnightButton)Field(window, "save")).Enabled, "preview menu modeless settings/history and no writes");
            Check(!((KnightButton)Field(window, "chooseWorkspace")).Enabled && !((KnightButton)Field(window, "chooseDelivery")).Enabled, "preview does not open directory selectors");
            entry.PerformClick(); Check(Object.ReferenceEquals(window, Field(pet, "assistantWindow")), "single assistant window reuse");
            pet.Dispose(); Check(window.IsDisposed, "pet disposal closes assistant");
        }
    }
    static System.Collections.Generic.IEnumerable<Control> Descendants(Control parent)
    {
        foreach (Control child in parent.Controls)
        { yield return child; foreach (Control nested in Descendants(child)) yield return nested; }
    }
    static void Pump(Func<bool> complete)
    {
        DateTime limit = DateTime.UtcNow.AddSeconds(8);
        while (!complete() && DateTime.UtcNow < limit) { Application.DoEvents(); Thread.Sleep(10); }
        Check(complete(), "conversation operation must finish without blocking UI");
    }
    static void Say(PetChatWindow form, string text)
    {
        ((TextBox)Field(form, "input")).Text = text;
        typeof(PetChatWindow).GetMethod("InputKeyDown", Private).Invoke(form, new object[] { Field(form, "input"), new KeyEventArgs(Keys.Enter) });
    }
    static void Conversation(string data)
    {
        Directory.CreateDirectory(data);
        string profile = Path.Combine(data, "ai_chat_config.json");
        var selected = new PetChatSettings { BaseUrl = "https://chat-configured.example/v1", ApiKey = "chat-configured-key", Model = "chat-selected-model" };
        PetChatSettingsStore.Save(profile, selected);
        string desktop = Path.Combine(data, "simulated-desktop"); Directory.CreateDirectory(desktop);
        string work = AssistantConfiguration.Load(data).Workspace;
        string scenario = "chat"; int phase = 0; bool waiting = false;
        using (var form = new PetChatWindow(data))
        {
            form.DesktopDirectory = () => desktop;
            form.CreateApi = snapshot => new PetChatApi(snapshot, new Mock { Send = async (request, token) => {
                var body = AssistantJson.Decode(await request.Content.ReadAsStringAsync());
                Check((string)body["model"] == selected.Model && request.Headers.Authorization.Parameter == selected.ApiKey && request.RequestUri.Host == "chat-configured.example", "conversation directly shares AI settings");
                Check(body.ContainsKey("tools") && !body.ContainsKey("thinking"), "chat offers tools on the configured compatible API");
                var messages = (System.Collections.IList)body["messages"];
                string system = (string)((Dictionary<string, object>)messages[0])["content"];
                Check(system.Contains("温柔") && !system.Contains("不假装能看到屏幕或执行电脑操作"), "conversation keeps personality while updating the obsolete capability statement");
                if (scenario == "chat") return Answer("我在呢，今天过得怎么样？");
                if (++phase == 1)
                {
                    if (scenario == "document") return Calls(
                        Call("doc", "create_document", "path", "output/工作计划.docx", "title", "工作计划", "body", "在非 C 盘生成，再交付桌面。"),
                        Call("deliver", "deliver_to_desktop", "source", "output/工作计划.docx", "name", "工作计划.docx"));
                    return scenario == "cancel" ? Calls(Call("partial", "write_text", "path", "temp/partial.txt", "content", "retained")) :
                        Calls(Call("desktop", "create_desktop_directory", "name", "工作资料"));
                }
                if (scenario == "cancel") { waiting = true; await Task.Delay(30000, token); return Answer("unreachable"); }
                if (scenario == "create") Check(Directory.Exists(Path.Combine(desktop, "工作资料")), "folder exists before model receives success");
                // Deliberately inaccurate model text must not defeat native failure/existence results.
                return Answer("已经创建成功啦。");
            } });
            form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-30000, -30000); form.Show(); Application.DoEvents();
            Say(form, "你好呀"); Pump(() => !form.Busy);
            Check(form.LastReply.Contains("我在呢") && !Directory.Exists(Path.Combine(work, "tasks")), "ordinary chat remains in-memory and creates no task files");
            scenario = "create"; phase = 0; Say(form, "帮我在桌面新建一个叫工作资料的文件夹"); Pump(() => !form.Busy);
            Check(form.LastReply.Contains(Path.Combine(desktop, "工作资料")) && Directory.Exists(Path.Combine(desktop, "工作资料")), "the existing chat box creates the desktop folder and reports its real location");
            var action = (AssistantRunner)Field(form, "assistantRunner");
            Check(File.Exists(Path.Combine(action.Task.Directory, "result.json")) && !Application.OpenForms.Cast<Form>().Any(f => f is PetAssistantWindow), "chat actions retain a task record without opening the assistant window");
            scenario = "existing"; phase = 0; Say(form, "再在桌面新建同名的工作资料文件夹"); Pump(() => !form.Busy);
            Check(form.LastReply.Contains("已存在") && !form.LastReply.Contains("创建成功"), "existing directory cannot be reported as newly created");
            scenario = "document"; phase = 0; Say(form, "帮我写一份工作计划放到桌面"); Pump(() => !form.Busy);
            action = (AssistantRunner)Field(form, "assistantRunner");
            Check(File.Exists(Path.Combine(desktop, "工作计划.docx")) && File.Exists(action.Task.Files.Resolve("output/工作计划.docx")), "chat stages document off C and delivers only the finished desktop artifact");
            Check(Directory.GetFiles(desktop).Length == 1 && Directory.GetDirectories(desktop).Length == 1 && !action.Task.Directory.StartsWith(desktop, StringComparison.OrdinalIgnoreCase),
                "desktop has only requested folder and final document; task scripts/temp/logs remain in workspace");
            form.DesktopDirectory = () => @"\\localhost\C$\Users\test\Desktop";
            scenario = "denied"; phase = 0; Say(form, "在桌面新建工作资料"); Pump(() => !form.Busy);
            Check(form.LastReply.Contains("没有完成") && !form.LastReply.Contains("创建成功"), "unsafe desktop aliases remain rejected and model text cannot fabricate success");
            form.DesktopDirectory = () => desktop;
            scenario = "cancel"; phase = 0; Say(form, "生成一个文件"); Pump(() => waiting); form.StopOperation(); Pump(() => !form.Busy);
            action = (AssistantRunner)Field(form, "assistantRunner");
            Check(form.LastReply.Contains("已停止") && File.Exists(action.Task.Files.Resolve("temp/partial.txt")) &&
                File.ReadAllText(Path.Combine(action.Task.Directory, "result.json")).Contains("cancelled"), "chat cancellation preserves partial files and status");
            selected.Model = "updated-chat-model"; selected.ApiKey = "updated-chat-key"; PetChatSettingsStore.Save(profile, selected);
            scenario = "chat"; Say(form, "继续聊天吧"); Pump(() => !form.Busy);
            Check(form.LastReply.Contains("我在呢"), "already-open conversation reloads shared settings");
            form.Close();
        }
        var task = new AssistantTask(AssistantConfiguration.Load(data), "桌面路径防绕过测试");
        var tools = new AssistantTools(task, "", () => desktop);
        Denied(() => tools.Execute("create_desktop_directory", Args("name", "../escape"), CancellationToken.None), "desktop folder name cannot escape");
        task.Finish("completed", "路径防绕过检查完成。");
        Console.WriteLine("PASS: in-chat tool selection, desktop folder creation/existence, unsafe-path denial, truthful replies, cancellation, personality/context, no ordinary-chat files and no execution window.");
    }
}
