using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

internal static class HairstyleTests
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Check(bool value,string message){if(!value)throw new Exception("Hairstyle: "+message);}
    static ToolStripMenuItem Menu(KeyboardPetWindow pet)
    {
        var menu=(ContextMenuStrip)typeof(KeyboardPetWindow).GetField("menu",Private).GetValue(pet);
        return menu.Items.OfType<ToolStripMenuItem>().Single(item=>item.Text=="选择发型");
    }
    internal static void Run(string root)
    {
        using(var sprites=new KeyboardPetSprites(root))
        using(var portrait=new LivingPetPortrait(sprites))
        {
            // Switching must invalidate the lazily generated hit mask even
            // before the next animation frame, and erase the old silhouette.
            using(var shortMask=(Bitmap)portrait.Image.Clone())
            {
                portrait.SetHairStyle(PetHairStyles.Reference);
                int added=0;
                for(int y=165;y<225;y++)for(int x=15;x<100;x++)
                {
                    int px=x+LivingPetPortrait.Padding,py=y+LivingPetPortrait.Padding;
                    if(shortMask.GetPixel(px,py).A>8||portrait.Image.GetPixel(px,py).A<240)continue;
                    Check(portrait.Hit(new PointF(x,y)),"New long locks must be clickable immediately");
                    added++;
                }
                Check(added>800,"Reference style must add the actual shoulder-length hair silhouette");
                portrait.SetHairStyle(PetHairStyles.Short);
                for(int y=165;y<225;y++)for(int x=15;x<100;x++)
                {
                    int px=x+LivingPetPortrait.Padding,py=y+LivingPetPortrait.Padding;
                    Check(shortMask.GetPixel(px,py).ToArgb()==portrait.Image.GetPixel(px,py).ToArgb(),"Switching back must remove every long-hair pixel");
                }
            }
            // The long back locks sit behind opaque clothing, so changing
            // styles cannot restore the old white scraps on the torso hem.
            for(int pose=0;pose<3;pose++)
            {
                portrait.Update(pose*2.7,.1,pose==2,pose==1,false);
                portrait.SetHairStyle(PetHairStyles.Short);
                using(var before=(Bitmap)portrait.Image.Clone())
                {
                    portrait.SetHairStyle(PetHairStyles.Reference);
                    for(int y=203;y<216;y++)for(int x=139;x<167;x++)
                    {
                        var points=new[] {new PointF(x,y)};using(var body=portrait.BodyMatrix())body.TransformPoints(points);
                        int px=(int)Math.Round(points[0].X)+LivingPetPortrait.Padding,py=(int)Math.Round(points[0].Y)+LivingPetPortrait.Padding;
                        Check(before.GetPixel(px,py).ToArgb()==portrait.Image.GetPixel(px,py).ToArgb(),"Reference hair must stay behind the torso in every pose");
                    }
                }
            }
        }

        // Compare the real composed keyboard, beyond the torso silhouette.
        // Back hair must be behind the desk, not just behind the clothing.
        using(var sprites=new KeyboardPetSprites(root))
        {
            foreach(int width in new[] {252,336,420})for(int mode=0;mode<3;mode++)
            {
                var input=new InputFeedbackState();
                using(var scene=new BoundPetScene(sprites,new PetInputBindings(),input))
                using(var shortScene=new Bitmap(width*480/336,600,PixelFormat.Format32bppArgb))
                using(var longScene=new Bitmap(shortScene.Width,shortScene.Height,PixelFormat.Format32bppArgb))
                using(var a=Graphics.FromImage(shortScene))using(var b=Graphics.FromImage(longScene))
                {
                    float scale=width/336f;a.ScaleTransform(scale,scale);b.ScaleTransform(scale,scale);
                    for(int frame=0;frame<=60;frame++)
                    {
                        double now=frame/30.0;
                        if(mode==1&&frame%3==0){input.Key(65,true,now);input.Key(65,false,now+.001);}
                        scene.Advance(now,1/30.0,mode==2);
                    }
                    input.Key(32,true,2);scene.Advance(2,0,mode==2);
                    scene.Portrait.SetHairStyle(PetHairStyles.Short);scene.Draw(a,2,mode==2,true);
                    scene.Portrait.SetHairStyle(PetHairStyles.Reference);scene.Draw(b,2,mode==2,true);
                    int checkedPixels=0;
                    for(int y=(int)Math.Ceiling((363-PetDeskProjection.Lift)*scale);y<(392-PetDeskProjection.Lift)*scale;y++)
                    for(int x=(int)Math.Ceiling(126*scale);x<270*scale;x++)
                    {
                        Color expected=shortScene.GetPixel(x,y),actual=longScene.GetPixel(x,y);
                        Check(Math.Abs(expected.R-actual.R)<=2&&Math.Abs(expected.G-actual.G)<=2&&Math.Abs(expected.B-actual.B)<=2&&Math.Abs(expected.A-actual.A)<=2,
                            "Long hair must not cover the keyboard below the hem: size="+width+" mode="+mode+" at "+x+","+y);
                        checkedPixels++;
                    }
                    Check(checkedPixels>2000,"Depth regression covers the central keyboard and lit keys");
                }
            }
        }

        // Save/reload through the real menu in an isolated configuration,
        // keeping the user's window, bindings and saved selection untouched.
        string isolated=Path.Combine(Path.GetTempPath(),"DesktopPet.HairstyleTests."+Guid.NewGuid().ToString("N"));
        try
        {
            string assets=Path.Combine(isolated,"assets","keyboard-knight"),rig=Path.Combine(assets,"rig");
            Directory.CreateDirectory(rig);
            string source=Path.Combine(root,"assets","keyboard-knight","rig");
            foreach(string file in Directory.GetFiles(source,"*",SearchOption.AllDirectories))
            {
                string destination=Path.Combine(rig,file.Substring(source.Length+1));
                Directory.CreateDirectory(Path.GetDirectoryName(destination));File.Copy(file,destination);
            }
            File.Copy(Path.Combine(root,"assets","keyboard-knight","soft-style-arms.png"),Path.Combine(assets,"soft-style-arms.png"));
            string config=Path.Combine(isolated,"keyboard_pet_config.json");
            File.WriteAllText(config,"{\"x\":0,\"y\":0,\"size\":336,\"follow\":0,\"hairStyle\":0}");
            using(var pet=new KeyboardPetWindow(isolated))
            {
                var original=pet.scene;var menu=Menu(pet);
                var display=((ContextMenuStrip)typeof(KeyboardPetWindow).GetField("menu",Private).GetValue(pet)).Items
                    .OfType<ToolStripMenuItem>().Single(item=>item.Text=="按键显示方式");
                Check(!pet.inputState.BubbleMode,"Old configuration defaults to fixed input frame");
                var musicToggle=((ContextMenuStrip)typeof(KeyboardPetWindow).GetField("menu",Private).GetValue(pet)).Items
                    .OfType<ToolStripMenuItem>().Single(item=>item.Text=="随音乐律动");
                Check(musicToggle.Checked,"Old configuration enables music by default");
                musicToggle.PerformClick();
                Check(File.ReadAllText(config).Contains("\"followMusic\":0"),"Music toggle saves its independent preference immediately");
                ((ToolStripMenuItem)display.DropDownItems[1]).PerformClick();
                Check(pet.inputState.BubbleMode&&display.DropDownItems.OfType<ToolStripMenuItem>().Count(i=>i.Checked)==1,"Display menu switches to bubbles with one selected option");
                Check(File.ReadAllText(config).Contains("\"bubbleKeys\":1"),"Display menu saves mode immediately");
                ((ToolStripMenuItem)display.DropDownItems[0]).PerformClick();
                Check(!pet.inputState.BubbleMode&&File.ReadAllText(config).Contains("\"bubbleKeys\":0"),"Display menu switches back to fixed frame and saves");
                ((ToolStripMenuItem)display.DropDownItems[1]).PerformClick();
                Check(pet.scene.Portrait.HairStyle==PetHairStyles.Short,"Load a saved short style");
                pet.inputState.Key(65,true,0);
                ((ToolStripMenuItem)menu.DropDownItems[1]).PerformClick();
                Check(pet.scene==original&&pet.scene.Portrait.HairStyle==PetHairStyles.Reference,"Menu switches style without resetting the scene");
                Check(pet.inputState.HeldKeys.Contains(65),"Switching style preserves active input");
                Check(menu.DropDownItems.OfType<ToolStripMenuItem>().Count(i=>i.Checked)==1,"Only the selected style is checked");
                foreach(int width in new[] {252,336,420})
                {
                    typeof(KeyboardPetWindow).GetMethod("SetSpriteSize",Private).Invoke(pet,new object[] {width});
                    Check(pet.scene.Portrait.HairStyle==PetHairStyles.Reference,"Resizing preserves hairstyle");
                    Check(pet.inputState.BubbleMode,"Resizing preserves bubble mode");
                    using(var image=new Bitmap(pet.Width,pet.Height,PixelFormat.Format32bppArgb))
                    using(var g=Graphics.FromImage(image))
                    {pet.DrawSceneAt(g,0);Check(image.GetPixel(0,0).A==0,"Long hair retains transparent window edges at every size");}
                }
                Check(File.ReadAllText(config).Contains("\"hairStyle\":1"),"Menu saves selection immediately");
            }
            using(var reload=new KeyboardPetWindow(isolated,true))
            {
                Check(reload.scene.Portrait.HairStyle==PetHairStyles.Reference&&reload.inputState.BubbleMode,"Restart restores hairstyle and bubble mode");
                var musicToggle=((ContextMenuStrip)typeof(KeyboardPetWindow).GetField("menu",Private).GetValue(reload)).Items
                    .OfType<ToolStripMenuItem>().Single(item=>item.Text=="随音乐律动");
                Check(!musicToggle.Checked,"Restart preserves disabled music preference");
            }
            File.WriteAllText(config,"{\"hairStyle\":999}");
            using(var invalid=new KeyboardPetWindow(isolated,true))
                Check(invalid.scene.Portrait.HairStyle==PetHairStyles.Short&&!invalid.inputState.BubbleMode,"Unknown style falls back safely, absent display mode uses fixed frame");
            File.WriteAllText(config,"{}");
            using(var upgrade=new KeyboardPetWindow(isolated,true))
                Check(upgrade.scene.Portrait.HairStyle==PetHairStyles.Reference,"Existing configurations can open the requested reference hairstyle");
        }
        finally
        {
            string resolved=Path.GetFullPath(isolated),temporary=Path.GetFullPath(Path.GetTempPath());
            if(!resolved.StartsWith(temporary,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(resolved).StartsWith("DesktopPet.HairstyleTests.",StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected hairstyle test directory");
            if(Directory.Exists(resolved))Directory.Delete(resolved,true);
        }
        Console.WriteLine("PASS: hairstyle silhouettes/hit masks, torso/keyboard depth in three sizes and poses, menu selection, immediate persistence, restart and legacy config.");
    }
}
