using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Thread = System.Threading.Thread;
using System.Windows.Forms;
using Microsoft.Win32;

internal static class UiInteractionTests
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Check(bool value,string message){if(!value)throw new Exception("UI: "+message);}
    static T Get<T>(object target,string name){return (T)target.GetType().GetField(name,Private).GetValue(target);}
    static void Key(KnightSlider slider,Keys key){typeof(KnightSlider).GetMethod("OnKeyDown",Private).Invoke(slider,new object[] {new KeyEventArgs(key)});}
    static void Prepare(Form form){form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-30000,-30000);form.Show();Application.DoEvents();}
    internal static void Run(string root)
    {
        CheckStartup(root);
        CheckWindowChrome();
        CheckAuxiliaryInput(root);
        using(var pet=new KeyboardPetWindow(root,true))
        {
            Get<Timer>(pet,"timer").Stop();var menu=Get<PetContextMenu>(pet,"menu");
            var work=new Rectangle(0,0,1920,1080);
            pet.Location=new Point(1200,300);var anchor=pet.MenuAnchor(work);
            Check(anchor.X+menu.Width<pet.Left&&anchor.Y==pet.Top,"Menu anchors to the pet's left and top");
            pet.Location=new Point(0,300);anchor=pet.MenuAnchor(work);
            Check(anchor.X>pet.Right,"Left edge moves the menu to the right of the pet");
            foreach(var p in new[] {new Point(0,0),new Point(1600,900),new Point(0,900)})
            {pet.Location=p;Check(work.Contains(new Rectangle(pet.MenuAnchor(work),menu.Size)),"Menu stays inside the working area");}
            // Open from two different opaque pixels through the real right-click
            // handler. Neither mouse coordinates nor menu selection move its anchor.
            pet.Location=new Point(600,200);pet.Show();Application.DoEvents();
            var b=pet.SpriteBounds;var first=new Point(b.X+b.Width/2,b.Y+b.Height/2);var second=new Point(first.X+3,first.Y+3);
            Check(pet.Hit(first)&&pet.Hit(second),"Right-click samples are opaque pet pixels");
            var mouse=typeof(KeyboardPetWindow).GetMethod("OnMouseDown",Private);
            mouse.Invoke(pet,new object[] {new MouseEventArgs(MouseButtons.Right,1,first.X,first.Y,0)});var location=menu.Location;menu.Close();
            mouse.Invoke(pet,new object[] {new MouseEventArgs(MouseButtons.Right,1,second.X,second.Y,0)});
            Check(menu.Location==location,"Different right-click positions open at the same location");menu.Close();
        }
        string path=Path.Combine(Path.GetTempPath(),"DesktopPet.UiTests."+Guid.NewGuid().ToString("N")+".tsv");
        try
        {
            var bindings=new PetInputBindings();int code=bindings.Map.Keys.OrderBy(k=>k).First();
            using(var editor=new PetBindingEditor(bindings,path))
            {
                Prepare(editor);var slider=Get<KnightSlider>(editor,"gain");
                slider.Value=-1;Check(slider.Value==5,"Slider clamps its lower bound");slider.Value=1000;Check(slider.Value==100,"Slider clamps its upper bound");
                Key(slider,Keys.Home);Key(slider,Keys.Right);Check(slider.Value==6,"Home and arrow keyboard controls work");
                Key(slider,Keys.End);Key(slider,Keys.Down);Check(slider.Value==99,"End and reverse arrow work");
                slider.AccessibilityObject.Value="75";Check(slider.Value==75,"Slider accessibility value changes the real setting");
                Get<DataGridView>(editor,"grid").Rows[0].Cells[1].Value=0;
                ((Button)editor.AcceptButton).PerformClick();
            }
            var saved=PetInputBindings.Load(path);Check(saved.Resolve(code)==0&&Math.Abs(saved.MouseGain-.75)<.001,"Styled save button persists both bindings and gain");
            string before=File.ReadAllText(path);
            using(var editor=new PetBindingEditor(bindings,path))
            {
                Prepare(editor);Get<KnightSlider>(editor,"gain").Value=20;Get<DataGridView>(editor,"grid").Rows[0].Cells[1].Value=65;
                ((Button)editor.CancelButton).PerformClick();
            }
            Check(File.ReadAllText(path)==before&&Math.Abs(bindings.MouseGain-.75)<.001&&bindings.Resolve(code)==0,"Cancel preserves the live and saved configuration");
            using(var editor=new PetBindingEditor(bindings,path))
            {
                Prepare(editor);editor.Controls.OfType<Button>().Single(button=>button.Text=="恢复默认").PerformClick();
                Check(Get<KnightSlider>(editor,"gain").Value==35,"Restore defaults updates the custom slider");
                ((Button)editor.AcceptButton).PerformClick();
            }
            saved=PetInputBindings.Load(path);Check(Math.Abs(saved.MouseGain-.35)<.001&&saved.Resolve(code)==new PetInputBindings().Resolve(code),"Restored defaults persist correctly");
        }
        finally{if(File.Exists(path))File.Delete(path);}
        Console.WriteLine("PASS: fixed menu placement, screen-edge fallback, slider keyboard/accessibility, binding save/cancel and restore defaults.");
    }
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window,int message,IntPtr parameter,IntPtr data);
    static void CheckWindowChrome()
    {
        using(var window=new PetChatSettingsWindow(new PetChatSettings(),Path.Combine(Path.GetTempPath(),"unused-chrome-settings.json")))
        {
            Prepare(window);
            var caption=window.Controls.OfType<Panel>().Single(panel=>panel.Controls.OfType<KnightButton>().Any(button=>button.AccessibleName=="最大化或还原"));
            var maximize=caption.Controls.OfType<KnightButton>().Single(button=>button.AccessibleName=="最大化或还原");
            maximize.PerformClick();Application.DoEvents();Check(window.WindowState==FormWindowState.Maximized,"Custom titlebar maximizes the settings window");
            maximize.PerformClick();Application.DoEvents();Check(window.WindowState==FormWindowState.Normal,"Custom titlebar restores the settings window");
            Point edge=window.PointToScreen(new Point(1,1));
            IntPtr packed=new IntPtr(unchecked((edge.Y&65535)<<16|(edge.X&65535)));
            Check(SendMessage(window.Handle,0x84,IntPtr.Zero,packed).ToInt32()==13,"Custom border exposes the native top-left resize grip");
            caption.Controls.OfType<KnightButton>().Single(button=>button.AccessibleName=="最小化").PerformClick();Application.DoEvents();
            Check(window.WindowState==FormWindowState.Minimized,"Custom titlebar minimizes the settings window");window.WindowState=FormWindowState.Normal;
            caption.Controls.OfType<KnightButton>().Single(button=>button.AccessibleName=="关闭窗口").PerformClick();Check(window.IsDisposed,"Custom titlebar closes the settings window");
        }
        Console.WriteLine("PASS: custom window titlebar maximize/restore/minimize/close and native resize hit testing.");
    }
    [StructLayout(LayoutKind.Sequential)] struct TestKey
    { internal uint Key,Scan,Flags,Time; internal UIntPtr Extra; }
    static void QueueTyping(GlobalInputFeedback hook)
    {
        var data=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(TestKey)));
        try
        {
            Marshal.StructureToPtr(new TestKey {Key=65,Scan=30},data,false);
            var callback=typeof(GlobalInputFeedback).GetMethod("Keyboard",Private);
            callback.Invoke(hook,new object[] {0,new IntPtr(0x100),data});
            callback.Invoke(hook,new object[] {0,new IntPtr(0x101),data});
        }
        finally {Marshal.FreeHGlobal(data);}
    }
    static void CheckModalInput(KeyboardPetWindow pet,Type windowType,Action open,Action<Form> nested=null)
    {
        var hook=Get<GlobalInputFeedback>(pet,"input");var worker=Get<Thread>(hook,"worker");
        Exception failure=null;bool inspected=false;
        using(var probe=new System.Windows.Forms.Timer {Interval=100})
        {
            probe.Tick+=(s,e)=>
            {
                probe.Stop();Form window=null;
                try
                {
                    window=Application.OpenForms.Cast<Form>().Single(form=>form.GetType()==windowType);inspected=true;
                    Check(hook.Enabled&&hook.ProbePump()&&Get<Thread>(hook,"worker")==worker,"Input worker stays active inside "+windowType.Name);
                    QueueTyping(hook);
                    typeof(KeyboardPetWindow).GetMethod("Tick",Private).Invoke(pet,new object[] {null,EventArgs.Empty});
                    double now=Get<System.Diagnostics.Stopwatch>(pet,"clock").Elapsed.TotalSeconds;
                    Check(pet.inputState.KeyDisplay(now).Contains("A")&&pet.scene.Hands.TypingBlend>0,"Typing reaches the key display and hand animation inside "+windowType.Name);
                    if(nested!=null)nested(window);
                    QueueTyping(hook);
                }
                catch(Exception error){failure=error;}
                finally
                {
                    if(window!=null)window.Close();
                    else foreach(Form form in Application.OpenForms.Cast<Form>().ToArray())if(form.Owner==pet)form.Close();
                }
            };
            probe.Start();open();
            // A modeless speech bubble returns immediately; still inspect it
            // through the same message-loop probe used by modal settings.
            var until=DateTime.UtcNow.AddSeconds(5);
            while(!inspected&&failure==null&&DateTime.UtcNow<until){Application.DoEvents();Thread.Sleep(10);}
            probe.Stop();
        }
        if(failure!=null)throw new Exception("Auxiliary input regression in "+windowType.Name,failure);
        Check(inspected&&hook.Enabled&&Get<Thread>(hook,"worker")==worker,"Closing a window preserves the same input worker");
        hook.DrainPending();
        Check(pet.inputState.KeyDisplay(Get<System.Diagnostics.Stopwatch>(pet,"clock").Elapsed.TotalSeconds).Contains("A"),"Closing a window preserves pending keystrokes");
    }
    static void CheckAuxiliaryInput(string root)
    {
        using(var pet=new KeyboardPetWindow(root,true))
        {
            Get<System.Windows.Forms.Timer>(pet,"timer").Stop();
            var menu=Get<PetContextMenu>(pet,"menu");
            menu.Items.OfType<ToolStripMenuItem>().Single(i=>i.Text=="跟随键鼠动作").Checked=true;
            pet.StartPosition=FormStartPosition.Manual;pet.Location=new Point(-30000,-30000);pet.Show();Application.DoEvents();
            var hook=Get<GlobalInputFeedback>(pet,"input");
            Check(hook.Enabled&&hook.ProbePump(),"Input worker starts before opening auxiliary windows");
            CheckModalInput(pet,typeof(PetChatWindow),()=>
            {
                var b=pet.SpriteBounds;var point=new Point(b.X+b.Width/2,b.Y+b.Height/2);
                var down=typeof(KeyboardPetWindow).GetMethod("OnMouseDown",Private);
                var up=typeof(KeyboardPetWindow).GetMethod("OnMouseUp",Private);
                down.Invoke(pet,new object[] {new MouseEventArgs(MouseButtons.Left,1,point.X,point.Y,0)});
                typeof(KeyboardPetWindow).GetField("dragging",Private).SetValue(pet,true);
                up.Invoke(pet,new object[] {new MouseEventArgs(MouseButtons.Left,1,point.X,point.Y,0)});
                Check(Get<PetChatWindow>(pet,"chatWindow")==null,"Releasing a drag does not open a conversation");
                down.Invoke(pet,new object[] {new MouseEventArgs(MouseButtons.Left,1,point.X,point.Y,0)});
                up.Invoke(pet,new object[] {new MouseEventArgs(MouseButtons.Left,1,point.X,point.Y,0)});
                var bubble=Get<PetChatWindow>(pet,"chatWindow");
                Check(bubble!=null&&bubble.Visible&&pet.Enabled&&bubble.Owner==pet,"Single click opens an owned modeless bubble and leaves pet enabled");
                var speech=Get<PetSpeechBubble>(pet,"speechBubble");
                string firstLine=bubble.LastReply;
                Check(speech.Visible&&speech.CurrentPage==firstLine,"Click immediately displays the random line on the existing pet speech bubble");
                Get<TextBox>(bubble,"input").Text="保留草稿";
                down.Invoke(pet,new object[] {new MouseEventArgs(MouseButtons.Left,1,point.X,point.Y,0)});
                up.Invoke(pet,new object[] {new MouseEventArgs(MouseButtons.Left,1,point.X,point.Y,0)});
                Check(bubble.Visible&&bubble.LastReply!=firstLine&&Get<TextBox>(bubble,"input").Text=="保留草稿","Another character click changes the line while keeping the input and draft visible");
                menu.Items.OfType<ToolStripMenuItem>().Single(i=>i.Text=="与她对话…").PerformClick();
                Check(Get<PetChatWindow>(pet,"chatWindow")==bubble,"Repeated entry reuses the same bubble");
                var area=Screen.FromControl(pet).WorkingArea;
                pet.Location=new Point(area.Left+50,area.Top+150);
                Check(area.Contains(bubble.Bounds)&&bubble.Left>=pet.Right,"Moving pet reanchors the existing bubble");
            },chat=>
            {
                // Preview chat disables its configuration button; invoke the
                // same settings handler directly without enabling networking.
                CheckModalInput(pet,typeof(PetChatSettingsWindow),()=>typeof(PetChatWindow).GetMethod("Configure",Private).Invoke(chat,new object[] {null,EventArgs.Empty}));
            });
            CheckModalInput(pet,typeof(PetBindingEditor),()=>menu.Items.OfType<ToolStripMenuItem>().Single(i=>i.Text=="按键与鼠标设置…").PerformClick());
            menu.Items.OfType<ToolStripMenuItem>().Single(i=>i.Text=="日历计划…").PerformClick();
            var worker=Get<Thread>(hook,"worker");QueueTyping(hook);hook.DrainPending();
            Check(hook.Enabled&&hook.ProbePump(),"Calendar leaves input active alongside other windows");
            var rest=menu.Items.OfType<ToolStripMenuItem>().Single(i=>i.Text=="闭眼休息");
            rest.Checked=true;Check(!hook.Enabled&&!pet.inputState.HasKeys,"Explicit rest still stops and clears input with calendar open");
            rest.Checked=false;Check(hook.Enabled,"Leaving rest resumes input with calendar open");
            var follow=menu.Items.OfType<ToolStripMenuItem>().Single(i=>i.Text=="跟随键鼠动作");
            follow.Checked=false;Check(!hook.Enabled,"Explicit follow opt-out remains effective");
            follow.Checked=true;Check(hook.Enabled,"Enabling follow works with calendar open");
            worker=Get<Thread>(hook,"worker");QueueTyping(hook);Get<PetCalendarWindow>(pet,"calendarWindow").Close();
            Check(hook.Enabled&&Get<Thread>(hook,"worker")==worker&&hook.PendingCount>=2,"Closing calendar does not restart the worker or lose pending events");
        }
        Console.WriteLine("PASS: typing hooks, key display and hand animation continue across chat, nested AI settings, binding settings and calendar; explicit rest/follow switches still work.");
    }
    static void CheckStartup(string root)
    {
        // Exercise real registry persistence under a disposable test key, never Run.
        string keyPath=@"Software\DesktopPet.Tests\Startup."+Guid.NewGuid().ToString("N");
        string executable=Path.Combine(Path.GetTempPath(),"桌宠 startup test","DesktopPet.exe");
        var registration=new PetStartupRegistration(executable,keyPath);
        try
        {
            Check(!registration.Enabled,"Missing startup registration defaults to disabled");
            registration.SetEnabled(false);
            using(var absent=Registry.CurrentUser.OpenSubKey(keyPath))
                Check(absent==null,"Disabling an absent entry does not create a registry key");
            using(var key=Registry.CurrentUser.CreateSubKey(keyPath))key.SetValue("OtherApp","untouched");
            using(var pet=new KeyboardPetWindow(root,false,registration))
            {
                Get<Timer>(pet,"timer").Stop();
                var menu=Get<PetContextMenu>(pet,"menu");
                var item=menu.Items.OfType<ToolStripMenuItem>().Single(i=>i.Text=="开机自启动");
                Check(item.Enabled&&!item.Checked,"Startup option is available and initially unchecked");
                item.PerformClick();
                Check(item.Checked&&registration.Enabled,"Menu enables actual Windows registration immediately");
                using(var key=Registry.CurrentUser.OpenSubKey(keyPath))
                    Check((string)key.GetValue(PetStartupRegistration.ValueName)=="\""+executable+"\""&&key.GetValueKind(PetStartupRegistration.ValueName)==RegistryValueKind.String,
                        "Startup command quotes the complete Unicode path with spaces and has no preview arguments");
                using(var reload=new KeyboardPetWindow(root,false,new PetStartupRegistration(executable,keyPath)))
                {
                    Get<Timer>(reload,"timer").Stop();
                    var restored=Get<PetContextMenu>(reload,"menu").Items.OfType<ToolStripMenuItem>().Single(i=>i.Text=="开机自启动");
                    Check(restored.Checked,"Restart restores startup state from Windows");
                }
                using(var preview=new KeyboardPetWindow(root,true,registration))
                {
                    Get<Timer>(preview,"timer").Stop();
                    var disabled=Get<PetContextMenu>(preview,"menu").Items.OfType<ToolStripMenuItem>().Single(i=>i.Text=="开机自启动");
                    disabled.PerformClick();
                    Check(disabled.Checked&&!disabled.Enabled&&registration.Enabled,"Preview displays state but cannot change startup");
                }
                item.PerformClick();
                Check(!item.Checked&&!registration.Enabled,"Menu removes startup registration immediately");
                using(var key=Registry.CurrentUser.OpenSubKey(keyPath))
                    Check((string)key.GetValue("OtherApp")=="untouched"&&key.GetValue(PetStartupRegistration.ValueName)==null,"Disabling only deletes the pet's own value");
                registration.SetEnabled(true);menu.Show(new Point(100,100));Application.DoEvents();
                Check(item.Checked,"Opening the menu refreshes externally changed registration");menu.Close();
                registration.SetEnabled(false);
            }
            using(var key=Registry.CurrentUser.OpenSubKey(keyPath,true))key.SetValue(PetStartupRegistration.ValueName,"\"C:\\old location\\DesktopPet.exe\"");
            Check(!registration.Enabled,"A registration for an old executable does not claim this copy is enabled");
            registration.SetEnabled(true);
            Check(registration.Enabled,"Enabling repairs the registered executable path");
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(keyPath,false); }
        Console.WriteLine("PASS: startup enable/disable, quoted Unicode path, persistence, menu refresh, stale path repair and preview isolation (temporary registry key only).");
    }
}
