using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal static class UiPreview
{
    static void CalendarPreview(string output)
    {
        string isolated=Path.Combine(Path.GetTempPath(),"DesktopPet.CalendarPreview."+Guid.NewGuid().ToString("N"));
        try
        {
            var store=new PetCalendarStore(isolated);var today=DateTime.Today;
            foreach(int offset in new[] {-4,-2,-1,0,2,5})
            {
                var date=today.AddDays(offset);var day=store.Load(date);
                day.Plans.Add(new PetCalendarPlan {Text=offset<=0 ? "完成桌宠日历模块" : "整理本周工作与下周计划",Done=offset<0});
                day.Plans.Add(new PetCalendarPlan {Text=offset<=0 ? "阅读 30 分钟" : "阅读与学习",Done=offset<0});
                if(offset==0){day.Plans[0].Done=true;day.Plans.Add(new PetCalendarPlan {Text="散步，早点休息"});}
                if(offset==0){day.Plans[0].Start="09:00";day.Plans[0].End="10:30";day.Plans[1].Start="10:00";day.Plans[1].End="11:00";}
                if(offset<=0)day.Journal=offset==0 ? "今天完成了日历计划模块。\r\n\r\n把要做的事情列下来，完成一项就勾选一项。\r\n晚上花几分钟记录今天做过的事与收获。\r\n\r\n今天的收获：保持记录，慢慢积累。" : "完成工作与阅读，记下今天的进展。";
                store.Save(date,day);
            }
            using(var calendar=new PetCalendarWindow(isolated,false,today))
            {
                using(var image=Render(calendar))image.Save(Path.Combine(output,"current-calendar.png"),ImageFormat.Png);
                var normalSize=calendar.Size;calendar.Size=calendar.MinimumSize;
                using(var image=Render(calendar))image.Save(Path.Combine(output,"current-calendar-compact.png"),ImageFormat.Png);
                calendar.Size=normalSize;
                calendar.SetView(CalendarView.Week);
                using(var image=Render(calendar))image.Save(Path.Combine(output,"current-calendar-week.png"),ImageFormat.Png);
                calendar.SetView(CalendarView.List);
                using(var image=Render(calendar))image.Save(Path.Combine(output,"current-calendar-global.png"),ImageFormat.Png);
            }
        }
        finally
        {
            string resolved=Path.GetFullPath(isolated),temporary=Path.GetFullPath(Path.GetTempPath());
            if(!resolved.StartsWith(temporary,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(resolved).StartsWith("DesktopPet.CalendarPreview.",StringComparison.Ordinal))throw new InvalidOperationException("Unexpected calendar preview directory");
            if(Directory.Exists(resolved))Directory.Delete(resolved,true);
        }
    }
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [StructLayout(LayoutKind.Sequential)] struct PrintRect { internal int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct PrintRange
    { internal IntPtr DC,TargetDC; internal PrintRect Area,Page; internal int First,Last; }
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window,int message,IntPtr parameter,IntPtr data);
    [DllImport("gdi32.dll")] static extern int GetDeviceCaps(IntPtr dc,int capability);
    static void RenderRichText(Control root,Control control,Bitmap image)
    {
        var rich=control as RichTextBox;
        if(rich!=null)
        {
            // RichEdit does not implement DrawToBitmap. Ask the actual native
            // control to print its formatted content, keeping font/color/wrap.
            Point origin=root.PointToClient(rich.PointToScreen(Point.Empty));
            var form=root as Form;
            if(form!=null)
            {
                // Form.DrawToBitmap includes the native frame and title bar;
                // child coordinates are relative to its client area.
                Point client=form.PointToScreen(Point.Empty);
                origin.Offset(client.X-form.Left,client.Y-form.Top);
            }
            using(var richImage=new Bitmap(rich.Width,rich.Height))
            {
                using(var g=Graphics.FromImage(richImage))
                {
                    g.Clear(rich.BackColor);
                    IntPtr dc=g.GetHdc(),memory=IntPtr.Zero;
                    try
                    {
                        float x=1440f/GetDeviceCaps(dc,88),y=1440f/GetDeviceCaps(dc,90);
                        var range=new PrintRange {DC=dc,TargetDC=dc,First=rich.GetCharIndexFromPosition(new Point(1,1)),Last=-1,
                            Area=new PrintRect {Left=(int)(4*x),Top=(int)(4*y),Right=(int)((rich.Width-20)*x),Bottom=(int)((rich.Height-4)*y)},
                            Page=new PrintRect {Right=(int)(rich.Width*x),Bottom=(int)(rich.Height*y)}};
                        memory=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(PrintRange)));Marshal.StructureToPtr(range,memory,false);
                        SendMessage(rich.Handle,0x0439,new IntPtr(1),memory);
                        SendMessage(rich.Handle,0x0439,IntPtr.Zero,IntPtr.Zero);
                    }
                    finally {if(memory!=IntPtr.Zero)Marshal.FreeHGlobal(memory);g.ReleaseHdc(dc);}
                }
                using(var g=Graphics.FromImage(image))g.DrawImageUnscaled(richImage,origin);
            }
        }
        foreach(Control child in control.Controls)RenderRichText(root,child,image);
    }
    static Bitmap Render(Control control)
    {
        var form=control as Form;
        if(form!=null){form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-30000,-30000);form.Show();Application.DoEvents();}
        control.CreateControl();control.PerformLayout();
        var image=new Bitmap(control.Width,control.Height,PixelFormat.Format32bppArgb);
        control.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));RenderRichText(control,control,image);
        if(form!=null){if(!form.TransparencyKey.IsEmpty)image.MakeTransparent(form.TransparencyKey);form.Hide();}return image;
    }
    static void Overview(string output)
    {
        using(var sheet=new Bitmap(1680,1180))using(var g=Graphics.FromImage(sheet))
        using(var title=new Font("Microsoft YaHei UI",28,FontStyle.Bold,GraphicsUnit.Pixel))
        using(var label=new Font("Microsoft YaHei UI",16,FontStyle.Bold,GraphicsUnit.Pixel))
        using(var note=new Font("Microsoft YaHei UI",13,FontStyle.Regular,GraphicsUnit.Pixel))
        using(var ink=new SolidBrush(KnightUi.Ink))using(var muted=new SolidBrush(KnightUi.Steel))using(var red=new SolidBrush(KnightUi.Red))
        {
            g.Clear(KnightUi.Paper);g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.FillRectangle(red,28,17,54,3);g.DrawString("桌宠助手 / 窗口设计",title,ink,28,29);
            g.DrawString("黑白主色 · 朱红点缀 · 统一线性图标与细边框",note,muted,29,69);
            string[] files={"current-calendar.png","current-chat-settings.png","current-settings.png","current-chat.png","current-menu.png","current-dialog.png"};
            string[] labels={"日历工作区","AI 接口设置","按键与鼠标设置","点击人物展开输入","桌宠菜单","提示对话框"};
            Rectangle[] bounds={new Rectangle(28,126,1048,589),new Rectangle(1104,126,548,494),new Rectangle(28,794,320,349),new Rectangle(382,794,284,353),new Rectangle(714,794,362,358),new Rectangle(1104,726,548,327)};
            for(int i=0;i<files.Length;i++)
            {
                g.DrawString(labels[i],label,ink,bounds[i].X,bounds[i].Y-30);
                using(var source=Image.FromFile(Path.Combine(output,files[i])))g.DrawImage(source,bounds[i]);
            }
            g.DrawString("所有预览均由真实窗口与控件渲染，使用示例数据。",note,muted,1104,1080);
            sheet.Save(Path.Combine(output,"current-window-design.png"),ImageFormat.Png);
        }
    }
    [STAThread] static int Main(string[] args)
    {
        SetProcessDPIAware();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        string root=args[0],output=Path.Combine(root,"assets","previews");
        if(args.Length>1&&args[1]=="--calendar-only"){CalendarPreview(output);return 0;}
        Directory.CreateDirectory(output);
        using(var chat=new PetChatWindow(root,true))
        using(var chatSettings=new PetChatSettingsWindow(new PetChatSettings(),Path.Combine(Path.GetTempPath(),"unused-chat-preview.json")))
        {
            using(var image=Render(chat))image.Save(Path.Combine(output,"current-chat.png"),ImageFormat.Png);
            using(var pet=new KeyboardPetWindow(root,true))
            using(var sheet=new Bitmap(900,560))
            using(var g=Graphics.FromImage(sheet))
            using(var title=new Font("Microsoft YaHei UI",14,FontStyle.Bold))
            using(var ink=new SolidBrush(KnightUi.Ink))
            {
                g.Clear(Color.FromArgb(234,234,233));
                g.DrawString("点击人物，她先开口，你来回应",title,ink,26,18);
                // The screenshot uses the same borderless bubble and pet renderer
                // as the live desktop, with no real API or configuration changes.
                chat.PlaceBeside(new Rectangle(504,92,352,460),new Rectangle(0,0,900,560));
                Point bubbleLocation=chat.Location;
                using(var bubble=Render(chat))
                {bubble.MakeTransparent(Color.Fuchsia);g.DrawImageUnscaled(bubble,bubbleLocation);}
                pet.Say("辛苦啦。先喝口水，伸个懒腰吧！我陪你休息一会儿。");
                using(var reply=new Bitmap(pet.Width,pet.Height))
                using(var replyGraphics=Graphics.FromImage(reply))
                { replyGraphics.Clear(Color.Transparent); pet.DrawScene(replyGraphics); reply.Save(Path.Combine(output,"current-chat-reply.png"),ImageFormat.Png); }
                g.TranslateTransform(504,92);pet.DrawScene(g);g.ResetTransform();
                sheet.Save(Path.Combine(output,"current-chat-bubble.png"),ImageFormat.Png);
            }
            using(var image=Render(chatSettings))image.Save(Path.Combine(output,"current-chat-settings.png"),ImageFormat.Png);
            var provider=(ComboBox)typeof(PetChatSettingsWindow).GetField("provider",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(chatSettings);
            provider.SelectedIndex=2;
            using(var image=Render(chatSettings))image.Save(Path.Combine(output,"current-chat-deepseek.png"),ImageFormat.Png);
            provider.SelectedIndex=3;
            using(var image=Render(chatSettings))image.Save(Path.Combine(output,"current-chat-glm.png"),ImageFormat.Png);
            var tabs=chatSettings.Controls.OfType<TableLayoutPanel>().Single().Controls.OfType<TabControl>().Single();
            tabs.SelectedIndex=1;
            using(var image=Render(chatSettings))image.Save(Path.Combine(output,"current-chat-audio-settings.png"),ImageFormat.Png);
            tabs.SelectedIndex=2;
            using(var image=Render(chatSettings))image.Save(Path.Combine(output,"current-chat-personality.png"),ImageFormat.Png);
        }
        CalendarPreview(output);
        using(var pet=new KeyboardPetWindow(root,true))
        using(var editor=new PetBindingEditor(new PetInputBindings(),Path.Combine(Path.GetTempPath(),"unused-ui-preview.tsv")))
        using(var error=KnightUi.ErrorDialog("保存绑定失败","无法写入绑定文件，请检查文件夹是否可写，然后重试。"))
        {
            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
            ((Timer)typeof(KeyboardPetWindow).GetField("timer",flags).GetValue(pet)).Stop();
            var menu=(ContextMenuStrip)typeof(KeyboardPetWindow).GetField("menu",flags).GetValue(pet);
            foreach(string label in new[] {"互动设置","桌面设置","AI 与助手","运行状态"})
            {
                var group=menu.Items.OfType<ToolStripMenuItem>().Single(item=>item.Text==label);
                using(var image=Render(group.DropDown))image.Save(Path.Combine(output,"current-menu-"+label+".png"),ImageFormat.Png);
            }
            var wardrobe=menu.Items.OfType<ToolStripMenuItem>().Single(item=>item.Text=="皮肤…");
            wardrobe.Select();
            using(var main=Render(menu))
            using(var settings=Render(editor))using(var alert=Render(error))
            using(var keys=new Bitmap(352,140))using(var kg=Graphics.FromImage(keys))
            using(var sheet=new Bitmap(1100,1060))using(var g=Graphics.FromImage(sheet))
            using(var font=new Font("Microsoft YaHei UI",11,FontStyle.Bold))
            {
                settings.Save(Path.Combine(output,"current-settings.png"),ImageFormat.Png);
                alert.Save(Path.Combine(output,"current-dialog.png"),ImageFormat.Png);
                using(var menus=new Bitmap(main.Width+32,main.Height+32))using(var mg=Graphics.FromImage(menus))
                {mg.Clear(Color.FromArgb(234,234,233));mg.DrawImageUnscaled(main,16,16);menus.Save(Path.Combine(output,"current-menu.png"),ImageFormat.Png);}
                var state=new InputFeedbackState();
                using(var sprites=new KeyboardPetSprites(root))using(var scene=new BoundPetScene(sprites,new PetInputBindings(),state))
                {
                    scene.SetViewWidth(BoundPetScene.CompactWidth);state.Key(17,true,0);state.Key(16,true,0);state.Key(65,true,0);
                    state.SetBubbleMode(false);scene.Advance(0,0,false);
                    kg.Clear(Color.FromArgb(234,234,233));kg.TranslateTransform(0,-56);
                    typeof(BoundPetScene).GetMethod("DrawKeyDisplay",flags).Invoke(scene,new object[] {kg,"Ctrl + Shift + A"});kg.ResetTransform();
                    var inputFont=(Font)typeof(BoundPetScene).GetField("inputFont",flags).GetValue(scene);
                    typeof(BoundPetScene).GetMethod("DrawBubble",flags).Invoke(scene,new object[] {kg,new RectangleF(80,70,192,44),"Ctrl + A",252,true,inputFont});
                }
                g.Clear(Color.FromArgb(234,234,233));
                int settingsX=430,settingsY=92,settingsHeight=600,settingsWidth=(int)(settings.Width*600.0/settings.Height);
                using(var ink=new SolidBrush(KnightUi.Ink))
                {g.DrawString("桌宠助手  /  UI 样式",font,ink,24,18);g.DrawString("紧凑菜单",font,ink,24,58);g.DrawString("按键绑定与鼠标反馈",font,ink,settingsX,58);g.DrawString("按键提示 · 固定框 / 冒泡",font,ink,24,710);g.DrawString("提示对话框",font,ink,settingsX,710);}
                g.DrawImageUnscaled(main,24,92);
                g.DrawImage(settings,new Rectangle(settingsX,settingsY,settingsWidth,settingsHeight));g.DrawImageUnscaled(keys,10,752);g.DrawImage(alert,new Rectangle(settingsX,752,480,(int)(alert.Height*480.0/alert.Width)));
                sheet.Save(Path.Combine(output,"current-ui.png"),ImageFormat.Png);
            }
        }
        Overview(output);
        Console.WriteLine("Rendered menu, settings, key displays and error dialog without changing configuration.");return 0;
    }
}
