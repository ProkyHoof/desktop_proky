using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

internal static class TypingHelmetTests
{
    static void Check(bool value,string message){if(!value)throw new Exception("Cyber helmet: "+message);}
    static PointF Transform(Matrix pose,PointF point)
    {var e=pose.Elements;return new PointF(point.X*e[0]+point.Y*e[2]+e[4],point.X*e[1]+point.Y*e[3]+e[5]);}
    static void Translation(Matrix pose)
    {
        var e=pose.Elements;
        Check(Math.Abs(e[0]-1)<.00001&&Math.Abs(e[3]-1)<.00001&&Math.Abs(e[1])<.00001&&Math.Abs(e[2])<.00001,
            "Sliding armor keeps its size and orientation throughout deployment and retraction");
    }
    static void Rails(TypingHelmetAction action)
    {
        for(int side=0;side<2;side++)
        {
            int temple=side+1,face=side+3;PointF attachment=new PointF(side==0 ? 104 : 200,119);
            using(var parent=LivingPetPortrait.HelmetPartMatrix(temple,action.Part(0),action.Part(temple),action.Part(temple)))
            using(var child=LivingPetPortrait.HelmetPartMatrix(face,action.Part(0),action.Part(temple),action.Part(face)))
            {
                Translation(parent);Translation(child);
                var a=Transform(parent,attachment);var b=Transform(child,attachment);
                float travel=(b.X-a.X)*(side==0 ? -1 : 1);
                Check(Math.Abs(a.Y-b.Y)<.001&&travel>=-.001&&travel<=38.001,
                    "Face carriage stays on its parent's horizontal guide within the housing travel");
            }
            using(var carrier=LivingPetPortrait.HelmetCarrierMatrix(action.Part(0)))
            using(var plate=LivingPetPortrait.HelmetPartMatrix(temple,action.Part(0),action.Part(temple),action.Part(temple)))
            {
                Translation(carrier);
                var a=Transform(carrier,attachment);var b=Transform(plate,attachment);
                float dx=(b.X-a.X)*(side==0 ? -1 : 1),dy=b.Y-a.Y;
                Check(dx>=-.001&&dx<=18.001&&dy>=-.001&&dy<=8.001&&Math.Abs(dx*8-dy*18)<.003,
                    "Side carriage approaches on the outer guide without traveling through the skull");
            }
        }
        foreach(int part in new[] {0,5})
        using(var pose=LivingPetPortrait.HelmetPartMatrix(part,action.Part(0),0,action.Part(part)))
        {Translation(pose);Check(Math.Abs(pose.Elements[4])<.001,"Crown and chin move only vertically");}
    }
    static void Motion()
    {
        var action=new TypingHelmetAction();
        for(int frame=0;frame<150;frame++)action.Update(frame/30.0,1/30.0,false,WorkMood.Focused);
        Check(action.Blend==0,"Short typing does not immediately trigger armor");
        var previous=new float[6];int appearances=0;bool visible=false;
        for(int frame=150;frame<1200;frame++)
        {
            action.Update(frame/30.0,1/30.0,false,WorkMood.Focused);
            Rails(action);
            for(int i=0;i<6;i++)
            {
                float p=action.Part(i);
                Check(p>=-.05f&&p<=1.08f,"Spring recoil remains bounded");
                Check(Math.Abs(p-previous[i])<.09,"Each independent panel moves continuously: frame="+frame+", part="+i+", before="+previous[i]+", after="+p);
                previous[i]=p;
            }
            if(action.Blend>.01f&&!visible)appearances++;
            visible=action.Blend>.01f;
            if(frame==180)Check(action.Part(0)>action.Part(1)&&action.Part(1)>action.Part(2)&&action.Visor==0,"Crown approaches before its side and face rails extend");
            if(frame==240)Check(action.Part(3)>action.Part(4)+.1f&&action.Part(4)>action.Part(5),"Face plates close in order before the chin locks");
            if(frame==315)Check(action.Visor>.99f,"Open helmet reaches its settled state after the slower deployment");
            if(frame==550)Check(action.Blend==0&&action.Visor==0,"Panels retract and return to the original portrait");
        }
        Check(appearances==2,"Sustained typing repeats with a quiet cooldown: "+appearances);
        action.Update(40,1/30.0,true,WorkMood.Focused);
        for(int i=0;i<6;i++)Check(action.Part(i)==0,"Rest clears every panel");
        for(int frame=0;frame<1200;frame++)
        {
            action.Update(41+frame/30.0,1/30.0,false,WorkMood.Idle);
            Check(action.Blend==0,"Idle and non-typing inputs never trigger armor");
        }
        for(int frame=0;frame<600;frame++)
        {
            action.Update(81+frame/30.0,1/30.0,false,frame%150<30 ? WorkMood.Thinking : WorkMood.Idle);
            Check(action.Blend==0,"Widely spaced single inputs do not accumulate into an action");
        }
        foreach(int cancelAt in new[] {165,195,225,285})
        {
            var interrupted=new TypingHelmetAction();var before=new float[6];
            for(int frame=0;frame<=cancelAt;frame++)interrupted.Update(frame/30.0,1/30.0,false,WorkMood.Focused);
            for(int i=0;i<6;i++)before[i]=interrupted.Part(i);
            for(int frame=cancelAt+1;frame<=cancelAt+165;frame++)
            {
                interrupted.Update(frame/30.0,1/30.0,false,WorkMood.Idle);
                Rails(interrupted);
                for(int i=0;i<6;i++)
                {
                    Check(Math.Abs(interrupted.Part(i)-before[i])<.09,"Stopping midway preserves each panel's motion");
                    before[i]=interrupted.Part(i);
                }
            }
            for(int i=0;i<6;i++)Check(interrupted.Part(i)==0,"Every interrupted phase finishes retracting");
        }
        using(var retracted=LivingPetPortrait.HelmetPartMatrix(3,1,1,0))
        using(var closed=LivingPetPortrait.HelmetPartMatrix(3,1,1,1))
        {
            var a=Transform(retracted,new PointF(120,100));var b=Transform(closed,new PointF(120,100));
            Check(a.X<b.X-30&&Math.Abs(a.Y-b.Y)<.001,"Open face plate slides from its side housing into the center seam");
        }
        for(int i=0;i<6;i++)
        using(var closed=LivingPetPortrait.HelmetPartMatrix(i,1))
        using(var traveling=LivingPetPortrait.HelmetPartMatrix(i,.4f))
        {
            var end=new[] {new PointF(120,100)};closed.TransformPoints(end);
            Check(Math.Abs(end[0].X-120)<.001&&Math.Abs(end[0].Y-100)<.001,"Every plate lands exactly on its registered seam");
            var start=new[] {new PointF(120,100)};traveling.TransformPoints(start);
            Check(Math.Abs(start[0].X-120)+Math.Abs(start[0].Y-100)>3,"Panels physically articulate instead of only fading");
        }
        foreach(int fps in new[] {15,60})
        {
            var paced=new TypingHelmetAction();
            for(int frame=0;frame<=10*fps;frame++)paced.Update(frame/(double)fps,1.0/fps,false,WorkMood.Focused);
            for(int i=0;i<6;i++)Check(Math.Abs(paced.Part(i)-1)<.005,"Both idle and interaction frame rates reach the same sealed pose");
        }
    }
    static void OriginalJaw(string root)
    {
        string design=Path.Combine(root,"assets","keyboard-knight","helmet-design");
        using(var source=new Bitmap(Path.Combine(design,"silver-knight-source.png")))
        using(var edited=new Bitmap(Path.Combine(design,"silver-knight-original-jaw-source.png")))
        {
            Check(source.Size==edited.Size,"The original helmet canvas is unchanged");
            int erased=0;
            for(int y=0;y<source.Height;y++)for(int x=0;x<source.Width;x++)
            {
                Color before=source.GetPixel(x,y),after=edited.GetPixel(x,y);
                if(after.A>0)Check(before.ToArgb()==after.ToArgb(),"Every retained helmet pixel comes unchanged from the original");
                else if(before.A>0)erased++;
            }
            Check(erased>100000,"The central faceplate is actually removed");
            for(int x=430;x<=600;x++)for(int y=(int)Math.Ceiling(845+(x-430)*.365);y<=960;y++)
                foreach(int jawX in new[] {x,source.Width-x})
                    Check(source.GetPixel(jawX,y).ToArgb()==edited.GetPixel(jawX,y).ToArgb(),"Both original jaw flanges retain every pixel");
            foreach(var point in new[] {new Point(470,875),new Point(530,908),new Point(565,922),new Point(718,1000),new Point(875,922),new Point(950,875)})
                Check(source.GetPixel(point.X,point.Y).A>200&&source.GetPixel(point.X,point.Y).ToArgb()==edited.GetPixel(point.X,point.Y).ToArgb(),
                    "Original jaw flanges, shading and bottom contour are preserved exactly");
        }
    }
    static void Seams(string root,KeyboardPetSprites sprites)
    {
        using(var source=new Bitmap(Path.Combine(root,"assets","keyboard-knight","rig","helmet-assembled-reference.png")))
        using(var reference=new Bitmap(300,229,PixelFormat.Format32bppArgb))
        using(var assembled=new Bitmap(300,229,PixelFormat.Format32bppArgb))
        using(var a=Graphics.FromImage(reference))
        using(var b=Graphics.FromImage(assembled))
        {
            a.InterpolationMode=InterpolationMode.HighQualityBicubic;
            a.DrawImage(source,new Rectangle(0,0,300,229));
            b.InterpolationMode=InterpolationMode.Bilinear;b.PixelOffsetMode=PixelOffsetMode.HighQuality;
            foreach(int index in LivingPetPortrait.HelmetDrawOrder)sprites.Rig.Parts[TypingHelmetAction.Parts[index]].Draw(b);
            int gaps=0,interior=0;
            // Check the crown, side guards and narrow jaw frame against the
            // source; the intentional transparent face opening is excluded.
            for(int y=1;y<174;y++)for(int x=1;x<299;x++)
            {
                if(reference.GetPixel(x,y).A<249||reference.GetPixel(x-1,y).A<249||reference.GetPixel(x+1,y).A<249||
                    reference.GetPixel(x,y-1).A<249||reference.GetPixel(x,y+1).A<249)continue;
                interior++;if(assembled.GetPixel(x,y).A<220)gaps++;
            }
            Check(interior>14000&&gaps==0,"All six parts retain the open helmet and jaw frame without seam holes: "+gaps);
        }
        foreach(var point in new[] {new PointF(119,129),new PointF(186,134),new PointF(152,151)})
        {
            bool covered=false;foreach(int index in LivingPetPortrait.HelmetDrawOrder)covered|=sprites.Rig.Parts[TypingHelmetAction.Parts[index]].Hit(point);
            Check(!covered,"The open helmet leaves both original eyes and mouth uncovered");
        }
        foreach(var point in new[] {new PointF(64,130),new PointF(100,150),new PointF(120,160),new PointF(148,170),new PointF(156,170),new PointF(183,160),new PointF(204,150),new PointF(239,130)})
        {
            bool covered=false;foreach(int index in LivingPetPortrait.HelmetDrawOrder)covered|=sprites.Rig.Parts[TypingHelmetAction.Parts[index]].Hit(point);
            Check(covered,"The original armor outline still wraps both cheeks and the jawline");
        }
    }
    static int VisiblePixels(Bitmap image)
    {
        var data=image.LockBits(new Rectangle(0,0,image.Width,image.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
        try
        {
            var row=new int[image.Width];int count=0;
            for(int y=0;y<image.Height;y++)
            {Marshal.Copy(IntPtr.Add(data.Scan0,y*data.Stride),row,0,row.Length);foreach(int pixel in row)if(((uint)pixel>>24)>=48)count++;}
            return count;
        }
        finally{image.UnlockBits(data);}
    }
    static void Deployment(KeyboardPetSprites sprites)
    {
        using(var portrait=new LivingPetPortrait(sprites))
        using(var image=new Bitmap(356,285,PixelFormat.Format32bppArgb))
        using(var g=Graphics.FromImage(image))
        {
            int previous=0,largest=0;bool first=true;
            for(int frame=0;frame<=315;frame++)
            {
                portrait.Update(frame/30.0,1/30.0,false,true,false,WorkMood.Focused,240);
                g.Clear(Color.Transparent);var saved=g.Save();g.TranslateTransform(28,28);
                portrait.DrawHelmet(g,true);portrait.DrawHelmet(g,false);g.Restore(saved);
                int count=VisiblePixels(image);
                if(portrait.HelmetBlend>0&&first)
                {Check(count<50,"The first active frame exposes only the extending attachment, never a complete helmet behind the head");first=false;}
                Check(Math.Abs(count-previous)<1800,"Armor coverage changes gradually rather than popping into or out of the rear layer");
                previous=count;largest=Math.Max(largest,count);
            }
            Check(!first&&largest>14000,"Progressive unfolding reveals the crown and jaw frame: "+largest);
            // Hair above the registered crown must already be tucked away,
            // rather than remaining translucent over/through its top edge.
            using(var head=portrait.HeadMatrix())
            {
                int stray=0;float top=sprites.Rig.Parts["helmet"].Bounds.Top-2;
                for(int y=8;y<top;y++)for(int x=100;x<185;x++)
                {
                    var p=Transform(head,new PointF(x,y));
                    if(portrait.Image.GetPixel((int)Math.Round(p.X+28),(int)Math.Round(p.Y+28)).A>=48)stray++;
                }
                Check(stray==0,"Locked crown has no ahoge or hair clip protruding through its upper surface: "+stray);
            }
        }
        foreach(int stop in new[] {195,240,315})
        {
            var action=new TypingHelmetAction();
            for(int frame=0;frame<=stop;frame++)action.Update(frame/30.0,1/30.0,false,WorkMood.Focused);
            var deployed=new float[3];for(int i=0;i<3;i++)deployed[i]=action.Part(i);
            for(int frame=stop+1;frame<=stop+165;frame++)
            {
                action.Update(frame/30.0,1/30.0,false,WorkMood.Idle);
                for(int side=1;side<=2;side++)if(action.Part(side+2)>.2f)
                    Check(action.Part(side)>=deployed[side]-.02f,"A side rail stays outside the head until its face carriage clears");
                if(Math.Max(action.Part(1),action.Part(2))>.2f)
                    Check(action.Part(0)>=deployed[0]-.02f,"Crown stays clear of the hair until both side housings withdraw");
            }
        }
    }
    static void Bounds(BoundPetScene scene,double now)
    {
        foreach(float scale in new[] {.75f,1f,1.25f})
        using(var image=new Bitmap((int)(352*scale),(int)(460*scale),PixelFormat.Format32bppArgb))
        using(var g=Graphics.FromImage(image))
        {
            g.ScaleTransform(scale,scale);scene.Draw(g,now,false,true);
            for(int x=0;x<image.Width;x++)Check(image.GetPixel(x,0).A==0&&image.GetPixel(x,image.Height-1).A==0,"Panel paths and light effects remain inside vertical bounds");
            for(int y=0;y<image.Height;y++)Check(image.GetPixel(0,y).A==0&&image.GetPixel(image.Width-1,y).A==0,"Panel paths and light effects remain inside horizontal bounds");
        }
    }
    static void Occlusion(LivingPetPortrait portrait,bool locked)
    {
        using(var front=new Bitmap(356,285,PixelFormat.Format32bppArgb))
        using(var rear=new Bitmap(356,285,PixelFormat.Format32bppArgb))
        using(var a=Graphics.FromImage(front))
        using(var b=Graphics.FromImage(rear))
        using(var head=portrait.HeadMatrix())
        {
            a.TranslateTransform(28,28);b.TranslateTransform(28,28);
            portrait.DrawHelmet(a,false);portrait.DrawHelmet(b,true);
            // Rear hardware stays inside the opaque scalp. No complete
            // plate may leak around the hair silhouette or cross its depth.
            using(var inner=new GraphicsPath())
            {
                inner.AddEllipse(80,42,144,127);
                for(int y=-5;y<178;y++)for(int x=38;x<267;x++)
                {
                    if(inner.IsVisible(x,y))continue;
                    var p=Transform(head,new PointF(x,y));int px=(int)Math.Round(p.X+28),py=(int)Math.Round(p.Y+28);
                    Check(rear.GetPixel(px,py).A<48,"Rear hardware never protrudes through the scalp silhouette");
                }
            }
            // The worn helmet keeps the face open, including both eyes and
            // mouth, through head movement and armored music animation.
            for(int y=126;y<=153;y+=3)for(int x=112;x<=191;x+=3)
            {
                // The original wider jaw flanges border the lower cheeks;
                // check both eyes and cheeks above them, and the mouth inside.
                if(y>144&&(x<130||x>174))continue;
                var p=Transform(head,new PointF(x,y));int alpha=front.GetPixel((int)Math.Round(p.X+28),(int)Math.Round(p.Y+28)).A;
                if(locked)Check(alpha<16,"Worn helmet leaves eyes, cheeks and mouth visible ("+x+","+y+", alpha="+alpha+")");
                if(portrait.HelmetBlend==0)Check(alpha==0,"Retracted helmet restores the uncovered face");
            }
        }
    }
    static void AudioFaceClear(LivingPetPortrait portrait)
    {
        using(var image=new Bitmap(356,285,PixelFormat.Format32bppArgb))
        using(var g=Graphics.FromImage(image))
        using(var head=portrait.HeadMatrix())
        {
            g.TranslateTransform(28,28);g.MultiplyTransform(head);
            typeof(LivingPetPortrait).GetMethod("DrawArmoredMusic",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(portrait,new object[] {g});
            for(int y=120;y<=153;y+=3)for(int x=112;x<=191;x+=3)
            {
                var p=Transform(head,new PointF(x,y));
                Check(image.GetPixel((int)Math.Round(p.X+28),(int)Math.Round(p.Y+28)).A==0,"Audio lights and meters stay on the outer temples, outside the visor center");
            }
        }
    }
    internal static void Run(string root)
    {
        Motion();OriginalJaw(root);
        using(var sprites=new KeyboardPetSprites(root))
        {
            Seams(root,sprites);
            Deployment(sprites);
            for(int style=0;style<3;style++)
            {
                var input=new InputFeedbackState();
                using(var scene=new BoundPetScene(sprites,new PetInputBindings(),input))
                {
                    scene.SetViewWidth(BoundPetScene.CompactWidth);scene.Portrait.SetHairStyle(style);
                    for(int frame=0;frame<=315;frame++)
                    {
                        double now=frame/30.0;
                        if(frame%6==0){input.Key(65,true,now);input.Key(65,false,now+.005);}
                        scene.Advance(now,1/30.0,false);
                        if(frame>=150&&frame%10==0||frame==315)Bounds(scene,now);
                        if(frame>=150&&frame%10==0||frame==315)Occlusion(scene.Portrait,frame==315);
                    }
                    Check(scene.Portrait.HelmetVisor>.99f&&scene.Hands.Typing,"Real short taps assemble the helmet while hands keep typing");
                    Check(scene.Hit(scene.Portrait.WorldPoint(new PointF(152,140)),false,true),"Assembled armor participates in alpha hit testing");
                    input.Clear();
                    for(int frame=316;frame<=480;frame++)
                    {
                        scene.Advance(frame/30.0,1/30.0,false);
                        if(frame%10==0){Bounds(scene,frame/30.0);Occlusion(scene.Portrait,false);}
                    }
                    Check(scene.Portrait.HelmetBlend==0,"Pausing input finishes the removal");
                    scene.Advance(0,0,false);Check(scene.Portrait.HelmetBlend==0,"Time reset clears transient armor");
                }
                using(var portrait=new LivingPetPortrait(sprites))
                {
                    portrait.SetHairStyle(style);
                    for(int frame=0;frame<=400;frame++)
                    {
                        portrait.Update(frame/30.0,1/30.0,false,true,false,WorkMood.Turbo,600,true,.8f);
                        if(frame>=150&&frame%15==0){Occlusion(portrait,frame>=315);AudioFaceClear(portrait);}
                    }
                }
            }
        }
        Console.WriteLine("PASS: open-face helmet, visible eyes/cheeks/mouth, continuous jaw armor, full seams, progressive deployment/retraction, temple audio lighting, tucked hair, rails, stop continuity, 15/60 fps, all three hairstyles and three sizes.");
    }
}
