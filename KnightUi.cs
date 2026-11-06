using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// The same ceramic armor, graphite seams and ruby visor as the pet's helmet.
internal static class KnightUi
{
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
    static Icon windowIcon;
    internal static Icon WindowIcon
    {
        get
        {
            if(windowIcon!=null)return windowIcon;
            using(var bitmap=new Bitmap(32,32))using(var g=Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);g.SmoothingMode=SmoothingMode.AntiAlias;
                using(var plate=Plate(new RectangleF(1,1,30,30),5))using(var fill=new SolidBrush(Ink))g.FillPath(fill,plate);
                KnightIcons.Draw(g,KnightIcon.Shield,new RectangleF(7,4,18,23),Color.White);
                using(var p=new Pen(Red,2))g.DrawLine(p,9,27,23,27);
                IntPtr handle=bitmap.GetHicon();try{windowIcon=(Icon)Icon.FromHandle(handle).Clone();}finally{DestroyIcon(handle);}
            }
            return windowIcon;
        }
    }
    [DllImport("uxtheme.dll",CharSet=CharSet.Unicode)] static extern int SetWindowTheme(IntPtr window,string app,string classes);
    internal static void FlatNative(Control control){control.HandleCreated+=(s,e)=>SetWindowTheme(control.Handle,"","");}
    internal static Color Ink { get { return SystemInformation.HighContrast ? SystemColors.WindowText : Color.FromArgb(26, 26, 29); } }
    internal static Color Paper { get { return SystemInformation.HighContrast ? SystemColors.Window : Color.FromArgb(246, 246, 245); } }
    internal static Color Surface { get { return SystemInformation.HighContrast ? SystemColors.Window : Color.White; } }
    internal static Color Silver { get { return SystemInformation.HighContrast ? SystemColors.ControlDark : Color.FromArgb(216, 216, 216); } }
    internal static Color Steel { get { return SystemInformation.HighContrast ? SystemColors.GrayText : Color.FromArgb(108, 108, 112); } }
    internal static Color Red { get { return SystemInformation.HighContrast ? SystemColors.Highlight : Color.FromArgb(196, 33, 51); } }
    internal static Color Ruby { get { return SystemInformation.HighContrast ? SystemColors.Highlight : Color.FromArgb(221, 58, 73); } }
    internal static Color OnDark { get { return SystemInformation.HighContrast ? SystemColors.HighlightText : Color.White; } }
    internal static Color Alpha(Color c, int alpha) { return Color.FromArgb(Math.Max(0, Math.Min(255, alpha)), c); }

    internal static GraphicsPath Plate(RectangleF r, float cut)
    {
        cut = Math.Min(cut, Math.Min(r.Width, r.Height) / 3);
        var path = new GraphicsPath();
        path.AddPolygon(new[] { new PointF(r.Left+cut,r.Top), new PointF(r.Right-cut,r.Top),
            new PointF(r.Right,r.Top+cut), new PointF(r.Right,r.Bottom-cut), new PointF(r.Right-cut,r.Bottom),
            new PointF(r.Left+cut,r.Bottom), new PointF(r.Left,r.Bottom-cut), new PointF(r.Left,r.Top+cut) });
        return path;
    }
    internal static void Armor(Graphics g, RectangleF r, int alpha, bool dark, float cut)
    {
        var state = g.Save();
        try
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            r.Inflate(-1, -1);
            using (var plate = Plate(r, cut))
            using (var fill = new SolidBrush(Alpha(dark ? Ink : Surface, alpha)))
            using (var edge = new Pen(Alpha(dark ? Ink : Silver, alpha), 1f))
            { g.FillPath(fill, plate); g.DrawPath(edge, plate); }
            var inner = r; inner.Inflate(-2, -2);
            using (var plate = Plate(inner, Math.Max(2, cut-1)))
            using (var shine = new Pen(Alpha(dark ? Color.FromArgb(52, 52, 55) : Surface, alpha), 1))
                g.DrawPath(shine, plate);
        }
        finally { g.Restore(state); }
    }
    internal static void Gem(Graphics g, RectangleF r, int alpha)
    {
        var state=g.Save(); g.SmoothingMode=SmoothingMode.AntiAlias;
        float cx=r.X+r.Width/2,cy=r.Y+r.Height/2;
        var points=new[] {new PointF(cx,r.Top),new PointF(r.Right,cy),new PointF(cx,r.Bottom),new PointF(r.Left,cy)};
        using(var fill=new SolidBrush(Alpha(Red,alpha)))g.FillPolygon(fill,points);
        using(var light=new SolidBrush(Alpha(Ruby,alpha)))g.FillPolygon(light,new[] {points[0],points[1],points[2],new PointF(cx,cy)});
        using(var glint=new Pen(Alpha(Color.FromArgb(255,198,213),alpha),1))g.DrawLines(glint,new[] {points[3],points[0],new PointF(cx,cy)});
        using(var edge=new Pen(Alpha(Ink,alpha),1))g.DrawPolygon(edge,points);
        g.Restore(state);
    }
    internal static void Visor(Graphics g, RectangleF r)
    {
        var state=g.Save();g.SmoothingMode=SmoothingMode.AntiAlias;
        float mid=r.X+r.Width/2;
        using(var black=new Pen(Ink,5))g.DrawLines(black,new[] {new PointF(r.Left,r.Top),new PointF(mid,r.Bottom),new PointF(r.Right,r.Top)});
        using(var red=new Pen(Red,2.6f))g.DrawLines(red,new[] {new PointF(r.Left,r.Top),new PointF(mid,r.Bottom),new PointF(r.Right,r.Top)});
        using(var shine=new Pen(Ruby,1))g.DrawLines(shine,new[] {new PointF(r.Left+2,r.Top),new PointF(mid,r.Bottom-1),new PointF(r.Right-2,r.Top)});
        g.Restore(state);
    }
    internal static void ShowError(IWin32Window owner, string title, string message)
    {
        using(var dialog=ErrorDialog(title,message))
        {
            if(owner==null)dialog.ShowDialog();else dialog.ShowDialog(owner);
        }
    }
    internal static KnightDialog ErrorDialog(string title,string message)
    {
        var dialog=new KnightDialog {Text=title,Caption=title,Subtitle="请检查后重试",ClientSize=new Size(520,310)};
        var body=new TextBox {Multiline=true,ReadOnly=true,BorderStyle=BorderStyle.None,ScrollBars=ScrollBars.Vertical,
            BackColor=Paper,ForeColor=Ink,Bounds=new Rectangle(28,108,464,128),Text=message,TabStop=false};
        var close=new KnightButton {Text="知道了",Primary=true,Bounds=new Rectangle(374,254,118,36),DialogResult=DialogResult.OK};
        dialog.Controls.AddRange(new Control[] {body,close});dialog.AcceptButton=dialog.CancelButton=close;
        return dialog;
    }
}

internal class KnightDialog : Form
{
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr handle,int message,IntPtr wParam,IntPtr lParam);
    readonly Font uiFont=new Font("Microsoft YaHei UI",14,FontStyle.Regular,GraphicsUnit.Pixel);
    internal string Caption="白发小骑士",Subtitle="桌宠设置";
    internal KnightDialog()
    {
        Font=uiFont;BackColor=KnightUi.Paper;ForeColor=KnightUi.Ink;Icon=KnightUi.WindowIcon;
        DoubleBuffered=true;FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;
        AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;
        StartPosition=FormStartPosition.CenterParent;
        var close=new KnightButton {Text="关闭窗口",IconKind=KnightIcon.Close,IconOnly=true,ChromeStyle=true,AccessibleName="关闭窗口",Size=new Size(32,30),Anchor=AnchorStyles.Top|AnchorStyles.Right,TabStop=true,TabIndex=5};
        Controls.Add(close);close.Click+=(s,e)=>Close();
        SizeChanged+=(s,e)=>close.Location=new Point(ClientSize.Width-48,20);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);var g=e.Graphics;float scale=DeviceDpi/96f;
        var state=g.Save();g.ScaleTransform(scale,scale);
        float w=ClientSize.Width/scale,h=ClientSize.Height/scale;
        using(var fill=new SolidBrush(KnightUi.Paper))g.FillRectangle(fill,0,0,w,h);
        using(var edge=new Pen(KnightUi.Ink))g.DrawRectangle(edge,0,0,w-1,h-1);
        using(var fill=new SolidBrush(KnightUi.Ink))g.FillRectangle(fill,1,1,w-2,83);
        KnightIcons.Draw(g,KnightIcon.Shield,new RectangleF(25,27,24,24),KnightUi.OnDark);
        using(var title=new Font(Font.FontFamily,19,FontStyle.Bold,GraphicsUnit.Pixel))
        using(var sub=new Font(Font.FontFamily,11.5f,FontStyle.Regular,GraphicsUnit.Pixel))
        using(var ink=new SolidBrush(KnightUi.OnDark))
        {g.DrawString(Caption,title,ink,63,21);g.DrawString(Subtitle,sub,ink,64,51);}
        using(var pen=new Pen(KnightUi.Red,2))g.DrawLine(pen,25,77,77,77);
        using(var pen=new Pen(KnightUi.Silver,1))g.DrawLine(pen,24,h-70,w-24,h-70);
        g.Restore(state);
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if(e.Button==MouseButtons.Left&&e.Y<84*DeviceDpi/96f)
        {ReleaseCapture();SendMessage(Handle,0x00A1,new IntPtr(2),IntPtr.Zero);}
    }
    protected override void Dispose(bool disposing){base.Dispose(disposing);if(disposing)uiFont.Dispose();}
}

internal sealed class KnightButton : Button
{
    internal bool Primary;
    internal KnightIcon IconKind;
    internal bool IconOnly;
    internal bool ChromeStyle;
    bool hovered,pressed;
    internal KnightButton()
    {
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.SupportsTransparentBackColor,true);
        FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;Cursor=Cursors.Hand;UseVisualStyleBackColor=false;
        BackColor=Color.Transparent;
    }
    protected override void OnMouseEnter(EventArgs e){hovered=true;Invalidate();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){hovered=pressed=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnMouseDown(MouseEventArgs e){pressed=e.Button==MouseButtons.Left;Invalidate();base.OnMouseDown(e);}
    protected override void OnMouseUp(MouseEventArgs e){pressed=false;Invalidate();base.OnMouseUp(e);}
    protected override void OnGotFocus(EventArgs e){Invalidate();base.OnGotFocus(e);}
    protected override void OnLostFocus(EventArgs e){Invalidate();base.OnLostFocus(e);}
    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics;float scale=DeviceDpi/96f;var state=g.Save();g.SmoothingMode=SmoothingMode.AntiAlias;
        bool dark=Enabled&&(Primary||pressed||ChromeStyle);
        Color fill=!Enabled ? KnightUi.Paper : dark ? KnightUi.Ink : hovered ? Color.FromArgb(237,237,237) : KnightUi.Surface;
        if(ChromeStyle&&hovered)fill=IconKind==KnightIcon.Close?KnightUi.Red:Color.FromArgb(54,54,57);
        Color ink=!Enabled ? KnightUi.Steel : dark ? KnightUi.OnDark : KnightUi.Ink;
        using(var plate=KnightUi.Plate(new RectangleF(.5f,pressed?1.5f:.5f,Width-1,Height-2),3*scale))
        using(var brush=new SolidBrush(fill))
        using(var edge=new Pen(Enabled&&(hovered||Focused) ? KnightUi.Ink : dark ? KnightUi.Ink : KnightUi.Silver))
        {g.FillPath(brush,plate);g.DrawPath(edge,plate);}
        if(Enabled&&Primary)using(var pen=new Pen(KnightUi.Red,2*scale))g.DrawLine(pen,2*scale,8*scale,2*scale,Height-9*scale);
        if(Focused&&ShowFocusCues)using(var pen=new Pen(KnightUi.Red))g.DrawRectangle(pen,3,3,Width-7,Height-8);
        string label=Text.Replace("＋ ","").Replace("‹ ","").Replace(" ›","");
        KnightIcon icon=IconKind==KnightIcon.None ? KnightIcons.ForText(Text) : IconKind;
        int iconSize=(int)(16*scale),gap=(int)(7*scale);
        int textWidth=TextRenderer.MeasureText(label,Font,Size.Empty,TextFormatFlags.NoPadding).Width;
        bool drawIcon=icon!=KnightIcon.None&&(IconOnly||textWidth+iconSize+gap+16*scale<=Width);
        var textRect=new Rectangle(6,pressed?1:0,Width-12,Height-1);
        if(drawIcon)
        {
            int x=IconOnly ? (Width-iconSize)/2 : Math.Max(9,(Width-textWidth-iconSize-gap)/2);
            KnightIcons.Draw(g,icon,new RectangleF(x,(Height-iconSize)/2+(pressed?1:0),iconSize,iconSize),ink);
            textRect.X=x+iconSize+gap;textRect.Width=Width-textRect.X-7;
        }
        g.Restore(state);
        if(!IconOnly)TextRenderer.DrawText(g,label,Font,textRect,ink,TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|(drawIcon?TextFormatFlags.Left:TextFormatFlags.HorizontalCenter));
    }
}

internal enum KnightIcon { None, Shield, Chat, Settings, Keyboard, Calendar, Mic, Send, Plus, Reset, Save, Close, Minimize, Maximize, Trash, Check, Search, Power, Size, Hair, Pin, Music, ArrowLeft, ArrowRight, Link }

internal static class KnightIcons
{
    internal static KnightIcon ForText(string text)
    {
        if(text.Contains("保存"))return KnightIcon.Save;
        if(text.Contains("设置")||text.Contains("接口"))return KnightIcon.Settings;
        if(text.Contains("新对话")||text.Contains("新建")||text.Contains("添加")||text.StartsWith("＋"))return KnightIcon.Plus;
        if(text.Contains("发送"))return KnightIcon.Send;
        if(text.Contains("通话")||text.Contains("朗读"))return KnightIcon.Mic;
        if(text.Contains("停止")||text.Contains("挂断")||text=="取消")return KnightIcon.Close;
        if(text.Contains("保存"))return KnightIcon.Save;
        if(text.Contains("删除"))return KnightIcon.Trash;
        if(text.Contains("默认")||text.Contains("刷新")||text.Contains("撤销"))return KnightIcon.Reset;
        if(text.Contains("测试"))return KnightIcon.Link;
        if(text.StartsWith("‹"))return KnightIcon.ArrowLeft;
        if(text.EndsWith("›"))return KnightIcon.ArrowRight;
        if(text=="今天")return KnightIcon.Calendar;
        if(text.Contains("完成")||text=="知道了")return KnightIcon.Check;
        return KnightIcon.None;
    }
    internal static void Draw(Graphics g,KnightIcon icon,RectangleF bounds,Color color)
    {
        if(icon==KnightIcon.None)return;
        var state=g.Save();g.SmoothingMode=SmoothingMode.AntiAlias;g.TranslateTransform(bounds.X,bounds.Y);g.ScaleTransform(bounds.Width/20,bounds.Height/20);
        using(var p=new Pen(color,1.5f){StartCap=LineCap.Round,EndCap=LineCap.Round,LineJoin=LineJoin.Round})
        {
            switch(icon)
            {
                case KnightIcon.Shield:g.DrawPolygon(p,new[] {new PointF(10,2),new PointF(17,5),new PointF(16,12),new PointF(10,18),new PointF(4,12),new PointF(3,5)});g.DrawLines(p,new[] {new PointF(6,8),new PointF(10,11),new PointF(14,8)});break;
                case KnightIcon.Chat:g.DrawLines(p,new[] {new PointF(3,3),new PointF(17,3),new PointF(17,14),new PointF(9,14),new PointF(4,18),new PointF(4,14),new PointF(3,14),new PointF(3,3)});g.DrawLine(p,6,7,14,7);g.DrawLine(p,6,10,11,10);break;
                case KnightIcon.Settings:for(int i=0;i<8;i++){double a=i*Math.PI/4;g.DrawLine(p,10+(float)Math.Cos(a)*6,10+(float)Math.Sin(a)*6,10+(float)Math.Cos(a)*8,10+(float)Math.Sin(a)*8);}g.DrawEllipse(p,4,4,12,12);g.DrawEllipse(p,8,8,4,4);break;
                case KnightIcon.Keyboard:g.DrawRectangle(p,2,5,16,11);for(int y=8;y<=11;y+=3)for(int x=5;x<=15;x+=3)g.DrawLine(p,x,y,x+.4f,y);g.DrawLine(p,6,14,14,14);break;
                case KnightIcon.Calendar:g.DrawRectangle(p,3,4,14,13);g.DrawLine(p,3,8,17,8);g.DrawLine(p,7,2,7,5);g.DrawLine(p,13,2,13,5);g.DrawRectangle(p,6,11,2,2);g.DrawLine(p,11,12,14,12);break;
                case KnightIcon.Mic:g.DrawArc(p,7,2,6,11,0,360);g.DrawArc(p,4,5,12,11,0,180);g.DrawLine(p,10,16,10,18);g.DrawLine(p,7,18,13,18);break;
                case KnightIcon.Send:g.DrawPolygon(p,new[] {new PointF(2,3),new PointF(18,10),new PointF(2,17),new PointF(5,10)});g.DrawLine(p,5,10,13,10);break;
                case KnightIcon.Plus:g.DrawLine(p,10,3,10,17);g.DrawLine(p,3,10,17,10);break;
                case KnightIcon.Reset:g.DrawArc(p,4,4,12,12,210,300);g.DrawLines(p,new[] {new PointF(3,3),new PointF(3,8),new PointF(8,8)});break;
                case KnightIcon.Save:g.DrawPolygon(p,new[] {new PointF(3,3),new PointF(14,3),new PointF(17,6),new PointF(17,17),new PointF(3,17)});g.DrawRectangle(p,6,11,8,6);g.DrawLines(p,new[] {new PointF(6,3),new PointF(6,7),new PointF(13,7),new PointF(13,3)});break;
                case KnightIcon.Close:g.DrawLine(p,5,5,15,15);g.DrawLine(p,15,5,5,15);break;
                case KnightIcon.Minimize:g.DrawLine(p,4,11,16,11);break;
                case KnightIcon.Maximize:g.DrawRectangle(p,4,4,12,12);break;
                case KnightIcon.Trash:g.DrawLines(p,new[] {new PointF(5,6),new PointF(6,17),new PointF(14,17),new PointF(15,6)});g.DrawLine(p,3,5,17,5);g.DrawLines(p,new[] {new PointF(7,5),new PointF(7,2),new PointF(13,2),new PointF(13,5)});g.DrawLine(p,8,8,8,14);g.DrawLine(p,12,8,12,14);break;
                case KnightIcon.Check:g.DrawLines(p,new[] {new PointF(3,10),new PointF(8,15),new PointF(17,5)});break;
                case KnightIcon.Search:g.DrawEllipse(p,3,3,10,10);g.DrawLine(p,12,12,17,17);break;
                case KnightIcon.Power:g.DrawArc(p,3,3,14,14,-50,280);g.DrawLine(p,10,2,10,10);break;
                case KnightIcon.Size:g.DrawRectangle(p,3,8,9,9);g.DrawLines(p,new[] {new PointF(8,3),new PointF(17,3),new PointF(17,12)});g.DrawLine(p,10,10,17,3);break;
                case KnightIcon.Hair:g.DrawArc(p,3,2,14,17,180,180);g.DrawLines(p,new[] {new PointF(3,10),new PointF(6,6),new PointF(8,12),new PointF(10,7),new PointF(14,12),new PointF(17,9)});break;
                case KnightIcon.Pin:g.DrawLines(p,new[] {new PointF(6,3),new PointF(14,3),new PointF(13,8),new PointF(17,12),new PointF(3,12),new PointF(7,8),new PointF(6,3)});g.DrawLine(p,10,12,10,18);break;
                case KnightIcon.Music:g.DrawLines(p,new[] {new PointF(7,15),new PointF(7,4),new PointF(16,2),new PointF(16,13)});g.DrawEllipse(p,3,13,4,4);g.DrawEllipse(p,12,11,4,4);break;
                case KnightIcon.ArrowLeft:g.DrawLines(p,new[] {new PointF(12,4),new PointF(6,10),new PointF(12,16)});break;
                case KnightIcon.ArrowRight:g.DrawLines(p,new[] {new PointF(8,4),new PointF(14,10),new PointF(8,16)});break;
                case KnightIcon.Link:g.DrawArc(p,2,6,10,8,70,260);g.DrawArc(p,8,6,10,8,250,260);g.DrawLine(p,7,10,13,10);break;
            }
        }
        g.Restore(state);
    }
}

// Shared borderless chrome, with real minimize/maximize controls and native
// resize hit testing. Content remains in the form for existing keyboard flows.
internal class KnightWindow : Form
{
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window,int message,IntPtr param,IntPtr data);
    readonly Panel caption=new Panel();
    readonly KnightButton minimize,maximize,close;
    internal bool Resizable=true;
    internal KnightWindow()
    {
        AutoScaleDimensions=new SizeF(96,96);AutoScaleMode=AutoScaleMode.Dpi;
        FormBorderStyle=FormBorderStyle.None;DoubleBuffered=true;BackColor=KnightUi.Paper;ForeColor=KnightUi.Ink;
        Padding=new Padding(1,45,1,1);Icon=KnightUi.WindowIcon;
        caption.BackColor=KnightUi.Ink;Controls.Add(caption);
        minimize=Chrome(KnightIcon.Minimize,"最小化",(s,e)=>WindowState=FormWindowState.Minimized);
        maximize=Chrome(KnightIcon.Maximize,"最大化或还原",(s,e)=>ToggleMaximize());
        close=Chrome(KnightIcon.Close,"关闭窗口",(s,e)=>Close());
        caption.Controls.AddRange(new Control[] {minimize,maximize,close});
        caption.Paint+=(s,e)=>
        {
            float scale=DeviceDpi/96f;
            KnightIcons.Draw(e.Graphics,KnightIcon.Shield,new RectangleF(17*scale,12*scale,20*scale,20*scale),Color.White);
            using(var p=new Pen(KnightUi.Red,2*scale))e.Graphics.DrawLine(p,18*scale,35*scale,36*scale,35*scale);
            using(var f=new Font("Microsoft YaHei UI",12,FontStyle.Regular,GraphicsUnit.Pixel))
                TextRenderer.DrawText(e.Graphics,Text,f,new Rectangle((int)(48*scale),0,Math.Max(0,caption.Width-(int)(190*scale)),caption.Height),Color.White,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine);
        };
        caption.MouseDown+=(s,e)=>{if(e.Button==MouseButtons.Left){ReleaseCapture();SendMessage(Handle,0x00A1,new IntPtr(2),IntPtr.Zero);}};
        caption.DoubleClick+=(s,e)=>ToggleMaximize();TextChanged+=(s,e)=>caption.Invalidate();
        Resize+=(s,e)=>LayoutChrome();
    }
    static KnightButton Chrome(KnightIcon icon,string name,EventHandler action)
    {var b=new KnightButton {IconKind=icon,IconOnly=true,Text=name,AccessibleName=name,ChromeStyle=true,TabStop=false};b.Click+=action;return b;}
    void LayoutChrome()
    {
        int h=(int)(44*DeviceDpi/96f),w=(int)(40*DeviceDpi/96f);
        Padding=new Padding(1,h+1,1,1);caption.Bounds=new Rectangle(1,1,Math.Max(0,ClientSize.Width-2),h);
        close.Bounds=new Rectangle(caption.Width-w-6,6,w,h-12);
        maximize.Bounds=new Rectangle(close.Left-w-2,6,w,h-12);maximize.Visible=Resizable;
        minimize.Bounds=new Rectangle((Resizable?maximize.Left:close.Left)-w-2,6,w,h-12);
        caption.BringToFront();Invalidate();
        if(IsHandleCreated&&WindowState==FormWindowState.Normal)MaximizedBounds=Screen.FromControl(this).WorkingArea;
    }
    void ToggleMaximize(){if(Resizable)WindowState=WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized;}
    protected override void OnShown(EventArgs e){LayoutChrome();base.OnShown(e);}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);using(var p=new Pen(KnightUi.Ink))e.Graphics.DrawRectangle(p,0,0,ClientSize.Width-1,ClientSize.Height-1);}
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if(m.Msg!=0x84||!Resizable||WindowState!=FormWindowState.Normal)return;
        long packed=m.LParam.ToInt64();Point p=PointToClient(new Point((short)(packed&65535),(short)((packed>>16)&65535)));
        int edge=(int)(6*DeviceDpi/96f);bool l=p.X<edge,r=p.X>=Width-edge,t=p.Y<edge,b=p.Y>=Height-edge;
        if(l||r||t||b)m.Result=new IntPtr(t?(l?13:r?14:12):b?(l?16:r?17:15):l?10:11);
    }
}

internal sealed class KnightSectionHeader : Control
{
    internal string Title,Subtitle;
    internal KnightIcon IconKind;
    internal KnightSectionHeader(string title,string subtitle,KnightIcon icon)
    {Title=title;Subtitle=subtitle;IconKind=icon;Dock=DockStyle.Fill;SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer,true);}
    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics;float s=DeviceDpi/96f;
        using(var b=new SolidBrush(KnightUi.Ink))g.FillRectangle(b,0,9*s,38*s,38*s);
        KnightIcons.Draw(g,IconKind,new RectangleF(10*s,19*s,18*s,18*s),Color.White);
        using(var p=new Pen(KnightUi.Red,2*s))g.DrawLine(p,0,49*s,38*s,49*s);
        using(var title=new Font(Font.FontFamily,20,FontStyle.Bold,GraphicsUnit.Pixel))
            TextRenderer.DrawText(g,Title,title,new Rectangle((int)(52*s),(int)(4*s),Width-(int)(52*s),(int)(32*s)),KnightUi.Ink,TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
        TextRenderer.DrawText(g,Subtitle,Font,new Rectangle((int)(52*s),(int)(36*s),Width-(int)(52*s),(int)(32*s)),KnightUi.Steel,TextFormatFlags.WordBreak);
    }
}

internal sealed class KnightTabs : TabControl
{
    internal KnightTabs()
    {
        DrawMode=TabDrawMode.OwnerDrawFixed;SizeMode=TabSizeMode.Fixed;ItemSize=new Size(150,40);Padding=new Point(16,8);
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(KnightUi.Paper);
        var area=DisplayRectangle;area.Inflate(2,2);
        using(var fill=new SolidBrush(KnightUi.Surface))e.Graphics.FillRectangle(fill,area);
        using(var p=new Pen(KnightUi.Silver))e.Graphics.DrawRectangle(p,area.X,area.Y,area.Width-1,area.Height-1);
        for(int i=0;i<TabCount;i++)OnDrawItem(new DrawItemEventArgs(e.Graphics,Font,GetTabRect(i),i,DrawItemState.None));
    }
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        bool active=e.Index==SelectedIndex;var r=GetTabRect(e.Index);var g=e.Graphics;
        using(var fill=new SolidBrush(active?KnightUi.Ink:KnightUi.Paper))g.FillRectangle(fill,r);
        Color ink=active?KnightUi.OnDark:KnightUi.Steel;
        var icon=e.Index==0?KnightIcon.Link:e.Index==1?KnightIcon.Mic:KnightIcon.Shield;
        KnightIcons.Draw(g,icon,new RectangleF(r.Left+12,r.Top+(r.Height-16)/2,16,16),ink);
        TextRenderer.DrawText(g,TabPages[e.Index].Text,Font,new Rectangle(r.Left+36,r.Top,r.Width-40,r.Height),ink,TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
        if(active)using(var p=new Pen(KnightUi.Red,2))g.DrawLine(p,r.Left+12,r.Bottom-2,r.Right-12,r.Bottom-2);
        if(Focused&&active)ControlPaint.DrawFocusRectangle(g,Rectangle.Inflate(r,-4,-4),ink,active?KnightUi.Ink:KnightUi.Paper);
    }
}

internal sealed class KnightField : Panel
{
    readonly Control editor;
    internal KnightField(Control editor)
    {
        this.editor=editor;Dock=editor.Dock;Margin=editor.Margin;Bounds=editor.Bounds;Anchor=editor.Anchor;
        Padding=new Padding(10,8,10,7);BackColor=KnightUi.Surface;TabStop=false;
        var text=editor as TextBoxBase;if(text!=null)text.BorderStyle=BorderStyle.None;
        editor.Dock=DockStyle.Fill;editor.Margin=Padding.Empty;editor.BackColor=KnightUi.Surface;editor.ForeColor=KnightUi.Ink;
        Controls.Add(editor);editor.Enter+=(s,e)=>Invalidate();editor.Leave+=(s,e)=>Invalidate();editor.EnabledChanged+=(s,e)=>Invalidate();
        SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);using(var p=new Pen(editor.ContainsFocus?KnightUi.Ink:KnightUi.Silver))e.Graphics.DrawRectangle(p,0,0,Width-1,Height-1);
        if(editor.ContainsFocus)using(var p=new Pen(KnightUi.Red,2))e.Graphics.DrawLine(p,1,Height-2,Width-2,Height-2);
    }
    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);if(editor==null)return;
        var text=editor as TextBoxBase;
        bool multi=text!=null&&text.Multiline;
        if(multi){editor.Dock=DockStyle.Fill;return;}
        editor.Dock=DockStyle.None;int h=Math.Min(Math.Max(1,Height-4),editor.PreferredSize.Height);
        editor.Bounds=new Rectangle(Padding.Left,Math.Max(2,(Height-h)/2),Math.Max(1,Width-Padding.Horizontal),h);
    }
}

internal sealed class KnightLevelMeter : ProgressBar
{
    internal KnightLevelMeter(){SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer,true);}
    public new int Value {get{return base.Value;}set{base.Value=value;Invalidate();}}
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent==null?KnightUi.Paper:Parent.BackColor);
        for(int i=0;i<8;i++)
        {
            float w=Width/8f,h=4+(i%4)*2;
            using(var b=new SolidBrush(i<Value*8/Math.Max(1,Maximum)?i>=6?KnightUi.Red:KnightUi.Ink:KnightUi.Silver))
                e.Graphics.FillRectangle(b,i*w,(Height-h)/2,Math.Max(1,w-2),h);
        }
    }
}

internal sealed class KnightSlider : Control
{
    int minimum=5,maximum=100,current=35;
    internal int Minimum {get{return minimum;} set{minimum=value;Value=current;}}
    internal int Maximum {get{return maximum;} set{maximum=value;Value=current;}}
    internal int Value {get{return current;} set{int next=Math.Max(minimum,Math.Min(maximum,value));if(current==next)return;current=next;Invalidate();if(ValueChanged!=null)ValueChanged(this,EventArgs.Empty);}}
    internal event EventHandler ValueChanged;
    internal KnightSlider()
    {
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Selectable|ControlStyles.SupportsTransparentBackColor,true);
        BackColor=Color.Transparent;
        TabStop=true;Cursor=Cursors.Hand;AccessibleName="鼠标位移增幅";AccessibleRole=AccessibleRole.Slider;
    }
    float Inset {get{return 12*DeviceDpi/96f;}}
    void Pick(int x){Value=minimum+(int)Math.Round((maximum-minimum)*Math.Max(0,Math.Min(1,(x-Inset)/Math.Max(1,Width-2*Inset))));}
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button==MouseButtons.Left){Focus();Capture=true;Pick(e.X);}}
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(Capture)Pick(e.X);}
    protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);if(e.Button==MouseButtons.Left)Capture=false;}
    protected override bool IsInputKey(Keys keyData){var key=keyData&Keys.KeyCode;return key==Keys.Left||key==Keys.Right||key==Keys.Up||key==Keys.Down||base.IsInputKey(keyData);}
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if(e.KeyCode==Keys.Left||e.KeyCode==Keys.Down)Value--;
        else if(e.KeyCode==Keys.Right||e.KeyCode==Keys.Up)Value++;
        else if(e.KeyCode==Keys.Home)Value=minimum;else if(e.KeyCode==Keys.End)Value=maximum;
        else if(e.KeyCode==Keys.PageDown)Value-=10;else if(e.KeyCode==Keys.PageUp)Value+=10;else return;
        e.Handled=true;e.SuppressKeyPress=true;
    }
    protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
    protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);Invalidate();}
    protected override void OnPaint(PaintEventArgs e)
    {
        var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;float y=Height/2f,x=Inset+(Width-2*Inset)*(Value-minimum)/(float)Math.Max(1,maximum-minimum);
        using(var pen=new Pen(KnightUi.Silver,2))g.DrawLine(pen,Inset,y,Width-Inset,y);
        using(var pen=new Pen(KnightUi.Ink,2))g.DrawLine(pen,Inset,y,x,y);
        using(var thumb=KnightUi.Plate(new RectangleF(x-7,y-10,14,20),3))
        using(var fill=new SolidBrush(KnightUi.Surface))using(var edge=new Pen(KnightUi.Ink))
        {g.FillPath(fill,thumb);g.DrawPath(edge,thumb);}
        using(var pen=new Pen(KnightUi.Red,2))g.DrawLine(pen,x,y-4,x,y+4);
        if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(g,new Rectangle(1,1,Width-2,Height-2),KnightUi.Ink,BackColor);
    }
    protected override AccessibleObject CreateAccessibilityInstance(){return new SliderAccessibility(this);}
    sealed class SliderAccessibility : ControlAccessibleObject
    {
        readonly KnightSlider slider;
        internal SliderAccessibility(KnightSlider slider):base(slider){this.slider=slider;}
        public override string Value {get{return slider.Value.ToString();}set{int number;if(Int32.TryParse(value,out number))slider.Value=number;}}
    }
}
