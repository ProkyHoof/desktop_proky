using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.Windows.Forms;

internal sealed class WardrobePreview : Control
{
    internal BoundPetScene Scene;
    internal double Time;
    internal bool Resting;
    internal WardrobePreview(){DoubleBuffered=true;ResizeRedraw=true;BackColor=KnightUi.Surface;AccessibleName="原版骑士与发型的动态预览";}
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);if(Scene==null)return;
        var g=e.Graphics;
        using(var fill=new LinearGradientBrush(ClientRectangle,Color.FromArgb(249,249,252),Color.FromArgb(232,237,242),90))g.FillRectangle(fill,ClientRectangle);
        float scale=Math.Max(.1f,Math.Min((Width-28)/352f,(Height-24)/306f));
        var saved=g.Save();g.TranslateTransform((Width-352*scale)/2,(Height-306*scale)/2-110*scale);g.ScaleTransform(scale,scale);
        Scene.Draw(g,Time,Resting,true);g.Restore(saved);
    }
}

internal sealed class PetWardrobeWindow : KnightWindow
{
    readonly KeyboardPetSprites sprites;
    readonly BoundPetScene scene;
    readonly InputFeedbackState input=new InputFeedbackState();
    readonly WardrobePreview preview=new WardrobePreview();
    readonly ComboBox hair=new ComboBox(),mode=new ComboBox();
    readonly Label status=new Label();
    readonly Font uiFont=new Font("Microsoft YaHei UI",14,FontStyle.Regular,GraphicsUnit.Pixel);
    readonly Action<int> apply;
    readonly bool readOnly;
    readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
    readonly Stopwatch clock=Stopwatch.StartNew();
    bool released;
    double previous,nextTap;
    internal PetWardrobeWindow(string root,int hairStyle,Action<int> apply,bool readOnly=false)
    {
        this.apply=apply;this.readOnly=readOnly;Font=uiFont;Text="皮肤";
        ClientSize=new Size(860,540);MinimumSize=new Size(800,500);StartPosition=FormStartPosition.CenterScreen;
        sprites=new KeyboardPetSprites(root);scene=new BoundPetScene(sprites,new PetInputBindings(),input);scene.SetViewWidth(BoundPetScene.CompactWidth);
        preview.Scene=scene;
        var layout=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Padding=new Padding(18,14,18,16)};
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,324));layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        var left=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=6,Padding=new Padding(0,0,14,0)};
        foreach(float height in new[]{32f,82f,62f,0f,48f,64f})left.RowStyles.Add(height==0 ? new RowStyle(SizeType.Percent,100) : new RowStyle(SizeType.Absolute,height));
        left.Controls.Add(new Label {Text="当前皮肤 · 原版骑士",Dock=DockStyle.Fill,ForeColor=KnightUi.Ink},0,0);
        left.Controls.Add(new Label {Text="白发、红围巾与原装键鼠。\n选择发型后可在右侧预览。",Dock=DockStyle.Fill,ForeColor=KnightUi.Steel},0,1);
        var fields=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=Padding.Empty};
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,70));fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        hair.DropDownStyle=ComboBoxStyle.DropDownList;hair.Dock=DockStyle.Fill;hair.Items.AddRange(new object[]{"利落短发","层次长发"});hair.AccessibleName="选择发型";
        hair.SelectedIndex=PetHairStyles.Normalize(hairStyle);hair.SelectedIndexChanged+=(s,e)=>UpdatePreview();
        fields.Controls.Add(new Label {Text="发型",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,0);fields.Controls.Add(hair,1,0);left.Controls.Add(fields,0,2);
        var buttons=new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=false,Margin=Padding.Empty};
        var wear=new KnightButton {Text="应用并保存",Primary=true,Size=new Size(142,36),Margin=new Padding(0,4,8,0),Enabled=!readOnly};
        wear.Click+=(s,e)=>ApplySelection();var close=new KnightButton {Text="关闭",Size=new Size(142,36),Margin=new Padding(0,4,0,0)};close.Click+=(s,e)=>Close();
        buttons.Controls.AddRange(new Control[]{wear,close});left.Controls.Add(buttons,0,4);
        status.Dock=DockStyle.Fill;status.ForeColor=KnightUi.Steel;status.Text=readOnly ? "预览模式，不保存个人设置。" : "预览满意后点击“应用并保存”。";left.Controls.Add(status,0,5);
        var right=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(8,0,0,0)};
        right.RowStyles.Add(new RowStyle(SizeType.Absolute,32));right.RowStyles.Add(new RowStyle(SizeType.Percent,100));right.RowStyles.Add(new RowStyle(SizeType.Absolute,43));
        right.Controls.Add(new Label {Text="动态预览 · 原版骑士",Dock=DockStyle.Fill,ForeColor=KnightUi.Steel},0,0);
        preview.Dock=DockStyle.Fill;right.Controls.Add(preview,0,1);
        mode.DropDownStyle=ComboBoxStyle.DropDownList;mode.Dock=DockStyle.Fill;mode.Items.AddRange(new object[]{"预览：待机","预览：打字与头盔","预览：音乐律动","预览：休息"});mode.SelectedIndex=0;
        mode.SelectedIndexChanged+=(s,e)=>{input.Clear();nextTap=clock.Elapsed.TotalSeconds;};right.Controls.Add(mode,0,2);
        layout.Controls.Add(left,0,0);layout.Controls.Add(right,1,0);Controls.Add(layout);layout.BringToFront();
        UpdatePreview();timer.Interval=50;timer.Tick+=Tick;timer.Start();
    }
    void UpdatePreview()
    {
        scene.Portrait.SetHairStyle(hair.SelectedIndex);preview.Invalidate();
        status.Text=readOnly ? "预览模式，不保存个人设置。" : "正在预览："+hair.SelectedItem+"；应用后生效。";
        status.ForeColor=KnightUi.Steel;
    }
    internal void SelectHairStyle(int style){hair.SelectedIndex=PetHairStyles.Normalize(style);}
    internal void ApplySelection()
    {
        if(readOnly)return;
        try {apply(hair.SelectedIndex);status.Text="已应用并保存："+hair.SelectedItem;status.ForeColor=KnightUi.Steel;}
        catch(Exception error){status.Text="未保存："+error.Message;status.ForeColor=KnightUi.Red;}
    }
    void Tick(object sender,EventArgs e)
    {
        double now=clock.Elapsed.TotalSeconds,dt=Math.Min(.1,Math.Max(0,now-previous));previous=now;
        bool typing=mode.SelectedIndex==1,resting=mode.SelectedIndex==3;
        if(typing&&now>=nextTap){int code=(int)(now*7)%2==0 ? 65 : 74;input.Key(code,true,now);input.Key(code,false,now+.003);nextTap=now+.14;}
        scene.MusicPlaying=mode.SelectedIndex==2;scene.MusicLevel=scene.MusicPlaying ? (float)(.16+.09*Math.Sin(now*3.2)) : 0;
        scene.Advance(now,dt,resting);preview.Time=now;preview.Resting=resting;preview.Invalidate();
    }
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);var work=Screen.FromControl(this).WorkingArea;
        int width=Math.Min(Width,work.Width-24),height=Math.Min(Height,work.Height-24);
        if(width!=Width||height!=Height)
        {
            MinimumSize=new Size(Math.Min(MinimumSize.Width,width),Math.Min(MinimumSize.Height,height));Size=new Size(width,height);
            Location=new Point(work.Left+(work.Width-Width)/2,work.Top+(work.Height-Height)/2);
        }
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing&&!released){released=true;timer.Stop();timer.Dispose();preview.Scene=null;scene.Dispose();sprites.Dispose();}
        base.Dispose(disposing);if(disposing)uiFont.Dispose();
    }
}
