using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

internal static class WardrobeTests
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static void Check(bool value,string message){if(!value)throw new Exception("Wardrobe: "+message);}
    static object Field(object value,string name){return value.GetType().GetField(name,Private).GetValue(value);}
    static void Invoke(object value,string name,params object[] args){value.GetType().GetMethod(name,Private).Invoke(value,args);}
    static int RenderChanges(BoundPetScene scene,Action change,bool preserveAlpha=true)
    {
        using(var before=new Bitmap(BoundPetScene.WorldWidth,BoundPetScene.WorldHeight))
        using(var after=new Bitmap(before.Width,before.Height))
        {
            using(var g=Graphics.FromImage(before))scene.Draw(g,0,false,true);
            change();
            using(var g=Graphics.FromImage(after))scene.Draw(g,0,false,true);
            int count=0;
            for(int y=0;y<before.Height;y++)for(int x=0;x<before.Width;x++)
            {
                Color a=before.GetPixel(x,y),b=after.GetPixel(x,y);
                if(preserveAlpha)Check(a.A==b.A,"palette and iris changes preserve transparency and geometry");
                if(a.ToArgb()!=b.ToArgb())count++;
            }
            return count;
        }
    }
    static void CheckStyles(BoundPetScene scene,int keyboard,string message)
    {Check(scene.KeyboardStyle==keyboard,message);}
    static PetWardrobeWindow Open(KeyboardPetWindow pet)
    {
        var menu=(PetContextMenu)Field(pet,"menu");
        Check(!menu.Items.Commands().Any(item=>item.Text=="选择发型"),"hairstyle entry is merged into wardrobe");
        menu.Items.Commands().Single(item=>item.Text=="皮肤…").PerformClick();
        Application.DoEvents();return (PetWardrobeWindow)Field(pet,"wardrobeWindow");
    }
    static void CheckPirateHat(string root,string previewPath)
    {
        using(var sprites=new KeyboardPetSprites(root))
        using(var sheet=new Bitmap(1060,510))
        using(var review=Graphics.FromImage(sheet))
        using(var font=new Font("Microsoft YaHei UI",16,FontStyle.Bold))
        {
            review.Clear(Color.FromArgb(245,241,231));
            for(int style=0;style<2;style++)
            {
                var input=new InputFeedbackState();
                using(var scene=new BoundPetScene(sprites,new PetInputBindings(),input))
                {
                    scene.SetPadStyle(1);scene.SetKeyboardStyle(3);scene.SetMouseStyle(1);
                    scene.Portrait.SetHairStyle(style);scene.Portrait.SetHeadwearStyle(1);
                    for(int frame=0;frame<=330;frame++)
                    {
                        double now=frame/30.0;
                        if(frame%6==0){input.Key(65,true,now);input.Key(65,false,now+.003);}
                        scene.Advance(now,1/30.0,false);
                    }
                    Check(scene.Portrait.HelmetBlend>.99f&&scene.Hands.Typing,"sustained typing deploys the pirate hat with both hairstyles");
                    using(var overlay=new Bitmap(356,285))
                    using(var g=Graphics.FromImage(overlay))
                    using(var head=scene.Portrait.HeadMatrix())
                    {
                        g.TranslateTransform(28,28);scene.Portrait.DrawHelmet(g,false);
                        var points=new[]{new PointF(119,131),new PointF(186,134),new PointF(151,156)};
                        head.TransformPoints(points);
                        foreach(var point in points)Check(overlay.GetPixel((int)Math.Round(point.X+28),(int)Math.Round(point.Y+28)).A<16,"pirate hat leaves both eyes and mouth clear");
                    }
                    Check(scene.Hit(scene.Portrait.WorldPoint(new PointF(150,30)),false,true),"pirate hat crown participates in the portrait alpha hit mask");
                    scene.MusicPlaying=true;scene.MusicLevel=.6f;
                    for(int frame=331;frame<=390;frame++)
                    {
                        double now=frame/30.0;
                        if(frame%6==0){input.Key(65,true,now);input.Key(65,false,now+.003);}
                        scene.Advance(now,1/30.0,false);
                    }
                    Check(scene.Portrait.MusicBlend>.99f&&scene.Portrait.MusicArmorBlend==0&&scene.Portrait.HeadphoneBandBlend==0,"pirate hat listening keeps ear cups while hiding the headband and metal modules");
                    using(var overlay=new Bitmap(scene.Portrait.Image.Width,scene.Portrait.Image.Height))
                    {
                        using(var g=Graphics.FromImage(overlay))
                        {
                            g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                            g.PixelOffsetMode=System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                            g.TranslateTransform(LivingPetPortrait.Padding,LivingPetPortrait.Padding);
                            scene.Portrait.DrawHelmet(g,false);
                        }
                        for(int y=0;y<overlay.Height;y++)for(int x=0;x<overlay.Width;x++)
                        {
                            var hat=overlay.GetPixel(x,y);
                            if(hat.A==255)
                            {
                                var actual=scene.Portrait.Image.GetPixel(x,y);
                                Check(Math.Abs(actual.R-hat.R)<=2&&Math.Abs(actual.G-hat.G)<=2&&Math.Abs(actual.B-hat.B)<=2,"listening never paints headphones or notes over the opaque pirate hat, x="+x+", y="+y+", expected="+hat+", actual="+actual);
                            }
                        }
                    }
                    review.DrawString(style==0 ? "短发 · 海盗帽 + 耳罩" : "长发 · 海盗帽 + 耳罩",font,Brushes.DarkSlateGray,style*530+22,16);
                    var state=review.Save();review.TranslateTransform(style*530+12,-88);review.ScaleTransform(1.4f,1.4f);
                    scene.Draw(review,13,false,false);review.Restore(state);
                    input.Clear();for(int frame=391;frame<=540;frame++)scene.Advance(frame/30.0,1/30.0,false);
                    Check(scene.Portrait.HelmetBlend==0,"stopping typing retracts the pirate hat");
                    Check(scene.Portrait.HeadphoneBandBlend>.99f,"removing the pirate hat restores the ordinary listening headband");
                    scene.Advance(18.1,.1,true);Check(scene.Portrait.HelmetBlend==0,"rest clears pirate hat deployment");
                }
            }
            if(!String.IsNullOrEmpty(previewPath))sheet.Save(Path.Combine(Path.GetDirectoryName(previewPath),"navigation-chart-pirate-hat.png"),ImageFormat.Png);
        }
    }
    static void CheckMusicLooks(string root,string previewPath)
    {
        using(var sprites=new KeyboardPetSprites(root))
        using(var sheet=new Bitmap(1080,820))
        using(var review=Graphics.FromImage(sheet))
        using(var hats=new Bitmap(720,820))
        using(var hatReview=Graphics.FromImage(hats))
        using(var font=new Font("Microsoft YaHei UI",13,FontStyle.Bold))
        {
            review.Clear(Color.FromArgb(242,247,249));
            hatReview.Clear(Color.FromArgb(242,247,249));
            for(int hair=0;hair<2;hair++)
            using(var scene=new BoundPetScene(sprites,new PetInputBindings(),new InputFeedbackState()))
            {
                scene.SetViewWidth(BoundPetScene.CompactWidth);scene.Portrait.SetHairStyle(hair);
                scene.MusicPlaying=true;scene.MusicLevel=.35f;
                for(int frame=0;frame<=120;frame++)scene.Advance(frame/30.0,1/30.0,false);
                for(int look=0;look<3;look++)
                {
                    scene.SetMusicStyle(0);scene.Portrait.SetListeningStyle(0);
                    if(look>0)Check(RenderChanges(scene,()=>scene.SetMusicStyle(1),false)>1000,"ocean waves render independently from level bars");
                    if(look==2)Check(RenderChanges(scene,()=>scene.Portrait.SetListeningStyle(1),false)>1000,"bandana replaces the original headphones");
                    review.DrawString((hair==0 ? "短发 · " : "长发 · ")+new[]{"光柱 + 耳机","海浪 + 耳机","海浪 + 头巾"}[look],font,Brushes.DarkSlateGray,look*360+15,hair*410+16);
                    var saved=review.Save();review.TranslateTransform(look*360+4,hair*410-70);
                    scene.Draw(review,4,false,false);review.Restore(saved);
                    foreach(float scale in new[]{.75f,1f,1.25f})
                    using(var image=new Bitmap((int)(scene.ViewWidth*scale),(int)(BoundPetScene.WorldHeight*scale)))
                    using(var g=Graphics.FromImage(image))
                    {
                        g.ScaleTransform(scale,scale);scene.Draw(g,4,false,false);
                        for(int y=0;y<image.Height;y++)Check(image.GetPixel(0,y).A==0&&image.GetPixel(image.Width-1,y).A==0,"music styles stay inside every window size");
                    }
                }
                using(var overlay=new Bitmap(356,285))
                using(var g=Graphics.FromImage(overlay))
                using(var head=scene.Portrait.HeadMatrix())
                {
                    g.TranslateTransform(28,28);Invoke(scene.Portrait,"DrawBandana",g);
                    var points=new[]{new PointF(119,131),new PointF(186,134),new PointF(151,156)};head.TransformPoints(points);
                    foreach(var point in points)Check(overlay.GetPixel((int)Math.Round(point.X+28),(int)Math.Round(point.Y+28)).A==0,"bandana leaves eyes and mouth unobstructed");
                }
                scene.MusicPlaying=false;
                for(int frame=121;frame<=210;frame++)scene.Advance(frame/30.0,1/30.0,false);
                Check(scene.Portrait.MusicBlend<.001f,"waves and bandana fade out when audio stops");
                for(int hat=0;hat<2;hat++)
                {
                    scene.Portrait.Update(0,0,true,false,false);scene.Portrait.SetHeadwearStyle(hat);
                    for(int frame=0;frame<=330;frame++)scene.Portrait.Update(frame/30.0,1/30.0,false,true,false,WorkMood.Turbo,600,true,.35f);
                    Check(scene.Portrait.HelmetBlend>.99f&&scene.Portrait.HeadphoneBandBlend==0&&scene.Portrait.MusicArmorBlend==0,"bandana and both typing hats avoid headphone overlays");
                    hatReview.DrawString((hair==0 ? "短发 · " : "长发 · ")+(hat==0 ? "骑士头盔 + 头巾" : "海盗帽 + 头巾"),font,Brushes.DarkSlateGray,hat*360+15,hair*410+16);
                    var saved=hatReview.Save();hatReview.TranslateTransform(hat*360+4,hair*410-70);
                    scene.Draw(hatReview,11,false,false);hatReview.Restore(saved);
                }
            }
            if(!String.IsNullOrEmpty(previewPath))
            {
                sheet.Save(Path.Combine(Path.GetDirectoryName(previewPath),"music-wave-bandana.png"),ImageFormat.Png);
                hats.Save(Path.Combine(Path.GetDirectoryName(previewPath),"music-bandana-hats.png"),ImageFormat.Png);
            }
        }
    }
    static Bitmap CodeBackdrop(BoundPetScene scene,double now)
    {
        var image=new Bitmap(scene.ViewWidth,BoundPetScene.WorldHeight,PixelFormat.Format32bppArgb);
        using(var g=Graphics.FromImage(image))Invoke(scene,"DrawMusicBackdrop",g,now);
        return image;
    }
    static int VerticalCodeMotion(Bitmap before,Bitmap after)
    {
        double best=-1;int shift=0;
        for(int dy=-9;dy<=9;dy++)
        {
            double score=0;
            for(int y=155;y<350;y+=2)for(int x=20;x<110;x+=2)
                score+=before.GetPixel(x,y).A*(double)after.GetPixel(x,y+dy).A;
            if(score>best){best=score;shift=dy;}
        }
        Check(best>10000,"Hacker code renders visible text in the transparent backdrop");
        return shift;
    }
    static void CheckHackerCode(string root)
    {
        using(var sprites=new KeyboardPetSprites(root))
        using(var scene=new BoundPetScene(sprites,new PetInputBindings(),new InputFeedbackState()))
        {
            scene.SetViewWidth(BoundPetScene.CompactWidth);scene.SetMusicStyle(PetMusicStyles.HackerCode);
            scene.SetTone(3);scene.MusicPlaying=true;scene.MusicLevel=.3f;
            for(int frame=0;frame<=90;frame++)scene.Advance(frame/30.0,1/30.0,false);
            double now=3;
            foreach(float level in new[]{.3f,0f,.3f,0f})
            using(var before=CodeBackdrop(scene,now))
            {
                scene.MusicLevel=level;now+=.18;scene.Advance(now,.18,false);
                using(var after=CodeBackdrop(scene,now))
                    Check(VerticalCodeMotion(before,after)<0,"Rendered code keeps moving upward through loud/quiet changes without jumping backward");
            }
            scene.MusicPlaying=false;
            for(int frame=0;frame<90;frame++){now+=1/30.0;scene.Advance(now,1/30.0,false);}
            using(var stopped=CodeBackdrop(scene,now))
                for(int y=124;y<388;y+=4)for(int x=16;x<scene.ViewWidth-16;x+=4)
                    Check(stopped.GetPixel(x,y).A==0,"Stopping music fades out the code stream");
            scene.MusicPlaying=true;scene.Advance(now+.1,.1,true);
            using(var rest=CodeBackdrop(scene,now+.1))
                Check(rest.GetPixel(20,200).A==0&&scene.Portrait.MusicBlend==0,"Rest clears the hacker code backdrop");
        }
        Console.WriteLine("PASS: rendered hacker code scrolls upward continuously through volume changes and fades on stop/rest.");
    }
    static Bitmap ProjectionFeedback(BoundPetScene scene,double now)
    {
        var image=new Bitmap(BoundPetScene.WorldWidth,BoundPetScene.WorldHeight,PixelFormat.Format32bppArgb);
        using(var g=Graphics.FromImage(image)){g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;scene.DrawMouseFeedback(g,now);}
        return image;
    }
    static void CheckCyberProjection(string root,string previewPath)
    {
        var input=new InputFeedbackState();
        using(var sprites=new KeyboardPetSprites(root))
        using(var scene=new BoundPetScene(sprites,new PetInputBindings(),input))
        using(var sheet=new Bitmap(1080,460))
        using(var canvas=Graphics.FromImage(sheet))
        using(var font=new Font("Microsoft YaHei UI",11,FontStyle.Bold))
        {
            scene.SetKeyboardStyle(4);scene.SetMouseStyle(3);scene.SetPadStyle(2);scene.SetTone(3);
            scene.Advance(4,0,false);
            using(var desk=new Bitmap(BoundPetScene.WorldWidth,BoundPetScene.WorldHeight))
            {
                using(var g=Graphics.FromImage(desk))scene.DrawDesk(g,4);
                var gap=PetDeskProjection.Project(1.4f,.5f);
                Check(desk.GetPixel((int)Math.Round(gap.X),(int)Math.Round(gap.Y)).A==255,"An opaque deck remains visible through gaps between holographic keys");
                Check(desk.GetPixel(200,385).A==255,"The deck underneath the light keys is opaque");
                int opaque=0;
                for(int y=342;y<388;y++)for(int x=160;x<280;x++)
                {
                    if(desk.GetPixel(x,y).A==255)opaque++;
                }
                Check(opaque>3000,"The solid deck covers the keyboard surface");
            }
            using(var key=new Bitmap(BoundPetScene.WorldWidth,BoundPetScene.WorldHeight))
            {
                using(var g=Graphics.FromImage(key))Invoke(scene,"DrawCap",g,PetKeyboardLayout.Find(65),false,1f);
                int translucent=0;
                for(int y=0;y<key.Height;y++)for(int x=0;x<key.Width;x++)
                {
                    int alpha=key.GetPixel(x,y).A;Check(alpha<255,"The projected key itself remains translucent");if(alpha>0)translucent++;
                }
                Check(translucent>15,"The translucent key renders independently of its opaque deck");
            }
            // Each button, including side buttons, must render inside the glass.
            for(int button=1;button<=5;button++)
            {
                input.Clear();input.Button(button,true,4);
                using(var image=ProjectionFeedback(scene,4.12))
                {
                    int pixels=0;var bounds=scene.CyberMouseScreen;bounds.Inflate(1,1);
                    for(int y=0;y<image.Height;y++)for(int x=0;x<image.Width;x++)
                    if(image.GetPixel(x,y).A>0){pixels++;Check(bounds.Contains(x,y),"Mouse effects stay inside the holographic screen");}
                    Check(pixels>30,"Every mouse button has visible projected feedback: "+button);
                }
                Check(input.ButtonFeedback(button,20)==1,"Long held buttons remain lit on the projection");
                input.Button(button,false,20);
                using(var faded=ProjectionFeedback(scene,21))
                    Check(!Enumerable.Range(0,faded.Height).Any(y=>Enumerable.Range(0,faded.Width).Any(x=>faded.GetPixel(x,y).A>0)),"Release fades out projected click effects");
            }
            input.Clear();input.Move(0,0,4);PointF before=scene.CyberMouseCursor;input.Move(10000,-10000,4.01);
            var cursor=scene.CyberMouseCursor;
            Check(cursor.X>before.X&&cursor.Y<before.Y&&scene.CyberMouseScreen.Contains(cursor),"Mouse movement updates a bounded cursor inside the projection");
            using(var vertical=ProjectionFeedback(scene,4.1))
            {
                input.Wheel(120,false,4);
                using(var up=ProjectionFeedback(scene,4.1))
                {
                    input.Wheel(-120,false,4);
                    using(var down=ProjectionFeedback(scene,4.1))
                    {
                        input.Wheel(120,true,4);
                        using(var horizontal=ProjectionFeedback(scene,4.1))
                        {
                            int opposite=0,axis=0,visible=0;
                            var bounds=scene.CyberMouseScreen;bounds.Inflate(1,1);
                            for(int y=0;y<up.Height;y++)for(int x=0;x<up.Width;x++)
                            {
                                if(up.GetPixel(x,y).ToArgb()!=down.GetPixel(x,y).ToArgb())opposite++;
                                if(up.GetPixel(x,y).ToArgb()!=horizontal.GetPixel(x,y).ToArgb())axis++;
                                if(horizontal.GetPixel(x,y).A>0){visible++;Check(bounds.Contains(x,y),"Scroll feedback is clipped to the projection");}
                            }
                            Check(opposite>10&&axis>10&&visible>20,"Scroll direction and horizontal axis produce distinct projected arrows");
                        }
                    }
                }
            }
            input.Clear();
            Check(RenderChanges(scene,()=>{input.Key(65,true,0);scene.Advance(0,0,false);},false)>30,"Keyboard input lights its individual holographic key");
            input.Clear();
            foreach(int width in new[]{252,336,420})
            foreach(int direction in new[]{-1,1})
            using(var image=new Bitmap((int)Math.Ceiling(scene.ViewWidth*width/336f),575))
            using(var g=Graphics.FromImage(image))
            {
                input.MouseOffset=new PointF(direction*43,direction*25);
                for(int frame=0;frame<25;frame++)scene.Advance(4+frame*.05,.05,false);
                input.Button(1,true,5.2);input.Wheel(direction*120,true,5.2);
                g.ScaleTransform(width/336f,width/336f);scene.Draw(g,5.25,false,true);
                for(int y=0;y<image.Height;y++)Check(image.GetPixel(0,y).A==0&&image.GetPixel(image.Width-1,y).A==0,"Extreme mouse motion and projection effects fit all pet sizes");
                input.Clear();
            }
            // Check actual rendered overlap rather than just comparing anchors.
            for(int pose=0;pose<4;pose++)
            {
                input.Clear();
                if(pose==1)input.MouseOffset=new PointF(43,-25);
                if(pose==2)input.Key(74,true,6);
                for(int frame=0;frame<35;frame++)scene.Advance(6+frame*.04,.04,false);
                using(var hand=new Bitmap(BoundPetScene.WorldWidth,BoundPetScene.WorldHeight))
                using(var device=new Bitmap(hand.Width,hand.Height))
                {
                    using(var g=Graphics.FromImage(hand))sprites.Arms[0].Draw(g,scene.Shoulder(true),new PointF(scene.Hands.LeftPalm.X,scene.Hands.LeftPalm.Y+9));
                    using(var g=Graphics.FromImage(device))scene.DrawCyberMouseInstrument(g);
                    int attached=0;
                    for(int y=0;y<hand.Height;y++)for(int x=0;x<hand.Width;x++)
                    if(device.GetPixel(x,y).A>32){attached++;Check(hand.GetPixel(x,y).A>32,"The hand instrument stays on the glove through mouse and typing poses");}
                    Check(attached>30,"The hand instrument remains visible");
                }
                if(pose==2)Check(scene.CyberMouseScreen.Width<1,"Typing retracts the cone into the hand instrument");
                if(pose==3)
                {
                    Check(scene.CyberMouseScreen.Width>69,"Leaving the keyboard restores the hand projection");
                    using(var cone=new Bitmap(BoundPetScene.WorldWidth,BoundPetScene.WorldHeight))
                    {
                        using(var g=Graphics.FromImage(cone)){g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;scene.DrawMouse(g,7.4);}
                        var emitter=scene.CyberMouseEmitter;var screen=scene.CyberMouseScreen;int beam=0;
                        Check(screen.Bottom<emitter.Y,"The inverted cone opens above the hand instrument");
                        for(int y=(int)Math.Ceiling(screen.Bottom+4);y<=Math.Ceiling(emitter.Y)+1;y++)for(int x=0;x<cone.Width;x++)
                        if(cone.GetPixel(x,y).A>8)
                        {
                            beam++;float spread=(emitter.Y-y)*screen.Width/2/(emitter.Y-screen.Top-screen.Height/2);
                            Check(y<=emitter.Y+1&&Math.Abs(x-emitter.X)<=spread+2,"The inverted cone narrows downward to the glove instrument without detached light");
                        }
                        for(int y=(int)Math.Ceiling(emitter.Y)+2;y<cone.Height;y++)for(int x=0;x<cone.Width;x++)
                            Check(cone.GetPixel(x,y).A==0,"The projection does not extend below its hand instrument");
                        Check(beam>30,"The cone visibly connects the projection to the hand");
                    }
                }
            }
            canvas.Clear(Color.FromArgb(235,242,247));
            string[] labels={"实体底座 / 透明悬浮按键","投影按键 / 打字时收起鼠标投影","倒锥投影 / 尖端固定在手部仪器","左键 + 右键 / 投影反馈","滚轮 / 上下滚动","横向滚轮 + 中键与侧键"};
            for(int look=0;look<6;look++)
            {
                double lookAt=10+look*3;
                input.Clear();input.MouseOffset=PointF.Empty;
                for(int frame=0;frame<35;frame++)scene.Advance(lookAt-1.5+frame*.04,.04,false);
                if(look==1){input.Key(65,true,lookAt);input.Key(74,true,lookAt+.01);}
                if(look==2){input.Move(0,0,lookAt);input.Move(75,45,lookAt+.01);input.Button(1,true,lookAt);}
                if(look==3){input.Button(1,true,lookAt);input.Button(2,true,lookAt+.01);}
                if(look==4)input.Wheel(120,false,lookAt);
                if(look==5){input.Wheel(-120,true,lookAt);for(int b=3;b<=5;b++)input.Button(b,true,lookAt);}
                scene.Advance(lookAt+.12,.05,false);
                int col=look%3,row=look/3;
                if(row==1)using(var dark=new SolidBrush(Color.FromArgb(17,24,39)))canvas.FillRectangle(dark,col*360,row*230,360,230);
                canvas.DrawString(labels[look],font,row==0 ? Brushes.DarkSlateGray : Brushes.WhiteSmoke,col*360+12,row*230+12);
                var saved=canvas.Save();canvas.SetClip(new Rectangle(col*360,row*230+38,360,192));
                canvas.TranslateTransform(col*360+5,row*230+44);canvas.ScaleTransform(1.15f,1.15f);canvas.TranslateTransform(0,-256);
                scene.Draw(canvas,lookAt+.12,false,true);canvas.Restore(saved);
            }
            if(!String.IsNullOrEmpty(previewPath))sheet.Save(Path.Combine(Path.GetDirectoryName(previewPath),"cyber-floating-projection.png"),ImageFormat.Png);
            input.Clear();scene.Advance(6,.05,true);
            Check(!input.HasMouseFeedback(6)&&input.MouseOffset==PointF.Empty,"Clearing input removes projected operations and movement");
        }
        Console.WriteLine("PASS: opaque deck with translucent light keys, inverted hand-mounted cone, glove attachment/retraction, projected cursor, five buttons, scroll axes and all pet sizes.");
    }
    static void CheckCyberpunk(string root,string testRoot,string previewPath)
    {
        CheckCyberProjection(root,previewPath);
        CheckHackerCode(root);
        using(var sprites=new KeyboardPetSprites(root))
        using(var scene=new BoundPetScene(sprites,new PetInputBindings(),new InputFeedbackState()))
        {
            Check(RenderChanges(scene,()=>scene.SetKeyboardStyle(PetKeyboardStyles.Cyberpunk),false)>1000,"Cyber keyboard becomes a floating display");
            Check(RenderChanges(scene,()=>scene.SetMouseStyle(PetMouseStyles.Cyberpunk),false)>80,"Cyber mouse becomes a holographic projection");
            Check(RenderChanges(scene,()=>scene.SetPadStyle(PetPadStyles.Cyberpunk),false)>1000,"Cyber pad has a new grid and circuit surface");
            scene.SetTone(3);
            Check(scene.KeyboardStyle==4&&scene.MouseStyle==3&&scene.PadStyle==2,"Cyber palette preserves independently chosen props");
        }
        using(var sprites=new KeyboardPetSprites(root))
        using(var sheet=new Bitmap(1080,820,PixelFormat.Format32bppArgb))
        using(var canvas=Graphics.FromImage(sheet))
        using(var font=new Font("Microsoft YaHei UI",12,FontStyle.Bold))
        {
            canvas.Clear(Color.FromArgb(239,242,247));
            for(int look=0;look<6;look++)
            {
                var input=new InputFeedbackState();
                using(var scene=new BoundPetScene(sprites,new PetInputBindings(),input))
                {
                    scene.SetViewWidth(BoundPetScene.CompactWidth);scene.SetKeyboardStyle(4);scene.SetMouseStyle(3);scene.SetPadStyle(2);
                    scene.SetTone(3);scene.SetMusicStyle(2);scene.Portrait.SetListeningStyle(2);scene.Portrait.SetHeadwearStyle(look==5 ? 1 : 2);
                    scene.Portrait.SetHairStyle(look==3 ? 2 : look==4 ? 1 : 0);
                    bool typing=look==1||look==3||look==5,music=look==2||look==3||look==5;
                    scene.MusicPlaying=music;scene.MusicLevel=.32f;
                    for(int frame=0;frame<=360;frame++)
                    {
                        double now=frame/30.0;
                        if(typing&&frame%3==0){input.Key(65,true,now);input.Key(65,false,now+.003);}
                        scene.Advance(now,1/30.0,false);
                        Check(scene.Portrait.MusicArmorBlend==0&&scene.Portrait.HeadphoneBandBlend==0,"Neural audio never spawns ordinary headbands or knight audio armor");
                    }
                    if(typing)Check(scene.Portrait.HelmetBlend>.99f,"Cyber headwear retains the sustained typing trigger");
                    if(music)Check(scene.Portrait.MusicBlend>.99f,"Neural audio retains the music gate");
                    if(look==1)
                    {
                        var portrait=scene.Portrait;
                        using(var visor=(Bitmap)portrait.Image.Clone())
                        {
                            portrait.SetHeadwearStyle(1);
                            foreach(var local in new[]{new PointF(119,129),new PointF(186,134),new PointF(152,153)})
                            {
                                var p=new[]{local};using(var pose=portrait.HeadMatrix())pose.TransformPoints(p);
                                int x=(int)p[0].X+LivingPetPortrait.Padding,y=(int)p[0].Y+LivingPetPortrait.Padding;
                                Check(visor.GetPixel(x,y).ToArgb()==portrait.Image.GetPixel(x,y).ToArgb(),"HUD leaves original eyes and mouth visible");
                            }
                            portrait.SetHeadwearStyle(2);
                        }
                    }
                    if(look==4){input.Button(1,true,12);input.Button(2,true,12.01);input.Wheel(120,false,12.01);}
                    foreach(int width in new[]{252,336,420})
                    using(var image=new Bitmap((int)Math.Ceiling(scene.ViewWidth*width/336f),575,PixelFormat.Format32bppArgb))
                    using(var g=Graphics.FromImage(image))
                    {
                        g.ScaleTransform(width/336f,width/336f);scene.Draw(g,12.05,false,true);
                        if(look!=4)for(int y=0;y<image.Height;y++)
                            Check(image.GetPixel(0,y).A==0&&image.GetPixel(image.Width-1,y).A==0,"Cyber theme fits all three sizes and retains transparent edges");
                        int transparent=0;
                        for(int y=140;y<340;y+=20)for(int x=16;x<scene.ViewWidth-16;x+=20)
                        {
                            var point=new PointF(x,y);bool actual=scene.Hit(point,false,true);
                            scene.SetMusicStyle(0);bool expected=scene.Hit(point,false,true);scene.SetMusicStyle(2);
                            Check(actual==expected,"Code backdrop does not add mouse hit regions");if(!actual)transparent++;
                        }
                        Check(transparent>10,"Music hit regression samples transparent space around the character");
                    }
                    int col=look%3,row=look/3;
                    canvas.DrawString(new[]{"赛博朋克 · 待机","打字 · 战术全息目镜","听歌 · 黑客代码与神经耳机","双马尾 · 边打字边听歌","长发 · 全息鼠标投影","混搭 · 海盗帽与赛博桌面"}[look],font,Brushes.DarkSlateGray,col*360+12,row*410+14);
                    var state=canvas.Save();canvas.TranslateTransform(col*360,row*410-14);scene.Draw(canvas,12.05,false,true);canvas.Restore(state);
                    scene.Advance(12.1,.1,true);
                    Check(scene.Portrait.HelmetBlend==0&&scene.Portrait.MusicBlend==0,"Rest clears cyber headwear and audio effects");
                }
            }
            if(!String.IsNullOrEmpty(previewPath))sheet.Save(Path.Combine(Path.GetDirectoryName(previewPath),"cyberpunk-theme.png"),ImageFormat.Png);
        }
        string data=Path.Combine(testRoot,"cyberpunk"),config=Path.Combine(data,"keyboard_pet_config.json");Directory.CreateDirectory(data);
        File.WriteAllText(config,"{\"hairStyle\":2,\"follow\":0}");
        using(var pet=new KeyboardPetWindow(root,false,dataRoot:data))
        {
            ((Timer)Field(pet,"timer")).Stop();var original=pet.scene;pet.inputState.Key(65,true,0);
            var window=Open(pet);
            var outfits=(KnightButton[])Field(window,"outfitButtons");
            Check(outfits.Select(button=>button.Text).SequenceEqual(new[]{"经典","海盗","赛博"}),"Wardrobe exposes exactly three outfit buttons");
            var draft=(BoundPetScene)Field(window,"scene");string before=File.ReadAllText(config);
            outfits[1].PerformClick();
            Check(draft.KeyboardStyle==3&&draft.MouseStyle==1&&draft.PadStyle==1&&draft.Tone==1&&draft.Portrait.HeadwearStyle==1&&draft.MusicStyle==1&&draft.Portrait.ListeningStyle==1&&draft.Portrait.HairStyle==PetHairStyles.Short,"Pirate button previews the complete pirate set with short hair");
            outfits[0].PerformClick();
            Check(draft.KeyboardStyle==0&&draft.MouseStyle==0&&draft.PadStyle==0&&draft.Tone==0&&draft.Portrait.HeadwearStyle==0&&draft.MusicStyle==0&&draft.Portrait.ListeningStyle==0&&draft.Portrait.HairStyle==PetHairStyles.Reference,"Classic button restores every original item with layered long hair");
            outfits[2].PerformClick();
            Check(File.ReadAllText(config)==before&&pet.scene.KeyboardStyle==0,"All outfit buttons preview without changing saved or live settings");
            var preview=(BoundPetScene)Field(window,"scene");
            Check(preview.Portrait.HairStyle==PetHairStyles.TwinPonytails&&pet.scene.KeyboardStyle==0,"Cyber preset selects twin ponytails and previews without applying");
            window.ApplySelection();
            Check(pet.scene==original&&pet.inputState.HasKeys,"Applying cyber theme preserves active input and animation scene");
            foreach(int width in new[]{252,336,420})
            {
                Invoke(pet,"SetSpriteSize",width);
                Check(pet.scene.KeyboardStyle==4&&pet.scene.MouseStyle==3&&pet.scene.PadStyle==2&&pet.scene.Tone==3&&pet.scene.Portrait.HeadwearStyle==2&&pet.scene.MusicStyle==2&&pet.scene.Portrait.ListeningStyle==2,"All cyber choices survive resizing");
            }
            window.SelectKeyboardStyle(3);window.SelectHeadwearStyle(1);window.ApplySelection();
            Check(pet.scene.KeyboardStyle==3&&pet.scene.Portrait.HeadwearStyle==1&&pet.scene.MouseStyle==3&&pet.scene.PadStyle==2,"Cyber theme supports independent pirate mixing");
            window.SelectCyberpunk();window.ApplySelection();window.Close();
        }
        using(var pet=new KeyboardPetWindow(root,true,dataRoot:data))
            Check(pet.scene.KeyboardStyle==4&&pet.scene.MouseStyle==3&&pet.scene.PadStyle==2&&pet.scene.Tone==3&&pet.scene.Portrait.HeadwearStyle==2&&pet.scene.MusicStyle==2&&pet.scene.Portrait.ListeningStyle==2&&pet.scene.Portrait.HairStyle==2,"Restart restores the complete cyber theme and hairstyle");
        Console.WriteLine("PASS: six cyberpunk objects, neon palette, preset, mixed outfits, visible face, three sizes, rest, isolated preview, apply, resize and restart.");
    }
    static void CheckDeskOrnaments(string root,string testRoot,string previewPath)
    {
        if(!String.IsNullOrEmpty(previewPath))Directory.CreateDirectory(Path.GetDirectoryName(previewPath));
        string data=Path.Combine(testRoot,"desk-ornaments"),config=Path.Combine(data,"keyboard_pet_config.json");
        Directory.CreateDirectory(data);File.WriteAllText(config,"{\"follow\":0,\"hairStyle\":1}");
        using(var pet=new KeyboardPetWindow(root,false,dataRoot:data))
        using(var sheet=new Bitmap(1080,680))
        using(var canvas=Graphics.FromImage(sheet))
        using(var font=new Font("Microsoft YaHei UI",13,FontStyle.Bold))
        {
            ((Timer)Field(pet,"timer")).Stop();
            Check(pet.scene.OrnamentStyle==0,"Old settings start without an ornament");
            var window=Open(pet);((Timer)Field(window,"timer")).Stop();
            var scene=(BoundPetScene)Field(window,"scene");var choices=(ComboBox)Field(window,"ornament");
            Check(choices.Items.Cast<object>().Select(v=>v.ToString()).SequenceEqual(new[]{"无桌饰","原版骑士","海盗船","高达"}),"Wardrobe has the three requested ornaments and an off option");
            var original=pet.scene;
            for(int outfit=0;outfit<3;outfit++)
            {
                original=pet.scene;
                string before=File.ReadAllText(config);int old=pet.scene.OrnamentStyle;
                window.SelectOutfit(outfit);
                Check(scene.OrnamentStyle==outfit+1&&pet.scene.OrnamentStyle==old&&File.ReadAllText(config)==before,"Each outfit previews the matching ornament without applying or saving");
                for(int frame=0;frame<40;frame++)scene.Advance(3+frame*.05,.05,false);
                Color background=outfit==0 ? Color.FromArgb(243,245,249) : outfit==1 ? Color.FromArgb(246,237,220) : Color.FromArgb(23,32,49);
                using(var brush=new SolidBrush(background))canvas.FillRectangle(brush,outfit*360,0,360,680);
                canvas.DrawString(new[]{"骑士 · 原版骑士","海盗 · 海盗船","赛博 · 高达"}[outfit],font,outfit==2 ? Brushes.WhiteSmoke : Brushes.DarkSlateGray,outfit*360+16,16);
                var saved=canvas.Save();canvas.TranslateTransform(outfit*360,-4);scene.Draw(canvas,5,false,true);canvas.Restore(saved);
                using(var image=PetDeskOrnaments.Create(outfit+1))canvas.DrawImage(image,new Rectangle(outfit*360+112,440,136,204));
                using(var ornament=new Bitmap(BoundPetScene.WorldWidth,BoundPetScene.WorldHeight))
                {
                    using(var g=Graphics.FromImage(ornament))scene.DrawOrnament(g);
                    int visible=0;var r=BoundPetScene.DeskOrnamentBounds;
                    for(int y=(int)r.Top;y<r.Bottom;y++)for(int x=(int)r.Left;x<r.Right;x++)
                    if(ornament.GetPixel(x,y).A>=128){visible++;Check(scene.Hit(new PointF(x+.5f,y+.5f),false,true),"Visible ornament pixels can be dragged with the pet: style="+(outfit+1)+", x="+x+", y="+y+", alpha="+ornament.GetPixel(x,y).A);}
                    Check(visible>600,"Each ornament has a visible miniature silhouette");
                    Check(!scene.Hit(new PointF(r.Right-1,r.Bottom-1),false,true),"Transparent space around the ornament stays click-through");
                }
                pet.inputState.Key(65,true,5);window.ApplySelection();
                Check(pet.scene==original&&pet.inputState.HasKeys&&pet.scene.OrnamentStyle==outfit+1,"Applying an ornament preserves scene and active input");
                Check(File.ReadAllText(config).Contains("\"deskOrnament\":"+(outfit+1)),"Ornament saves with the wardrobe");
                pet.inputState.Clear();
                foreach(int width in new[]{252,336,420})
                {
                    Invoke(pet,"SetSpriteSize",width);
                    Check(pet.scene.OrnamentStyle==outfit+1,"Resizing retains the ornament");
                    using(var image=new Bitmap(pet.Width,pet.Height))
                    using(var g=Graphics.FromImage(image))
                    {
                        pet.DrawSceneAt(g,6);
                        for(int y=0;y<image.Height;y++)Check(image.GetPixel(image.Width-1,y).A==0,"The ornament fits inside the native window at every pet size");
                    }
                }
            }
            window.SelectOrnamentStyle(1);window.SelectKeyboardStyle(3);window.SelectPalette(3);
            Check(scene.OrnamentStyle==1&&scene.KeyboardStyle==3&&scene.Tone==3,"Ornaments support independent mixing");
            window.SelectOrnamentStyle(0);Check(scene.OrnamentStyle==0&&pet.scene.OrnamentStyle==3,"Off previews independently");
            window.Close();Check(pet.scene.OrnamentStyle==3,"Closing discards an unapplied ornament selection");
            if(!String.IsNullOrEmpty(previewPath))sheet.Save(Path.Combine(Path.GetDirectoryName(previewPath),"desk-ornaments.png"),ImageFormat.Png);
        }
        using(var pet=new KeyboardPetWindow(root,false,dataRoot:data))
        {
            ((Timer)Field(pet,"timer")).Stop();Check(pet.scene.OrnamentStyle==3,"Restart restores the saved Gundam ornament");
            var window=Open(pet);((Timer)Field(window,"timer")).Stop();
            if(!String.IsNullOrEmpty(previewPath))
            {
                Application.DoEvents();using(var image=new Bitmap(window.Width,window.Height))
                {window.DrawToBitmap(image,window.ClientRectangle);image.Save(Path.Combine(Path.GetDirectoryName(previewPath),"wardrobe-desk-ornaments.png"),ImageFormat.Png);}
            }
            string before=File.ReadAllText(config);Directory.CreateDirectory(config+".tmp");
            window.SelectOrnamentStyle(2);window.ApplySelection();
            Check(pet.scene.OrnamentStyle==3&&(int)Field(pet,"ornamentStyle")==3&&File.ReadAllText(config)==before,"A save failure rolls back ornament selection");
            window.Close();
        }
        File.WriteAllText(config,"{\"follow\":0,\"deskOrnament\":999}");
        using(var pet=new KeyboardPetWindow(root,true,dataRoot:data))Check(pet.scene.OrnamentStyle==0,"An unknown ornament value falls back to none");
        Console.WriteLine("PASS: knight/ship/Gundam ornaments, presets, isolated preview/cancel, mixing/off, save/reload/resize, alpha hit testing and rollback.");
    }
    internal static void Run(string root,string testRoot,string previewPath)
    {
        Directory.CreateDirectory(testRoot);
        string data=Path.Combine(testRoot,"user-data"),config=Path.Combine(data,"keyboard_pet_config.json");
        Directory.CreateDirectory(data);
        File.WriteAllText(config,"{\"mouseShape\":2,\"hairStyle\":0,\"wearHair\":3,\"wearBody\":2,\"wearHands\":1,\"follow\":0,\"keyboardStyle\":2,\"mouseStyle\":1,\"eyeStyle\":2}");
        using(var pet=new KeyboardPetWindow(root,false,dataRoot:data))
        {
            ((Timer)Field(pet,"timer")).Stop();
            var original=pet.scene;pet.inputState.Key(65,true,0);
            var window=Open(pet);
            Check(window.Visible&&!window.Modal&&pet.Enabled,"wardrobe is modeless");
            Check(Object.ReferenceEquals(window,Open(pet)),"repeated entry reuses the window");
            string before=File.ReadAllText(config);
            window.SelectHairStyle(PetHairStyles.Reference);
            var scene=(BoundPetScene)Field(window,"scene");
            CheckStyles(scene,0,"legacy configuration defaults to original props and eyes");
            ((Timer)Field(window,"timer")).Stop();scene.Advance(0,0,false);
            Check(scene.Tone==0,"old color selectors do not override the classic palette");
            Check(scene.MusicStyle==0&&scene.Portrait.ListeningStyle==0,"legacy settings retain original music visuals");
            Check(((ComboBox)Field(window,"palette")).Items.Cast<object>().Select(item=>item.ToString()).SequenceEqual(new[]{"经典","海盗","赛博"}),"Palette selector displays exactly classic, pirate and cyber");
            Check(((ComboBox)Field(window,"keyboard")).Items.Count==3,"original, parchment and cyberpunk keyboards are available");
            Check(RenderChanges(scene,()=>window.SelectKeyboardStyle(1),false)==0&&scene.KeyboardStyle==0,"retired rounded keyboard falls back to original");
            Check(RenderChanges(scene,()=>window.SelectKeyboardStyle(2),false)==0&&scene.KeyboardStyle==0,"retired mechanical keyboard falls back to original");
            Check(RenderChanges(scene,()=>window.SelectPadStyle(1),false)>1000,"navigation chart has a distinct paper, coast and compass surface");
            Check(scene.PadStyle==1&&pet.scene.PadStyle==0,"pad selection previews independently");
            Check(scene.MouseStyle==0&&pet.scene.MouseStyle==0,"retired mouse styles fall back to the restored original mouse");
            Check(RenderChanges(scene,()=>window.SelectMouseStyle(1),false)>50,"knight sword is an additional rendered mouse choice");
            Check(scene.MouseStyle==1&&pet.scene.MouseStyle==0,"mouse selection previews independently");
            Check(RenderChanges(scene,()=>window.SelectMouseStyle(0),false)>50,"original mouse can be restored");
            Check(!scene.Hit(new PointF(51,400),false,true),"switching back removes the sword blade hit area");
            Check(typeof(PetWardrobeWindow).GetField("eyes",Private)==null,"wardrobe has no retired eye selector");
            Check(RenderChanges(scene,()=>window.SelectKeyboardStyle(3),false)>1000,"parchment has a distinct paper surface, ink cells and rolled ends");
            var previewInput=(InputFeedbackState)Field(window,"input");
            Check(RenderChanges(scene,()=>previewInput.Key(65,true,0),false)>10,"parchment key responds to typing");previewInput.Clear();
            Check(RenderChanges(scene,()=>window.SelectPalette(1))>1000,"pirate palette recolors the whole outfit and props");
            CheckStyles(scene,3,"palette leaves every selected shape intact");
            Check(RenderChanges(scene,()=>window.SelectPalette(3))>1000&&scene.Tone==3,"third palette option retains the cyber configuration ID");
            Check(((ComboBox)Field(window,"palette")).SelectedIndex==2,"cyber palette occupies the third visible option");
            window.SelectPalette(2);Check(scene.Tone==0,"retired green palette falls back to classic");
            window.SelectPalette(0);
            Check(RenderChanges(scene,()=>{window.SelectKeyboardStyle(0);},false)>300,"original artwork can be restored after changing shapes");
            scene.Advance(1,0,true);
            CheckStyles(pet.scene,0,"preview choices leave live appearance intact");
            Check(scene.Portrait.HairStyle==PetHairStyles.Reference&&pet.scene.Portrait.HairStyle==PetHairStyles.Short,"choice updates preview only");
            Check(File.ReadAllText(config)==before,"preview leaves saved configuration intact");
            window.Close();
            Check(pet.scene.Portrait.HairStyle==PetHairStyles.Short,"closing discards unapplied choice");
            CheckStyles(pet.scene,0,"closing discards all unapplied palettes");
            window=Open(pet);scene=(BoundPetScene)Field(window,"scene");window.SelectHairStyle(PetHairStyles.Reference);
            window.SelectKeyboardStyle(3);window.SelectMouseStyle(1);window.SelectPadStyle(1);window.SelectHeadwearStyle(1);window.SelectPalette(1);window.SelectMusicStyle(1);window.SelectListeningStyle(1);window.ApplySelection();
            Check(pet.scene.MusicStyle==1&&pet.scene.Portrait.ListeningStyle==1&&File.ReadAllText(config).Contains("\"musicStyle\":1")&&File.ReadAllText(config).Contains("\"listeningStyle\":1"),"apply saves both independent music choices");
            CheckStyles(pet.scene,3,"apply updates all live shapes");Check(pet.scene.Tone==1,"apply updates global palette");
            Check(pet.scene.PadStyle==1&&pet.scene.Portrait.HeadwearStyle==1&&pet.scene.MouseStyle==1&&pet.scene==original&&pet.inputState.HasKeys&&pet.scene.Portrait.HairStyle==1,"apply preserves scene and held input");
            Check(File.ReadAllText(config).Contains("\"hairStyle\":1")&&!File.ReadAllText(config).Contains("\"wear")&&!File.ReadAllText(config).Contains("\"eyeShape\"")&&File.ReadAllText(config).Contains("\"mouseShape\":1")&&File.ReadAllText(config).Contains("\"keyboardShape\":3")&&File.ReadAllText(config).Contains("\"mousePadShape\":1")&&File.ReadAllText(config).Contains("\"headwearShape\":1"),"save retains parchment, sword and hairstyle and drops retired prototype fields");
            Invoke(pet,"SetSpriteSize",420);
            Check(pet.scene.Portrait.HairStyle==1,"resizing preserves applied choice");
            Check(pet.scene.MusicStyle==1&&pet.scene.Portrait.ListeningStyle==1,"resize preserves music choices");
            CheckStyles(pet.scene,3,"resize preserves every shape");Check(pet.scene.Tone==1&&pet.scene.MouseStyle==1&&pet.scene.PadStyle==1&&pet.scene.Portrait.HeadwearStyle==1,"resize preserves palette, mouse, chart and pirate hat");
            var mode=(ComboBox)Field(window,"mode");
            var input=(InputFeedbackState)Field(window,"input");
            foreach(int index in new[]{0,1,2,3,4,5})
            {
                mode.SelectedIndex=index;Invoke(window,"Tick",null,EventArgs.Empty);
                Check(scene.MusicPlaying==(index==2||index==5),"preview modes use isolated simulated music");
                Check(((WardrobePreview)Field(window,"preview")).Resting==(index==3),"rest preview is available");
                if(index==4)Check(!input.HasKeys&&input.HasMouseFeedback(((WardrobePreview)Field(window,"preview")).Time),"sword preview simulates mouse feedback without typing");
            }
            Check(input!=pet.inputState,"preview input is isolated from the live pet");
            if(!String.IsNullOrEmpty(previewPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(previewPath));
                mode.SelectedIndex=0;Invoke(window,"Tick",null,EventArgs.Empty);Application.DoEvents();
                window.SelectPalette(0);
                using(var sheet=new Bitmap(960,450))
                using(var g=Graphics.FromImage(sheet))
                using(var title=new Font("Microsoft YaHei UI",16,FontStyle.Bold))
                {
                    g.Clear(Color.FromArgb(249,246,239));
                    for(int style=0;style<2;style++)
                    {
                        window.SelectMouseStyle(style);window.SelectKeyboardStyle(3);
                        g.DrawString(style==0 ? "羊皮卷 + 原始骑士鼠标" : "羊皮卷 + 骑士剑鼠标",title,Brushes.DarkSlateGray,style*480+24,18);
                        var graphicsState=g.Save();g.TranslateTransform(style*480+20,-95);g.ScaleTransform(1.2f,1.2f);
                        scene.Draw(g,0,false,true);g.Restore(graphicsState);
                    }
                    sheet.Save(Path.Combine(Path.GetDirectoryName(previewPath),"parchment-and-mice.png"),ImageFormat.Png);
                }
                window.SelectMouseStyle(1);
                foreach(int palette in new[]{0,1,3})
                {
                    window.SelectPalette(palette);Application.DoEvents();
                    string path=palette==0 ? previewPath : Path.Combine(Path.GetDirectoryName(previewPath),"wardrobe-"+(palette==1 ? "pirate" : "cyber")+".png");
                    using(var image=new Bitmap(window.Width,window.Height))
                    {window.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(path,ImageFormat.Png);}
                }
            }
            window.Close();
        }
        using(var reload=new KeyboardPetWindow(root,true,dataRoot:data))
        {
            Check(reload.scene.MusicStyle==1&&reload.scene.Portrait.ListeningStyle==1,"restart restores both music choices");
            Check(reload.scene.Portrait.HairStyle==1,"restart restores applied hairstyle");
            CheckStyles(reload.scene,3,"restart restores every shape");Check(reload.scene.Tone==1&&reload.scene.MouseStyle==1&&reload.scene.PadStyle==1&&reload.scene.Portrait.HeadwearStyle==1,"restart restores palette, sword, chart and pirate hat");
        }
        string saved=File.ReadAllText(config);
        using(var pet=new KeyboardPetWindow(root,true,dataRoot:data))
        {
            var window=Open(pet);window.SelectHairStyle(0);window.SelectKeyboardStyle(0);window.SelectMouseStyle(0);window.SelectPadStyle(0);window.SelectHeadwearStyle(0);window.SelectPalette(2);window.SelectMusicStyle(0);window.SelectListeningStyle(0);window.ApplySelection();
            Check(File.ReadAllText(config)==saved&&pet.scene.Portrait.HairStyle==1,"read-only preview cannot apply or save");
            CheckStyles(pet.scene,3,"read-only preview cannot apply shapes");Check(pet.scene.Tone==1,"read-only preview cannot apply palette");
        }
        int applies=0;
        using(var window=new PetWardrobeWindow(root,1,hair=>{applies++;throw new IOException("test save failure");}))
        {
            window.SelectHairStyle(0);window.ApplySelection();
            Check(applies==1&&((Label)Field(window,"status")).Text.StartsWith("未保存"),"failed save stays visible to user");
        }
        // A blocked temporary file makes the real save fail before replacement.
        Directory.CreateDirectory(config+".tmp");
        using(var pet=new KeyboardPetWindow(root,false,dataRoot:data))
        {
            var window=Open(pet);window.SelectHairStyle(0);window.SelectKeyboardStyle(0);window.SelectMouseStyle(0);window.SelectPadStyle(0);window.SelectHeadwearStyle(0);window.SelectPalette(2);window.SelectMusicStyle(0);window.SelectListeningStyle(0);window.ApplySelection();
            Check(pet.scene.Portrait.HairStyle==1&&(int)Field(pet,"hairStyle")==1,"failed save rolls back live and persisted selection");
            CheckStyles(pet.scene,3,"failed save keeps all live shapes");Check(pet.scene.Tone==1&&pet.scene.PadStyle==1&&pet.scene.Portrait.HeadwearStyle==1,"failed save keeps palette, chart and pirate hat");
            Check(File.ReadAllText(config)==saved,"failed save keeps the original configuration");
            Check(pet.scene.MusicStyle==1&&pet.scene.Portrait.ListeningStyle==1,"failed save retains music choices");
        }
        string invalid=Path.Combine(testRoot,"invalid");Directory.CreateDirectory(invalid);
        File.WriteAllText(Path.Combine(invalid,"keyboard_pet_config.json"),"{\"keyboardShape\":999,\"mouseShape\":-1,\"eyeShape\":2147483647,\"skinPalette\":999,\"mousePadShape\":999,\"headwearShape\":999,\"musicStyle\":999,\"listeningStyle\":-1}");
        using(var pet=new KeyboardPetWindow(root,true,dataRoot:invalid))
            {Check(pet.scene.MusicStyle==0&&pet.scene.Portrait.ListeningStyle==0,"invalid music choices restore original looks");CheckStyles(pet.scene,0,"unknown styles fall back to original appearance");Check(pet.scene.MouseStyle==0&&pet.scene.PadStyle==0&&pet.scene.Portrait.HeadwearStyle==0,"unknown mouse, pad and headwear default to original");}
        string legacyEyes=Path.Combine(testRoot,"retired-eyes");Directory.CreateDirectory(legacyEyes);
        foreach(int retired in new[]{1,2,3,-1,999})
        {
            string legacyConfig=Path.Combine(legacyEyes,"keyboard_pet_config.json");
            File.WriteAllText(legacyConfig,"{\"eyeShape\":"+retired+",\"hairStyle\":0}");
            using(var pet=new KeyboardPetWindow(root,false,dataRoot:legacyEyes))
            {
                ((Timer)Field(pet,"timer")).Stop();
                var window=Open(pet);window.ApplySelection();window.Close();
                Check(!File.ReadAllText(legacyConfig).Contains("\"eyeShape\""),"saving drops every retired eye setting");
                Check(pet.scene.Portrait.HairStyle==0,"retiring eye settings preserves other appearance choices");
            }
        }
        CheckPirateHat(root,previewPath);
        CheckMusicLooks(root,previewPath);
        CheckCyberpunk(root,testRoot,previewPath);
        CheckDeskOrnaments(root,testRoot,previewPath);
        Console.WriteLine("PASS: navigation chart, pirate hat, retired keyboards, original eyes and keyboard shapes and original mouse, optional knight sword and parchment keyboard, unified palettes, transparency, isolated previews, apply/cancel, input continuity, save/reload/resize, legacy/invalid configuration and save failure.");
    }
}
