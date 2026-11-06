using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;

internal static class TransparencyTests
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr window,int index);
    [DllImport("user32.dll")] static extern uint GetGuiResources(IntPtr process,uint flags);
    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    internal static void Run(string root)
    {
        using(var pet=new KeyboardPetWindow(root,true))
        {
            ((System.Windows.Forms.Timer)typeof(KeyboardPetWindow).GetField("timer",Private).GetValue(pet)).Stop();
            Check(pet.TransparencyKey.IsEmpty,"Color-key transparency must remain disabled");
            Check((GetWindowLong(pet.Handle,-20)&0x80000)!=0,"Window must use native layered alpha");
            foreach(int width in new[] {252,336,420})
            {
                typeof(KeyboardPetWindow).GetMethod("SetSpriteSize",Private).Invoke(pet,new object[] {width});
                using(var surface=new LayeredSurface(pet.Width,pet.Height))
                {
                    for(int pose=0;pose<4;pose++)
                    {
                        bool resting=pose==3;double now=pose==1 ? 2.90 : pose*4.0;
                        typeof(KeyboardPetWindow).GetField("resting",Private).SetValue(pet,resting);
                        pet.inputState.Clear();
                        if(pose==2){pet.inputState.Key(65,true,now-.4);pet.inputState.Key(74,true,now-.4);}
                        for(int i=0;i<=20;i++)pet.scene.Advance(now-.4+i*.02,.02,resting);
                        surface.Graphics.Clear(Color.Transparent);pet.DrawSceneAt(surface.Graphics,now);
                        surface.Present(pet.Handle,pet.Location);
                        Check(surface.Bitmap.GetPixel(0,0).A==0,"Canvas outside the pet must remain transparent");
                        // Check the final native-size composite as well as the
                        // lazily rendered rig mask used by mouse hit testing.
                        PointF jaw=pet.scene.Portrait.WorldPoint(new PointF(151,160));
                        PointF collar=pet.scene.Portrait.WorldPoint(new PointF(151,173));
                        float scale=width/336f;
                        for(int sample=0;sample<=12;sample++)
                        {
                            float t=sample/12f;
                            var p=new Point((int)Math.Round((jaw.X*(1-t)+collar.X*t)*scale),(int)Math.Round((jaw.Y*(1-t)+collar.Y*t)*scale));
                            Check(surface.Bitmap.GetPixel(p.X,p.Y).A>=230,"Neck/scarf seam must stay opaque at each actual window size");
                            Check(pet.Hit(p),"Native portrait and lazy alpha mask must agree on the neck/scarf hit area");
                        }
                        int opaque=0,partial=0;
                        for(int y=0;y<pet.Height;y+=3)for(int x=0;x<pet.Width;x+=3)
                        {
                            Color c=surface.Bitmap.GetPixel(x,y);
                            if(c.A>=240)opaque++;else if(c.A>0)partial++;
                            Check(!(c.A>=32&&c.R>200&&c.B>180&&c.G<80),"Magenta matte must not appear on visible edges");
                        }
                        Check(opaque>500&&partial>25,"Scaled portrait must retain opaque content and soft alpha edges");
                        foreach(Color background in new[] {Color.White,Color.FromArgb(25,27,33),Color.CornflowerBlue})
                        using(var composite=new Bitmap(pet.Width,pet.Height,PixelFormat.Format32bppPArgb))
                        using(var g=Graphics.FromImage(composite))
                        {
                            g.Clear(background);g.DrawImageUnscaled(surface.Bitmap,0,0);
                            Check(composite.GetPixel(0,0).ToArgb()==background.ToArgb(),"Desktop background outside the silhouette must remain unchanged");
                        }
                    }
                    uint handles=GetGuiResources(GetCurrentProcess(),0);
                    for(int i=0;i<100;i++)
                    {
                        surface.Graphics.Clear(Color.Transparent);pet.DrawSceneAt(surface.Graphics,12);
                        surface.Present(pet.Handle,pet.Location);
                    }
                    Check(GetGuiResources(GetCurrentProcess(),0)<=handles+2,"Repeated native presentation must not leak GDI objects");
                }
            }
        }
        Console.WriteLine("PASS: all three sizes, idle/blink/typing/rest alpha, white/dark/blue composites and 300 native presentations without GDI leaks.");
    }
}
