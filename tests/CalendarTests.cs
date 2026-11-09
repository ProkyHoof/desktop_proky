using System;
using System.Collections.Generic;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

internal static class CalendarTests
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Check(bool value,string message){if(!value)throw new Exception("Calendar: "+message);}
    static T Get<T>(object target,string name){return (T)target.GetType().GetField(name,Private).GetValue(target);}
    static KnightButton FindButton(Control root,string text)
    {
        foreach(Control child in root.Controls)
        {var button=child as KnightButton;if(button!=null&&button.Text==text)return button;var nested=FindButton(child,text);if(nested!=null)return nested;}
        return null;
    }
    static void WaitForSave(PetCalendarWindow window)
    {
        var clock=Stopwatch.StartNew();
        while(window.HasUnsavedChanges&&clock.ElapsedMilliseconds<2500){Application.DoEvents();Thread.Sleep(10);}
        Check(!window.HasUnsavedChanges,"Debounced timer automatically saves edits");
    }
    internal static void Run(string root)
    {
        string isolated=Path.Combine(Path.GetTempPath(),"DesktopPet.CalendarTests."+Guid.NewGuid().ToString("N"));
        try
        {
            var date=new DateTime(2026,11,6);var store=new PetCalendarStore(isolated);
            Check(!store.Load(date).HasContent&&!Directory.Exists(store.DirectoryPath),"Viewing an empty calendar does not write files");
            var day=store.Load(date);day.Journal="完成日历模块\r\n读书 30 分钟，记录 \"引号\"、\\ 路径与 ☀";
            day.Plans.Add(new PetCalendarPlan {Text="完成项目",Done=true});
            day.Plans.Add(new PetCalendarPlan {Text="整理明天的计划"});store.Save(date,day);
            var reload=new PetCalendarStore(isolated).Load(date);
            Check(reload.Journal==day.Journal&&reload.Plans.Count==2&&reload.Plans[0].Done&&!reload.Plans[1].Done,"Unicode, multiline journal, text and completion persist across reloads");
            string original=File.ReadAllText(store.FilePath(date));day.Journal="第二版";store.Save(date,day);
            Check(File.ReadAllText(store.FilePath(date)+".bak")==original,"Atomic replacement keeps the previous version as a backup");
            day.Journal="第三版";store.Save(date,day);
            Check(store.Load(date).Journal=="第三版"&&Directory.GetFiles(store.DirectoryPath,"*.tmp").Length==0,"Repeated atomic saves replace backup and clean temporary files");
            Check(!store.Load(date.AddDays(1)).HasContent,"Dates are stored independently");
            using(var window=new PetCalendarWindow(isolated,false,date))
            {
                window.StartPosition=FormStartPosition.Manual;window.Location=new Point(-30000,-30000);window.Show();Application.DoEvents();
                var originalSize=window.Size;window.Size=window.MinimumSize;window.PerformLayout();Application.DoEvents();
                var refresh=FindButton(window,"刷新");
                Check(refresh!=null&&window.ClientRectangle.Contains(window.RectangleToClient(refresh.RectangleToScreen(refresh.ClientRectangle))),"Calendar toolbar wraps without hiding actions at minimum size");
                var compactDays=Get<CalendarDayButton[]>(window,"days");
                var compactPlans=Get<DataGridView>(window,"plans");
                Check(compactDays[0].Width>=85&&compactPlans.ClientSize.Width>=320,"At minimum size the calendar and editor both retain usable width");
                Check(window.ClientRectangle.Contains(window.RectangleToClient(Get<KnightButton>(window,"save").RectangleToScreen(Get<KnightButton>(window,"save").ClientRectangle))),"Save remains reachable at minimum size");
                window.Size=originalSize;
                var journal=Get<TextBox>(window,"journal");var plans=Get<DataGridView>(window,"plans");
                Check(journal.Text=="第三版"&&plans.Rows.Count==2,"Editor loads real saved entries");
                journal.Text="今天完成了接口联调\r\n明天继续测试";plans.Rows[1].Cells[0].Value=true;
                Check(window.HasUnsavedChanges,"Editing journal and completion marks the document dirty");
                WaitForSave(window);
                Check(store.Load(date).Journal.Contains("接口联调"),"Timer persists journal text without a save click");
                journal.Text+="\r\n已完成复查";
                Check(window.SelectDate(date.AddDays(1)),"Date navigation flushes edits");
                Check(store.Load(date).Journal.Contains("接口联调")&&store.Load(date).Plans.All(p=>p.Done),"Navigation saves both journal and plan state");
                plans.Rows.Add(false,"未来的计划");journal.Text="明天的准备";
                Check(window.FlushChanges(),"Explicit save succeeds");
                window.SelectDate(date);
                Check(plans.Rows.Count==2&&journal.Text.Contains("接口联调"),"Returning to a day restores its own data");
                plans.CurrentCell=plans.Rows[0].Cells[1];Get<KnightButton>(window,"remove").PerformClick();window.FlushChanges();
                Check(store.Load(date).Plans.Count==1,"Delete-selected removes only the selected plan");
                Get<KnightButton>(window,"add").PerformClick();
                var editing=plans.EditingControl as TextBox;
                Check(editing!=null,"Add-plan button starts inline text editing");
                editing.Text="自动保存输入中的计划";WaitForSave(window);
                Check(plans.IsCurrentCellInEditMode&&store.Load(date).Plans.Last().Text=="自动保存输入中的计划","Autosaving an inline plan preserves the active text editor");
                plans.EndEdit();Get<KnightButton>(window,"remove").PerformClick();window.FlushChanges();
                var cells=Get<CalendarDayButton[]>(window,"days");
                Check(cells.Length==42&&cells[0].Date.DayOfWeek==DayOfWeek.Monday,"Calendar is a complete six-week Monday-first grid");
                window.SelectDate(new DateTime(2026,2,1));
                Check(cells.Count(c=>c.Visible)==35&&cells.Where(c=>c.Date.Month==2).All(c=>c.Visible),"Five-week month uses five rows without hiding dates");
                window.SelectDate(new DateTime(2026,8,1));
                Check(cells.Count(c=>c.Visible)==42&&cells.Where(c=>c.Date.Month==8).All(c=>c.Visible),"Six-week month restores the last row");
                window.SelectDate(new DateTime(2027,2,1));
                Check(cells.Count(c=>c.Visible)==28&&cells.Take(28).All(c=>c.Date.Month==2),"Four-week February fills the available height with four rows");
                window.SelectDate(date);
                Check(cells.Single(c=>c.Date==date).Day.Journal.Contains("接口联调"),"Calendar cells show actual day data after saving");
                window.SelectDate(new DateTime(2024,2,29));
                Check(cells.Any(c=>c.Date==new DateTime(2024,2,29)),"Leap days display correctly");
                window.SelectDate(new DateTime(2026,12,31));
                FindButton(window,"下个月 ›").PerformClick();
                Check(window.SelectedDate==new DateTime(2027,1,31),"Month navigation crosses years");
                FindButton(window,"下个月 ›").PerformClick();
                Check(window.SelectedDate==new DateTime(2027,2,28),"Short months clamp the selected day");
                FindButton(window,"今天").PerformClick();
                Check(window.SelectedDate==DateTime.Today,"Today navigation uses the local calendar date");
                Check(window.SetView(CalendarView.Week)&&window.CurrentView==CalendarView.Week,"Week view is selectable");
                var timeline=Get<CalendarTimeline>(window,"timeline");
                Check(timeline.DayCount==7&&timeline.FirstDate.DayOfWeek==DayOfWeek.Monday,"Week time axis starts on Monday and contains seven days");
                window.SetView(CalendarView.WorkWeek);Check(timeline.DayCount==5,"Work-week view excludes weekend columns");
                window.SetView(CalendarView.Day);window.MovePeriod(1);
                Check(window.SelectedDate==DateTime.Today.AddDays(1)&&timeline.DayCount==1,"Day-view navigation advances one day");
                window.SetView(CalendarView.List);
                Check(Get<DataGridView>(window,"allEntries").Rows.Count>0,"All-dates list displays existing records");
                Get<TextBox>(window,"search").Text="接口联调";
                var searchClock=Stopwatch.StartNew();while(searchClock.ElapsedMilliseconds<550){Application.DoEvents();Thread.Sleep(10);}
                Check(Get<DataGridView>(window,"allEntries").Rows.Count==1,"Cross-date search filters journal text");
                window.OpenListEntry(0);Check(window.SelectedDate==date,"Opening a search result loads its original date");
            }
            string saved=File.ReadAllText(store.FilePath(date));
            using(var preview=new PetCalendarWindow(isolated,true,date))
            {
                Check(!Get<TextBox>(preview,"journal").Enabled&&!Get<DataGridView>(preview,"plans").Enabled,"Preview is read only");
                Get<TextBox>(preview,"journal").Text="do not save";preview.FlushChanges();
            }
            Check(File.ReadAllText(store.FilePath(date))==saved,"Preview cannot overwrite real data");
            using(var closing=new PetCalendarWindow(isolated,false,date.AddDays(4)))
            {
                closing.StartPosition=FormStartPosition.Manual;closing.Location=new Point(-30000,-30000);closing.Show();Application.DoEvents();
                Get<TextBox>(closing,"journal").Text="关闭窗口前立即保存";closing.Close();
                Check(store.Load(date.AddDays(4)).Journal=="关闭窗口前立即保存","Closing flushes edits before the debounce timer fires");
            }
            var broken=date.AddDays(3);string corrupt="{broken JSON";File.WriteAllText(store.FilePath(broken),corrupt);
            bool rejected=false;try{store.Load(broken);}catch(InvalidDataException){rejected=true;}
            Check(rejected,"Invalid data is reported instead of treated as an empty day");
            using(var window=new PetCalendarWindow(isolated,false,broken))
            {
                Check(!Get<TextBox>(window,"journal").Enabled&&window.SaveStatus.Contains("无法读取"),"Damaged day is protected against editing");
                Check(window.SelectDate(date),"A damaged day does not block viewing healthy dates");
            }
            Check(File.ReadAllText(store.FilePath(broken))==corrupt,"Viewing invalid data preserves its original bytes");
            // Lock the target to simulate a failed write. Keep the edit buffer
            // and selected date until the file can be saved successfully.
            using(var window=new PetCalendarWindow(isolated,false,date))
            {
                Get<TextBox>(window,"journal").Text="未保存内容必须保留";
                using(var locked=new FileStream(store.FilePath(date),FileMode.Open,FileAccess.Read,FileShare.None))
                {
                    Check(!window.FlushChanges()&&window.HasUnsavedChanges,"Failed saves retain unsaved edits");
                    Check(!window.SelectDate(date.AddDays(1))&&window.SelectedDate==date,"Failed saves prevent losing edits during date navigation");
                }
                Check(window.FlushChanges()&&store.Load(date).Journal=="未保存内容必须保留","Retry saves the retained buffer");
            }
            PetCalendarInteraction(root);
            BatchTests(Path.Combine(isolated,"batch"));
        }
        finally
        {
            string resolved=Path.GetFullPath(isolated),temporary=Path.GetFullPath(Path.GetTempPath());
            if(!resolved.StartsWith(temporary,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(resolved).StartsWith("DesktopPet.CalendarTests.",StringComparison.Ordinal))throw new InvalidOperationException("Unexpected calendar test directory");
            if(Directory.Exists(resolved))Directory.Delete(resolved,true);
        }
        Console.WriteLine("PASS: calendar views/search/batches/persistence, modeless pet dragging, single-window reuse, reopen and guarded application shutdown.");
    }
    static void PetCalendarInteraction(string root)
    {
        using(var pet=new KeyboardPetWindow(root,true))
        using(var watchdog=new System.Windows.Forms.Timer {Interval=500})
        {
            Get<System.Windows.Forms.Timer>(pet,"timer").Stop();
            pet.StartPosition=FormStartPosition.Manual;pet.Location=Screen.PrimaryScreen.WorkingArea.Location+new Size(30,30);pet.Show();
            var item=Get<PetContextMenu>(pet,"menu").Items.Commands().Single(i=>i.Text=="日历计划…");
            bool blocked=false;
            watchdog.Tick+=(s,e)=>{blocked=true;watchdog.Stop();foreach(Form form in Application.OpenForms.Cast<Form>().ToArray())if(form is PetCalendarWindow)form.Close();};
            watchdog.Start();item.PerformClick();watchdog.Stop();
            Check(!blocked,"Opening the calendar returns immediately without a modal message loop");
            var calendar=Get<PetCalendarWindow>(pet,"calendarWindow");
            Check(calendar.Visible&&!calendar.Modal&&pet.Enabled&&calendar.Owner==null,"Calendar leaves the pet enabled and does not inherit its topmost owner");
            Check(Get<GlobalInputFeedback>(pet,"input").Enabled&&Get<GlobalInputFeedback>(pet,"input").ProbePump(),"Calendar leaves global input tracking active");
            var savedCursor=Cursor.Position;
            try
            {
                // Exercise the existing drag handlers with the calendar open.
                var bounds=pet.SpriteBounds;var hit=new Point(bounds.X+bounds.Width/2,bounds.Y+bounds.Height/2);
                Check(pet.Hit(hit),"Drag begins on an opaque pet pixel");
                Cursor.Position=pet.PointToScreen(hit);var startCursor=Cursor.Position;var start=pet.Location;
                typeof(KeyboardPetWindow).GetMethod("OnMouseDown",Private).Invoke(pet,new object[] {new MouseEventArgs(MouseButtons.Left,1,hit.X,hit.Y,0)});
                Cursor.Position=new Point(startCursor.X+40,startCursor.Y+30);var moved=Cursor.Position;
                typeof(KeyboardPetWindow).GetMethod("OnMouseMove",Private).Invoke(pet,new object[] {new MouseEventArgs(MouseButtons.Left,0,hit.X+40,hit.Y+30,0)});
                Check(pet.Location==start+new Size(moved.X-startCursor.X,moved.Y-startCursor.Y)&&pet.Location!=start,"Pet moves while calendar stays open");
                typeof(KeyboardPetWindow).GetMethod("OnMouseUp",Private).Invoke(pet,new object[] {new MouseEventArgs(MouseButtons.Left,1,hit.X+40,hit.Y+30,0)});
                Check(calendar.Visible&&!pet.Capture,"Releasing a drag keeps the calendar open and releases capture");
            }
            finally {pet.Capture=false;Cursor.Position=savedCursor;}
            calendar.WindowState=FormWindowState.Minimized;item.PerformClick();
            Check(Get<PetCalendarWindow>(pet,"calendarWindow")==calendar&&calendar.WindowState==FormWindowState.Normal,"Repeated open restores the same calendar");
            calendar.Close();Check(calendar.IsDisposed&&Get<PetCalendarWindow>(pet,"calendarWindow")==null,"Closing clears the calendar reference");
            Check(Get<GlobalInputFeedback>(pet,"input").Enabled,"Closing calendar keeps pet input tracking active");
            item.PerformClick();calendar=Get<PetCalendarWindow>(pet,"calendarWindow");
            FormClosingEventHandler cancel=(s,e)=>e.Cancel=true;calendar.FormClosing+=cancel;
            Get<EventWaitHandle>(pet,"shutdown").Set();
            typeof(KeyboardPetWindow).GetMethod("Tick",Private).Invoke(pet,new object[] {null,EventArgs.Empty});
            Check(!pet.IsDisposed&&calendar.Visible&&!Get<EventWaitHandle>(pet,"shutdown").WaitOne(0),"Cancelled calendar close keeps application alive without repeating shutdown requests");
            calendar.FormClosing-=cancel;pet.Close();
            Check(pet.IsDisposed&&calendar.IsDisposed,"Exiting the pet closes the independent calendar");
        }
    }
    static void BatchTests(string root)
    {
        var store=new PetCalendarStore(root);var a=new DateTime(2026,10,31);var b=a.AddDays(1);var target=b.AddDays(3);
        var first=store.Load(a);first.Journal="跨月记录 Alpha";
        first.Plans.Add(new PetCalendarPlan {Text="重复标题",Start="09:00",End="10:00"});
        first.Plans.Add(new PetCalendarPlan {Text="重复标题",Start="09:30",End="10:30"});store.Save(a,first);
        var second=store.Load(b);second.Journal="另一篇记录";second.Plans.Add(new PetCalendarPlan {Text="Alpha 项目",Done=true});store.Save(b,second);
        List<string> errors;var all=store.Query("",0,true,true,out errors);
        Check(all.Count==5&&errors.Count==0,"Global query includes plans and journals across months");
        Check(store.Query("alpha",0,true,true,out errors).Count==2,"Search matches case-insensitive plan and journal text");
        Check(store.Query("2026-10",0,true,true,out errors).Count==3,"Global search also matches ISO dates and months");
        Check(store.Query("",1,true,true,out errors).Count==2&&store.Query("",2,true,true,out errors).Count==1,"Completion filters do not mislabel journals");
        var selected=all.Where(item=>item.Index>=0&&!item.Done).ToList();
        Check(store.ApplyBatch(selected,CalendarBatchAction.Complete,target)==2&&store.Load(a).Plans.All(plan=>plan.Done),"Bulk completion updates duplicate-title plans by source index");
        Check(store.UndoBatch()&&store.Load(a).Plans.All(plan=>!plan.Done),"Undo restores completion state");
        selected=store.Query("",0,true,false,out errors);
        store.ApplyBatch(selected,CalendarBatchAction.Reopen,target);
        Check(store.Load(b).Plans.All(plan=>!plan.Done),"Bulk reopen changes completed plans");
        selected=store.Query("",0,true,false,out errors);
        store.ApplyBatch(selected,CalendarBatchAction.Move,target);
        Check(store.Load(a).Plans.Count==0&&store.Load(b).Plans.Count==0&&store.Load(target).Plans.Count==3,"Bulk move handles multiple source dates");
        Check(store.Load(a).Journal=="跨月记录 Alpha"&&store.Load(target).Plans[0].Start=="09:00","Moving preserves source journals and plan times");
        Check(store.UndoBatch()&&store.Load(a).Plans.Count==2&&store.Load(b).Plans.Count==1&&!store.Load(target).HasContent,"Undo restores both source and destination dates");
        selected=store.Query("",0,true,true,out errors).Where(item=>item.Date==a).ToList();store.ApplyBatch(selected,CalendarBatchAction.Delete,target);
        Check(!store.Load(a).HasContent&&store.Load(b).HasContent,"Bulk deletion affects selected plans and journal only");
        Check(store.UndoBatch()&&store.Load(a).Plans.Count==2&&store.Load(a).Journal.Contains("Alpha"),"Deleted journal and plans can be restored");
        var stale=store.Query("",0,true,false,out errors);var edited=store.Load(a);edited.Journal="新修改";store.Save(a,edited);
        bool rejected=false;try{store.ApplyBatch(stale,CalendarBatchAction.Delete,target);}catch(InvalidDataException){rejected=true;}
        Check(rejected&&store.Load(a).Plans.Count==2&&store.Load(b).Plans.Count==1,"Stale search results cannot mutate shifted or edited data");
        selected=store.Query("",0,true,false,out errors);store.ApplyBatch(selected,CalendarBatchAction.Complete,target);
        edited=store.Load(a);edited.Journal="撤销之后也不能覆盖这段新内容";store.Save(a,edited);
        rejected=false;try{store.UndoBatch();}catch(InvalidDataException){rejected=true;}
        Check(rejected&&store.Load(a).Journal.Contains("新内容"),"Undo cannot overwrite subsequent edits");
        string corrupt=store.FilePath(a.AddDays(20));File.WriteAllText(corrupt,"broken");
        Check(store.Query("",0,true,true,out errors).Count==5&&errors.Count==1&&File.ReadAllText(corrupt)=="broken","A corrupt file is reported and skipped during global search");
        var invalid=store.Load(b);invalid.Plans[0].Start="15:00";invalid.Plans[0].End="14:00";
        rejected=false;try{store.Save(b,invalid);}catch(InvalidDataException){rejected=true;}
        Check(rejected&&String.IsNullOrEmpty(store.Load(b).Plans[0].Start),"Invalid time ranges cannot overwrite a valid day");
        Check(Directory.GetDirectories(Path.Combine(store.DirectoryPath,"history")).Length>0,"Batch operations persist recovery snapshots inside the project data directory");
        var beforeFailure=store.Load(a);var lockedPath=store.FilePath(b);File.SetAttributes(lockedPath,File.GetAttributes(lockedPath)|FileAttributes.ReadOnly);
        rejected=false;
        try{store.ApplyBatch(store.Query("",0,true,false,out errors),CalendarBatchAction.Reopen,target);}catch(IOException){rejected=true;}
        finally{File.SetAttributes(lockedPath,File.GetAttributes(lockedPath)&~FileAttributes.ReadOnly);}
        Check(rejected&&store.Load(a).Plans.All(plan=>plan.Done)&&store.Load(a).Journal==beforeFailure.Journal&&store.Load(b).Plans.All(plan=>plan.Done),"A second-date write failure rolls back already-written days");
        using(var window=new PetCalendarWindow(root,false,a))
        {
            window.StartPosition=FormStartPosition.Manual;window.Location=new Point(-30000,-30000);window.Show();Application.DoEvents();window.SetView(CalendarView.List);
            var grid=Get<DataGridView>(window,"allEntries");grid.ClearSelection();
            foreach(DataGridViewRow row in grid.Rows)if(((CalendarEntry)row.Tag).Index>=0)row.Selected=true;
            Check(window.ApplySelection(CalendarBatchAction.Reopen,target)==3&&store.Load(a).Plans.All(plan=>!plan.Done),"Visible multi-selection drives the real batch operation");
            Check(window.UndoBatch()&&store.Load(a).Plans.All(plan=>plan.Done),"Toolbar undo restores a UI batch operation");
            window.SetView(CalendarView.Day);var timeline=Get<CalendarTimeline>(window,"timeline");
            using(var image=new Bitmap(Math.Max(1,timeline.Width),timeline.Height))timeline.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));
            var cards=Get<System.Collections.Generic.List<Tuple<RectangleF,CalendarSlotEventArgs>>>(timeline,"hits");
            var overlapping=cards.Where(card=>card.Item2.Index>=0).ToList();
            Check(overlapping.Count==2&&overlapping[0].Item1.Right<=overlapping[1].Item1.Left+3,"Overlapping timed plans render in separate columns");
            Get<ComboBox>(window,"stateFilter").SelectedIndex=1;
            using(var image=new Bitmap(Math.Max(1,timeline.Width),timeline.Height))timeline.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));
            Check(cards.Count==0,"Completion filters also apply to timeline cards");
            Get<ComboBox>(window,"stateFilter").SelectedIndex=0;
            typeof(CalendarTimeline).GetMethod("OnMouseDoubleClick",Private).Invoke(timeline,new object[] {new MouseEventArgs(MouseButtons.Left,2,80,12*timeline.HourHeight+1,0)});
            var editor=Get<DataGridView>(window,"plans");editor.Rows[editor.Rows.Count-1].Cells[1].Value="双击时间轴新建";window.FlushChanges();
            Check(store.Load(a).Plans.Last().Start=="12:00"&&store.Load(a).Plans.Last().End=="12:30","Double-clicking an empty timeline slot creates a timed plan at that position");
            editor.CurrentCell=editor.Rows[editor.Rows.Count-1].Cells[1];Get<KnightButton>(window,"remove").PerformClick();window.FlushChanges();
            var moving=store.Query("",0,true,false,out errors).First(entry=>entry.Date==a&&entry.Index==0);
            Check(window.MoveTimelineEntry(moving,target,1410),"Timeline drop uses the same atomic move operation");
            Check(store.Load(target).Plans.Single().Start=="23:00"&&store.Load(target).Plans.Single().End=="24:00","Late timeline drops preserve duration and end at midnight");
            Check(window.UndoBatch()&&store.Load(a).Plans.Count==2&&!store.Load(target).HasContent,"A drag move can be undone across dates");
            moving=store.Query("",0,true,false,out errors).First(entry=>entry.Date==a&&entry.Index==0);
            window.SelectDate(a);var payload=new DataObject();payload.SetData(typeof(CalendarEntry),moving);
            var dropPoint=timeline.PointToScreen(new Point(80,12*timeline.HourHeight+1));
            var drag=new DragEventArgs(payload,0,dropPoint.X,dropPoint.Y,DragDropEffects.Move,DragDropEffects.None);
            typeof(Control).GetMethod("OnDragEnter",Private).Invoke(timeline,new object[] {drag});Check(drag.Effect==DragDropEffects.Move,"Timeline accepts its own typed plan drag payload");
            typeof(Control).GetMethod("OnDragDrop",Private).Invoke(timeline,new object[] {drag});
            Check(store.Load(a).Plans[0].Start=="12:00","Native drop coordinates update the original plan's time");
            Check(window.UndoBatch()&&store.Load(a).Plans[0].Start=="09:00","Same-day drag undo preserves the original time");
            window.SetView(CalendarView.Month);var cell=Get<CalendarDayButton[]>(window,"days").Single(item=>item.Date==a);int count=editor.Rows.Count;
            typeof(CalendarDayButton).GetMethod("OnMouseDoubleClick",Private).Invoke(cell,new object[] {new MouseEventArgs(MouseButtons.Left,2,12,cell.PlanTop+3,0)});
            Check(editor.Rows.Count==count&&editor.CurrentRow.Index==0&&editor.IsCurrentCellInEditMode,"Double-clicking a month card edits its source plan without adding duplicates");
        }
    }
}
