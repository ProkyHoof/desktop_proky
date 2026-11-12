using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using System.Runtime.InteropServices;

internal static class CurrentPetTests
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    static object Get(KeyboardPetWindow pet,string name){return typeof(KeyboardPetWindow).GetField(name,Private).GetValue(pet);}
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    [STAThread] static int Main(string[] args)
    {
        try
        {
            SetProcessDPIAware();
            string root=args[0],config=Path.Combine(root,"keyboard_pet_config.json");
            bool exists=File.Exists(config);string before=exists ? File.ReadAllText(config) : null;
            StorageTests.Run(root);
            KeyboardPetTests.Run(root);
            Console.WriteLine("PASS: input thread/queue, 78 keys, hands/eyes/neck/hair, typing speed, eight mouths and work expressions.");
            TransparencyTests.Run(root);
            InteractionTests.Run(root);
            TypingHelmetTests.Run(root);
            MusicPlaybackTests.Run(root);
            HairstyleTests.Run(root);
            TopMostTests.Run(root);
            UiInteractionTests.Run(root);
            CalendarTests.Run(root);
            ChatTests.Run(root);
            WindowEdgeTests.Run(root);
            using(var pet=new KeyboardPetWindow(root,true))
            {
                ((System.Windows.Forms.Timer)Get(pet,"timer")).Stop();
                pet.Show();Application.DoEvents();
                Check(pet.Visible&&pet.IsHandleCreated,"Preview must create a visible native window");
                ((EventWaitHandle)Get(pet,"shutdown")).Set();
                typeof(KeyboardPetWindow).GetMethod("Tick",Private).Invoke(pet,new object[] {null,EventArgs.Empty});
                Check(pet.IsDisposed,"Preview must close through its own shutdown signal");
            }
            Check(File.Exists(config)==exists&&(!exists||File.ReadAllText(config)==before),"Tests must not overwrite the saved window configuration");
            Console.WriteLine("PASS: native window lifecycle, graceful shutdown, input cleanup and preview configuration isolation.");
            return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}

internal static class StorageTests
{
    static void Check(bool value,string message){if(!value)throw new Exception("Storage: "+message);}
    internal static void Run(string assetsRoot)
    {
        string temporary=Path.Combine(Path.GetTempPath(),"DesktopPet-storage-"+Guid.NewGuid().ToString("N"));
        string legacy=Path.Combine(temporary,"legacy"),user=PetStorage.DataRoot(legacy);
        try
        {
            Directory.CreateDirectory(Path.Combine(legacy,"data","calendar","history","snapshot"));
            Directory.CreateDirectory(user);
            File.WriteAllText(Path.Combine(legacy,"keyboard_pet_config.json"),"{\"size\":252}");
            File.WriteAllText(Path.Combine(user,"keyboard_pet_config.json"),"{\"size\":420}");
            File.WriteAllText(Path.Combine(legacy,"ai_chat_config.json"),"encrypted-settings");
            File.WriteAllText(Path.Combine(legacy,"data","calendar","2026-11-06.json.bak"),"backup");
            File.WriteAllText(Path.Combine(legacy,"data","calendar","history","snapshot","2026-11-06.json"),"history");
            PetStorage.Migrate(legacy,user);
            Check(File.ReadAllText(Path.Combine(user,"keyboard_pet_config.json")).Contains("420"),"Migration must not overwrite newer preferences");
            Check(File.ReadAllText(Path.Combine(user,"ai_chat_config.json"))=="encrypted-settings","Encrypted settings copied verbatim");
            Check(File.Exists(Path.Combine(user,"data","calendar","2026-11-06.json.bak")),"Calendar backup migrated");
            Check(File.Exists(Path.Combine(user,"data","calendar","history","snapshot","2026-11-06.json")),"Recovery history migrated");
            File.Delete(Path.Combine(user,"ai_chat_config.json"));
            PetStorage.Migrate(legacy,user);
            Check(!File.Exists(Path.Combine(user,"ai_chat_config.json")),"Deleted credentials must not be reimported");
            using(var pet=new KeyboardPetWindow(assetsRoot,true,dataRoot:user))
            {
                const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
                Check((string)typeof(KeyboardPetWindow).GetField("configPath",flags).GetValue(pet)==Path.Combine(user,"keyboard_pet_config.json"),"Preferences use separate user root");
                Check((string)typeof(KeyboardPetWindow).GetField("bindingsPath",flags).GetValue(pet)==Path.Combine(user,"input_bindings.tsv"),"Bindings use separate user root");
            }
            Check(user==Path.Combine(Path.GetFullPath(legacy),"user-data"),"All persistent data must stay inside the installation");
            Console.WriteLine("PASS: installation-local user data, non-destructive one-time migration, calendar backups and recovery history.");
        }
        finally { if(Directory.Exists(temporary))Directory.Delete(temporary,true); }
    }
}

internal static class WindowEdgeTests
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Check(bool value,string message){if(!value)throw new Exception("Window edge: "+message);}
    internal static void Run(string root)
    {
        using(var pet=new KeyboardPetWindow(root,true))
        {
            ((System.Windows.Forms.Timer)typeof(KeyboardPetWindow).GetField("timer",Private).GetValue(pet)).Stop();
            var menu=(ContextMenuStrip)typeof(KeyboardPetWindow).GetField("menu",Private).GetValue(pet);
            ToolStripMenuItem sizes=null,corner=null;
            foreach(ToolStripItem item in menu.Items.Commands())
            {
                if(item.Text=="桌宠大小")sizes=(ToolStripMenuItem)item;
                if(item.Text=="回到屏幕右下角")corner=(ToolStripMenuItem)item;
            }
            var workingArea=Screen.FromPoint(Cursor.Position).WorkingArea;
            corner.PerformClick();
            Check(pet.Right==workingArea.Right,"Corner action reaches the screen edge without a fixed right inset");
            foreach(int style in new[] {0,1,2})
            {
                typeof(KeyboardPetWindow).GetMethod("ApplyWardrobe",Private).Invoke(pet,new object[] {style,3,1,1,1,1,0,0});
                for(int size=0;size<3;size++)
                {
                    ((ToolStripMenuItem)sizes.DropDownItems[size]).PerformClick();
                    Check(pet.Right==workingArea.Right,"Resizing preserves a docked right edge");
                    int width=new[] {252,336,420}[size];float scale=width/336f;
                    Check(pet.Width<(BoundPetScene.WorldWidth-50)*scale,"Native window no longer reserves the wide empty strip");
                    using(var image=new System.Drawing.Bitmap(pet.Width,pet.Height,System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                    using(var g=System.Drawing.Graphics.FromImage(image))
                    {
                        pet.inputState.Clear();
                        // Resizing initializes animation at the live stopwatch
                        // time. Settle at a fixed time before measuring a static
                        // margin, independent of how long earlier tests took.
                        for(int frame=0;frame<30;frame++)pet.scene.Advance(0,.1,false);
                        pet.DrawSceneAt(g,0);
                        int lastVisible=-1;
                        for(int x=pet.Width-1;x>=0&&lastVisible<0;x--)
                        for(int y=0;y<pet.Height;y++)if(image.GetPixel(x,y).A>=32){lastVisible=x;break;}
                        // All three hairstyles share the registered canvas so switching
                        // styles does not move or resize the desktop window.
                        float margin=(style==PetHairStyles.Short ? 64 : style==PetHairStyles.TwinPonytails ? 20 : 16)*scale;
                        Check(lastVisible>=pet.Width-margin&&lastVisible<=pet.Width-3*scale,
                            "Actual artwork reaches the window edge with a small transparent margin, size="+width+", style="+style+", lastVisible="+lastVisible+", nativeWidth="+pet.Width+", margin="+margin+", helmet="+pet.scene.Portrait.HelmetBlend);
                        for(int y=0;y<pet.Height;y++)Check(image.GetPixel(pet.Width-1,y).A==0,"Permanent artwork is not clipped at the right edge");
                    }
                    pet.inputState.SetBubbleMode(false);
                    foreach(int key in new[] {17,16,18,91,65})pet.inputState.Key(key,true,0);
                    Check(BoundPetScene.KeyDisplayFrame.Right<=pet.Width/scale-5,"Fixed key display remains inside the compact window");
                    var center=new System.Drawing.PointF(BoundPetScene.KeyDisplayFrame.X+BoundPetScene.KeyDisplayFrame.Width/2,BoundPetScene.KeyDisplayFrame.Y+20);
                    Check(pet.Hit(new System.Drawing.Point((int)(center.X*scale),(int)(center.Y*scale))),"Fixed input frame and native hit testing agree");
                    pet.inputState.SetBubbleMode(true);
                    Check(pet.scene.LiveBubble.Right<=pet.Width/scale-5,"Live bubble stays inside compact window");
                    var particle=new InputFeedbackState.FloatingKey {Label="Ctrl + Shift + Alt + A",Born=0,Sequence=-1};
                    foreach(double now in new[] {0.0,1.0,2.2})
                        Check(pet.scene.FloatingBubble(particle,now).Right<=pet.Width/scale-5,"Floating bubbles stay inside compact window");
                    pet.inputState.SetBubbleMode(false);
                    pet.Location=new System.Drawing.Point(workingArea.Right-pet.Width+20,workingArea.Top);
                    typeof(KeyboardPetWindow).GetMethod("KeepVisible",Private).Invoke(pet,null);
                    Check(pet.Right==workingArea.Right,"Dragging to the right clamps at the compact boundary");
                }
            }
        }
        Console.WriteLine("PASS: compact native window, visible right margin, docking/drag/resize, all three hairstyles, three sizes and input frame visibility/hit tests.");
    }
}
