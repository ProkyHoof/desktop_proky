using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;

internal static class InteractionTests
{
    static void Check(bool value,string message){if(!value)throw new Exception("Interaction: "+message);}
    static int DifferentPixels(Bitmap a,Bitmap b)
    {
        int count=0;
        for(int y=0;y<a.Height;y++)for(int x=0;x<a.Width;x++)
            if(a.GetPixel(x,y).ToArgb()!=b.GetPixel(x,y).ToArgb())count++;
        return count;
    }
    static void MouseFeedback(string root)
    {
        var input=new InputFeedbackState();
        using(var sprites=new KeyboardPetSprites(root))
        using(var scene=new BoundPetScene(sprites,new PetInputBindings(),input))
        using(var idle=new Bitmap(352,460,PixelFormat.Format32bppArgb))
        using(var active=new Bitmap(352,460,PixelFormat.Format32bppArgb))
        using(var opposite=new Bitmap(352,460,PixelFormat.Format32bppArgb))
        using(var a=Graphics.FromImage(idle))
        using(var b=Graphics.FromImage(active))
        using(var c=Graphics.FromImage(opposite))
        {
            scene.SetViewWidth(BoundPetScene.CompactWidth);scene.SetMouseStyle(PetMouseStyles.KnightSword);
            scene.DrawSword(a,1);
            int bladePixels=0;
            for(int y=384;y<idle.Height;y++)for(int x=0;x<idle.Width;x++)
                if(idle.GetPixel(x,y).A>=64){bladePixels++;Check(scene.Hit(new PointF(x,y),false,true),"Exposed sword blade must remain draggable beyond the mat");}
            Check(bladePixels>60,"Long silver sword blade must extend beyond the old mouse silhouette");
            Check(!scene.Hit(new PointF(100,430),false,true),"Transparent space beside the blade lets clicks through");
            input.Button(1,true,1);input.Button(1,false,1.005);
            scene.DrawSword(b,1.08);
            Check(DifferentPixels(idle,active)>40,"A sub-frame left click must visibly light the sword edge");
            input.Clear();input.Button(2,true,1);input.Button(2,false,1.005);
            scene.DrawSword(c,1.08);
            Check(DifferentPixels(idle,opposite)>40&&DifferentPixels(active,opposite)>60,"Left and right clicks must light opposite sword edges");
            PointF restPalm=scene.Hands.LeftPalm;
            scene.Advance(1.08,.08,false);
            Check(scene.Hands.LeftPalm.X<restPalm.X-.5f&&scene.Hands.LeftPalm.Y>restPalm.Y+1,"Right click must lean toward the character's right button on screen-left");
            input.Clear();input.Button(1,true,1.2);input.Button(1,false,1.205);scene.Advance(1.28,.1,false);
            Check(scene.Hands.LeftPalm.X>restPalm.X+.5f&&scene.Hands.LeftPalm.Y>restPalm.Y+1,"Left click must lean toward the character's left button on screen-right");
            scene.Advance(1.8,.1,false);scene.Advance(1.9,.1,false);
            Check(Math.Abs(scene.Hands.LeftPalm.X-restPalm.X)<.1&&Math.Abs(scene.Hands.LeftPalm.Y-restPalm.Y)<.1,"Click release returns the hand to its original grip");
            input.Clear();input.Button(1,true,2);input.Button(2,true,2);input.Wheel(-120,false,2);
            b.Clear(Color.Transparent);scene.DrawMouseFeedback(b,2.08);
            int pixels=0;
            for(int y=0;y<active.Height;y++)for(int x=0;x<active.Width;x++)
                if(active.GetPixel(x,y).A>0){pixels++;Check(x>=7&&x<BoundPetScene.CompactWidth-7,"Animated marks stay inside the compact window");}
            Check(pixels>80,"Sword slash trails must remain visible outside the glove");
            c.Clear(Color.Transparent);scene.DrawMouseFeedback(c,2.22);
            Check(DifferentPixels(active,opposite)>40,"Motion marks must move between animation frames");
            input.Button(1,false,2.1);input.Button(2,false,2.1);scene.Advance(2.1,.02,false);
            Check(scene.Advance(2.8,.1,false),"First expired frame must redraw to remove feedback");
            b.Clear(Color.Transparent);scene.DrawMouseFeedback(b,2.8);
            c.Clear(Color.Transparent);Check(DifferentPixels(active,opposite)==0,"Expired feedback leaves no stale motion marks");
            foreach(bool horizontal in new[] {false,true})
            {
                input.Wheel(120,horizontal,3);scene.DrawSword(b,3.08);
                input.Wheel(-120,horizontal,3);scene.DrawSword(c,3.08);
                Check(DifferentPixels(active,opposite)>20,"Opposite scroll directions must move sword light in distinct directions");
                b.Clear(Color.Transparent);c.Clear(Color.Transparent);
            }
            input.Clear();scene.DrawMouseFeedback(b,3.09);
            Check(DifferentPixels(active,opposite)==0,"Pausing clears mouse feedback immediately");
            scene.SetMouseStyle(PetMouseStyles.Original);
            a.Clear(Color.Transparent);scene.DrawMouse(a,4);
            PointF mouse=scene.MousePosition;
            Color ruby=idle.GetPixel((int)Math.Round(mouse.X-3),(int)Math.Round(mouse.Y+8));
            Check(ruby.R>180&&ruby.G<90&&ruby.B<115,"Restored original mouse retains the ruby wheel at the fingertips");
            Check(!scene.Hit(new PointF(51,400),false,true),"Original mouse does not leave a stale sword hit area");
            foreach(int button in new[]{1,2,3,4,5})
            {
                input.Clear();input.Button(button,true,4);input.Button(button,false,4.005);
                b.Clear(Color.Transparent);scene.DrawMouse(b,4.08);scene.DrawMouseFeedback(b,4.08);
                Check(DifferentPixels(idle,active)>40,"Restored original mouse preserves each button's feedback");
            }
            foreach(bool horizontal in new[]{false,true})
            {
                input.Clear();input.Wheel(120,horizontal,5);
                b.Clear(Color.Transparent);scene.DrawMouse(b,5.08);scene.DrawMouseFeedback(b,5.08);
                input.Wheel(-120,horizontal,5);
                c.Clear(Color.Transparent);scene.DrawMouse(c,5.08);scene.DrawMouseFeedback(c,5.08);
                Check(DifferentPixels(active,opposite)>20,"Original mouse keeps directional wheel feedback");
            }
        }
        Console.WriteLine("PASS: short left/right press/rebound, directional wrist motion, sword blade hit detection, slash bounds, directional sword light and feedback expiry.");
    }
    internal static void Run(string root)
    {
        MouseFeedback(root);
        using(var sprites=new KeyboardPetSprites(root))
        {
            var input=new InputFeedbackState();
            using(var scene=new BoundPetScene(sprites,new PetInputBindings(),input))
            {
                var portrait=scene.Portrait;double now=0;
                var directions=new[] {new PointF(-100,0),new PointF(100,0),new PointF(0,-90),new PointF(0,90)};
                foreach(var direction in directions)
                {
                    for(int frame=0;frame<60;frame++)
                    {
                        now+=1/60.0;PointF face=portrait.WorldPoint(new PointF(151,131));
                        scene.ObservePointer(new PointF(face.X+direction.X,face.Y+direction.Y),now,true);
                        scene.Advance(now,1/60.0,false);
                    }
                    Check(direction.X==0 ? portrait.GazeY*direction.Y>45 : portrait.GazeX*direction.X>55,"Eyes must follow cursor in all four directions");
                    Check(Math.Abs(portrait.GazeX)<=1&&Math.Abs(portrait.GazeY)<=1,"Gaze remains bounded inside eye aperture");
                }
                // A stationary distant cursor eventually releases attention.
                scene.ObservePointer(new PointF(10000,10000),now,true);
                for(int frame=0;frame<360;frame++){now+=1/60.0;scene.Advance(now,1/60.0,false);}
                Check(Math.Abs(portrait.GazeY)<.12,"Distant stationary cursor must release the gaze");
                scene.ObservePointer(new PointF(10000,-10000),now,false);
                for(int frame=0;frame<90;frame++){now+=1/60.0;scene.Advance(now,1/60.0,true);}
                Check(Math.Abs(portrait.GazeX)<.01&&Math.Abs(portrait.GazeY)<.01,"Rest must relax the gaze");

                PointF faceClick=portrait.WorldPoint(new PointF(151,144));
                Check(scene.Interact(faceClick,now),"Clicking the face must respond");
                float maxSmile=0,rangeLeft=0,rangeRight=0;
                float beforeLeft=portrait.CheekHairLeft,beforeRight=portrait.CheekHairRight;
                for(int frame=0;frame<120;frame++)
                {
                    now+=1/60.0;scene.Advance(now,1/60.0,false);
                    maxSmile=Math.Max(maxSmile,portrait.Smile);
                    rangeLeft=Math.Max(rangeLeft,Math.Abs(portrait.CheekHairLeft-beforeLeft));
                    rangeRight=Math.Max(rangeRight,Math.Abs(portrait.CheekHairRight-beforeRight));
                }
                Check(maxSmile>.82&&rangeLeft>.25&&rangeRight>.25,"Face click must produce a smile and independently moving side hair");

                for(int frame=0;frame<360;frame++)
                {
                    float left=portrait.CheekHairLeft,right=portrait.CheekHairRight;
                    now+=1/60.0;
                    portrait.Update(now,1/60.0,frame>=240,frame>=120&&frame<240,false);
                    Check(Math.Abs(portrait.CheekHairLeft)<=2.8f&&Math.Abs(portrait.CheekHairRight)<=2.8f,"Side hair never exceeds its safe travel");
                    Check(Math.Abs(portrait.CheekHairLeft-left)<.3&&Math.Abs(portrait.CheekHairRight-right)<.3,"Hair remains continuous across activity transitions");
                }
                portrait.Update(now+.4,.4,false,true,false);
                Check(!float.IsNaN(portrait.HairInertia)&&Math.Abs(portrait.HairInertia)<=3,"Hair remains stable after a slow frame");
                for(int frame=0;frame<48;frame++)
                {
                    portrait.Update(frame*.3,.1,false,frame%2==0,false);
                    foreach(var source in new[] {new PointF(180,200),new PointF(188,210),new PointF(140,224)})
                    {
                        var point=new[] {source};using(var pose=portrait.BodyMatrix())pose.TransformPoints(point);
                        Color pixel=portrait.Image.GetPixel((int)Math.Round(point[0].X)+28,(int)Math.Round(point[0].Y)+28);
                        Check(pixel.A<16,"Old white hair scraps and strap tail must not extend past the torso onto the keyboard");
                    }
                    var interior=new[] {new PointF(155,215)};using(var pose=portrait.BodyMatrix())pose.TransformPoints(interior);
                    Color cloth=portrait.Image.GetPixel((int)Math.Round(interior[0].X)+28,(int)Math.Round(interior[0].Y)+28);
                    Check(cloth.A>=240&&cloth.R<140&&cloth.G<140&&cloth.B<140,"Lower cloth remains opaque and dark in motion");
                }

                // Isolate the crown to detect actual transparent strip seams.
                using(var cap=new Bitmap(356,285,PixelFormat.Format32bppArgb))
                using(var g=Graphics.FromImage(cap))
                {
                    foreach(float offset in new[] {-2.8f,0,2.8f})
                    {
                        portrait.CheekHairLeft=offset;portrait.CheekHairRight=-offset;
                        g.Clear(Color.Transparent);g.ResetTransform();g.TranslateTransform(28,28);
                        typeof(LivingPetPortrait).GetMethod("DrawCheekHair",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(portrait,new object[] {g});
                        int checkedPixels=0;
                        foreach(int seam in new[] {105,140})for(int y=seam-1;y<=seam+1;y++)for(int x=48;x<250;x++)
                        {
                            bool solid=true;
                            for(int sy=y-3;sy<=y+3&&solid;sy++)for(int sx=x-4;sx<=x+4;sx++)
                            {
                                var part=sprites.Rig.Parts["hair-cap"];
                                if(!part.Bounds.Contains(sx,sy)){solid=false;break;}
                                int tx=(int)((sx-part.Bounds.X)*part.Image.Width/part.Bounds.Width),ty=(int)((sy-part.Bounds.Y)*part.Image.Height/part.Bounds.Height);
                                if(part.Image.GetPixel(tx,ty).A<240){solid=false;break;}
                            }
                            if(!solid)continue;
                            var p=new[] {new PointF(x,y)};
                            using(var head=portrait.HeadMatrix())head.TransformPoints(p);
                            Check(cap.GetPixel((int)Math.Round(p[0].X)+28,(int)Math.Round(p[0].Y)+28).A>=220,"Moving hair must not expose transparent seams at roots or bend at "+x+","+y+" offset="+offset);
                            checkedPixels++;
                        }
                        Check(checkedPixels>50,"Hair seam check must sample the real opaque locks");
                    }
                }
            }
            // The raised keyboard must replace opaque clothing pixels at the
            // waist, including a pressed spacebar. Hands remain above the desk.
            using(var scene=new BoundPetScene(sprites,new PetInputBindings(),input))
            using(var body=new Bitmap(480,460,PixelFormat.Format32bppArgb))
            using(var full=new Bitmap(480,460,PixelFormat.Format32bppArgb))
            using(var desk=new Bitmap(480,460,PixelFormat.Format32bppArgb))
            using(var bg=Graphics.FromImage(body))using(var fg=Graphics.FromImage(full))using(var dg=Graphics.FromImage(desk))
            {
                for(int style=0;style<2;style++)for(int pressed=0;pressed<2;pressed++)
                {
                input.Clear();scene.Advance(0,0,false);
                scene.Portrait.SetHairStyle(style);
                bg.Clear(Color.Transparent);fg.Clear(Color.Transparent);dg.Clear(Color.Transparent);
                scene.Portrait.Draw(bg);
                if(pressed==1)input.Key(32,true,0);
                scene.Advance(0,0,false);scene.Draw(fg,0,false,true);scene.DrawDesk(dg,0);
                int protectedPixels=0;
                for(int y=355;y<365;y++)for(int x=155;x<209;x++)
                {
                    Color cloth=body.GetPixel(x,y),expected=desk.GetPixel(x,y);
                    if(cloth.A<255||cloth.R>110||cloth.G>110||cloth.B>110||expected.A<255)continue;
                    Color actual=full.GetPixel(x,y);
                    Check(Math.Abs(actual.R-expected.R)<3&&Math.Abs(actual.G-expected.G)<3&&Math.Abs(actual.B-expected.B)<3,"Raised keyboard must cover torso hem at "+x+","+y+" expected="+expected+" actual="+actual);
                    protectedPixels++;
                }
                Check(protectedPixels>30,"Depth test must cover the keyboard/torso overlap");
                }
            }
        }
        Console.WriteLine("PASS: cursor gaze, attention release, face response, hair continuity/limits/seams and keyboard depth.");
    }
}
