using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

internal static class MusicPlaybackTests
{
    static void Check(bool value,string message){if(!value)throw new Exception("Music: "+message);}
    internal static void Run(string root)
    {
        var gate=new MusicActivityGate();
        for(int i=0;i<4;i++)gate.Update(.2f,.1);
        for(int i=0;i<15;i++)gate.Update(0,.1);
        Check(!gate.Playing,"Short notification does not trigger listening");
        for(int i=0;i<8;i++)gate.Update(.02f,.1);
        Check(gate.Playing,"Sustained quiet playback starts listening in under one second");
        for(int i=0;i<20;i++)gate.Update(0,.1);
        Check(gate.Playing,"Brief silence between phrases keeps the action");
        for(int i=0;i<5;i++)gate.Update(0,.1);
        Check(!gate.Playing,"Pause or mute releases the action after silence");
        for(int i=0;i<100;i++)gate.Update(float.NaN,.1);
        Check(!gate.Playing,"Invalid meter samples do not start listening");
        using(var sprites=new KeyboardPetSprites(root))
        using(var scene=new BoundPetScene(sprites,new PetInputBindings(),new InputFeedbackState()))
        {
            for(int style=0;style<2;style++)
            {
                scene.Portrait.SetHairStyle(style);scene.MusicPlaying=true;scene.MusicLevel=.2f;
                float previous=scene.Portrait.Tilt;
                for(int i=0;i<150;i++)
                {
                    scene.Advance(i/30.0,1/30.0,false);
                    Check(Math.Abs(scene.Portrait.Tilt-previous)<.03f,"Music sway has no pose jumps");
                    previous=scene.Portrait.Tilt;
                }
                Check(scene.Portrait.MusicBlend>.99&&scene.Portrait.MouthIndex==6,"Both hairstyles use the relaxed listening smile");
                var local=new PointF(65,123); // Outer ear cushion extends the silhouette.
                using(var pose=scene.Portrait.HeadMatrix())
                {
                    var p=new[] {local};pose.TransformPoints(p);
                    Check(scene.Portrait.Hit(p[0]),"Earphone follows the animated head and is clickable");
                }
                foreach(float scale in new[] {.75f,1f,1.25f})
                using(var bitmap=new Bitmap((int)(BoundPetScene.CompactWidth*scale),(int)(BoundPetScene.WorldHeight*scale),PixelFormat.Format32bppArgb))
                using(var g=Graphics.FromImage(bitmap))
                {
                    g.ScaleTransform(scale,scale);scene.Draw(g,5,false,true);
                    for(int y=0;y<bitmap.Height;y++)Check(bitmap.GetPixel(0,y).A==0&&bitmap.GetPixel(bitmap.Width-1,y).A==0,"Music artwork stays inside all window sizes");
                }
                scene.Portrait.Update(5.1,.1,false,true,false,WorkMood.Focused,240,true,.2f);
                Check(scene.Portrait.MusicMotion<.35&&scene.Portrait.MouthIndex==2,"Typing softens the sway and keeps the work expression");
                for(int i=0;i<240;i++)scene.Portrait.Update(5.2+i/30.0,1/30.0,false,true,false,WorkMood.Focused,240,true,.2f);
                Check(scene.Portrait.HelmetBlend==0,"Moderate typing while listening does not start a helmet deployment");
                scene.Advance(14,.1,true);
                Check(scene.Portrait.MusicBlend==0&&scene.Portrait.MouthIndex==7,"Rest immediately clears listening");
                scene.Advance(14.1,.1,false);
                Check(scene.Portrait.MusicBlend>0,"Waking resumes music still playing");
                scene.MusicPlaying=false;
                for(int i=0;i<90;i++)scene.Advance(14.2+i/30.0,1/30.0,false);
                Check(scene.Portrait.MusicBlend<.001,"Stopping music removes earphones smoothly");
            }
            ArmoredListening(sprites);
            // Starting music on an existing helmet switches directly to its
            // integrated audio modules without cancelling the armor action.
            foreach(int fps in new[] {15,60})
            {
                var pet=scene.Portrait;
                pet.Update(0,0,true,false,false);
                for(int i=0;i<fps*9;i++)pet.Update(i/(double)fps,1.0/fps,false,true,false,WorkMood.Focused,240);
                Check(pet.HelmetBlend>.9f,"Conflict test starts with deployed helmet");
                for(int i=0;i<fps;i++)
                {
                    pet.Update(9+i/(double)fps,1.0/fps,false,true,false,WorkMood.Focused,240,true,.2f);
                    Check(pet.HelmetBlend>.99f&&!pet.HelmetRemoving,"Music does not force deployed armor to retract");
                    Check(pet.HeadphoneBandBlend==0,"A deployed helmet never wears the ordinary headband");
                }
                Check(pet.MusicBlend>.99f&&pet.MusicArmorBlend>.99f,"Music fades into armored listening directly");
            }
        }
        using(var pet=new KeyboardPetWindow(root,true))
        {
            var menu=(ContextMenuStrip)typeof(KeyboardPetWindow).GetField("menu",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(pet);
            var toggle=menu.Items.OfType<ToolStripMenuItem>().Single(item=>item.Text=="随音乐律动");
            bool original=toggle.Checked;toggle.PerformClick();
            Check(toggle.Checked!=original,"Independent music preference can be toggled");
            Check(typeof(KeyboardPetWindow).GetField("music",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(pet)==null,"Previews never listen to actual system output");
        }
        using(var monitor=new SystemMusicMonitor())
        {
            Thread.Sleep(450);
            Check(monitor.LastError==0,"Windows output meter initialization: 0x"+monitor.LastError.ToString("X8"));
            Console.WriteLine("PASS: Windows output-meter smoke check (available="+monitor.Available+", peak="+monitor.Peak+").");
        }
        Console.WriteLine("PASS: sustained output gate, music poses, fast typing armor/audio modules, uninterrupted listening, rest/stop transitions, both hairstyles and three sizes.");
    }
    static void ArmoredListening(KeyboardPetSprites sprites)
    {
        foreach(int fps in new[] {15,60})
        for(int style=0;style<2;style++)
        {
            var input=new InputFeedbackState();
            using(var scene=new BoundPetScene(sprites,new PetInputBindings(),input))
            {
                scene.SetViewWidth(BoundPetScene.CompactWidth);scene.Portrait.SetHairStyle(style);
                double nextTap=0;float previousTilt=0,previousArmor=0;
                bool locked=false;
                for(int frame=0;frame<=fps*19;frame++)
                {
                    double now=frame/(double)fps;
                    while(nextTap<=now&&nextTap<12)
                    {input.Key(65,true,nextTap);input.Key(65,false,nextTap+.004);nextTap+=.1;}
                    scene.MusicPlaying=true;scene.MusicLevel=(float)(.12+.1*Math.Sin(now*3));
                    scene.Advance(now,1.0/fps,false);
                    var pet=scene.Portrait;
                    Check(Math.Abs(pet.Tilt-previousTilt)<.04f,"Armored nod and sway stay continuous at 15/60 fps");
                    Check(Math.Abs(pet.MusicArmorBlend-previousArmor)<.16f,"Audio modules blend continuously with side armor, fps="+fps+", t="+now+", change="+Math.Abs(pet.MusicArmorBlend-previousArmor));
                    previousTilt=pet.Tilt;previousArmor=pet.MusicArmorBlend;
                    if(now>2)Check(pet.MusicBlend>.99f,"Music and backdrop stay active throughout deployment and removal");
                    if(now<5)Check(pet.HelmetBlend==0,"Brief rapid input cannot immediately trigger armor");
                    if(now>10.5&&now<12)
                    {
                        locked=true;
                        Check(pet.HelmetVisor>.99f&&pet.MusicArmorBlend>.99f&&scene.Hands.Typing,"Real rapid taps deploy armor while listening and typing");
                        Check(pet.HeadphoneBandBlend==0,"Integrated audio mode clears the ordinary headband");
                        Check(scene.Hit(pet.WorldPoint(new PointF(78,108)),false,true),"Armor audio module participates in hit testing");
                    }
                    if(frame%(fps/3)==0)
                    foreach(float scale in new[] {.75f,1f,1.25f})
                    using(var image=new Bitmap((int)(scene.ViewWidth*scale),(int)(BoundPetScene.WorldHeight*scale),PixelFormat.Format32bppArgb))
                    using(var g=Graphics.FromImage(image))
                    {
                        g.ScaleTransform(scale,scale);scene.Draw(g,now,false,true);
                        for(int y=0;y<image.Height;y++)Check(image.GetPixel(0,y).A==0&&image.GetPixel(image.Width-1,y).A==0,"Armored audio stays inside all three window sizes");
                    }
                }
                Check(locked&&scene.Portrait.HelmetBlend==0&&scene.Portrait.HeadphoneBandBlend>.99f,"Stopping input retracts armor and restores normal listening");
                // Restarting the simulated clock, rest and stopped audio all
                // release transient lighting and modules without stale overlays.
                scene.Advance(0,0,true);
                Check(scene.Portrait.MusicBlend==0&&scene.Portrait.MusicArmorBlend==0&&scene.Portrait.HelmetBlend==0,"Rest clears both audio and armor immediately");
                for(int i=0;i<fps*11;i++)scene.Portrait.Update(i/(double)fps,1.0/fps,false,true,false,WorkMood.Turbo,600,true,.3f);
                Check(scene.Portrait.MusicArmorBlend>.99f,"Second fast session can enter armored audio");
                for(int i=0;i<fps;i++)scene.Portrait.Update(11+i/(double)fps,1.0/fps,false,true,false,WorkMood.Turbo,600,false,0);
                Check(scene.Portrait.MusicBlend<.01f&&scene.Portrait.HelmetVisor>.99f,"Stopping audio fades listening but preserves typing armor");
            }
        }
    }
}
