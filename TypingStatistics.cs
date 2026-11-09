using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

// UI-thread owned aggregates. Never retain key names, text or individual event times.
internal sealed class TypingStatistics
{
    public sealed class Document
    {
        public int Version { get; set; }
        public Dictionary<string, long[]> Days { get; set; }
    }
    readonly string path;
    readonly bool memoryOnly;
    Dictionary<string, long[]> days = new Dictionary<string, long[]>();
    bool dirty, blocked, recovered;
    internal long Revision { get; private set; }
    internal string Error { get; private set; }
    internal string Notice { get; private set; }
    internal TypingStatistics(string dataRoot, bool memoryOnly = false)
    {
        path = Path.Combine(dataRoot, "data", "typing", "counts.json");
        this.memoryOnly = memoryOnly;
        if (memoryOnly) return;
        try
        {
            if (File.Exists(path)) days = Read(path);
            else if (File.Exists(path + ".bak"))
            { days = Read(path + ".bak"); dirty = true; Notice = "已从备份恢复统计。"; }
        }
        catch (Exception error)
        {
            if (!IsStorageError(error)) throw;
            try
            {
                days = Read(path + ".bak");
                Notice = "已从备份恢复统计；原文件已保留。";
                // Preserve a damaged primary before replacing it on the next save.
                File.Copy(path, path + ".damaged-" + Guid.NewGuid().ToString("N"));
                dirty = recovered = true;
            }
            catch (Exception backupError)
            {
                if (!IsStorageError(backupError)) throw;
                blocked = true;
                Error = "统计文件读取失败，原文件已保留；请恢复文件后重启。";
            }
        }
    }
    static bool IsStorageError(Exception error)
    {
        return error is IOException || error is UnauthorizedAccessException ||
            error is System.Security.SecurityException || error is ArgumentException || error is InvalidOperationException;
    }
    static Dictionary<string, long[]> Read(string file)
    {
        var json = new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 };
        var doc = json.Deserialize<Document>(File.ReadAllText(file, Encoding.UTF8));
        if (doc == null || doc.Version != 1 || doc.Days == null) throw new InvalidDataException("无效的打字统计文件。");
        long total = 0;
        foreach (var day in doc.Days)
        {
            DateTime date;
            if (!DateTime.TryParseExact(day.Key, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date) ||
                day.Value == null || day.Value.Length != 24 || day.Value.Any(n => n < 0))
                throw new InvalidDataException("无效的日期或小时计数。");
            foreach (long count in day.Value)
            {
                if (count > long.MaxValue - total) throw new InvalidDataException("计数超出范围。");
                total += count;
            }
        }
        return doc.Days;
    }
    static string Key(DateTime date) { return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }
    internal void Record(DateTime localAt)
    {
        if (blocked) return;
        long[] hours;
        string key = Key(localAt);
        if (!days.TryGetValue(key, out hours)) days.Add(key, hours = new long[24]);
        hours[localAt.Hour]++;
        dirty = true; Revision++;
    }
    internal long[] Hours(DateTime date)
    {
        long[] hours;
        return days.TryGetValue(Key(date), out hours) ? (long[])hours.Clone() : new long[24];
    }
    internal long Count(DateTime date) { long[] hours; return days.TryGetValue(Key(date), out hours) ? hours.Sum() : 0; }
    internal long Total { get { return days.Values.Sum(h => h.Sum()); } }
    internal long Between(DateTime first, DateTime last)
    {
        long count = 0;
        for (DateTime date = first.Date; date <= last.Date; date = date.AddDays(1))
        { count += Count(date); if (date == last.Date) break; }
        return count;
    }
    internal bool Save()
    {
        if (blocked) return true; // Nothing was accepted; a damaged history must not prevent exiting.
        if (memoryOnly || !dirty) return true;
        string temp = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var json = new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 };
            string contents = json.Serialize(new Document { Version = 1, Days = days });
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(contents);
                stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temp, path, recovered ? null : path + ".bak");
            else File.Move(temp, path);
            dirty = recovered = false; Error = null; return true;
        }
        catch (Exception error)
        {
            if (!IsStorageError(error)) throw;
            Error = "统计尚未保存，将自动重试：" + error.Message;
            return false;
        }
    }
}

internal sealed class TypingStatisticsWindow : KnightWindow
{
    readonly TypingStatistics statistics;
    readonly Func<string> listeningStatus;
    readonly Font uiFont = new Font("Microsoft YaHei UI", 14f, FontStyle.Regular, GraphicsUnit.Pixel);
    readonly Font summaryFont = new Font("Microsoft YaHei UI", 23f, FontStyle.Bold, GraphicsUnit.Pixel);
    readonly TypingHeatmap heatmap;
    readonly Label summary = new Label(), monthLabel = new Label(), detail = new Label(), status = new Label();
    readonly System.Windows.Forms.Timer refresh = new System.Windows.Forms.Timer();
    DateTime selected = DateTime.Today;
    long revision = -1;
    DateTime today;
    internal TypingStatisticsWindow(TypingStatistics statistics, Func<string> listeningStatus)
    {
        this.statistics = statistics; this.listeningStatus = listeningStatus;
        Text = "打字统计 · 白发小骑士";
        Font = uiFont;
        ClientSize = new Size(780, 800); MinimumSize = new Size(660, 740);
        StartPosition = FormStartPosition.CenterScreen;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22, 14, 22, 16), ColumnCount = 1, RowCount = 6 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (int height in new[] { 76, 44, 0, 110, 34, 52 })
            layout.RowStyles.Add(height == 0 ? new RowStyle(SizeType.Percent, 100) : new RowStyle(SizeType.Absolute, height));
        summary.Dock = DockStyle.Fill; summary.TextAlign = ContentAlignment.MiddleLeft;
        summary.Font = summaryFont;
        summary.AccessibleName = "打字次数汇总";
        layout.Controls.Add(summary, 0, 0);
        var navigation = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = Padding.Empty };
        navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
        navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
        var previous = new KnightButton { Text = "上个月", Dock = DockStyle.Fill, AccessibleName = "查看上个月" };
        var next = new KnightButton { Text = "下个月", Dock = DockStyle.Fill, AccessibleName = "查看下个月" };
        var current = new KnightButton { Text = "回到今天", Dock = DockStyle.Fill, Primary = true };
        monthLabel.Dock = DockStyle.Fill; monthLabel.TextAlign = ContentAlignment.MiddleCenter;
        navigation.Controls.Add(previous, 0, 0); navigation.Controls.Add(monthLabel, 1, 0);
        navigation.Controls.Add(next, 2, 0); navigation.Controls.Add(current, 3, 0);
        layout.Controls.Add(navigation, 0, 1);
        heatmap = new TypingHeatmap(statistics) { Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 4) };
        heatmap.DateSelected += date => { selected = date; UpdateView(true); };
        previous.Click += (s, e) => ChangeMonth(-1);
        next.Click += (s, e) => ChangeMonth(1);
        current.Click += (s, e) => { selected = DateTime.Today; UpdateView(true); };
        layout.Controls.Add(heatmap, 0, 2);
        detail.Dock = DockStyle.Fill; detail.TextAlign = ContentAlignment.MiddleLeft;
        detail.AccessibleName = "所选日期每小时打字次数";
        layout.Controls.Add(detail, 0, 3);
        status.Dock = DockStyle.Fill; status.TextAlign = ContentAlignment.MiddleLeft;
        status.AutoEllipsis = true;
        layout.Controls.Add(status, 0, 4);
        var note = new Label { Dock = DockStyle.Fill, ForeColor = KnightUi.Steel,
            Text = "单位：实际打字按键次数（不是成文文字数）。长按重复、快捷键和鼠标不计入。\n仅桌宠运行且键鼠跟随开启、非休息时计数；仅在本地保存汇总，不记录输入内容。" };
        layout.Controls.Add(note, 0, 5);
        Controls.Add(layout); layout.BringToFront();
        refresh.Interval = 1000; refresh.Tick += (s, e) => UpdateView(false); refresh.Start();
        UpdateView(true);
    }
    void ChangeMonth(int delta)
    {
        if (delta < 0 && selected.Year == 1 && selected.Month == 1 || delta > 0 && selected.Year == 9999 && selected.Month == 12) return;
        selected = new DateTime(selected.Year, selected.Month, 1).AddMonths(delta);
        UpdateView(true);
    }
    internal void UpdateView(bool force)
    {
        status.Text = statistics.Error ?? statistics.Notice ?? listeningStatus();
        status.ForeColor = statistics.Error == null ? KnightUi.Steel : KnightUi.Red;
        if (!force && revision == statistics.Revision && today == DateTime.Today) return;
        revision = statistics.Revision; today = DateTime.Today;
        summary.Text = "今日  " + statistics.Count(today).ToString("N0") + "     近 7 天  " +
            statistics.Between(today.AddDays(-6), today).ToString("N0") + "     累计  " + statistics.Total.ToString("N0");
        var first = new DateTime(selected.Year, selected.Month, 1);
        var last = first.AddDays(DateTime.DaysInMonth(first.Year, first.Month) - 1);
        monthLabel.Text = first.ToString("yyyy 年 M 月") + "  ·  " + statistics.Between(first, last).ToString("N0") + " 次";
        heatmap.SelectedDate = selected; heatmap.Invalidate();
        long[] hours = statistics.Hours(selected);
        var lines = new List<string>();
        for (int row = 0; row < 3; row++)
            lines.Add(String.Join("    ", Enumerable.Range(row * 8, 8).Select(h => h.ToString("00") + "时 " + hours[h].ToString("N0"))));
        detail.Text = selected.ToString("yyyy-MM-dd") + "  ·  " + hours.Sum().ToString("N0") + " 次\n" + String.Join("\n", lines);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) { refresh.Stop(); refresh.Dispose(); }
        base.Dispose(disposing);
        if (disposing) { uiFont.Dispose(); summaryFont.Dispose(); }
    }
}

internal sealed class TypingHeatmap : Control
{
    readonly TypingStatistics statistics;
    readonly ToolTip tips = new ToolTip();
    readonly List<KeyValuePair<Rectangle, DateTime>> cells = new List<KeyValuePair<Rectangle, DateTime>>();
    internal DateTime SelectedDate = DateTime.Today;
    internal event Action<DateTime> DateSelected;
    internal TypingHeatmap(TypingStatistics statistics)
    {
        this.statistics = statistics;
        DoubleBuffered = true; ResizeRedraw = true; TabStop = true;
        AccessibleName = "每月打字热力图，方向键选择日期";
        Cursor = Cursors.Hand;
    }
    internal static int Level(long count)
    { return count == 0 ? 0 : count < 100 ? 1 : count < 500 ? 2 : count < 2000 ? 3 : 4; }
    internal static Color HeatColor(int level)
    {
        if (SystemInformation.HighContrast) return level >= 3 ? SystemColors.Highlight : SystemColors.Window;
        return new[] { Color.FromArgb(235, 235, 234), Color.FromArgb(250, 221, 224),
            Color.FromArgb(237, 166, 174), Color.FromArgb(206, 80, 95), Color.FromArgb(159, 24, 43) }[level];
    }
    void DrawText(Graphics g, string text, Font font, Rectangle rect, Color color, TextFormatFlags align)
    { TextRenderer.DrawText(g, text, font, rect, color, align | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); cells.Clear();
        float scale = DeviceDpi / 96f;
        int header = (int)(27 * scale), legend = (int)(30 * scale), gap = Math.Max(3, (int)(5 * scale));
        float width = ClientSize.Width / 7f, height = Math.Max(1, (ClientSize.Height - header - legend) / 6f);
        var first = new DateTime(SelectedDate.Year, SelectedDate.Month, 1);
        int offset = ((int)first.DayOfWeek + 6) % 7, length = DateTime.DaysInMonth(first.Year, first.Month);
        using (var small = new Font(Font.FontFamily, 11f * scale, FontStyle.Regular, GraphicsUnit.Pixel))
        using (var number = new Font(Font.FontFamily, 14f * scale, FontStyle.Bold, GraphicsUnit.Pixel))
        {
            string[] names = { "一", "二", "三", "四", "五", "六", "日" };
            for (int col = 0; col < 7; col++) DrawText(e.Graphics, "周" + names[col], small,
                new Rectangle((int)(col * width), 0, (int)width, header), KnightUi.Steel, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            for (int day = 1; day <= length; day++)
            {
                DateTime date = first.AddDays(day - 1); int slot = offset + day - 1;
                var rect = new Rectangle((int)(slot % 7 * width) + gap / 2, header + (int)(slot / 7 * height) + gap / 2,
                    (int)width - gap, (int)height - gap);
                cells.Add(new KeyValuePair<Rectangle, DateTime>(rect, date));
                long count = statistics.Count(date); int level = Level(count);
                using (var fill = new SolidBrush(HeatColor(level))) e.Graphics.FillRectangle(fill, rect);
                Color ink = level >= 3 ? KnightUi.OnDark : KnightUi.Ink;
                if (date == SelectedDate.Date || date == DateTime.Today)
                    using (var edge = new Pen(date == SelectedDate.Date ? KnightUi.Ink : KnightUi.Red, date == SelectedDate.Date ? 2 : 1))
                        e.Graphics.DrawRectangle(edge, rect.X + 1, rect.Y + 1, Math.Max(1, rect.Width - 3), Math.Max(1, rect.Height - 3));
                DrawText(e.Graphics, day + (date == DateTime.Today ? " · 今天" : ""), small,
                    new Rectangle(rect.X + 7, rect.Y + 3, rect.Width - 10, (int)(19 * scale)), ink, TextFormatFlags.Left);
                // Exact totals remain visible, including zeros; never abbreviate to k/w.
                DrawText(e.Graphics, count.ToString("N0"), number,
                    new Rectangle(rect.X + 3, rect.Y + (int)(22 * scale), rect.Width - 6, Math.Max(1, rect.Height - (int)(22 * scale))),
                    ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            int y = ClientSize.Height - legend + (int)(7 * scale);
            DrawText(e.Graphics, "次数 / 天", small, new Rectangle(0, y, (int)(72 * scale), legend), KnightUi.Steel, TextFormatFlags.Left);
            string[] labels = { "0", "1–99", "100–499", "500–1,999", "2,000+" };
            float itemWidth = (ClientSize.Width - 74 * scale) / 5;
            for (int level = 0; level < 5; level++)
            {
                int x = (int)(74 * scale + level * itemWidth);
                using (var fill = new SolidBrush(HeatColor(level))) e.Graphics.FillRectangle(fill, x, y + 2, 12 * scale, 12 * scale);
                DrawText(e.Graphics, labels[level], small, new Rectangle(x + (int)(17 * scale), y, (int)itemWidth - (int)(17 * scale), legend), KnightUi.Steel, TextFormatFlags.Left);
            }
        }
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        string text = "点击日期查看每小时次数";
        foreach (var cell in cells) if (cell.Key.Contains(e.Location))
            text = cell.Value.ToString("yyyy-MM-dd") + "：" + statistics.Count(cell.Value).ToString("N0") + " 次";
        if (tips.GetToolTip(this) != text) tips.SetToolTip(this, text);
    }
    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e); if (e.Button != MouseButtons.Left) return; Focus();
        foreach (var cell in cells) if (cell.Key.Contains(e.Location) && DateSelected != null) { DateSelected(cell.Value); break; }
    }
    protected override bool IsInputKey(Keys keyData)
    { return keyData == Keys.Left || keyData == Keys.Right || keyData == Keys.Up || keyData == Keys.Down || base.IsInputKey(keyData); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        int delta = e.KeyCode == Keys.Left ? -1 : e.KeyCode == Keys.Right ? 1 : e.KeyCode == Keys.Up ? -7 : e.KeyCode == Keys.Down ? 7 : 0;
        if (delta == 0) return;
        if (SelectedDate.Ticks + delta * TimeSpan.TicksPerDay >= DateTime.MinValue.Ticks &&
            SelectedDate.Ticks + delta * TimeSpan.TicksPerDay <= DateTime.MaxValue.Ticks && DateSelected != null)
            DateSelected(SelectedDate.AddDays(delta));
        e.Handled = true;
    }
    protected override void Dispose(bool disposing) { if (disposing) tips.Dispose(); base.Dispose(disposing); }
}
