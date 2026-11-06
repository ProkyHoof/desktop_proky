using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

// Keep the Windows registration as the source of truth rather than a second
// preference in the JSON file. Quote the full path for folders containing spaces.
internal static class PetStorage
{
    internal static string UserRoot
    { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopPet"); } }

    // Copy legacy data once, without replacing anything already saved by a newer installation.
    // Original files remain available for recovery until the user removes them.
    internal static void Migrate(string legacyRoot, string userRoot)
    {
        Directory.CreateDirectory(userRoot);
        string marker = Path.Combine(userRoot, ".legacy-migrated");
        if (File.Exists(marker)) return;
        foreach (string name in new[] { "keyboard_pet_config.json", "input_bindings.tsv", "ai_chat_config.json", "ai_chat_config.json.bak" })
            CopyMissing(Path.Combine(legacyRoot, name), Path.Combine(userRoot, name));
        string calendar = Path.Combine(legacyRoot, "data", "calendar");
        if (Directory.Exists(calendar))
            foreach (string source in Directory.GetFiles(calendar, "*", SearchOption.AllDirectories))
                if (source.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || source.EndsWith(".json.bak", StringComparison.OrdinalIgnoreCase))
                    CopyMissing(source, Path.Combine(userRoot, "data", "calendar", source.Substring(calendar.Length + 1)));
        File.WriteAllText(marker, "Legacy import completed. Existing user files were preserved.");
    }
    static void CopyMissing(string source, string target)
    {
        if (!File.Exists(source) || File.Exists(target)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(target));
        File.Copy(source, target, false);
    }
}

internal sealed class PetStartupRegistration
{
    internal const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string ValueName = "DesktopPet_ProkyHoof";
    readonly string keyPath, command;
    internal PetStartupRegistration(string executablePath, string keyPath = RunKeyPath)
    {
        this.keyPath = keyPath;
        command = "\"" + Path.GetFullPath(executablePath) + "\"";
    }
    internal bool Enabled
    {
        get
        {
            using (var key = Registry.CurrentUser.OpenSubKey(keyPath))
                return key != null && String.Equals(key.GetValue(ValueName) as string, command, StringComparison.OrdinalIgnoreCase);
        }
    }
    internal void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(keyPath))
                key.SetValue(ValueName, command, RegistryValueKind.String);
        }
        else
        {
            using (var key = Registry.CurrentUser.OpenSubKey(keyPath, true))
                if (key != null) key.DeleteValue(ValueName, false);
        }
    }
    internal static bool IsAccessError(Exception error)
    {
        return error is IOException || error is UnauthorizedAccessException || error is System.Security.SecurityException;
    }
}

internal sealed class LayeredSurface : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct NativeSize { public int Width, Height; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] struct BlendFunction
    {
        public byte Operation, Flags, ConstantAlpha, AlphaFormat;
    }
    [StructLayout(LayoutKind.Sequential)] struct BitmapInfo
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, ImageSize;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ColorsUsed, ColorsImportant;
    }
    [DllImport("gdi32.dll", SetLastError = true)] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll", SetLastError = true)] static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll", SetLastError = true)] static extern IntPtr SelectObject(IntPtr dc, IntPtr handle);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr handle);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("user32.dll", SetLastError = true)] static extern bool UpdateLayeredWindow(IntPtr window, IntPtr destinationDC,
        ref NativePoint destination, ref NativeSize size, IntPtr sourceDC, ref NativePoint source, uint colorKey, ref BlendFunction blend, uint flags);

    IntPtr memoryDC, dib, previousBitmap;
    public Bitmap Bitmap { get; private set; }
    public Graphics Graphics { get; private set; }
    public LayeredSurface(int width, int height)
    {
        try
        {
            memoryDC = CreateCompatibleDC(IntPtr.Zero);
            if (memoryDC == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            var info = new BitmapInfo { Size = (uint)Marshal.SizeOf(typeof(BitmapInfo)), Width = width, Height = -height, Planes = 1, BitCount = 32 };
            IntPtr pixels;
            dib = CreateDIBSection(memoryDC, ref info, 0, out pixels, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            previousBitmap = SelectObject(memoryDC, dib);
            if (previousBitmap == IntPtr.Zero || previousBitmap == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
            Bitmap = new Bitmap(width, height, width * 4, PixelFormat.Format32bppPArgb, pixels);
            Bitmap.SetResolution(96, 96);
            Graphics = Graphics.FromImage(Bitmap);
        }
        catch { Dispose(); throw; }
    }
    public void Present(IntPtr window, Point location)
    {
        Graphics.Flush(FlushIntention.Sync);
        var destination = new NativePoint { X = location.X, Y = location.Y };
        var size = new NativeSize { Width = Bitmap.Width, Height = Bitmap.Height };
        var source = new NativePoint();
        var blend = new BlendFunction { ConstantAlpha = 255, AlphaFormat = 1 };
        if (!UpdateLayeredWindow(window, IntPtr.Zero, ref destination, ref size, memoryDC, ref source, 0, ref blend, 2))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    public void Dispose()
    {
        if (Graphics != null) { Graphics.Dispose(); Graphics = null; }
        if (Bitmap != null) { Bitmap.Dispose(); Bitmap = null; }
        if (memoryDC != IntPtr.Zero && previousBitmap != IntPtr.Zero && previousBitmap != new IntPtr(-1)) SelectObject(memoryDC, previousBitmap);
        previousBitmap = IntPtr.Zero;
        if (dib != IntPtr.Zero) { DeleteObject(dib); dib = IntPtr.Zero; }
        if (memoryDC != IntPtr.Zero) { DeleteDC(memoryDC); memoryDC = IntPtr.Zero; }
    }
}

internal sealed class KeyboardPetSprites : IDisposable
{
    internal LayeredRigAssets Rig;
    internal WholeArmSprite[] Arms = new WholeArmSprite[0];
    public KeyboardPetSprites(string root)
    {
        try
        {
            Arms=WholeArmSprite.LoadPair(root);
            Rig=new LayeredRigAssets(root);
        }
        catch { Dispose(); throw; }
    }
    public void Dispose()
    {
        if(Rig!=null)Rig.Dispose();
        foreach (var arm in Arms) if (arm != null) arm.Dispose();
    }
}

internal sealed class KeyboardPetWindow : Form
{
    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    readonly string root, dataRoot, configPath, bindingsPath;
    readonly bool preview;
    readonly PetStartupRegistration startup;
    readonly ToolStripMenuItem startupItem = new ToolStripMenuItem("开机自启动") { Tag = PetContextMenu.Glyph.Toggle };
    internal readonly InputFeedbackState inputState = new InputFeedbackState();
    readonly PetInputBindings bindings;
    readonly GlobalInputFeedback input;
    SystemMusicMonitor music;
    bool followMusic = true;
    readonly Stopwatch clock = Stopwatch.StartNew();
    readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
    readonly PetContextMenu menu = new PetContextMenu();
    readonly ToolStripMenuItem status = new ToolStripMenuItem("输入监听准备中") { Enabled = false, Tag = PetContextMenu.Glyph.Status };
    readonly ToolStripMenuItem musicStatus = new ToolStripMenuItem("音乐：等待声音播放") { Enabled = false, Tag = PetContextMenu.Glyph.Status };
    readonly ToolStripMenuItem restItem = new ToolStripMenuItem("闭眼休息") { CheckOnClick = true, Tag = PetContextMenu.Glyph.Toggle };
    readonly EventWaitHandle shutdown;
    KeyboardPetSprites sprites;
    internal BoundPetScene scene;
    LayeredSurface surface;
    bool follow = true, resting, pressed, dragging, released;
    Point pressCursor, pressLocation, lastLocation;
    int spriteWidth = 336;
    int hairStyle = PetHairStyles.Reference;
    bool bubbleKeys;
    PetCalendarWindow calendarWindow;
    PetChatWindow chatWindow;
    readonly PetSpeechBubble speechBubble = new PetSpeechBubble();
    double previous, nextReconcile, nextTopMostRefresh;
    float SceneScale { get { return spriteWidth / 336f; } }
    internal Rectangle SpriteBounds
    {
        get
        {
            RectangleF r = BoundPetScene.CharacterBounds;
            return Rectangle.Round(new RectangleF(r.X * SceneScale, r.Y * SceneScale, r.Width * SceneScale, r.Height * SceneScale));
        }
    }
    public KeyboardPetWindow(string root, bool preview = false, PetStartupRegistration startup = null, string dataRoot = null)
    {
        this.root = root; this.preview = preview; this.dataRoot = dataRoot ?? root;
        this.startup = startup ?? new PetStartupRegistration(Path.Combine(root, "DesktopPet.exe"));
        bindingsPath = Path.Combine(this.dataRoot, "input_bindings.tsv");
        bindings = PetInputBindings.Load(bindingsPath);
        inputState.MouseGain = bindings.MouseGain;
        input = new GlobalInputFeedback(inputState, () => clock.Elapsed.TotalSeconds);
        configPath = Path.Combine(this.dataRoot, "keyboard_pet_config.json");
        shutdown = new EventWaitHandle(false, EventResetMode.ManualReset, preview ? null : Program.ExitEventName);
        shutdown.Reset();
        Text = "白发小骑士 · 键鼠桌宠";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false; TopMost = true; StartPosition = FormStartPosition.Manual;
        string config = "";
        try { if (File.Exists(configPath)) config = File.ReadAllText(configPath); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        int size = ReadInt(config, "size", 336);
        spriteWidth = size == 252 || size == 420 ? size : 336;
        follow = ReadInt(config, "follow", 1) != 0;
        followMusic = ReadInt(config, "followMusic", 1) != 0;
        hairStyle = PetHairStyles.Normalize(ReadInt(config, "hairStyle", PetHairStyles.Reference));
        bubbleKeys = ReadInt(config, "bubbleKeys", 0) == 1;
        SetSpriteSize(spriteWidth);
        Rectangle work = Screen.PrimaryScreen.WorkingArea;
        Location = new Point(ReadInt(config, "x", work.Right - Width), ReadInt(config, "y", work.Bottom - Height - 12));
        KeepVisible();
        var followItem = new ToolStripMenuItem("跟随键鼠动作") { Checked = follow, CheckOnClick = true, Tag = PetContextMenu.Glyph.Toggle };
        followItem.CheckedChanged += (s, e) => { follow = followItem.Checked; UpdateInputSubscription(); };
        menu.Items.Add(followItem);
        restItem.CheckedChanged += (s, e) => { resting = restItem.Checked; UpdateInputSubscription(); };
        menu.Items.Add(restItem);
        var musicItem = new ToolStripMenuItem("随音乐律动") { Checked = followMusic, CheckOnClick = true, Tag = PetContextMenu.Glyph.Toggle };
        musicItem.CheckedChanged += (s, e) => { followMusic = musicItem.Checked; SaveConfig(); };
        menu.Items.Add(musicItem);
        menu.Items.Add("与她对话…", null, (s, e) => OpenChat(ChatScene())).Tag = PetContextMenu.Glyph.Settings;
        menu.Items.Add("AI 接口设置…", null, (s, e) => { EnsureChat(ChatScene()); chatWindow.OpenSettings(); }).Tag = PetContextMenu.Glyph.Settings;
        var restartChat = new ToolStripMenuItem("重新开始对话", null, (s, e) => { EnsureChat(ChatScene()); chatWindow.ResetConversation(); }) { Tag = PetContextMenu.Glyph.Settings };
        menu.Items.Add(restartChat);
        menu.Opening += (s, e) => restartChat.Enabled = chatWindow == null || chatWindow.IsDisposed || !chatWindow.Busy;
        menu.Items.Add("日历计划…", null, (s, e) => OpenCalendar()).Tag = PetContextMenu.Glyph.Settings;
        menu.Items.Add("按键与鼠标设置…", null, (s, e) =>
        {
            try { using (var editor = new PetBindingEditor(bindings, bindingsPath)) editor.ShowDialog(this); }
            finally { inputState.MouseGain = bindings.MouseGain; UpdateInputSubscription(); }
        }).Tag = PetContextMenu.Glyph.Settings;
        menu.Items.Add(new ToolStripSeparator());
        var sizes = new ToolStripMenuItem("桌宠大小") { Tag = PetContextMenu.Glyph.Size };
        foreach (int value in new[] { 252, 336, 420 })
        {
            int captured = value;
            var item = new ToolStripMenuItem(value == 252 ? "小" : value == 336 ? "中" : "大") { Checked = spriteWidth == value, Tag = PetContextMenu.Glyph.Choice };
            item.Click += (s, e) =>
            {
                Point anchor = new Point(Right, Bottom);
                SetSpriteSize(captured); Location = new Point(anchor.X - Width, anchor.Y - Height); KeepVisible();
                foreach (ToolStripMenuItem option in sizes.DropDownItems) option.Checked = option == item;
            };
            sizes.DropDownItems.Add(item);
        }
        menu.Items.Add(sizes);
        var hairstyles = new ToolStripMenuItem("选择发型") { Tag = PetContextMenu.Glyph.Hair };
        foreach (int value in new[] { PetHairStyles.Short, PetHairStyles.Reference })
        {
            int captured = value;
            var item = new ToolStripMenuItem(value == PetHairStyles.Short ? "利落短发" : "参考图长发 · 层次外翘") { Checked = hairStyle == value, Tag = PetContextMenu.Glyph.Choice };
            item.Click += (s, e) =>
            {
                hairStyle = captured; scene.Portrait.SetHairStyle(hairStyle);
                foreach (ToolStripMenuItem option in hairstyles.DropDownItems) option.Checked = option == item;
                SaveConfig(); Present(true);
            };
            hairstyles.DropDownItems.Add(item);
        }
        menu.Items.Add(hairstyles);
        var keyDisplay = new ToolStripMenuItem("按键显示方式") { Tag = PetContextMenu.Glyph.Keys };
        foreach (bool bubbles in new[] { false, true })
        {
            bool captured = bubbles;
            var item = new ToolStripMenuItem(bubbles ? "冒泡显示" : "固定框显示") { Checked = bubbleKeys == bubbles, Tag = PetContextMenu.Glyph.Choice };
            item.Click += (s, e) =>
            {
                bubbleKeys = captured; inputState.SetBubbleMode(bubbleKeys);
                foreach (ToolStripMenuItem option in keyDisplay.DropDownItems) option.Checked = option == item;
                SaveConfig(); Present(true);
            };
            keyDisplay.DropDownItems.Add(item);
        }
        menu.Items.Add(keyDisplay);
        var top = new ToolStripMenuItem("保持置顶") { Checked = true, CheckOnClick = true, Tag = PetContextMenu.Glyph.Toggle };
        top.CheckedChanged += (s, e) => { TopMost = top.Checked; nextTopMostRefresh = 0; RestoreTopMost(); }; menu.Items.Add(top);
        RefreshStartupItem();
        startupItem.Click += (s, e) =>
        {
            if (preview) return;
            try { this.startup.SetEnabled(!startupItem.Checked); }
            catch (Exception error)
            {
                if (!PetStartupRegistration.IsAccessError(error)) throw;
                KnightUi.ShowError(this, "自启动设置失败", "无法更新开机自启动设置，请检查当前用户的权限后重试。\n" + error.Message);
            }
            RefreshStartupItem();
        };
        menu.Items.Add(startupItem);
        menu.Opening += (s, e) => RefreshStartupItem();
        menu.Items.Add("回到屏幕右下角", null, (s, e) =>
        {
            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(area.Right - Width, area.Bottom - Height - 12); KeepVisible();
        }).Tag = PetContextMenu.Glyph.Corner;
        menu.Items.Add(new ToolStripSeparator()); menu.Items.Add(status);
        menu.Items.Add(musicStatus);
        menu.Items.Add("退出", null, (s, e) => Close()).Tag = PetContextMenu.Glyph.Exit;
        menu.ApplyTheme();
        timer.Interval = 66; timer.Tick += Tick; timer.Start();
    }
    void RefreshStartupItem()
    {
        // Preview/self-test windows must never change the user's startup entry.
        startupItem.Enabled = !preview;
        startupItem.ToolTipText = preview ? "预览模式不修改开机自启动设置" : "登录 Windows 后自动启动桌宠";
        try { startupItem.Checked = startup.Enabled; }
        catch (Exception error)
        {
            if (!PetStartupRegistration.IsAccessError(error)) throw;
            startupItem.Checked = false; startupItem.Enabled = false;
            startupItem.ToolTipText = "无法读取开机自启动设置：" + error.Message;
        }
    }
    static int ReadInt(string config, string name, int fallback)
    {
        Match m = Regex.Match(config, "\"" + name + "\"\\s*:\\s*(-?[0-9]+)");
        int value;
        return m.Success && Int32.TryParse(m.Groups[1].Value, out value) ? value : fallback;
    }
    void SetSpriteSize(int width)
    {
        var replacement = new KeyboardPetSprites(root);
        BoundPetScene replacementScene;
        try { replacementScene = new BoundPetScene(replacement, bindings, inputState); }
        catch { replacement.Dispose(); throw; }
        if (scene != null) scene.Dispose();
        if (sprites != null) sprites.Dispose();
        sprites = replacement; scene = replacementScene; spriteWidth = width;
        scene.Portrait.SetHairStyle(hairStyle);
        inputState.SetBubbleMode(bubbleKeys);
        scene.SetViewWidth(BoundPetScene.CompactWidth);
        Size = new Size((int)Math.Round(scene.ViewWidth * SceneScale), (int)Math.Round(BoundPetScene.WorldHeight * SceneScale));
        if (surface != null) { surface.Dispose(); surface = null; }
        scene.Advance(clock.Elapsed.TotalSeconds, 1, resting);
    }
    void KeepVisible()
    {
        Rectangle area = Screen.FromRectangle(Bounds).WorkingArea;
        Location = new Point(Math.Max(area.Left, Math.Min(Left, area.Right - Width)),
                             Math.Max(area.Top, Math.Min(Top, area.Bottom - Height)));
    }
    string ChatScene()
    {
        if (resting) return "rest";
        if (scene.MusicPlaying) return "music";
        if (inputState.Work.Mood == WorkMood.Relieved) return "relieved";
        if (inputState.Work.Mood != WorkMood.Idle) return "typing";
        return "idle";
    }
    void OpenChat(string context)
    {
        EnsureChat(context);
        chatWindow.TopMost = TopMost;
        chatWindow.PlaceBeside(Bounds, Screen.FromRectangle(Bounds).WorkingArea);
        if (!chatWindow.Visible) chatWindow.Show(this);
        chatWindow.SayRandomOpening();
        chatWindow.Activate(); chatWindow.FocusInput();
    }
    void EnsureChat(string context)
    {
        if (chatWindow == null || chatWindow.IsDisposed)
        {
            var chat = new PetChatWindow(dataRoot, preview, context);
            chatWindow = chat;
            chat.Owner = this; chat.TopMost = TopMost;
            chat.SceneContext = ChatScene;
            chat.ReplyReady += Say;
            chat.FormClosed += (s, e) => { if (chatWindow == chat) chatWindow = null; };
        }
    }
    internal void Say(string text)
    {
        if (released) return;
        speechBubble.Say(text, clock.Elapsed.TotalSeconds); scene.DialogueActive = speechBubble.Visible;
        scene.Interact(new PointF(170, 160), clock.Elapsed.TotalSeconds); Present(true);
    }
    protected override void OnLocationChanged(EventArgs e)
    {
        base.OnLocationChanged(e); PositionChat();
    }
    void PositionChat()
    {
        if (chatWindow == null || chatWindow.IsDisposed || !chatWindow.Visible) return;
        chatWindow.TopMost = TopMost;
        chatWindow.PlaceBeside(Bounds, Screen.FromRectangle(Bounds).WorkingArea);
    }
    void OpenCalendar()
    {
        if (calendarWindow != null && !calendarWindow.IsDisposed)
        {
            if (calendarWindow.WindowState == FormWindowState.Minimized) calendarWindow.WindowState = FormWindowState.Normal;
            calendarWindow.Show(); calendarWindow.Activate(); return;
        }
        var calendar = new PetCalendarWindow(dataRoot, preview);
        calendarWindow = calendar;
        calendar.FormClosed += (s, e) =>
        {
            if (calendarWindow == calendar) calendarWindow = null;
            if (!released && !Disposing && !IsDisposed) UpdateInputSubscription();
        };
        UpdateInputSubscription();
        try
        {
            // A separate, modeless window leaves the pet enabled and draggable.
            // It also avoids inheriting the pet's always-on-top window group.
            calendar.Show();
        }
        catch
        {
            calendarWindow = null; calendar.Dispose(); UpdateInputSubscription(); throw;
        }
    }
    void UpdateInputSubscription()
    {
        inputState.MouseGain = bindings.MouseGain;
        status.Text = resting ? "休息中" : !follow ? "键鼠跟随已暂停" : "输入监听准备中";
        // Auxiliary windows must not interrupt the global input worker or discard
        // pending keystrokes. Only the explicit follow/rest switches stop it.
        if (!Visible || !follow || resting)
        {
            input.Dispose(); inputState.Clear();
            return;
        }
        try { input.Enable(); status.Text = "键鼠实时绑定已开启"; }
        catch (Win32Exception error) { status.Text = "输入监听未开启（错误 " + error.NativeErrorCode + "）"; }
    }
    void Tick(object sender, EventArgs e)
    {
        if (shutdown.WaitOne(0)) { shutdown.Reset(); Close(); return; }
        long started=Stopwatch.GetTimestamp();
        double now = clock.Elapsed.TotalSeconds, dt = Math.Min(.1, Math.Max(0, now - previous)); previous = now;
        PositionChat();
        if (now >= nextTopMostRefresh) { RestoreTopMost(); nextTopMostRefresh = now + .5; }
        input.DrainPending();
        Point pointer=PointToClient(Cursor.Position);
        scene.ObservePointer(new PointF(pointer.X/SceneScale,pointer.Y/SceneScale),now,follow&&!resting);
        if (input.Enabled && now >= nextReconcile) { input.ReconcileReleased(); nextReconcile = now + .25; }
        scene.MusicPlaying = followMusic && music != null && music.Playing;
        scene.MusicLevel = music == null ? 0 : music.Peak;
        musicStatus.Text = !followMusic ? "音乐：律动已关闭" : resting ? "音乐：休息中" : music == null ? "音乐：等待声音播放" :
            music.LastError != 0 ? "音乐：暂时无法检测声音" : !music.Available ? "音乐：未找到声音输出设备" : music.Playing ? "音乐：播放中 · 正在律动" : "音乐：等待声音播放";
        bool speechChanged = speechBubble.Advance(now); scene.DialogueActive = speechBubble.Visible;
        bool changed = scene.Advance(now, dt, resting); Present(changed || speechChanged);
        bool active=dragging||scene.Portrait.MusicBlend>.02f||inputState.HasKeyboardActivity(now)||inputState.HasMouse(now)||scene.Hands.TypingBlend>.02f;
        double elapsedMs=(Stopwatch.GetTimestamp()-started)*1000.0/Stopwatch.Frequency;
        // Idle/rest animation needs fewer frames. On a slow machine leave
        // at least twice the frame's CPU time free instead of chasing 60 fps.
        timer.Interval=Math.Min(250,Math.Max(resting ? 166 : active ? 33 : 66,(int)Math.Ceiling(elapsedMs*2)));
    }
    void RestoreTopMost()
    {
        if (!TopMost || !Visible || !Enabled || !IsHandleCreated || IsDisposed || Disposing || menu.Visible) return;
        // Other topmost applications can move above us without changing TopMost.
        // Reassert native Z order without activation, movement or resizing. Leave
        // our own context menu and modal editor above the pet while they are open.
        SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010 | 0x0200);
    }
    protected override bool ShowWithoutActivation { get { return !preview; } }
    protected override CreateParams CreateParams
    {
        get { var p = base.CreateParams; p.ExStyle |= 0x80000; if (!preview) p.ExStyle |= 0x08000000 | 0x80; return p; }
    }
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (!preview && music == null) music = new SystemMusicMonitor();
        RestoreTopMost(); UpdateInputSubscription(); Present(true);
    }
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e) { DrawScene(e.Graphics); }
    internal bool Hit(Point point)
    {
        var world = new PointF(point.X / SceneScale, point.Y / SceneScale);
        return speechBubble.Hit(world) || scene.Hit(world, resting, follow);
    }
    internal void DrawScene(Graphics g) { DrawSceneAt(g, clock.Elapsed.TotalSeconds); }
    internal void DrawSceneAt(Graphics g, double now)
    {
        var saved = g.Save();
        try { g.ScaleTransform(SceneScale, SceneScale); scene.DialogueActive = speechBubble.Visible; scene.Draw(g, now, resting, follow); speechBubble.Draw(g); }
        finally { g.Restore(saved); }
    }
    void Present(bool changed)
    {
        if (!Visible || !IsHandleCreated || IsDisposed || Disposing) return;
        if (surface != null && !changed && Location == lastLocation) return;
        if (surface == null) surface = new LayeredSurface(Width, Height);
        surface.Graphics.Clear(Color.Transparent); DrawScene(surface.Graphics); surface.Present(Handle, Location); lastLocation = Location;
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        var world = new PointF(e.X / SceneScale, e.Y / SceneScale);
        if (e.Button == MouseButtons.Left && speechBubble.Hit(world))
        {
            speechBubble.Touch(clock.Elapsed.TotalSeconds); speechBubble.Click(world); scene.DialogueActive = speechBubble.Visible; Present(true); return;
        }
        if (!Hit(e.Location)) return;
        if (e.Button == MouseButtons.Right)
        {
            menu.Show(this, PointToClient(MenuAnchor(Screen.FromRectangle(Bounds).WorkingArea))); return;
        }
        if (e.Button != MouseButtons.Left) return;
        pressed = true; dragging = false; pressCursor = Cursor.Position; pressLocation = Location; Capture = true;
    }
    internal Point MenuAnchor(Rectangle area)
    {
        // Anchor to the pet, independent of the pixel that was right-clicked.
        int gap=(int)Math.Round(8*DeviceDpi/96f);
        int x=Left-menu.Width-gap;
        if(x<area.Left)x=Right+gap;
        x=Math.Max(area.Left,Math.Min(x,area.Right-menu.Width));
        int y=Math.Max(area.Top,Math.Min(Top,area.Bottom-menu.Height));
        return new Point(x,y);
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!pressed) { Cursor = Hit(e.Location) ? Cursors.Hand : Cursors.Default; return; }
        Point cursor = Cursor.Position; int dx = cursor.X - pressCursor.X, dy = cursor.Y - pressCursor.Y;
        if (Math.Abs(dx) > SystemInformation.DragSize.Width / 2 || Math.Abs(dy) > SystemInformation.DragSize.Height / 2) dragging = true;
        if (dragging) { Cursor = Cursors.SizeAll; Location = new Point(pressLocation.X + dx, pressLocation.Y + dy); Present(false); }
    }
    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left || !pressed) return;
        bool openChat = false;
        string context = ChatScene();
        if (dragging) KeepVisible();
        else if (Hit(e.Location))
        {
            speechBubble.Touch(clock.Elapsed.TotalSeconds);
            scene.Interact(new PointF(e.X/SceneScale,e.Y/SceneScale),clock.Elapsed.TotalSeconds);
            resting = false; restItem.Checked = false;
            openChat = true;
        }
        pressed = dragging = false; Capture = false; Present(true);
        if (openChat)
        {
            OpenChat(context);
        }
    }
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (speechBubble.Hit(new PointF(e.X / SceneScale, e.Y / SceneScale)))
        { speechBubble.Touch(clock.Elapsed.TotalSeconds); speechBubble.Turn(e.Delta < 0 ? 1 : -1); Present(true); }
        base.OnMouseWheel(e);
    }
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        if (!Capture) { pressed = dragging = false; } base.OnMouseCaptureChanged(e);
    }
    void SaveConfig()
    {
        if (!preview)
        {
            try { File.WriteAllText(configPath, String.Format(CultureInfo.InvariantCulture,
                "{{\"x\":{0},\"y\":{1},\"size\":{2},\"follow\":{3},\"hairStyle\":{4},\"bubbleKeys\":{5},\"followMusic\":{6}}}", Left, Top, spriteWidth, follow ? 1 : 0, hairStyle, bubbleKeys ? 1 : 0, followMusic ? 1 : 0)); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // The independent calendar must flush edits before the application exits.
        // Its existing save-failure guard can cancel both window closures.
        if (calendarWindow != null && !calendarWindow.IsDisposed)
        {
            var calendar = calendarWindow;
            calendar.Close();
            if (!calendar.IsDisposed) e.Cancel = true;
        }
        base.OnFormClosing(e);
    }
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        SaveConfig(); base.OnFormClosed(e);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing && !released)
        {
            released = true; timer.Stop(); timer.Dispose(); input.Dispose(); inputState.Clear(); menu.Dispose(); shutdown.Dispose();
            if (calendarWindow != null) { calendarWindow.Dispose(); calendarWindow = null; }
            if (chatWindow != null) { chatWindow.Dispose(); chatWindow = null; }
            speechBubble.Dispose();
            if (music != null) { music.Dispose(); music = null; }
            if (surface != null) surface.Dispose();
            if (scene != null) scene.Dispose();
            if (sprites != null) sprites.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal static class KeyboardPetTests
{
    static void Check(bool condition, string message) { if (!condition) throw new Exception("键鼠桌宠自检：" + message); }
    internal static void Run(string root)
    {
        InputFeedbackTests.Run();
        BoundPetTests.Run(root);
        using (var pet = new KeyboardPetWindow(root, true))
        using (var buffer = new Bitmap(pet.Width, pet.Height, PixelFormat.Format32bppArgb))
        using (var g = Graphics.FromImage(buffer))
        using (var native = new LayeredSurface(pet.Width, pet.Height))
        {
            Check(!pet.Hit(new Point(0, 0)) && !pet.Hit(new Point(-1, 50)), "透明区域与边界点击");
            Rectangle b = pet.SpriteBounds;
            Check(pet.Hit(new Point(b.X + b.Width / 2, b.Y + b.Height / 2)), "人物点击");
            pet.DrawScene(g);
            Check(buffer.GetPixel(0, 0).A == 0, "合成背景");
            pet.DrawScene(native.Graphics); native.Present(pet.Handle, pet.Location);
            pet.inputState.Key(162, true, 0); pet.inputState.Key(160, true, 0); pet.inputState.Key(83, true, 0);
            pet.inputState.Button(1, true, 0); pet.inputState.Wheel(120, false, 0);
            for (int i=1;i<=40;i++) pet.scene.Advance(i*.016,.016,false);
            g.Clear(Color.Transparent); pet.DrawSceneAt(g, .64);
        }
    }
}

internal static class Program
{
    internal const string ExitEventName = "DesktopPet_Exit_ProkyHoof";
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [STAThread] static int Main(string[] args)
    {
        string root = AppDomain.CurrentDomain.BaseDirectory;
        try
        {
            if (args.Length > 1 || args.Length == 1 && args[0] != "--exit" && args[0] != "--self-test" && args[0] != "--preview" && args[0] != "--migrate-data")
                throw new ArgumentException("未识别的启动参数。");
            if (args.Length == 1 && args[0] == "--migrate-data")
            {
                PetStorage.Migrate(root, PetStorage.UserRoot);
                return 0;
            }
            if (args.Length == 1 && args[0] == "--exit")
            {
                EventWaitHandle signal;
                if (EventWaitHandle.TryOpenExisting(ExitEventName, out signal))
                    using (signal) signal.Set();
                return 0;
            }
            if (args.Length == 1 && args[0] == "--self-test")
            {
                KeyboardPetTests.Run(root);
                return 0;
            }
            if (args.Length == 1 && args[0] == "--preview")
            {
                SetProcessDPIAware(); Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (var preview = new KeyboardPetWindow(root, true))
                {
                    preview.ShowInTaskbar = true; preview.Text = "桌宠预览";
                    Application.Run(preview);
                }
                return 0;
            }
            bool created;
            using (var mutex = new Mutex(true, "DesktopPet_FrameAnimation_ProkyHoof", out created))
            {
                if (!created) return 0;
                SetProcessDPIAware(); Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                PetStorage.Migrate(root, PetStorage.UserRoot);
                using (var pet = new KeyboardPetWindow(root, dataRoot: PetStorage.UserRoot)) Application.Run(pet);
            }
            return 0;
        }
        catch (Exception error)
        {
            if (args.Length == 1 && args[0] == "--self-test") { Console.Error.WriteLine(error); return 1; }
            KnightUi.ShowError(null, "桌宠启动失败", error.Message);
            return 1;
        }
    }
}
