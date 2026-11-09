using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows.Forms;

internal static class TypingStatisticsTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [StructLayout(LayoutKind.Sequential)] struct TestKey { internal uint Key, Scan, Flags, Time; internal UIntPtr Extra; }
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    static void Check(bool value, string message) { if (!value) throw new Exception("打字统计：" + message); }
    static void Tap(InputFeedbackState input, int key, DateTime at)
    { input.Key(key, true, 1, at); input.Key(key, false, 1.01, at); }
    static void QueueKey(GlobalInputFeedback hook, int message, uint flags, DateTime at)
    {
        IntPtr memory = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(TestKey)));
        try
        {
            Marshal.StructureToPtr(new TestKey { Key = 65, Flags = flags }, memory, false);
            typeof(GlobalInputFeedback).GetMethod("Keyboard", Private).Invoke(hook, new object[] { 0, new IntPtr(message), memory });
            var pending = (IList)typeof(GlobalInputFeedback).GetField("pending", Private).GetValue(hook);
            object item = pending[pending.Count - 1];
            item.GetType().GetField("LocalAt", Private).SetValue(item, at); pending[pending.Count - 1] = item;
        }
        finally { Marshal.FreeHGlobal(memory); }
    }
    internal static void Run(string assetsRoot, string previewPath)
    {
        SetProcessDPIAware();
        string root = Path.Combine(Path.GetTempPath(), "DesktopPet-typing-" + Guid.NewGuid().ToString("N"));
        string file = Path.Combine(root, "data", "typing", "counts.json");
        try
        {
            var statistics = new TypingStatistics(root);
            var input = new InputFeedbackState(); input.TypingPressed += statistics.Record;
            DateTime before = new DateTime(2026, 11, 9, 23, 59, 59), after = before.AddSeconds(2);
            input.Key(65, true, 0, before);
            for (int i = 0; i < 10; i++) input.Key(65, true, .01 + i * .01, before);
            input.Key(65, false, .2, before);
            Check(statistics.Total == 1, "长按的自动重复不重复累计");
            input.Key(160, true, 1, before); Tap(input, 66, before); input.Key(160, false, 2, before);
            foreach (int modifier in new[] { 162, 164, 91 })
            { input.Key(modifier, true, 1, before); Tap(input, 83, before); input.Key(modifier, false, 2, before); }
            foreach (int key in new[] { 37, 112, 9 }) Tap(input, key, before);
            foreach (int key in new[] { 32, 13, 269, 8, 186, 96 }) Tap(input, key, after);
            input.Button(1, true, 2); input.Button(1, false, 3);
            Check(statistics.Count(before) == 2 && statistics.Count(after) == 6 && statistics.Total == 8,
                "Shift 打字计入；快捷键、修饰键、方向键、功能键和鼠标排除；空格/回车/退格/标点/数字区计入");
            input.Clear(); Check(statistics.Total == 8, "暂停和清空动作状态不会清空历史");
            input.Key(65, true, 1, before, false); input.Clear();
            Check(statistics.Total == 8, "恢复监听时已按住的键不计数");
            using (var hook = new GlobalInputFeedback(input, () => 3))
            {
                QueueKey(hook, 0x100, 0, before); QueueKey(hook, 0x101, 0, before);
                QueueKey(hook, 0x100, 0, after); QueueKey(hook, 0x101, 0, after);
                QueueKey(hook, 0x100, 0x10, after); QueueKey(hook, 0x101, 0x10, after);
                Check(statistics.Total == 8, "回调仅入队"); hook.DrainPending();
            }
            Check(statistics.Hours(before)[23] == 3 && statistics.Hours(after)[0] == 7,
                "延迟处理按事件当地时间跨日分桶，注入的模拟按键不计数");
            Check(statistics.Between(before.Date, after.Date) == 10 && statistics.Count(before.AddDays(-1)) == 0, "区间汇总和无记录日期");
            Check(statistics.Save(), "首次保存");
            string contents = File.ReadAllText(file);
            Check(!contents.Contains("Key") && !contents.Contains("Label") && contents.Contains("2026-11-09"), "仅保存日期和小时汇总");
            var restored = new TypingStatistics(root);
            Check(restored.Total == 10 && restored.Hours(before)[23] == 3, "重启恢复计数");
            long[] copy = restored.Hours(before); copy[23] = 999;
            Check(restored.Count(before) == 3, "读取明细不能修改内部历史");
            restored.Record(after); Check(restored.Save(), "更新保存与备份");
            File.WriteAllText(file, "broken-json");
            var recovered = new TypingStatistics(root);
            Check(recovered.Total == 10 && recovered.Notice != null && recovered.Save(), "损坏时从上一份有效备份恢复");
            Check(Directory.GetFiles(Path.GetDirectoryName(file), "*.damaged-*").Length == 1, "损坏原文件留存");
            Check(new TypingStatistics(root).Total == 10, "恢复的主文件可再次读取");
            File.WriteAllText(file, "broken"); File.WriteAllText(file + ".bak", "broken");
            var blocked = new TypingStatistics(root); blocked.Record(after);
            Check(blocked.Error != null && blocked.Total == 0 && blocked.Save() && File.ReadAllText(file) == "broken", "两份损坏时不覆盖历史且不阻止退出");
            string retryRoot = Path.Combine(root, "retry"); Directory.CreateDirectory(retryRoot);
            File.WriteAllText(Path.Combine(retryRoot, "data"), "directory-blocker");
            var retry = new TypingStatistics(retryRoot); retry.Record(before);
            Check(!retry.Save() && retry.Error != null && retry.Total == 1, "保存失败仍保留内存计数");
            File.Delete(Path.Combine(retryRoot, "data"));
            Check(retry.Save() && retry.Error == null && new TypingStatistics(retryRoot).Total == 1, "恢复可写后保存重试成功");
            var preview = new TypingStatistics(Path.Combine(root, "preview"), true); preview.Record(before); preview.Save();
            Check(!Directory.Exists(Path.Combine(root, "preview")), "预览不写入数据");
            Check(TypingHeatmap.Level(0) == 0 && TypingHeatmap.Level(1) == 1 && TypingHeatmap.Level(100) == 2 &&
                TypingHeatmap.Level(500) == 3 && TypingHeatmap.Level(2000) == 4, "图例阈值与颜色分档一致");
            Check(preview.Between(new DateTime(9999, 12, 1), DateTime.MaxValue) == 0, "最大月份边界");
            using (var hook = new GlobalInputFeedback(input, () => 4))
            {
                long previousTotal = statistics.Total;
                QueueKey(hook, 0x100, 0, after); QueueKey(hook, 0x101, 0, after);
                hook.StopAndDrain();
                Check(statistics.Total == previousTotal + 1 && hook.PendingCount == 0, "停止监听前的待处理打字会结算");
            }
            RenderAndCheck(root, assetsRoot, previewPath);
            InputFeedbackTests.Run(); TypingWorkTests.Run();
            Console.WriteLine("PASS: typing counts, key filtering, event-time rollover, hourly/day totals, atomic persistence, backup recovery, retry, preview isolation, heatmap and window lifecycle; input/rhythm regressions.");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    static void RenderAndCheck(string root, string assetsRoot, string previewPath)
    {
        DateTime today = DateTime.Today, first = new DateTime(today.Year, today.Month, 1);
        string sampleRoot = Path.Combine(root, "sample"), file = Path.Combine(sampleRoot, "data", "typing", "counts.json");
        var days = new Dictionary<string, long[]>();
        for (int day = 1; day <= DateTime.DaysInMonth(today.Year, today.Month); day++)
        {
            var hours = new long[24];
            if (day <= today.Day) { hours[9] = day % 5 == 0 ? 0 : day * 19; hours[14] = day % 4 == 0 ? 2000 + day * 101 : day * 37; }
            days.Add(first.AddDays(day - 1).ToString("yyyy-MM-dd"), hours);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(file));
        File.WriteAllText(file, new JavaScriptSerializer().Serialize(new TypingStatistics.Document { Version = 1, Days = days }));
        var stats = new TypingStatistics(sampleRoot);
        using (var window = new TypingStatisticsWindow(stats, () => "示例数据 · 本地统计"))
        {
            window.Show(); Application.DoEvents();
            var heatmap = (TypingHeatmap)typeof(TypingStatisticsWindow).GetField("heatmap", Private).GetValue(window);
            using (var bitmap = new Bitmap(window.Width, window.Height))
            {
                window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                if (!String.IsNullOrEmpty(previewPath)) { Directory.CreateDirectory(Path.GetDirectoryName(previewPath)); bitmap.Save(previewPath, ImageFormat.Png); }
            }
            var cells = (IList)typeof(TypingHeatmap).GetField("cells", Private).GetValue(heatmap);
            Check(cells.Count == DateTime.DaysInMonth(today.Year, today.Month), "月热力图天数正确");
            typeof(TypingStatisticsWindow).GetMethod("ChangeMonth", Private).Invoke(window, new object[] { -1 });
            Check(heatmap.SelectedDate == first.AddMonths(-1), "上个月导航更新热力图");
            typeof(TypingStatisticsWindow).GetMethod("ChangeMonth", Private).Invoke(window, new object[] { 1 });
            Check(heatmap.SelectedDate == first, "下个月导航更新热力图");
            Application.DoEvents();
            int last = cells.Count - 1;
            var cell = (KeyValuePair<Rectangle, DateTime>)cells[last];
            typeof(TypingHeatmap).GetMethod("OnMouseClick", Private).Invoke(heatmap,
                new object[] { new MouseEventArgs(MouseButtons.Left, 1, cell.Key.X + 5, cell.Key.Y + 5, 0) });
            Check(heatmap.SelectedDate == cell.Value, "点击日期更新选中与小时明细");
            stats.Record(today); window.UpdateView(false);
            foreach (var date in new[] { new DateTime(2024, 2, 29), new DateTime(2026, 2, 1), new DateTime(2026, 8, 31) })
            {
                typeof(TypingStatisticsWindow).GetField("selected", Private).SetValue(window, date); window.UpdateView(true);
                window.ClientSize = new Size(660, 740);
                using (var bitmap = new Bitmap(window.Width, window.Height)) window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                Check(cells.Count == DateTime.DaysInMonth(date.Year, date.Month), "闰年/平年/六周月份与小窗口布局");
            }
            window.Close();
        }
        using (var pet = new KeyboardPetWindow(assetsRoot, true, dataRoot:Path.Combine(root, "pet")))
        {
            var timer = (System.Windows.Forms.Timer)typeof(KeyboardPetWindow).GetField("timer", Private).GetValue(pet);
            timer.Stop(); pet.Show(); Application.DoEvents();
            var menu = (PetContextMenu)typeof(KeyboardPetWindow).GetField("menu", Private).GetValue(pet);
            menu.Items.Commands().Single(i => i.Text == "跟随键鼠动作").Checked = true;
            var entry = menu.Items.Commands().Single(i => i.Text == "打字统计…");
            entry.PerformClick(); Application.DoEvents();
            var field = typeof(KeyboardPetWindow).GetField("typingStatisticsWindow", Private);
            var firstWindow = (TypingStatisticsWindow)field.GetValue(pet);
            var hook = (GlobalInputFeedback)typeof(KeyboardPetWindow).GetField("input", Private).GetValue(pet);
            Check(hook.Enabled && !firstWindow.Modal && pet.Enabled, "统计窗口不阻塞桌宠或键盘监听");
            var petStats = (TypingStatistics)typeof(KeyboardPetWindow).GetField("typingStatistics", Private).GetValue(pet);
            long previousTotal = petStats.Total;
            QueueKey(hook, 0x100, 0, today); QueueKey(hook, 0x101, 0, today);
            var rest = menu.Items.Commands().Single(i => i.Text == "闭眼休息");
            rest.Checked = true;
            Check(!hook.Enabled && petStats.Total == previousTotal + 1, "休息停止监听并结算此前计数");
            rest.Checked = false; Check(hook.Enabled, "离开休息恢复统计监听");
            var follow = menu.Items.Commands().Single(i => i.Text == "跟随键鼠动作");
            follow.Checked = false; Check(!hook.Enabled && petStats.Total == previousTotal + 1, "关闭跟随暂停计数并保留历史");
            follow.Checked = true;
            entry.PerformClick(); Check(Object.ReferenceEquals(firstWindow, field.GetValue(pet)), "重复打开复用窗口");
            firstWindow.Close(); entry.PerformClick();
            Check(!Object.ReferenceEquals(firstWindow, field.GetValue(pet)), "关闭后可重新打开统计窗口");
            var reopened = (TypingStatisticsWindow)field.GetValue(pet);
            pet.Dispose(); Check(reopened.IsDisposed, "桌宠关闭时释放统计窗口");
        }
    }
}
