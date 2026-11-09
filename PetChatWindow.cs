using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class PetChatUi
{
    internal static void Style(Form form, string title, Size size)
    {
        form.AutoScaleDimensions = new SizeF(96, 96); form.AutoScaleMode = AutoScaleMode.Dpi;
        form.Text = title; form.Icon=KnightUi.WindowIcon;form.Font = new Font("Microsoft YaHei UI", 14, FontStyle.Regular, GraphicsUnit.Pixel);
        form.BackColor = KnightUi.Paper; form.ForeColor = KnightUi.Ink;
        form.StartPosition = FormStartPosition.CenterParent; form.ClientSize = size;
        form.MinimumSize = new Size(size.Width + 16, size.Height + 39);
    }
    internal static KnightButton Button(string text, EventHandler click)
    { var button = new KnightButton { Text = text, AutoSize = false, Size = new Size(106, 36), Margin = new Padding(4) }; button.Click += click; return button; }
    internal static Label Label(string text)
    { return new Label { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoSize = false }; }
}

internal sealed class PetChatSettingsWindow : KnightWindow
{
    readonly PetChatSettings original;
    readonly string path;
    readonly TextBox address, key, audioAddress, audioKey, transcription, speech, voice, personality;
    readonly ComboBox provider, model;
    readonly NumericUpDown threshold, silence;
    readonly CheckBox read;
    readonly Label result = new Label { AutoSize = true, MaximumSize = new Size(620, 0) };
    readonly CancellationTokenSource lifetime = new CancellationTokenSource();
    readonly KnightButton test;
    bool released;
    bool changingProvider;
    string lastAddress;
    internal PetChatSettings Saved { get; private set; }
    internal PetChatSettingsWindow(PetChatSettings settings, string path)
    {
        original = settings.Copy(); this.path = path;
        PetChatUi.Style(this, "AI 接口设置 · 白发小骑士", new Size(760, 660));
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24,12,24,16), ColumnCount = 1, RowCount = 4 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 84)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48)); Controls.Add(layout);
        layout.Controls.Add(new KnightSectionHeader("连接你的 AI 伙伴","选择模型与声音，定义她的说话方式。密钥仅在本机加密保存。",KnightIcon.Settings), 0, 0);
        var tabs = new KnightTabs { Dock = DockStyle.Fill }; layout.Controls.Add(tabs, 0, 1);
        var chat = Page(tabs, "聊天接口");
        chat.Controls.Add(PetChatUi.Label("服务商预设"), 0, 0);
        provider = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(4, 7, 4, 5), AccessibleName = "聊天服务商" };
        provider.FlatStyle=FlatStyle.Flat;provider.Items.AddRange(PetChatProvider.Presets); chat.Controls.Add(new KnightField(provider), 1, 0);chat.RowStyles[0].Height=46;
        address = Field(chat, "API 基础地址", settings.BaseUrl, 1); key = Field(chat, "API Key（可留空）", settings.ApiKey, 2); key.UseSystemPasswordChar = true;
        chat.Controls.Add(PetChatUi.Label("聊天模型（可编辑）"), 0, 3);
        model = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Dock = DockStyle.Fill, Margin = new Padding(4, 7, 4, 5), AccessibleName = "聊天模型" };
        model.FlatStyle=FlatStyle.Flat;chat.Controls.Add(new KnightField(model), 1, 3);chat.RowStyles[3].Height=46; provider.SelectedIndex = PetChatProvider.Detect(settings.BaseUrl);
        SuggestModels(); model.Text = settings.Model;
        Note(chat, "选择预设后填写对应服务的 Key，模型名称也可手动输入。\nDeepSeek / GLM 通话需在“语音接口”配置独立语音服务。\nGLM 使用标准 API Key；自定义地址不包含 /chat/completions。", 4);
        var audio = Page(tabs, "语音接口");
        audioAddress = Field(audio, "语音基础地址（选填）", settings.AudioBaseUrl, 0);
        audioKey = Field(audio, "语音 API Key", settings.AudioApiKey, 1); audioKey.UseSystemPasswordChar = true;
        transcription = Field(audio, "语音识别模型", settings.TranscriptionModel, 2); speech = Field(audio, "语音合成模型", settings.SpeechModel, 3);
        voice = Field(audio, "音色", settings.Voice, 4);
        Note(audio, "语音地址留空：使用聊天地址和聊天 Key。填写独立地址时只使用语音 Key。\n服务须支持 /audio/transcriptions 与 /audio/speech，返回 PCM WAV。\n参考值：whisper-1 / tts-1 / nova。兼容聊天不代表兼容语音。", 5);
        var behavior = Page(tabs, "角色与通话");
        personality = Field(behavior, "她的性格与说话方式", settings.Personality, 0); personality.Multiline = true; personality.ScrollBars = ScrollBars.Vertical; personality.Height = 118;
        behavior.RowStyles[0].Height = 130;
        threshold = Number(behavior, "收音阈值", settings.VoiceThreshold, 100, 5000, 100, 1);
        silence = Number(behavior, "停顿断句（毫秒）", settings.SilenceMilliseconds, 600, 2500, 100, 2);
        read = new CheckBox { Text = "文字聊天也朗读回复", Checked = settings.ReadReplies, AutoSize = true, Dock = DockStyle.Fill };
        behavior.Controls.Add(read, 1, 3);behavior.RowStyles[3].Height=40;
        Note(behavior, "收音阈值越低越灵敏；噪声多时可调高。通话使用 Windows 默认麦克风。\n通话为轮流说话：她回复时暂停收音，回复后自动继续。\n通话中的声音由 AI 合成。当前会话只保存在内存，关闭窗口后清除。", 4);
        layout.Controls.Add(result, 0, 2);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var save = PetChatUi.Button("保存设置", Save); save.Primary = true;
        var cancel = PetChatUi.Button("取消", (s, e) => Close());
        test = PetChatUi.Button("测试聊天连接", Test);
        buttons.Controls.Add(save); buttons.Controls.Add(cancel); buttons.Controls.Add(test); layout.Controls.Add(buttons, 0, 3);
        CancelButton = cancel;
        lastAddress = address.Text;
        provider.SelectedIndexChanged += (s, e) => ApplyProvider();
        address.TextChanged += (s, e) =>
        {
            if (changingProvider) return;
            bool changed = !PetChatProvider.SameEndpoint(address.Text, lastAddress);
            if (changed) key.Clear();
            lastAddress = address.Text;
            changingProvider = true;
            try { provider.SelectedIndex = PetChatProvider.Detect(address.Text); SuggestModels(); }
            finally { changingProvider = false; }
            if (changed) { AdjustAudio(); result.Text = "地址已更改，请填写对应服务的 API Key。"; }
        };
    }
    void SuggestModels()
    {
        string current = model.Text;
        model.Items.Clear();
        model.Items.AddRange(PetChatProvider.Presets[Math.Max(0, provider.SelectedIndex)].Models);
        model.Text = current;
    }
    void AdjustAudio()
    {
        if (String.IsNullOrWhiteSpace(audioAddress.Text) && PetChatProvider.Presets[PetChatProvider.Detect(address.Text)].SeparateAudio)
            read.Checked = false;
    }
    void ApplyProvider()
    {
        if (changingProvider || provider.SelectedIndex < 0) return;
        var selected = (PetChatProvider)provider.SelectedItem;
        changingProvider = true;
        try
        {
            SuggestModels();
            if (selected.BaseUrl.Length == 0) return;
            if (!PetChatProvider.SameEndpoint(address.Text, selected.BaseUrl)) key.Clear();
            address.Text = selected.BaseUrl; lastAddress = address.Text; model.Text = selected.Models[0];
            AdjustAudio(); result.Text = "已选择 " + selected.Name + "，请填写该服务的 API Key，再测试聊天连接。";
        }
        finally { changingProvider = false; }
    }
    static TableLayoutPanel Page(TabControl tabs, string title)
    {
        var page = new TabPage(title) { BackColor = KnightUi.Surface, Padding = new Padding(20,18,20,12), AutoScroll = true }; tabs.TabPages.Add(page);
        var grid = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = 7 };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 174)); grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 7; i++) grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
        page.Controls.Add(grid); return grid;
    }
    static TextBox Field(TableLayoutPanel grid, string title, string value, int row)
    {
        grid.Controls.Add(PetChatUi.Label(title), 0, row);
        var field = new TextBox { Text = value, Dock = DockStyle.Fill, Margin = new Padding(4, 4, 4, 4) }; grid.Controls.Add(new KnightField(field), 1, row);grid.RowStyles[row].Height=46; return field;
    }
    static NumericUpDown Number(TableLayoutPanel grid, string title, int value, int min, int max, int step, int row)
    {
        grid.Controls.Add(PetChatUi.Label(title), 0, row);
        var number = new NumericUpDown { Minimum = min, Maximum = max, Value = value, Increment = step, Dock=DockStyle.Fill, BorderStyle=BorderStyle.None, Margin = new Padding(4) };
        grid.Controls.Add(new KnightField(number), 1, row);grid.RowStyles[row].Height=44; return number;
    }
    static void Note(TableLayoutPanel grid, string text, int row)
    { var label = PetChatUi.Label(text); label.ForeColor = KnightUi.Steel;label.Padding=new Padding(0,12,0,0); grid.Controls.Add(label, 0, row); grid.SetColumnSpan(label, 2); grid.RowStyles[row].Height = 92; }
    PetChatSettings Read()
    {
        var s = original.Copy();
        s.BaseUrl = address.Text.Trim(); s.ApiKey = key.Text.Trim(); s.Model = model.Text.Trim();
        s.AudioBaseUrl = audioAddress.Text.Trim(); s.AudioApiKey = audioKey.Text.Trim();
        s.TranscriptionModel = transcription.Text.Trim(); s.SpeechModel = speech.Text.Trim(); s.Voice = voice.Text.Trim();
        s.Personality = personality.Text.Trim(); s.VoiceThreshold = (int)threshold.Value; s.SilenceMilliseconds = (int)silence.Value; s.ReadReplies = read.Checked;
        return s;
    }
    void Save(object sender, EventArgs e)
    {
        try { var s = Read(); PetChatSettingsStore.Save(path, s); Saved = s; DialogResult = DialogResult.OK; Close(); }
        catch (Exception error) { result.Text = "保存失败：" + SafeError(error); }
    }
    async void Test(object sender, EventArgs e)
    {
        test.Enabled = false; result.Text = "正在测试聊天连接（会发送一句测试消息，可能产生 API 费用）…";
        try
        {
            using (var api = new PetChatApi(Read()))
                await api.ChatAsync(new List<PetChatMessage> { new PetChatMessage("user", "请只回复：连接成功") }, lifetime.Token);
            if (!IsDisposed) result.Text = "聊天连接成功。语音接口会在首次通话时单独验证。";
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!IsDisposed) result.Text = "测试失败：" + SafeError(error); }
        finally { if (!IsDisposed) test.Enabled = true; }
    }
    internal static string SafeError(Exception error)
    {
        if (error is InvalidOperationException || error is IOException || error is UnauthorizedAccessException) return error.Message;
        return "操作失败，请检查接口设置与设备后重试。";
    }
    protected override void Dispose(bool disposing)
    { if (disposing && !released) { released = true; lifetime.Cancel(); lifetime.Dispose(); } base.Dispose(disposing); }
}

// Speech is painted into the pet's existing layered surface, never a second form.
internal sealed class PetSpeechBubble : IDisposable
{
    internal static readonly RectangleF Bounds = new RectangleF(12, 8, 328, 98);
    readonly Font font = new Font("Microsoft YaHei UI", 14, FontStyle.Regular, GraphicsUnit.Pixel);
    readonly Font hintFont = new Font("Microsoft YaHei UI", 10, FontStyle.Regular, GraphicsUnit.Pixel);
    readonly List<string> pages = new List<string>();
    int page;
    internal const double IdleSeconds = 5, FadeSeconds = 2;
    double lastInteraction;
    Bitmap rendered;
    internal float Opacity { get; private set; }
    internal string Text { get; private set; }
    internal bool Visible { get { return pages.Count > 0; } }
    internal int PageCount { get { return pages.Count; } }
    internal int PageIndex { get { return page; } }
    internal string CurrentPage { get { return Visible ? pages[page] : ""; } }
    internal void Say(string text, double now)
    {
        pages.Clear(); page = 0; Text = text ?? "";
        using (var image = new Bitmap(1, 1))
        using (var g = Graphics.FromImage(image))
        using (var format = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.LineLimit })
        {
            for (int offset = 0; offset < Text.Length; )
            {
                int fitted, lines;
                g.MeasureString(Text.Substring(offset), font, new SizeF(286, 54), format, out fitted, out lines);
                fitted = Math.Max(1, fitted);
                if (offset + fitted < Text.Length && Char.IsHighSurrogate(Text[offset + fitted - 1])) fitted--;
                if (fitted == 0) fitted = Math.Min(2, Text.Length - offset);
                pages.Add(Text.Substring(offset, fitted)); offset += fitted;
            }
        }
        ClearRendered(); Touch(now);
    }
    internal bool Advance(double now)
    {
        if (!Visible) return false;
        float progress = (float)Math.Max(0, Math.Min(1, (now - lastInteraction - IdleSeconds) / FadeSeconds));
        float next = 1 - progress * progress * (3 - 2 * progress);
        bool changed = next != Opacity; Opacity = next;
        if (progress >= 1) Dismiss();
        return changed;
    }
    internal void Touch(double now) { lastInteraction = now; Opacity = Visible ? 1 : 0; }
    void ClearRendered() { if (rendered != null) { rendered.Dispose(); rendered = null; } }
    internal void Dismiss() { pages.Clear(); Opacity = 0; ClearRendered(); }
    internal void Turn(int direction) { if (Visible) { page = (page + direction + pages.Count) % pages.Count; ClearRendered(); } }
    internal bool Hit(PointF point) { return Visible && Bounds.Contains(point); }
    internal void Click(PointF point)
    {
        if (!Hit(point)) return;
        if (point.X >= Bounds.Right - 26 && point.Y < Bounds.Top + 25) Dismiss();
        else if (PageCount > 1 && point.X < Bounds.Left + 42) Turn(-1);
        else if (PageCount > 1) Turn(1);
        else Dismiss();
    }
    internal void Draw(Graphics g)
    {
        if (!Visible) return;
        if (rendered == null)
        {
            rendered = new Bitmap(352, 128, PixelFormat.Format32bppPArgb);
            using (var canvas = Graphics.FromImage(rendered)) DrawContents(canvas);
        }
        // Fade the entire composited bubble together, including text and tail.
        if (Opacity >= 1) { g.DrawImage(rendered, new Rectangle(0, 0, rendered.Width, rendered.Height)); return; }
        using (var attributes = new ImageAttributes())
        {
            var matrix = new ColorMatrix(); matrix.Matrix33 = Opacity;
            attributes.SetColorMatrix(matrix);
            g.DrawImage(rendered, new Rectangle(0, 0, rendered.Width, rendered.Height), 0, 0, rendered.Width, rendered.Height, GraphicsUnit.Pixel, attributes);
        }
    }
    void DrawContents(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var shape = PetBubbleInput.Round(Bounds, 14))
        using (var fill = new SolidBrush(KnightUi.Surface))
        using (var edge = new Pen(KnightUi.Ink, 1.2f))
        using (var ink = new SolidBrush(KnightUi.Ink))
        using (var muted = new SolidBrush(KnightUi.Steel))
        using (var format = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.LineLimit })
        {
            g.FillPath(fill, shape); g.DrawPath(edge, shape);
            var tail = new[] { new PointF(172, 105), new PointF(184, 118), new PointF(193, 105) };
            g.FillPolygon(fill, tail); g.DrawLines(edge, tail);
            g.DrawString(CurrentPage, font, ink, new RectangleF(26, 20, 286, 54), format);
            string hint = PageCount > 1 ? "‹  " + (page + 1) + " / " + PageCount + "  ›   点击或滚轮翻页" : "点击人物继续聊聊";
            g.DrawString(hint, hintFont, muted, new PointF(26, 84));
            KnightIcons.Draw(g, KnightIcon.Close, new RectangleF(320, 16, 10, 10), KnightUi.Steel);
        }
    }
    public void Dispose() { ClearRendered(); font.Dispose(); hintFont.Dispose(); }
}

internal sealed class PetBubbleInput : Panel
{
    readonly TextBox editor;
    readonly Label hint;
    internal PetBubbleInput(TextBox editor)
    {
        this.editor = editor; Dock = DockStyle.Fill;
        BackColor = KnightUi.Surface; editor.BorderStyle = BorderStyle.None; editor.BackColor = KnightUi.Surface;
        editor.AccessibleName = "自由输入对话";
        var surface = new Panel { Dock = DockStyle.Fill, BackColor = KnightUi.Surface };
        hint = new Label { Text = "说点什么吧，Enter 发送…", ForeColor = KnightUi.Steel, BackColor = KnightUi.Surface,
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopLeft, Cursor = Cursors.IBeam };
        surface.Controls.Add(editor); surface.Controls.Add(hint); Controls.Add(surface);
        hint.Click += (s, e) => editor.Focus();
        editor.TextChanged += (s, e) => UpdateHint();
        editor.Enter += (s, e) => { UpdateHint(); Invalidate(); };
        editor.Leave += (s, e) => { UpdateHint(); Invalidate(); };
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
    void UpdateHint() { hint.Visible = editor.TextLength == 0 && !editor.Focused; if (hint.Visible) hint.BringToFront(); }
    internal static GraphicsPath Round(RectangleF rect, float radius)
    {
        float d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
        var path = new GraphicsPath();
        path.AddArc(rect.Left, rect.Top, d, d, 180, 90); path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90); path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure(); return path;
    }
}

internal sealed class PetChatInput : TextBox
{
    internal bool Composing { get; private set; }
    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x010D) Composing = true; // WM_IME_STARTCOMPOSITION
        base.WndProc(ref m);
        if (m.Msg == 0x010E) Composing = false; // WM_IME_ENDCOMPOSITION
    }
}

internal sealed class PetChatWindow : Form
{
    [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr window, int id);
    const int StopHotkey = 0x5042;
    bool hotkey;
    readonly string settingsPath, dataRoot;
    readonly bool preview;
    PetChatSettings settings;
    readonly PetChatHistory history = new PetChatHistory();
    readonly PetLocalDialogue local = new PetLocalDialogue();
    internal Func<string> SceneContext;
    bool tailLeft;
    int tailY = 46;
    readonly string initialScene;
    bool LocalMode
    {
        get
        {
            if (invalidSettings) return true;
            Uri uri;
            return String.IsNullOrWhiteSpace(settings.ApiKey) &&
                !(Uri.TryCreate(settings.BaseUrl, UriKind.Absolute, out uri) && uri.IsLoopback);
        }
    }
    readonly PetChatInput input = new PetChatInput { Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, MaxLength = 8000, AcceptsReturn = false };
    CancellationTokenSource operation;
    bool closing, disposed, invalidSettings;
    internal Func<PetChatSettings, PetChatApi> CreateApi = s => new PetChatApi(s);
    internal Func<string> DesktopDirectory = () => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    AssistantRunner assistantRunner;
    internal string LastReply { get; private set; }
    internal event Action<string> ReplyReady;
    internal bool Busy { get { return operation != null; } }
    internal void Announce() { if (ReplyReady != null && !String.IsNullOrEmpty(LastReply)) ReplyReady(LastReply); }
    void ShowReply(string text) { LastReply = text; Announce(); }
    internal void SayRandomOpening() { if (!Busy) ShowReply(local.RandomOpening()); }
    internal void DismissInput() { Hide(); }
    internal void ResetConversation()
    {
        if (Busy) return;
        history.Clear(); input.Clear();
        ShowReply(local.Greeting(SceneContext == null ? initialScene : SceneContext(), DateTime.Now));
    }
    internal void OpenSettings() { if (!Busy) Configure(this, EventArgs.Empty); }
    internal void StopOperation() { if (operation != null) operation.Cancel(); }

    internal PetChatWindow(string root, bool preview = false, string context = "idle")
    {
        initialScene = context;
        this.preview = preview; dataRoot = root; settingsPath = Path.Combine(root, "ai_chat_config.json");
        settings = new PetChatSettings();
        if (!preview)
        {
            try { settings = PetChatSettingsStore.Load(settingsPath); }
            catch (InvalidOperationException) { invalidSettings = true; }
        }
        PetChatUi.Style(this, "和她聊聊天", new Size(320, 92));
        MinimumSize = Size.Empty; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual; KeyPreview = true; DoubleBuffered = true;
        BackColor = Color.Fuchsia; TransparencyKey = Color.Fuchsia; Padding = new Padding(30, 15, 30, 15);
        Controls.Add(new PetBubbleInput(input));
        KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; if (Busy) StopOperation(); else DismissInput(); } };
        input.KeyDown += InputKeyDown;
        Deactivate += (s, e) => Hide();
        SetBusy(false);
        LastReply = LocalMode ? local.Greeting(context, DateTime.Now) : "你来啦，想你了。今天有什么想和我说的？";
    }
    void InputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter || input.Composing) return;
        e.SuppressKeyPress = true;
        if (input.Enabled) Send(sender, e);
    }
    protected override CreateParams CreateParams
    { get { var p = base.CreateParams; p.ExStyle |= 0x80; return p; } }
    internal void FocusInput() { if (input.Enabled) input.Focus(); }
    internal void PlaceBeside(Rectangle pet, Rectangle work)
    {
        int gap = 4;
        bool left = pet.Left - Width - gap >= work.Left;
        int x = left ? pet.Left - Width - gap : pet.Right + gap;
        if (!left && x + Width > work.Right) x = pet.Left + (pet.Width - Width) / 2;
        int targetY = pet.Top + (int)(pet.Height * .46);
        int y = targetY - Height / 2;
        if (x < pet.Right && x + Width > pet.Left) y = pet.Top - Height - gap;
        x = Math.Max(work.Left, Math.Min(x, work.Right - Width));
        y = Math.Max(work.Top, Math.Min(y, work.Bottom - Height));
        bool changed = tailLeft != !left;
        tailLeft = !left;
        int nextTail = Math.Max(30, Math.Min(Height - 30, targetY - y));
        changed |= nextTail != tailY; tailY = nextTail;
        Location = new Point(x, y);
        if (changed) Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        // Color-key transparency needs unblended outer pixels to avoid a pink rim.
        e.Graphics.SmoothingMode = SmoothingMode.None;
        using (var shape = PetBubbleInput.Round(new RectangleF(16, 1, Width - 33, Height - 3), 16))
        using (var fill = new SolidBrush(KnightUi.Surface))
        using (var edge = new Pen(KnightUi.Silver))
        {
            e.Graphics.FillPath(fill, shape); e.Graphics.DrawPath(edge, shape);
            Point[] tail = tailLeft ? new[] { new Point(17, tailY - 9), new Point(2, tailY), new Point(17, tailY + 9) } :
                new[] { new Point(Width - 18, tailY - 9), new Point(Width - 3, tailY), new Point(Width - 18, tailY + 9) };
            e.Graphics.FillPolygon(fill, tail); e.Graphics.DrawLines(edge, tail);
        }
    }
    void SetBusy(bool busy) { input.Enabled = !busy && !preview; }
    void ProgressReply(string progress)
    {
        if (disposed || !progress.StartsWith("步骤 ", StringComparison.Ordinal)) return;
        if (InvokeRequired)
        {
            try { if (IsHandleCreated) BeginInvoke(new Action<string>(ProgressReply), progress); }
            catch (InvalidOperationException) { if (!disposed && !Disposing && !IsDisposed) throw; }
            return;
        }
        string message = progress.Contains("create_desktop_directory") ? "我在帮你创建桌面文件夹…" :
            progress.Contains("create_document") ? "我在帮你生成文档…" : progress.Contains("export_file") || progress.Contains("deliver_to_desktop") ? "我在保存成品…" : "我在处理你交代的事情…";
        ShowReply(message);
    }
    void Configure(object sender, EventArgs e)
    {
        using (var editor = new PetChatSettingsWindow(settings, settingsPath))
            if (editor.ShowDialog(this) == DialogResult.OK)
            {
                settings = editor.Saved; invalidSettings = false; history.Clear(); SetBusy(false);
                ShowReply("好啦，我在呢，想聊什么？");
            }
    }
    async void Send(object sender, EventArgs e)
    {
        if (Busy || preview) return;
        string text = input.Text.Trim(); if (text.Length == 0) return;
        try
        {
            var latest = PetChatSettingsStore.Load(settingsPath);
            if (latest.BaseUrl != settings.BaseUrl || latest.Model != settings.Model || latest.ApiKey != settings.ApiKey) history.Clear();
            settings = latest; invalidSettings = false;
        }
        catch (InvalidOperationException) { invalidSettings = true; }
        input.Clear(); Hide();
        if (LocalMode)
        {
            ShowReply("我真的很想拥有你，但是你却没有牵住我的手");
            return;
        }
        var source = new CancellationTokenSource(); operation = source; SetBusy(true);
        hotkey = RegisterHotKey(Handle, StopHotkey, 0x4003, 0x79);
        bool committed = false;
        try
        {
            ShowReply("让我想一想，马上回答你…");
            settings.Validate(false);
            using (var api = CreateApi(settings))
            {
                assistantRunner = new AssistantRunner();
                string reply = await assistantRunner.Run(AssistantConfiguration.Load(dataRoot), settings, text, ProgressReply, source.Token,
                    turn: api.ToolTurnAsync, conversation: history.Request(settings.Personality, text), desktopDirectory: DesktopDirectory);
                source.Token.ThrowIfCancellationRequested();
                if (disposed) return;
                history.Commit(text, reply); committed = true; ShowReply(reply);
            }
        }
        catch (OperationCanceledException) { if (!disposed && !closing) ShowReply("已停止，已经产生的文件和操作记录都保留了。"); }
        catch (Exception error)
        {
            if (!disposed && !closing)
            {
                if (!committed && input.TextLength == 0) input.Text = text;
                ShowReply("这次没有完成回复，输入已保留。" + PetChatSettingsWindow.SafeError(error));
                if (!Visible) { Show(); Activate(); }
            }
        }
        finally
        {
            source.Dispose(); operation = null;
            if (hotkey && IsHandleCreated) { UnregisterHotKey(Handle, StopHotkey); hotkey = false; }
            if (!disposed) { SetBusy(false); if (closing) Close(); else if (Visible) FocusInput(); }
        }
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (Busy) { closing = true; operation.Cancel(); e.Cancel = true; }
        base.OnFormClosing(e);
    }
    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0312 && message.WParam.ToInt32() == StopHotkey) StopOperation();
        base.WndProc(ref message);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !disposed)
        {
            disposed = true; if (Busy) operation.Cancel(); history.Clear();
            if (hotkey && IsHandleCreated) { UnregisterHotKey(Handle, StopHotkey); hotkey = false; }
        }
        base.Dispose(disposing);
    }
}
