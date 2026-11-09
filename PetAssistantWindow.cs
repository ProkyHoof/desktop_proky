using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

// Configuration and retained task history only. Commands are sent through chat.
internal sealed class PetAssistantWindow : KnightWindow
{
    readonly string dataRoot;
    readonly bool preview;
    readonly Action openAiSettings;
    readonly TextBox workspace = new TextBox(), delivery = new TextBox(), output = new TextBox();
    readonly Label currentModel = new Label(), currentApi = new Label(), state = new Label();
    readonly ListBox history = new ListBox();
    readonly Font uiFont = new Font("Microsoft YaHei UI", 14, FontStyle.Regular, GraphicsUnit.Pixel);
    readonly KnightButton save = new KnightButton(), apiSettings = new KnightButton();
    readonly KnightButton chooseWorkspace = new KnightButton(), chooseDelivery = new KnightButton();
    internal Func<string, string, string> SelectDirectory;
    string lastDirectory;
    bool released;
    internal PetAssistantWindow(string dataRoot, bool preview = false, Action openAiSettings = null)
    {
        this.dataRoot = dataRoot; this.preview = preview; this.openAiSettings = openAiSettings;
        SelectDirectory = ShowDirectoryPicker;
        Font = uiFont; Text = "电脑助手设置与记录";
        ClientSize = new Size(920, 760); MinimumSize = new Size(800, 650); StartPosition = FormStartPosition.CenterScreen;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(20, 12, 20, 16) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (float height in new[] { 66f, 192f, 42f, 28f, 0f, 120f })
            layout.RowStyles.Add(height == 0 ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.Absolute, height));
        layout.Controls.Add(new Label { Dock = DockStyle.Fill, ForeColor = KnightUi.Steel,
            Text = "直接在她的对话框里说需求，例如“帮我在桌面新建一个叫工作资料的文件夹”。\n操作目标和最终文件可以在 C 盘；执行中间的工程、脚本、临时文件、缓存和日志只放在非 C 盘。" }, 0, 0);
        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 5 };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140)); fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (int height in new[] { 38, 38, 36, 36, 44 }) fields.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        AddPathField(fields, "内部工程（非C）", workspace, chooseWorkspace, 0, false);
        AddPathField(fields, "成品交付目录", delivery, chooseDelivery, 1, true);
        currentModel.TextAlign = currentApi.TextAlign = ContentAlignment.MiddleLeft;
        currentModel.AutoEllipsis = currentApi.AutoEllipsis = true;
        currentModel.AccessibleName = "AI 接口设置中的当前模型"; currentApi.AccessibleName = "AI 接口设置中的当前地址";
        AddField(fields, "当前模型", currentModel, 2); AddField(fields, "当前接口", currentApi, 3);
        fields.Controls.Add(new Label { Dock = DockStyle.Fill, ForeColor = KnightUi.Steel,
            Text = "接口、密钥和模型统一在“AI 接口设置”中管理。任务文字、工具结果会发送给所选服务。\n停止当前请求：Ctrl + Alt + F10，或右键桌宠选择“停止当前操作”。" }, 0, 4);
        fields.SetColumnSpan(fields.GetControlFromPosition(0, 4), 2); layout.Controls.Add(fields, 0, 1);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        Button(save, "保存路径", SaveSettings); Button(apiSettings, "AI 接口设置", ConfigureApi);
        var refresh = new KnightButton(); Button(refresh, "刷新记录", (s, e) => RefreshHistory());
        var copy = new KnightButton(); Button(copy, "复制任务路径", (s, e) => CopyPath());
        actions.Controls.AddRange(new Control[] { save, apiSettings, refresh, copy }); layout.Controls.Add(actions, 0, 2);
        state.Dock = DockStyle.Fill; state.AutoEllipsis = true; state.TextAlign = ContentAlignment.MiddleLeft; layout.Controls.Add(state, 0, 3);
        output.Multiline = true; output.ReadOnly = true; output.ScrollBars = ScrollBars.Both; output.Dock = DockStyle.Fill;
        output.BackColor = KnightUi.Surface; output.AccessibleName = "任务记录与结果";
        output.Text = "在对话框中交代事情后，实际操作的记录会出现在下方。普通聊天不创建任务文件。";
        layout.Controls.Add(output, 0, 4);
        history.Dock = DockStyle.Fill; history.AccessibleName = "任务历史，双击查看记录";
        history.DoubleClick += (s, e) => InspectHistory(); layout.Controls.Add(history, 0, 5);
        Controls.Add(layout); layout.BringToFront(); LoadSettings();
        save.Enabled = apiSettings.Enabled = chooseWorkspace.Enabled = chooseDelivery.Enabled = !preview;
        Activated += (s, e) => { RefreshApiSettings(); RefreshHistory(); };
    }
    static void AddField(TableLayoutPanel parent, string title, Control control, int row)
    {
        parent.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        control.Dock = DockStyle.Fill; control.Margin = new Padding(4, 5, 4, 5); parent.Controls.Add(control, 1, row);
    }
    void AddPathField(TableLayoutPanel parent, string title, TextBox editor, KnightButton choose, int row, bool userTarget)
    {
        parent.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        var field = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(4, 3, 4, 3) };
        field.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); field.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118));
        field.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        editor.Dock = DockStyle.Fill; editor.Margin = new Padding(0, 4, 8, 0); editor.AccessibleName = title;
        choose.Text = "选择文件夹…"; choose.AccessibleName = userTarget ? "选择成品交付目录" : "选择内部工程目录";
        choose.Dock = DockStyle.Fill; choose.Margin = Padding.Empty;
        choose.Click += (s, e) => ChooseDirectory(editor, userTarget);
        field.Controls.Add(editor, 0, 0); field.Controls.Add(choose, 1, 0); parent.Controls.Add(field, 1, row);
    }
    void ChooseDirectory(TextBox editor, bool userTarget)
    {
        if (preview) return;
        try
        {
            string title = userTarget ? "选择成品交付目录（允许 C 盘）" : "选择内部工程目录（工程、脚本、临时文件与日志须在非 C 盘）";
            string selected = SelectDirectory(title, InitialDirectory(editor.Text, userTarget));
            if (String.IsNullOrEmpty(selected)) return;
            var paths = userTarget ? AssistantPaths.ForUserTarget(selected) : new AssistantPaths(selected);
            editor.Text = paths.Root; editor.SelectionStart = 0; editor.SelectionLength = 0; editor.ScrollToCaret();
            state.Text = "已选择" + (userTarget ? "成品交付目录" : "内部工程目录") + "；点击“保存路径”生效。";
        }
        catch (Exception error) { state.Text = "目录未更改：" + PetChatSettingsWindow.SafeError(error); }
    }
    string InitialDirectory(string current, bool userTarget)
    {
        string candidate;
        try { candidate = (userTarget ? AssistantPaths.ForUserTarget(current) : new AssistantPaths(current)).Root; }
        catch (Exception error)
        {
            if (!AssistantRunner.IsExpected(error)) throw;
            candidate = new AssistantPaths(dataRoot).Root;
        }
        while (!Directory.Exists(candidate))
        {
            string parent = Path.GetDirectoryName(candidate);
            if (String.IsNullOrEmpty(parent)) return Path.GetPathRoot(dataRoot);
            candidate = parent;
        }
        return candidate;
    }
    string ShowDirectoryPicker(string description, string initialDirectory)
    {
        using (var picker = new FolderBrowserDialog { Description = description, RootFolder = Environment.SpecialFolder.MyComputer,
            SelectedPath = initialDirectory, ShowNewFolderButton = true })
            return picker.ShowDialog(this) == DialogResult.OK ? picker.SelectedPath : null;
    }
    static void Button(KnightButton button, string text, EventHandler click)
    { button.Text = text; button.Size = new Size(text.Length > 4 ? 116 : 100, 34); button.Margin = new Padding(0, 3, 8, 3); button.Click += click; }
    void LoadSettings()
    {
        try
        {
            var config = AssistantConfiguration.Load(dataRoot); workspace.Text = config.Workspace; delivery.Text = config.ExportDirectory;
            state.Text = preview ? "预览模式：不保存，不连接 API。" : "在对话框中直接说需求；此窗口只管理路径和记录。";
            RefreshHistory();
        }
        catch (Exception error) { workspace.Text = Path.Combine(dataRoot, "assistant", "workspace"); state.Text = "配置未加载，原文件保留：" + error.Message; }
        RefreshApiSettings();
    }
    PetChatSettings Credentials() { return AssistantSettings.LoadChat(dataRoot); }
    void RefreshApiSettings()
    {
        try { var settings = Credentials(); currentModel.Text = settings.Model; currentApi.Text = settings.BaseUrl; }
        catch (Exception error)
        {
            if (!AssistantRunner.IsExpected(error) && !(error is System.Security.Cryptography.CryptographicException)) throw;
            currentModel.Text = "请先完成 AI 接口设置"; currentApi.Text = PetChatSettingsWindow.SafeError(error);
        }
    }
    void ConfigureApi(object sender, EventArgs e)
    {
        if (preview) return;
        try
        {
            if (openAiSettings != null) openAiSettings();
            else
            {
                var paths = new AssistantPaths(dataRoot); string file = paths.Resolve("ai_chat_config.json"); paths.Resolve("ai_chat_config.json.bak");
                using (var editor = new PetChatSettingsWindow(PetChatSettingsStore.Load(file), file)) editor.ShowDialog(this);
            }
            RefreshApiSettings();
        }
        catch (Exception error) { state.Text = PetChatSettingsWindow.SafeError(error); }
    }
    void SaveSettings(object sender, EventArgs e)
    {
        try
        {
            if (preview) return;
            new AssistantConfiguration { Workspace = workspace.Text, ExportDirectory = delivery.Text }.Save(dataRoot);
            state.Text = "路径已保存；下一次对话中的操作会使用此目录。"; RefreshHistory();
        }
        catch (Exception error) { state.Text = "未保存：" + PetChatSettingsWindow.SafeError(error); }
    }
    sealed class HistoryItem
    {
        internal string Directory, Status, Id;
        public override string ToString() { return Id + "  ·  " + Status; }
    }
    void RefreshHistory()
    {
        if (released) return;
        history.Items.Clear();
        try
        {
            var paths = new AssistantPaths(workspace.Text); string tasks = paths.Resolve("tasks"); if (!Directory.Exists(tasks)) return;
            foreach (string directory in Directory.EnumerateDirectories(tasks).OrderByDescending(p => p).Take(100))
            {
                string relative = paths.Relative(directory); paths.Resolve(relative); string status = "中断或运行中（未清理）";
                if (File.Exists(paths.Resolve(relative + "/result.json")))
                {
                    var result = AssistantJson.Decode(Encoding.UTF8.GetString(paths.Read(relative + "/result.json", 256000)));
                    string raw = AssistantJson.Text(result, "status", 100);
                    status = raw == "completed" ? "已完成" : raw == "failed" ? "失败，现场保留" : raw == "cancelled" ? "已取消，现场保留" : raw == "timeout" ? "超时，现场保留" : raw == "completed_with_errors" ? "结束，存在工具错误" : raw;
                }
                history.Items.Add(new HistoryItem { Id = Path.GetFileName(directory), Directory = directory, Status = status });
            }
        }
        catch (Exception error) { if (!AssistantRunner.IsExpected(error)) throw; state.Text = "历史未加载：" + error.Message; }
    }
    void InspectHistory()
    {
        var selected = history.SelectedItem as HistoryItem; if (selected == null) return;
        try
        {
            var paths = new AssistantPaths(workspace.Text); string relative = paths.Relative(selected.Directory);
            string file = File.Exists(paths.Resolve(relative + "/result.json")) ? "result.json" : "task.json";
            var record = AssistantJson.Decode(Encoding.UTF8.GetString(paths.Read(relative + "/" + file, 256000)));
            lastDirectory = paths.Resolve(relative); object message; record.TryGetValue("message", out message);
            output.Text = "要求：" + AssistantJson.Text(record, "prompt") + "\r\n状态：" + selected.Status + "\r\n结果：" + (message as string ?? "尚无最终结果") + "\r\n任务目录：" + lastDirectory;
            state.Text = selected.Status + "；详细工具记录保留在任务目录。";
        }
        catch (Exception error) { state.Text = PetChatSettingsWindow.SafeError(error); }
    }
    void CopyPath()
    {
        if (String.IsNullOrEmpty(lastDirectory)) { state.Text = "先双击一条任务记录，再复制其目录。"; return; }
        try { Clipboard.SetText(lastDirectory); state.Text = "已复制任务目录。"; }
        catch (System.Runtime.InteropServices.ExternalException) { state.Text = "剪贴板正忙，任务路径见结果区。"; }
    }
    protected override void Dispose(bool disposing) { if (disposing) released = true; base.Dispose(disposing); if (disposing) uiFont.Dispose(); }
}
