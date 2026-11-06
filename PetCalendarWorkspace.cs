using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

internal sealed class CalendarDayButton : Button
{
    internal DateTime Date { get; set; }
    internal PetCalendarDay Day;
    internal List<int> SourceIndices;
    internal bool Selected,InMonth,Failed;
    internal int PlanTop {get{return (int)(36*DeviceDpi/96f);}}
    int LineHeight {get{return Math.Max((int)(24*DeviceDpi/96f),Font.Height+4);}}
    int VisiblePlanCount
    {
        get
        {
            if(Day==null)return 0;
            int available=Math.Max(0,(Height-PlanTop-2)/LineHeight);
            bool footer=!String.IsNullOrWhiteSpace(Day.Journal)||Day.Plans.Count>available;
            return Math.Max(0,Math.Min(Day.Plans.Count,available-(footer ? 1 : 0)));
        }
    }
    internal CalendarDayButton()
    {
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
        SetStyle(ControlStyles.StandardClick|ControlStyles.StandardDoubleClick,true);
        FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;Margin=Padding.Empty;Dock=DockStyle.Fill;Cursor=Cursors.Hand;
    }
    internal int PlanAt(Point point)
    {
        int index=(point.Y-PlanTop)/LineHeight;
        return point.Y>=PlanTop&&index>=0&&index<VisiblePlanCount ? index : -1;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics;Color ink=InMonth ? KnightUi.Ink : KnightUi.Steel;
        Color paper=Selected ? Color.FromArgb(238,238,237) : InMonth ? KnightUi.Surface : Color.FromArgb(243,243,242);
        if(SystemInformation.HighContrast){paper=Selected ? SystemColors.Highlight : SystemColors.Window;ink=Selected ? SystemColors.HighlightText : SystemColors.WindowText;}
        g.Clear(paper);
        using(var border=new Pen(KnightUi.Silver))g.DrawRectangle(border,0,0,Width-1,Height-1);
        int line=LineHeight,pad=(int)(8*DeviceDpi/96f),diameter=PlanTop-6;
        var number=new Rectangle(pad,3,diameter,diameter);
        using(var dateFont=new Font(Font.FontFamily,Font.Size+4,FontStyle.Bold,Font.Unit))
        {
        if(Date==DateTime.Today)
        {
            using(var red=new SolidBrush(KnightUi.Red))g.FillEllipse(red,number);
            TextRenderer.DrawText(g,Date.Day.ToString(),dateFont,number,KnightUi.OnDark,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
        }
        else TextRenderer.DrawText(g,Date.Day.ToString(),dateFont,number,ink,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
        }
        if(Selected)using(var stripe=new SolidBrush(KnightUi.Red))g.FillRectangle(stripe,0,0,Width,3);
        if(Failed)TextRenderer.DrawText(g,"读取失败",Font,new Rectangle(pad,PlanTop,Width-pad*2,line),KnightUi.Red,TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine);
        else if(Day!=null)
        {
            int count=Day.Plans.Count,shown=VisiblePlanCount;
            for(int i=0;i<shown;i++)
            {
                var plan=Day.Plans[i];var r=new Rectangle(pad,PlanTop+i*line,Math.Max(1,Width-pad*2),line-3);
                using(var fill=new SolidBrush(plan.Done ? Color.FromArgb(240,240,240) : Color.FromArgb(235,235,234)))g.FillRectangle(fill,r);
                using(var stripe=new SolidBrush(plan.Done ? KnightUi.Steel : KnightUi.Red))g.FillRectangle(stripe,r.X,r.Y,3,r.Height);
                string text=(plan.Done ? "✓ " : "")+(String.IsNullOrEmpty(plan.Start) ? "" : plan.Start+" ")+plan.Text;
                r.X+=4;r.Width-=4;TextRenderer.DrawText(g,text,Font,r,ink,TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
            }
            string footer=count>shown ? "+"+(count-shown)+" 项" : "";
            if(!String.IsNullOrWhiteSpace(Day.Journal))footer+=(footer.Length>0 ? " · " : "")+"记："+Day.Journal.Replace("\r"," ").Replace("\n"," ");
            if(PlanTop+(shown+1)*line<=Height-2)TextRenderer.DrawText(g,footer,Font,new Rectangle(pad,PlanTop+shown*line,Width-pad*2,line),KnightUi.Steel,TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
        }
        if(Focused)ControlPaint.DrawFocusRectangle(g,new Rectangle(3,3,Width-6,Height-6),ink,paper);
    }
}

internal sealed class CalendarSlotEventArgs : EventArgs
{
    internal DateTime Date;
    internal int Minutes,Index=-1;
}
internal sealed class CalendarDropEventArgs : EventArgs
{
    internal CalendarEntry Entry;
    internal DateTime Date;
    internal int Minutes;
}
internal sealed class CalendarHeaderButton : Button
{
    internal string[] Lines;
    internal CalendarHeaderButton(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.StandardClick|ControlStyles.StandardDoubleClick,true);}
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);using(var pen=new Pen(KnightUi.Silver))e.Graphics.DrawRectangle(pen,0,0,Width-1,Height-1);
        if(Lines==null)return;int line=Math.Max(20,Font.Height+2);
        for(int i=0;i<Lines.Length&&i*line<Height;i++)TextRenderer.DrawText(e.Graphics,Lines[i],Font,new Rectangle(3,3+i*line,Width-6,line),
            i==0 ? KnightUi.Ink : KnightUi.Red,TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix|TextFormatFlags.VerticalCenter);
        if(Focused)ControlPaint.DrawFocusRectangle(e.Graphics,new Rectangle(3,3,Width-6,Height-6));
    }
}

// A scrollable 24-hour schedule. The same time scale is used for day, work
// week and full week views; overlapping events receive separate columns.
internal sealed class CalendarTimeline : Control
{
    internal DateTime FirstDate { get; set; }
    internal int DayCount=7;
    internal bool IncludePlans=true,IncludeJournal=true;
    internal int State;
    internal readonly Dictionary<DateTime,PetCalendarDay> Data=new Dictionary<DateTime,PetCalendarDay>();
    internal event EventHandler<CalendarSlotEventArgs> SlotClicked,SlotOpened;
    internal event EventHandler<CalendarDropEventArgs> PlanDropped;
    Point dragStart;
    CalendarEntry dragEntry;
    const int Header=0;
    internal int HourHeight {get{return (int)(64*DeviceDpi/96f);}}
    internal int GutterWidth {get{return (int)(64*DeviceDpi/96f);}}
    int Hour {get{return HourHeight;}}
    int Gutter {get{return GutterWidth;}}
    readonly List<Tuple<RectangleF,CalendarSlotEventArgs>> hits=new List<Tuple<RectangleF,CalendarSlotEventArgs>>();
    readonly ToolTip tips=new ToolTip();
    internal CalendarTimeline()
    {
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
        Height=Header+24*Hour+2;BackColor=KnightUi.Surface;TabStop=true;AllowDrop=true;
        DragEnter+=(s,e)=>e.Effect=e.Data.GetDataPresent(typeof(CalendarEntry)) ? DragDropEffects.Move : DragDropEffects.None;
        DragDrop+=(s,e)=>
        {
            var entry=e.Data.GetData(typeof(CalendarEntry)) as CalendarEntry;if(entry==null)return;
            var slot=Slot(PointToClient(new Point(e.X,e.Y)),false);
            if(PlanDropped!=null)PlanDropped(this,new CalendarDropEventArgs {Entry=entry,Date=slot.Date,Minutes=slot.Minutes});
        };
    }
    protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);Height=Header+24*Hour+2;}
    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics;g.Clear(BackColor);hits.Clear();float width=(Width-Gutter)/(float)DayCount;
        using(var grid=new Pen(KnightUi.Silver))
        {
            for(int hour=0;hour<=24;hour++)
            {
                float y=Header+hour*Hour;g.DrawLine(grid,Gutter,y,Width,y);
                if(hour<24)TextRenderer.DrawText(g,hour.ToString("D2")+":00",Font,new Rectangle(0,(int)y+1,Gutter,Font.Height+4),KnightUi.Steel,TextFormatFlags.SingleLine);
            }
            for(int col=0;col<DayCount;col++)
            {
                DateTime date=FirstDate.AddDays(col);float left=Gutter+col*width;
                g.DrawLine(grid,left,0,left,Height);
                PetCalendarDay day;if(!Data.TryGetValue(date,out day))continue;
                var timed=new List<Tuple<int,int,int>>();
                for(int index=0;index<day.Plans.Count;index++)
                {
                    var plan=day.Plans[index];int start=PetCalendarStore.Minutes(plan.Start);
                    if(!IncludePlans||State==1&&plan.Done||State==2&&!plan.Done)continue;
                    if(start>=0)timed.Add(Tuple.Create(index,start,PetCalendarStore.Minutes(plan.End)));
                }
                var ordered=timed.OrderBy(item=>item.Item2).ThenBy(item=>item.Item3).ToList();
                int at=0;
                while(at<ordered.Count)
                {
                    var group=new List<Tuple<int,int,int>>();int end=ordered[at].Item3;
                    do{var item=ordered[at++];group.Add(item);end=Math.Max(end,item.Item3);}while(at<ordered.Count&&ordered[at].Item2<end);
                    var ends=new List<int>();var columns=new List<int>();
                    foreach(var item in group)
                    {
                        int column=ends.FindIndex(value=>value<=item.Item2);
                        if(column<0){column=ends.Count;ends.Add(item.Item3);}else ends[column]=item.Item3;
                        columns.Add(column);
                    }
                    for(int i=0;i<group.Count;i++)
                    {
                        var item=group[i];float cardWidth=(width-6)/ends.Count;
                        Card(g,new RectangleF(left+3+columns[i]*cardWidth,Header+item.Item2*Hour/60f,cardWidth-2,
                            Math.Max(17,(item.Item3-item.Item2)*Hour/60f-2)),day.Plans[item.Item1],new CalendarSlotEventArgs {Date=date,Index=item.Item1,Minutes=item.Item2});
                    }
                }
                if(IncludeJournal&&State==0&&!String.IsNullOrWhiteSpace(day.Journal))
                    TextRenderer.DrawText(g,"有当天记录",Font,new Rectangle((int)left+3,Height-23,(int)width-6,20),KnightUi.Steel,TextFormatFlags.EndEllipsis);
            }
            if(DateTime.Today>=FirstDate&&DateTime.Today<FirstDate.AddDays(DayCount))
            {
                float x=Gutter+(DateTime.Today-FirstDate).Days*width,y=Header+(float)DateTime.Now.TimeOfDay.TotalMinutes*Hour/60;
                using(var now=new Pen(KnightUi.Red,2))g.DrawLine(now,x,y,x+width,y);
            }
        }
    }
    void Card(Graphics g,RectangleF bounds,PetCalendarPlan plan,CalendarSlotEventArgs slot)
    {
        using(var fill=new SolidBrush(plan.Done ? Color.FromArgb(238,238,238) : Color.FromArgb(245,245,244)))g.FillRectangle(fill,bounds);
        using(var pen=new Pen(KnightUi.Red))g.DrawLine(pen,bounds.Left,bounds.Top,bounds.Left,bounds.Bottom);
        int minutes=PetCalendarStore.Minutes(plan.Start);string clock=minutes<0 ? "" : (minutes/60)+":"+(minutes%60).ToString("D2");
        string text=(plan.Done ? "✓ " : "")+plan.Text;
        var rect=Rectangle.Round(bounds);rect.Inflate(-3,0);
        if(rect.Height>=Font.Height*2)
        {
            TextRenderer.DrawText(g,clock,Font,new Rectangle(rect.X,rect.Y,rect.Width,Font.Height),KnightUi.Ink,TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis);
            rect.Y+=Font.Height;rect.Height-=Font.Height;
        }
        else text=clock+" "+text;
        TextRenderer.DrawText(g,text,Font,rect,KnightUi.Ink,TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix|(rect.Width<70 ? TextFormatFlags.SingleLine : TextFormatFlags.WordBreak));
        hits.Add(Tuple.Create(bounds,slot));
    }
    CalendarSlotEventArgs Slot(Point point,bool includeCards=true)
    {
        if(includeCards)foreach(var hit in hits.AsEnumerable().Reverse())if(hit.Item1.Contains(point))return hit.Item2;
        float width=(Width-Gutter)/(float)DayCount;int col=Math.Max(0,Math.Min(DayCount-1,(int)((point.X-Gutter)/width)));
        int minutes=point.Y<Header ? -1 : Math.Max(0,Math.Min(1410,((point.Y-Header)*60/Hour)/30*30));
        return new CalendarSlotEventArgs {Date=FirstDate.AddDays(col),Minutes=minutes};
    }
    protected override void OnMouseClick(MouseEventArgs e)
    {base.OnMouseClick(e);if(e.Button==MouseButtons.Left&&SlotClicked!=null)SlotClicked(this,Slot(e.Location));}
    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {base.OnMouseDoubleClick(e);if(e.Button==MouseButtons.Left&&SlotOpened!=null)SlotOpened(this,Slot(e.Location));}
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);dragEntry=null;if(e.Button!=MouseButtons.Left)return;
        var slot=Slot(e.Location);PetCalendarDay day;
        if(slot.Index>=0&&Data.TryGetValue(slot.Date,out day))
        {
            var plan=day.Plans[slot.Index];dragStart=e.Location;
            dragEntry=new CalendarEntry {Date=slot.Date,Index=slot.Index,Text=plan.Text,Done=plan.Done,Start=plan.Start,End=plan.End,Stamp=day.UpdatedAt};
        }
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var slot=Slot(e.Location);PetCalendarDay day;
        if(slot.Index>=0&&Data.TryGetValue(slot.Date,out day))
        {var plan=day.Plans[slot.Index];tips.SetToolTip(this,plan.Text+"\n"+slot.Date.ToString("yyyy-MM-dd")+" "+plan.Start+" – "+plan.End+"\n拖动改期 / 双击编辑");}
        else tips.SetToolTip(this,"双击空白时间段新建计划");
        if(dragEntry!=null&&e.Button==MouseButtons.Left&&(Math.Abs(e.X-dragStart.X)>SystemInformation.DragSize.Width||Math.Abs(e.Y-dragStart.Y)>SystemInformation.DragSize.Height))
        {var entry=dragEntry;dragEntry=null;DoDragDrop(entry,DragDropEffects.Move);}
    }
    protected override void OnMouseUp(MouseEventArgs e){dragEntry=null;base.OnMouseUp(e);}
    protected override void Dispose(bool disposing){if(disposing)tips.Dispose();base.Dispose(disposing);}
}

internal sealed class PetCalendarWindow : KnightWindow
{
    readonly PetCalendarStore store;
    readonly CalendarDayButton[] days=new CalendarDayButton[42];
    readonly Label monthLabel,monthSummary,status,listSummary;
    readonly DateTimePicker datePicker;
    readonly DataGridView plans,allEntries;
    readonly TextBox journal,search;
    readonly KnightButton add,remove,save,previous,next,undo;
    readonly ComboBox stateFilter;
    readonly CheckBox showPlans,showJournal;
    readonly TableLayoutPanel monthGrid;
    readonly Panel boardHost,monthPage,timelineHost,timelinePage,listHost;
    readonly TableLayoutPanel timelineHeader;
    readonly CalendarTimeline timeline;
    readonly ToolTip dayTips=new ToolTip();
    readonly Timer saveTimer=new Timer {Interval=650},searchTimer=new Timer {Interval=300};
    readonly Font uiFont=new Font("Microsoft YaHei UI",17,FontStyle.Regular,GraphicsUnit.Pixel);
    readonly Font compactFont=new Font("Microsoft YaHei UI",15,FontStyle.Regular,GraphicsUnit.Pixel);
    readonly Font calendarFont=new Font("Microsoft YaHei UI",16,FontStyle.Regular,GraphicsUnit.Pixel);
    readonly List<KnightButton> viewButtons=new List<KnightButton>();
    DateTime selected,month;
    CalendarView view=CalendarView.Month;
    bool loading,dirty,loadFailed,refreshing,preserveList;
    internal DateTime SelectedDate {get{return selected;}}
    internal CalendarView CurrentView {get{return view;}}
    internal bool HasUnsavedChanges {get{return dirty;}}
    internal string SaveStatus {get{return status.Text;}}
    internal PetCalendarWindow(string root,bool preview=false,DateTime? initialDate=null)
    {
        store=new PetCalendarStore(root,preview);Font=uiFont;Text="日历计划 · 白发小骑士";BackColor=KnightUi.Paper;ForeColor=KnightUi.Ink;
        StartPosition=FormStartPosition.CenterScreen;AutoScaleMode=AutoScaleMode.Dpi;AutoScaleDimensions=new SizeF(96,96);KeyPreview=true;
        var area=Screen.PrimaryScreen.WorkingArea;
        ClientSize=new Size(Math.Min(1660,area.Width-40),Math.Min(1000,area.Height-50));MinimumSize=new Size(Math.Min(1120,area.Width-40),Math.Min(740,area.Height-40));
        var header=new Panel {Dock=DockStyle.Top,Height=70,Padding=new Padding(20,14,20,14),BackColor=KnightUi.Surface};
        var title=new Label {Text="日历计划",Dock=DockStyle.Left,Width=220,Padding=new Padding(38,0,0,0),TextAlign=ContentAlignment.MiddleLeft,ForeColor=KnightUi.Ink,Font=new Font(Font.FontFamily,26,FontStyle.Bold,GraphicsUnit.Pixel)};
        title.Paint+=(s,e)=>KnightIcons.Draw(e.Graphics,KnightIcon.Calendar,new RectangleF(2,(title.Height-21)/2,21,21),KnightUi.Red);
        search=new TextBox {Dock=DockStyle.Fill,AccessibleName="搜索所有日期的计划和记录"};
        var searchBox=new TableLayoutPanel {Dock=DockStyle.Right,Width=500,ColumnCount=2,RowCount=1};
        searchBox.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,140));searchBox.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        searchBox.Controls.Add(new Label {Text="搜索全部日期",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,ForeColor=KnightUi.Steel},0,0);searchBox.Controls.Add(new KnightField(search),1,0);
        header.Controls.Add(title);header.Controls.Add(searchBox);
        var tools=new FlowLayoutPanel {Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,MinimumSize=new Size(0,60),Padding=new Padding(16,8,8,8),WrapContents=true};
        var create=Button("＋ 新建计划",138);create.Primary=true;
        var today=Button("今天",80);previous=Button("‹ 上个月",112);next=Button("下个月 ›",112);
        monthLabel=new Label {Width=220,Height=40,TextAlign=ContentAlignment.MiddleCenter,Font=new Font(Font.FontFamily,21,FontStyle.Bold,GraphicsUnit.Pixel)};
        tools.Controls.AddRange(new Control[] {create,today,previous,monthLabel,next});
        foreach(var item in new[] {Tuple.Create("日",CalendarView.Day),Tuple.Create("工作周",CalendarView.WorkWeek),Tuple.Create("周",CalendarView.Week),Tuple.Create("月",CalendarView.Month),Tuple.Create("全部列表",CalendarView.List)})
        {var captured=item.Item2;var button=Button(item.Item1,item.Item1.Length>3 ? 110 : item.Item1.Length>1 ? 88 : 52);button.Click+=(s,e)=>SetView(captured);tools.Controls.Add(button);viewButtons.Add(button);}
        undo=Button("撤销批量",112);undo.Enabled=false;tools.Controls.Add(undo);
        var refresh=Button("刷新",80);tools.Controls.Add(refresh);refresh.Click+=(s,e)=>{if(FlushChanges()||!dirty)RefreshViews();};
        var footer=new Panel {Dock=DockStyle.Bottom,Height=36,Padding=new Padding(18,3,10,3),Font=compactFont};
        status=new Label {Dock=DockStyle.Fill,AutoEllipsis=true,TextAlign=ContentAlignment.MiddleLeft,ForeColor=KnightUi.Steel};footer.Controls.Add(status);
        var workspace=new Panel {Dock=DockStyle.Fill,Padding=new Padding(18,4,18,8)};
        var filters=new FlowLayoutPanel {Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=true,Padding=new Padding(18,0,10,8),Font=compactFont};
        showPlans=new CheckBox {Text="计划",Checked=true,Width=82,Height=38};showJournal=new CheckBox {Text="每日记录",Checked=true,Width=118,Height=38};
        stateFilter=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,FlatStyle=FlatStyle.Flat,Dock=DockStyle.Fill};stateFilter.Items.AddRange(new object[] {"全部状态","未完成计划","已完成计划"});stateFilter.SelectedIndex=0;
        monthSummary=new Label {Width=380,Height=38,ForeColor=KnightUi.Steel,TextAlign=ContentAlignment.MiddleLeft,AutoEllipsis=true,Margin=new Padding(22,3,0,3)};
        filters.Controls.AddRange(new Control[] {showPlans,showJournal,new KnightField(stateFilter){Width=174,Height=38},monthSummary});
        dayTips.SetToolTip(monthSummary,"双击日期 / 时间段新建 · Ctrl+F 搜索 · Ctrl+N 新建 · Ctrl+S 保存");
        var split=new SplitContainer {Dock=DockStyle.Fill,Size=new Size(1400,650),FixedPanel=FixedPanel.Panel2,SplitterWidth=10,Panel1MinSize=620,Panel2MinSize=380,SplitterDistance=990};
        var inspector=new TableLayoutPanel {Dock=DockStyle.Fill,BackColor=KnightUi.Surface,Padding=new Padding(16),ColumnCount=1,RowCount=4};
        inspector.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        inspector.RowStyles.Add(new RowStyle(SizeType.Absolute,44));inspector.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        inspector.RowStyles.Add(new RowStyle(SizeType.Absolute,30));inspector.RowStyles.Add(new RowStyle(SizeType.Absolute,46));
        var detailHost=new Panel {Dock=DockStyle.Fill,AutoScroll=true,Margin=Padding.Empty};
        var detailBody=new TableLayoutPanel {Dock=DockStyle.Top,ColumnCount=1,RowCount=5,Margin=Padding.Empty};
        detailBody.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        detailBody.RowStyles.Add(new RowStyle(SizeType.Absolute,36));detailBody.RowStyles.Add(new RowStyle(SizeType.Absolute,48));
        detailBody.RowStyles.Add(new RowStyle(SizeType.Percent,48));detailBody.RowStyles.Add(new RowStyle(SizeType.Absolute,38));detailBody.RowStyles.Add(new RowStyle(SizeType.Percent,52));
        detailHost.Controls.Add(detailBody);inspector.Controls.Add(detailHost,0,1);
        detailHost.SizeChanged+=(s,e)=>detailBody.Height=Math.Max(detailHost.ClientSize.Height,(int)(440*DeviceDpi/96f));
        datePicker=new DateTimePicker {Format=DateTimePickerFormat.Custom,CustomFormat="yyyy年MM月dd日 dddd",Dock=DockStyle.Fill,AccessibleName="选择记录日期"};
        inspector.Controls.Add(datePicker,0,0);detailBody.Controls.Add(new Label {Text="当天详情 · 全部计划",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,0);
        var planTools=new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=false};add=Button("添加计划",122);remove=Button("删除所选",122);planTools.Controls.AddRange(new Control[] {add,remove});detailBody.Controls.Add(planTools,0,1);
        plans=Grid();plans.ReadOnly=false;plans.MultiSelect=false;plans.AccessibleName="当天计划列表";
        plans.Columns.Add(new DataGridViewCheckBoxColumn {Name="Done",HeaderText="✓",Width=32,AutoSizeMode=DataGridViewAutoSizeColumnMode.None});
        plans.Columns.Add(new DataGridViewTextBoxColumn {Name="Plan",HeaderText="计划内容",FillWeight=100,MinimumWidth=116,SortMode=DataGridViewColumnSortMode.NotSortable});
        plans.Columns.Add(new DataGridViewTextBoxColumn {Name="Start",HeaderText="开始",Width=76,AutoSizeMode=DataGridViewAutoSizeColumnMode.None,SortMode=DataGridViewColumnSortMode.NotSortable});
        plans.Columns.Add(new DataGridViewTextBoxColumn {Name="End",HeaderText="结束",Width=76,AutoSizeMode=DataGridViewAutoSizeColumnMode.None,SortMode=DataGridViewColumnSortMode.NotSortable});
        plans.Columns[1].DefaultCellStyle.WrapMode=DataGridViewTriState.True;plans.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.AllCells;
        detailBody.Controls.Add(plans,0,2);detailBody.Controls.Add(new Label {Text="当天记录",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,3);
        journal=new TextBox {Dock=DockStyle.Fill,Multiline=true,AcceptsReturn=true,ScrollBars=ScrollBars.Vertical,MaxLength=200000,BackColor=KnightUi.Paper,ForeColor=KnightUi.Ink,AccessibleName="当天记录"};
        detailBody.Controls.Add(new KnightField(journal),0,4);inspector.Controls.Add(new Label {Text="时间格式 HH:mm；留空表示全天",Dock=DockStyle.Fill,ForeColor=KnightUi.Steel,Font=compactFont},0,2);
        save=Button("保存当天记录",160);save.Primary=true;save.Dock=DockStyle.Fill;inspector.Controls.Add(save,0,3);split.Panel2.Controls.Add(inspector);
        boardHost=new Panel {Dock=DockStyle.Fill};split.Panel1.Controls.Add(boardHost);
        monthPage=new Panel {Dock=DockStyle.Fill,AutoScroll=true};
        monthGrid=new TableLayoutPanel {Dock=DockStyle.Top,ColumnCount=7,RowCount=7,Margin=Padding.Empty,Padding=Padding.Empty};monthPage.Controls.Add(monthGrid);
        monthPage.SizeChanged+=(s,e)=>LayoutMonth();
        for(int col=0;col<7;col++)monthGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f/7));
        monthGrid.RowStyles.Add(new RowStyle(SizeType.Absolute,36));for(int row=1;row<7;row++)monthGrid.RowStyles.Add(new RowStyle(SizeType.Percent,100f/6));
        string[] weekdays={"周一","周二","周三","周四","周五","周六","周日"};
        for(int col=0;col<7;col++)monthGrid.Controls.Add(new Label {Text=weekdays[col],Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter,ForeColor=col>=5 ? KnightUi.Red : KnightUi.Steel},col,0);
        for(int i=0;i<days.Length;i++)
        {
            var cell=new CalendarDayButton {Font=calendarFont};days[i]=cell;monthGrid.Controls.Add(cell,i%7,i/7+1);
            cell.Click+=(s,e)=>SelectDate(cell.Date);
            cell.MouseDoubleClick+=(s,e)=>
            {
                int visible=cell.PlanAt(e.Location),index=visible<0||cell.SourceIndices==null ? -1 : cell.SourceIndices[visible];
                if(!SelectDate(cell.Date))return;
                if(index>=0&&index<plans.Rows.Count&&plans.Enabled){plans.CurrentCell=plans.Rows[index].Cells[1];plans.Focus();plans.BeginEdit(true);}
                else if(index<0)AddPlan(-1);
            };
            cell.KeyDown+=(s,e)=>
            {
                int offset=e.KeyCode==Keys.Left ? -1 : e.KeyCode==Keys.Right ? 1 : e.KeyCode==Keys.Up ? -7 : e.KeyCode==Keys.Down ? 7 : 0;
                if(offset!=0){SelectDate(selected.AddDays(offset));foreach(var day in days)if(day.Date==selected)day.Focus();e.Handled=e.SuppressKeyPress=true;}
            };
        }
        timelinePage=new Panel {Dock=DockStyle.Fill,Visible=false};
        timelineHeader=new TableLayoutPanel {Dock=DockStyle.Top,Height=112,RowCount=1,Padding=new Padding(0,0,SystemInformation.VerticalScrollBarWidth,0)};
        timelineHost=new Panel {Dock=DockStyle.Fill,AutoScroll=true};timeline=new CalendarTimeline {Font=calendarFont,Width=600};timelineHost.Controls.Add(timeline);
        timelinePage.Controls.Add(timelineHost);timelinePage.Controls.Add(timelineHeader);
        timelineHost.SizeChanged+=(s,e)=>timeline.Width=Math.Max(200,timelineHost.ClientSize.Width);
        timeline.SlotClicked+=(s,e)=>{if(SelectDate(e.Date)&&e.Index>=0&&e.Index<plans.Rows.Count)plans.CurrentCell=plans.Rows[e.Index].Cells[1];};
        timeline.SlotOpened+=(s,e)=>{if(!SelectDate(e.Date))return;if(e.Index<0)AddPlan(e.Minutes);else if(e.Index<plans.Rows.Count){plans.CurrentCell=plans.Rows[e.Index].Cells[1];plans.BeginEdit(true);}};
        timeline.PlanDropped+=(s,e)=>MoveTimelineEntry(e.Entry,e.Date,e.Minutes);
        listHost=new Panel {Dock=DockStyle.Fill,Visible=false};allEntries=Grid();allEntries.ReadOnly=true;allEntries.MultiSelect=true;allEntries.AccessibleName="全部日期的计划与记录";
        allEntries.Columns.Add(new DataGridViewTextBoxColumn {Name="Date",HeaderText="日期",Width=140,AutoSizeMode=DataGridViewAutoSizeColumnMode.None});
        allEntries.Columns.Add(new DataGridViewTextBoxColumn {Name="Type",HeaderText="类型",Width=70,AutoSizeMode=DataGridViewAutoSizeColumnMode.None});
        allEntries.Columns.Add(new DataGridViewTextBoxColumn {Name="State",HeaderText="状态 / 时间",Width=158,AutoSizeMode=DataGridViewAutoSizeColumnMode.None});
        allEntries.Columns.Add(new DataGridViewTextBoxColumn {Name="Content",HeaderText="内容（双击打开）",FillWeight=100});
        var batchTools=new FlowLayoutPanel {Dock=DockStyle.Top,AutoSize=true,WrapContents=true,Padding=new Padding(0,4,0,4)};
        foreach(var command in new[] {"全选","取消选择","完成","重开","移动到…","删除所选"})
        {string captured=command;var button=Button(command,command.Length>3 ? 106 : 76);batchTools.Controls.Add(button);button.Click+=(s,e)=>BatchCommand(captured);}
        listSummary=new Label {Dock=DockStyle.Bottom,Height=36,AutoEllipsis=true,TextAlign=ContentAlignment.MiddleLeft,ForeColor=KnightUi.Steel,Font=compactFont};
        listHost.Controls.Add(allEntries);listHost.Controls.Add(batchTools);listHost.Controls.Add(listSummary);
        allEntries.CellDoubleClick+=(s,e)=>OpenListEntry(e.RowIndex);
        allEntries.CellClick+=(s,e)=>{if((ModifierKeys&(Keys.Control|Keys.Shift))==Keys.None)OpenListEntry(e.RowIndex,false);};
        allEntries.SelectionChanged+=(s,e)=>{if(!refreshing)listSummary.Text="共 "+allEntries.Rows.Count+" 条 · 已选 "+allEntries.SelectedRows.Count+" 条；Ctrl / Shift 多选";};
        boardHost.Controls.AddRange(new Control[] {monthPage,timelinePage,listHost});workspace.Controls.Add(split);
        Controls.Add(workspace);Controls.Add(footer);Controls.Add(filters);Controls.Add(tools);Controls.Add(header);
        create.Click+=(s,e)=>AddPlan(-1);today.Click+=(s,e)=>SelectDate(DateTime.Today);previous.Click+=(s,e)=>MovePeriod(-1);next.Click+=(s,e)=>MovePeriod(1);
        undo.Click+=(s,e)=>UndoBatch();
        datePicker.ValueChanged+=(s,e)=>{if(!loading)SelectDate(datePicker.Value);};
        add.Click+=(s,e)=>AddPlan(-1);remove.Click+=(s,e)=>{if(plans.CurrentRow!=null){plans.Rows.Remove(plans.CurrentRow);MarkDirty();}};
        plans.CurrentCellDirtyStateChanged+=(s,e)=>{if(plans.IsCurrentCellDirty&&plans.CurrentCell is DataGridViewCheckBoxCell)plans.CommitEdit(DataGridViewDataErrorContexts.Commit);};
        plans.EditingControlShowing+=(s,e)=>{e.Control.TextChanged-=PlanTextChanged;e.Control.TextChanged+=PlanTextChanged;};
        plans.CellValueChanged+=(s,e)=>MarkDirty();journal.TextChanged+=(s,e)=>MarkDirty();
        save.Click+=(s,e)=>FlushChanges();saveTimer.Tick+=(s,e)=>{saveTimer.Stop();FlushChanges(false);};
        search.TextChanged+=(s,e)=>{searchTimer.Stop();searchTimer.Start();};
        searchTimer.Tick+=(s,e)=>{searchTimer.Stop();if(!String.IsNullOrWhiteSpace(search.Text))SetView(CalendarView.List);else RefreshViews();};
        stateFilter.SelectedIndexChanged+=(s,e)=>RefreshViews();showPlans.CheckedChanged+=(s,e)=>RefreshViews();showJournal.CheckedChanged+=(s,e)=>RefreshViews();
        SelectDate(initialDate??DateTime.Today);SetView(CalendarView.Month);
    }
    static KnightButton Button(string text,int width) {return new KnightButton {Text=text,Size=new Size(width,40),Margin=new Padding(3,2,3,2)};}
    static DataGridView Grid()
    {
        var grid=new DataGridView {Dock=DockStyle.Fill,BackgroundColor=KnightUi.Surface,BorderStyle=BorderStyle.None,GridColor=KnightUi.Silver,CellBorderStyle=DataGridViewCellBorderStyle.SingleHorizontal,RowHeadersVisible=false,
            AllowUserToAddRows=false,AllowUserToDeleteRows=false,AllowUserToResizeRows=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode=DataGridViewSelectionMode.FullRowSelect,EnableHeadersVisualStyles=false,ColumnHeadersHeight=30};
        grid.RowTemplate.Height=42;grid.RowTemplate.MinimumHeight=42;grid.ColumnHeadersDefaultCellStyle.BackColor=KnightUi.Paper;grid.ColumnHeadersDefaultCellStyle.ForeColor=KnightUi.Steel;
        grid.ColumnHeadersBorderStyle=DataGridViewHeaderBorderStyle.None;grid.ColumnHeadersHeight=40;
        grid.DefaultCellStyle.Padding=new Padding(6,3,4,3);grid.ColumnHeadersDefaultCellStyle.Padding=new Padding(6,0,4,0);
        grid.DefaultCellStyle.BackColor=KnightUi.Surface;grid.DefaultCellStyle.ForeColor=KnightUi.Ink;
        grid.DefaultCellStyle.SelectionBackColor=KnightUi.Ink;grid.DefaultCellStyle.SelectionForeColor=KnightUi.OnDark;return grid;
    }
    void MarkDirty()
    {if(loading||loadFailed||store.ReadOnly)return;dirty=true;status.Text="正在编辑 · 稍后自动保存";saveTimer.Stop();saveTimer.Start();}
    void PlanTextChanged(object sender,EventArgs e) {MarkDirty();}
    internal bool FlushChanges(bool finishEdit=true)
    {
        saveTimer.Stop();if(finishEdit)plans.EndEdit();else if(plans.IsCurrentCellDirty)plans.CommitEdit(DataGridViewDataErrorContexts.Commit);
        if(!dirty)return !loadFailed;
        try
        {
            var day=new PetCalendarDay {Date=PetCalendarStore.DateKey(selected),Journal=journal.Text};
            foreach(DataGridViewRow row in plans.Rows)
            {
                string text=Convert.ToString(row.Cells[1].Value).Trim();if(text.Length==0)continue;
                string start=Convert.ToString(row.Cells[2].Value).Trim(),end=Convert.ToString(row.Cells[3].Value).Trim();
                day.Plans.Add(new PetCalendarPlan {Text=text,Done=Convert.ToBoolean(row.Cells[0].Value??false),Start=start.Length==0 ? null : start,End=end.Length==0 ? null : end});
            }
            store.Save(selected,day);dirty=false;RefreshViews();status.Text="已保存 · "+DateTime.Now.ToString("HH:mm:ss")+"   修改会自动保存";return true;
        }
        catch(Exception error){if(!PetCalendarStore.IsDataError(error))throw;status.Text="保存失败，内容仍保留："+error.Message;return false;}
        finally{saveTimer.Stop();}
    }
    internal bool SelectDate(DateTime date)
    {
        date=date.Date;if(date<datePicker.MinDate.Date||date>datePicker.MaxDate.Date)return false;
        if(selected!=default(DateTime)&&!FlushChanges()&&dirty)
        {loading=true;datePicker.Value=selected;loading=false;return false;}
        loading=true;
        try
        {
            selected=date;month=new DateTime(date.Year,date.Month,1);datePicker.Value=date;
            plans.Rows.Clear();journal.Clear();loadFailed=false;
            try
            {
                var day=store.Load(date);foreach(var plan in day.Plans)plans.Rows.Add(plan.Done,plan.Text,plan.Start,plan.End);journal.Text=day.Journal;
                status.Text=store.ReadOnly ? "预览模式 · 不修改日历数据" : day.HasContent ? "已加载当天记录 · 修改会自动保存" : "这一天还没有记录 · 双击日历或点击新建计划";
            }
            catch(Exception error){if(!PetCalendarStore.IsDataError(error))throw;loadFailed=true;status.Text="无法读取，原文件已保留："+error.Message;}
            plans.Enabled=journal.Enabled=add.Enabled=remove.Enabled=save.Enabled=!loadFailed&&!store.ReadOnly;
            dirty=false;RefreshViews();return true;
        }
        finally{loading=false;}
    }
    internal void AddPlan(int minutes)
    {
        if(loadFailed||store.ReadOnly)return;
        int row=plans.Rows.Add(false,"",minutes<0 ? null : PetCalendarStore.Clock(minutes),minutes<0 ? null : PetCalendarStore.Clock(Math.Min(1440,minutes+30)));
        plans.CurrentCell=plans.Rows[row].Cells[1];plans.Focus();plans.BeginEdit(true);
    }
    internal bool SetView(CalendarView value)
    {
        if(!FlushChanges()&&dirty)return false;
        view=value;monthPage.Visible=value==CalendarView.Month;timelinePage.Visible=value!=CalendarView.Month&&value!=CalendarView.List;listHost.Visible=value==CalendarView.List;
        for(int i=0;i<viewButtons.Count;i++){viewButtons[i].Primary=i==(int)value;viewButtons[i].Invalidate();}
        if(value==CalendarView.Month)monthPage.BringToFront();else if(value==CalendarView.List)listHost.BringToFront();else timelinePage.BringToFront();
        RefreshViews();QueueTimelineScroll();return true;
    }
    void QueueTimelineScroll()
    {if(IsHandleCreated&&Visible&&view!=CalendarView.Month&&view!=CalendarView.List)BeginInvoke((Action)(()=>{if(!IsDisposed&&view!=CalendarView.Month&&view!=CalendarView.List)timelineHost.AutoScrollPosition=new Point(0,8*timeline.HourHeight);}));}
    protected override void OnVisibleChanged(EventArgs e){base.OnVisibleChanged(e);if(timelineHost!=null)QueueTimelineScroll();}
    internal void MovePeriod(int offset)
    {
        DateTime target;
        try{target=view==CalendarView.Month||view==CalendarView.List ? selected.AddMonths(offset) : selected.AddDays(offset*(view==CalendarView.Day ? 1 : 7));}
        catch(ArgumentOutOfRangeException){return;}
        SelectDate(target);
    }
    PetCalendarDay FilterDay(PetCalendarDay day)
    {
        return new PetCalendarDay {Date=day.Date,Journal=showJournal.Checked&&stateFilter.SelectedIndex==0 ? day.Journal : "",
            Plans=day.Plans.Where(ShowPlan).ToList()};
    }
    bool ShowPlan(PetCalendarPlan plan){return showPlans.Checked&&(stateFilter.SelectedIndex==0||stateFilter.SelectedIndex==1&&!plan.Done||stateFilter.SelectedIndex==2&&plan.Done);}
    void LayoutMonth()
    {
        if(monthGrid==null||month==default(DateTime))return;
        int offset=((int)month.DayOfWeek+6)%7,weeks=(offset+DateTime.DaysInMonth(month.Year,month.Month)+6)/7;
        monthGrid.Height=Math.Max(monthPage.ClientSize.Height,(int)((36+weeks*112)*DeviceDpi/96f));
    }
    void RefreshViews()
    {
        if(selected==default(DateTime)||refreshing)return;refreshing=true;
        try
        {
            DateTime first=month.AddDays(-((int)month.DayOfWeek+6)%7);int records=0,total=0,done=0,errors=0;
            int weeks=((month-first).Days+DateTime.DaysInMonth(month.Year,month.Month)+6)/7;
            monthGrid.SuspendLayout();
            for(int row=1;row<=6;row++)
            {
                monthGrid.RowStyles[row].SizeType=row<=weeks ? SizeType.Percent : SizeType.Absolute;
                monthGrid.RowStyles[row].Height=row<=weeks ? 100f/weeks : 0;
            }
            for(int i=0;i<days.Length;i++)
            {
                var cell=days[i];cell.Date=first.AddDays(i);cell.InMonth=cell.Date.Month==month.Month;cell.Selected=cell.Date==selected;cell.Failed=false;cell.Day=null;cell.SourceIndices=null;
                cell.Visible=i<weeks*7;
                cell.Enabled=cell.Date>=datePicker.MinDate.Date&&cell.Date<=datePicker.MaxDate.Date;
                try
                {
                    var day=store.Load(cell.Date);cell.Day=FilterDay(day);cell.SourceIndices=Enumerable.Range(0,day.Plans.Count).Where(index=>ShowPlan(day.Plans[index])).ToList();
                    if(cell.InMonth){if(day.HasContent)records++;total+=day.Plans.Count;done+=day.Plans.Count(plan=>plan.Done);}
                }
                catch(Exception error){if(!PetCalendarStore.IsDataError(error))throw;cell.Failed=true;if(cell.InMonth)errors++;}
                cell.AccessibleName=cell.Date.ToString("yyyy年M月d日")+(cell.Failed ? "，读取失败" : "，"+(cell.Day==null ? 0 : cell.Day.Plans.Count)+"项计划");
                string tip=cell.AccessibleName;
                if(cell.Day!=null)tip+="\n"+String.Join("\n",cell.Day.Plans.Select(plan=>(plan.Done ? "✓ " : "○ ")+plan.Text))+"\n"+cell.Day.Journal;
                dayTips.SetToolTip(cell,tip.Substring(0,Math.Min(tip.Length,800)));cell.Invalidate();
            }
            monthGrid.ResumeLayout();
            LayoutMonth();
            monthSummary.Text="本月记录 "+records+" 天    ·    计划完成 "+done+" / "+total+(errors>0 ? "    ·    "+errors+" 天读取失败" : "");
            previous.Text=view==CalendarView.Day ? "‹ 前一天" : view==CalendarView.Week||view==CalendarView.WorkWeek ? "‹ 上一周" : "‹ 上个月";
            next.Text=view==CalendarView.Day ? "后一天 ›" : view==CalendarView.Week||view==CalendarView.WorkWeek ? "下一周 ›" : "下个月 ›";
            monthLabel.Text=view==CalendarView.Day ? selected.ToString("yyyy 年 M 月 d 日") : month.ToString("yyyy 年 M 月");
            if(view==CalendarView.List){if(!preserveList)RefreshList();}
            else if(view!=CalendarView.Month)
            {
                timeline.FirstDate=view==CalendarView.Day ? selected : selected.AddDays(-((int)selected.DayOfWeek+6)%7);
                timeline.DayCount=view==CalendarView.Day ? 1 : view==CalendarView.WorkWeek ? 5 : 7;timeline.Data.Clear();
                timeline.IncludePlans=showPlans.Checked;timeline.IncludeJournal=showJournal.Checked;timeline.State=stateFilter.SelectedIndex;
                for(int i=0;i<timeline.DayCount;i++)
                {
                    DateTime date=timeline.FirstDate.AddDays(i);
                    try
                    {
                        // Preserve source row indices even when a filter hides
                        // cards, so opening one always edits the original row.
                        var day=store.Load(date);timeline.Data[date]=day;
                    }
                    catch(Exception error){if(!PetCalendarStore.IsDataError(error))throw;status.Text="部分日期读取失败："+error.Message;}
                }
                RefreshTimelineHeader();
                timeline.Invalidate();
                monthLabel.Text=timeline.FirstDate.ToString("M/d")+" – "+timeline.FirstDate.AddDays(timeline.DayCount-1).ToString("M/d");
            }
            undo.Enabled=!store.ReadOnly&&store.CanUndo;
        }
        finally{refreshing=false;}
    }
    void RefreshTimelineHeader()
    {
        timelineHeader.SuspendLayout();
        foreach(Control child in timelineHeader.Controls.Cast<Control>().ToArray()){timelineHeader.Controls.Remove(child);child.Dispose();}
        timelineHeader.ColumnStyles.Clear();timelineHeader.ColumnCount=timeline.DayCount+1;
        timelineHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,timeline.GutterWidth));
        timelineHeader.Controls.Add(new Label {Text="全天",Font=compactFont,Dock=DockStyle.Fill,Margin=Padding.Empty,TextAlign=ContentAlignment.MiddleCenter},0,0);
        for(int i=0;i<timeline.DayCount;i++)
        {
            DateTime date=timeline.FirstDate.AddDays(i);timelineHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f/timeline.DayCount));
            var lines=new List<string> {date.ToString("ddd M/d")};PetCalendarDay day;
            if(timeline.Data.TryGetValue(date,out day))
            {
                var entries=FilterDay(day).Plans.Where(plan=>String.IsNullOrEmpty(plan.Start)).ToList();
                lines.AddRange(entries.Take(2).Select(plan=>(plan.Done ? "✓ " : "")+plan.Text));
                if(entries.Count>2)lines.Add("+"+(entries.Count-2)+" 项");
            }
            var button=new CalendarHeaderButton {Lines=lines.ToArray(),Font=compactFont,Dock=DockStyle.Fill,FlatStyle=FlatStyle.Flat,Margin=Padding.Empty,AllowDrop=true,BackColor=date==DateTime.Today ? Color.FromArgb(238,238,237) : KnightUi.Paper,
                AccessibleName=date.ToString("yyyy年M月d日")+"全天计划"};button.FlatAppearance.BorderColor=KnightUi.Silver;
            button.Click+=(s,e)=>SelectDate(date);button.MouseDoubleClick+=(s,e)=>{if(SelectDate(date))AddPlan(-1);};
            button.DragEnter+=(s,e)=>e.Effect=e.Data.GetDataPresent(typeof(CalendarEntry)) ? DragDropEffects.Move : DragDropEffects.None;
            button.DragDrop+=(s,e)=>{var entry=e.Data.GetData(typeof(CalendarEntry)) as CalendarEntry;if(entry!=null)MoveTimelineEntry(entry,date,-1);};
            timelineHeader.Controls.Add(button,i+1,0);
        }
        timelineHeader.ResumeLayout();
    }
    void RefreshList()
    {
        allEntries.Rows.Clear();List<string> errors;
        try
        {
            var entries=store.Query(search.Text,stateFilter.SelectedIndex,showPlans.Checked,showJournal.Checked,out errors);
            foreach(var entry in entries)
            {
                string state=entry.Index<0 ? "" : (entry.Done ? "已完成" : "未完成")+(String.IsNullOrEmpty(entry.Start) ? " · 全天" : " "+entry.Start);
                int row=allEntries.Rows.Add(PetCalendarStore.DateKey(entry.Date),entry.Index<0 ? "记录" : "计划",state,entry.Text.Replace("\r"," ").Replace("\n"," "));
                allEntries.Rows[row].Tag=entry;allEntries.Rows[row].Cells[3].ToolTipText=entry.Text;
            }
            allEntries.ClearSelection();listSummary.Text="共 "+entries.Count+" 条 · Ctrl / Shift 多选"+(errors.Count>0 ? " · "+errors.Count+" 个文件读取失败" : "");
            if(errors.Count>0){status.Text="已跳过读取失败的文件，原件已保留："+errors[0];dayTips.SetToolTip(status,String.Join("\n",errors));}
        }
        catch(Exception error){if(!PetCalendarStore.IsDataError(error))throw;status.Text="无法加载全部列表："+error.Message;}
    }
    internal void OpenListEntry(int row,bool focus=true)
    {
        if(row<0||row>=allEntries.Rows.Count)return;var entry=allEntries.Rows[row].Tag as CalendarEntry;if(entry==null)return;
        preserveList=true;
        try
        {
            if(!SelectDate(entry.Date))return;
            if(entry.Index>=0&&entry.Index<plans.Rows.Count&&Convert.ToString(plans.Rows[entry.Index].Cells[1].Value)==entry.Text)
            {plans.CurrentCell=plans.Rows[entry.Index].Cells[1];if(focus)plans.Focus();}
            else if(entry.Index<0&&focus)journal.Focus();
        }
        finally{preserveList=false;}
    }
    internal int ApplySelection(CalendarBatchAction action,DateTime target)
    {
        var selection=allEntries.SelectedRows.Cast<DataGridViewRow>().Select(row=>row.Tag as CalendarEntry).Where(entry=>entry!=null).ToList();
        if(store.ReadOnly){status.Text="预览模式不修改日历数据";return 0;}
        if(selection.Count==0){status.Text="请先在全部列表中选择要操作的条目（Ctrl / Shift 多选）";return 0;}
        // Finish even an apparently clean inline edit before changing files;
        // reloading after a batch must never commit a stale editor buffer.
        if(!FlushChanges()&&dirty)return 0;
        try
        {
            int count=store.ApplyBatch(selection,action,target);SelectDate(selected);undo.Enabled=store.CanUndo;
            status.Text="已处理 "+count+" 条"+(action==CalendarBatchAction.Delete ? "，可点击“撤销批量”恢复" : "；完成、重开和移动仅应用于计划");return count;
        }
        catch(Exception error){if(!PetCalendarStore.IsDataError(error))throw;RefreshViews();status.Text="批量操作失败："+error.Message;dayTips.SetToolTip(status,status.Text);return 0;}
    }
    void BatchCommand(string command)
    {
        if(command=="全选"){allEntries.SelectAll();return;}if(command=="取消选择"){allEntries.ClearSelection();return;}
        if(command=="移动到…")
        {
            if(store.ReadOnly)return;
            using(var dialog=new Form {Text="移动选中的计划",ClientSize=new Size(340,132),StartPosition=FormStartPosition.CenterParent,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false,Font=Font,BackColor=KnightUi.Paper})
            {
                var picker=new DateTimePicker {Format=DateTimePickerFormat.Long,Value=selected,Bounds=new Rectangle(18,18,302,32)};
                var apply=Button("移动计划",128);apply.Location=new Point(192,76);apply.DialogResult=DialogResult.OK;
                var cancel=Button("取消",96);cancel.Location=new Point(88,76);cancel.DialogResult=DialogResult.Cancel;
                dialog.Controls.AddRange(new Control[] {picker,apply,cancel});dialog.AcceptButton=apply;dialog.CancelButton=cancel;
                if(dialog.ShowDialog(this)==DialogResult.OK)ApplySelection(CalendarBatchAction.Move,picker.Value);
            }
        }
        else ApplySelection(command=="完成" ? CalendarBatchAction.Complete : command=="重开" ? CalendarBatchAction.Reopen : CalendarBatchAction.Delete,selected);
    }
    internal bool UndoBatch()
    {
        if(!FlushChanges()&&dirty)return false;
        try{bool restored=store.UndoBatch();SelectDate(selected);status.Text=restored ? "已撤销上一次批量操作" : "没有可撤销的批量操作";return restored;}
        catch(Exception error){if(!PetCalendarStore.IsDataError(error))throw;status.Text="无法撤销："+error.Message;return false;}
    }
    internal bool MoveTimelineEntry(CalendarEntry entry,DateTime date,int minutes)
    {
        if(store.ReadOnly)return false;
        if(!FlushChanges()&&dirty)return false;
        try
        {
            store.ApplyBatch(new[] {entry},CalendarBatchAction.Move,date,minutes);SelectDate(date);status.Text="已调整计划日期 / 时间，可点击“撤销批量”恢复";return true;
        }
        catch(Exception error){if(!PetCalendarStore.IsDataError(error))throw;status.Text="无法移动计划："+error.Message;return false;}
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if(e.Control&&e.KeyCode==Keys.S)FlushChanges();
        else if(e.Control&&e.KeyCode==Keys.F){search.Focus();search.SelectAll();}
        else if(e.Control&&e.KeyCode==Keys.N)AddPlan(-1);
        else if(e.Control&&e.Alt&&e.KeyCode>=Keys.D1&&e.KeyCode<=Keys.D5)SetView((CalendarView)(e.KeyCode-Keys.D1));
        else{base.OnKeyDown(e);return;}
        e.Handled=e.SuppressKeyPress=true;
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if(!FlushChanges()&&dirty){e.Cancel=true;KnightUi.ShowError(this,"记录尚未保存","保存失败，窗口保持打开。请检查项目目录与计划时间，再点击“保存当天记录”。");}
        base.OnFormClosing(e);
    }
    protected override void Dispose(bool disposing)
    {if(disposing){saveTimer.Dispose();searchTimer.Dispose();dayTips.Dispose();}base.Dispose(disposing);if(disposing){uiFont.Dispose();compactFont.Dispose();calendarFont.Dispose();}}
}
