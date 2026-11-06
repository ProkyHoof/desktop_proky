using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

internal sealed class RigSprite : IDisposable
{
    internal readonly Bitmap Image;
    internal readonly RectangleF Bounds;
    internal readonly string Parent;
    internal readonly PointF Pivot;
    internal RigSprite(string file,string parent,PointF pivot)
    {
        Parent=parent;Pivot=pivot;
        using(var source=new Bitmap(file))
        {
            if(source.Width!=900||source.Height!=687)throw new InvalidDataException("图层必须使用统一的 900x687 注册画布："+file);
            int w=source.Width,h=source.Height,l=w,t=h,r=-1,b=-1;
            // Ignore barely visible alpha dust around generated transparent
            // assets when finding their registration-preserving crop.
            uint minimumAlpha=Path.GetFileName(file)=="short-hair-cap.png" ? 8u : 1u;
            var data=source.LockBits(new Rectangle(0,0,w,h),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
            var row=new int[w];
            try
            {
                for(int y=0;y<h;y++)
                {
                    Marshal.Copy(IntPtr.Add(data.Scan0,y*data.Stride),row,0,w);
                    for(int x=0;x<w;x++)if(((uint)row[x]>>24)>=minimumAlpha){l=Math.Min(l,x);r=Math.Max(r,x);t=Math.Min(t,y);b=Math.Max(b,y);}
                }
            }
            finally{source.UnlockBits(data);}
            if(r<l)throw new InvalidDataException("独立图层为空："+file);
            var crop=Rectangle.FromLTRB(l,t,r+1,b+1);Bounds=new RectangleF(l/3f,t/3f,crop.Width/3f,crop.Height/3f);
            // Resample once to the portrait's 2x rendering density. Repeatedly
            // shrinking every original 3x texture made each frame expensive.
            Image=new Bitmap((int)Math.Ceiling(crop.Width*2/3.0),(int)Math.Ceiling(crop.Height*2/3.0),PixelFormat.Format32bppPArgb);
            using(var g=Graphics.FromImage(Image))
            {
                g.InterpolationMode=InterpolationMode.HighQualityBicubic;
                g.DrawImage(source,new Rectangle(0,0,Image.Width,Image.Height),crop,GraphicsUnit.Pixel);
            }
        }
    }
    internal void Draw(Graphics g) {g.DrawImage(Image,Bounds,new RectangleF(0,0,Image.Width,Image.Height),GraphicsUnit.Pixel);}
    internal void Draw(Graphics g,float opacity)
    {
        if(opacity<=0)return;if(opacity>=1){Draw(g);return;}
        using(var attributes=new ImageAttributes())
        {
            var color=new ColorMatrix();color.Matrix33=opacity;attributes.SetColorMatrix(color);
            var corners=new[] {new PointF(Bounds.Left,Bounds.Top),new PointF(Bounds.Right,Bounds.Top),new PointF(Bounds.Left,Bounds.Bottom)};
            g.DrawImage(Image,corners,new RectangleF(0,0,Image.Width,Image.Height),GraphicsUnit.Pixel,attributes);
        }
    }
    internal bool Hit(PointF p)
    {
        if(!Bounds.Contains(p))return false;
        int x=(int)((p.X-Bounds.X)*Image.Width/Bounds.Width),y=(int)((p.Y-Bounds.Y)*Image.Height/Bounds.Height);
        return Image.GetPixel(x,y).A>=48;
    }
    public void Dispose(){Image.Dispose();}
}

internal static class PetHairStyles
{
    internal const int Short=0,Reference=1;
    internal static int Normalize(int value){return value==Reference ? Reference : Short;}
}

internal sealed class LayeredRigAssets : IDisposable
{
    internal readonly Dictionary<string,RigSprite> Parts=new Dictionary<string,RigSprite>();
    internal readonly RigSprite ReferenceHairCap,ReferenceBackLeft,ReferenceBackRight;
    internal readonly RigSprite[][] Eyelids={new RigSprite[33],new RigSprite[33]};
    internal readonly GraphicsPath[][] EyeClips={new GraphicsPath[33],new GraphicsPath[33]};
    internal LayeredRigAssets(string root)
    {
        string folder=Path.Combine(root,"assets","keyboard-knight","rig");
        try
        {
            foreach(string line in File.ReadAllLines(Path.Combine(folder,"layers.tsv")))
            {
                string[] c=line.Split('\t');if(c[1].Contains("{"))continue;
                Parts.Add(c[0],new RigSprite(Path.Combine(folder,c[1]),c[2],new PointF(float.Parse(c[3],CultureInfo.InvariantCulture),float.Parse(c[4],CultureInfo.InvariantCulture))));
            }
            ReferenceHairCap=new RigSprite(Path.Combine(folder,"hair-cap.png"),"head",new PointF(150,66));
            ReferenceBackLeft=new RigSprite(Path.Combine(folder,"hair-back-left.png"),"head",new PointF(83,163));
            ReferenceBackRight=new RigSprite(Path.Combine(folder,"hair-back-right.png"),"head",new PointF(219,163));
            for(int eye=0;eye<2;eye++)for(int i=0;i<=32;i++)
            {
                string name="eyelid-"+(eye==0 ? "left" : "right");
                Eyelids[eye][i]=new RigSprite(Path.Combine(folder,name,i.ToString("D2")+".png"),"head",new PointF(eye==0 ? 119 : 186,eye==0 ? 129 : 134));
                EyeClips[eye][i]=LivingPetPortrait.EyeAperture(eye==0,i/32f);
            }
        }
        catch{Dispose();throw;}
    }
    public void Dispose()
    {
        foreach(var part in Parts.Values)part.Dispose();
        if(ReferenceHairCap!=null)ReferenceHairCap.Dispose();
        if(ReferenceBackLeft!=null)ReferenceBackLeft.Dispose();
        if(ReferenceBackRight!=null)ReferenceBackRight.Dispose();
        foreach(var row in Eyelids)foreach(var frame in row)if(frame!=null)frame.Dispose();
        foreach(var row in EyeClips)foreach(var path in row)if(path!=null)path.Dispose();
    }
}

// A parented cutout rig: no whole-character image warping and no painting over
// existing facial features. Skin, irises, lids, lips and each hair lock remain
// distinct textures throughout animation and compositing.
internal sealed class TypingHelmetAction
{
    internal static readonly string[] Parts={"helmet","helmet-temple-left","helmet-temple-right","helmet-face-left","helmet-face-right","helmet-chin"};
    static readonly double[] Entry={0,.65,.85,1.65,1.9,2.8},Duration={2.1,1.85,1.85,1.9,1.9,1.35};
    readonly float[] position=new float[6],velocity=new float[6],depart=new float[6];
    readonly double[] returnTime=new double[6];
    double workTime,idleTime,nextAt=5,age=-1,lastTime=double.NaN,removeAge;
    bool removing;
    static float Clamp(float p){return Math.Max(0,Math.Min(1,p));}
    internal float Blend { get { return Clamp(position[0]); } }
    internal float Visor { get { return Clamp(Math.Min(position[3],Math.Min(position[4],position[5]))); } }
    internal float Part(int index){return position[index];}
    internal bool Removing { get { return removing; } }
    internal float Pulse { get { return age<0||removing ? 0 : (float)Math.Exp(-Math.Pow((age-4.65)/.3,2)); } }
    static float Smooth(float t){t=Math.Max(0,Math.Min(1,t));return t*t*(3-2*t);}
    internal void Update(double now,double dt,bool resting,WorkMood mood,bool allowStart=true)
    {
        if(resting||!double.IsNaN(lastTime)&&now<lastTime)
        {workTime=idleTime=0;nextAt=5;age=-1;removing=false;Array.Clear(position,0,6);Array.Clear(velocity,0,6);}
        lastTime=now;
        if(resting)return;
        // Real typing moods exclude shortcuts, mouse use and a held key.
        bool working=mood>=WorkMood.Thinking&&mood<=WorkMood.Turbo;
        double step=Math.Min(.1,Math.Max(0,dt));
        if(working&&allowStart){workTime+=step;idleTime=0;}
        else
        {
            idleTime+=step;
            if(idleTime>=2){workTime=0;nextAt=5;}
        }
        if(age>=0)
        {
            if(!removing&&(!working||age>=8.5))
            {
                removing=true;removeAge=0;Array.Copy(position,depart,6);Array.Clear(returnTime,0,6);
            }
            if(removing)removeAge+=step;else age+=step;
            for(int i=0;i<6;i++)
            {
                float target=Smooth((float)((age-Entry[i])/Duration[i]));
                if(removing)
                {
                    // Open the face before withdrawing its rails, and clear
                    // both rails before withdrawing the crown above the hair.
                    // Reversing all parents together dragged plates through
                    // the cheeks and let the hair reappear through the shell.
                    bool clear=i==0 ? Math.Max(position[1],position[2])<.1f : i<=2 ? position[i+2]<.1f : true;
                    if(clear)returnTime[i]+=step;
                    target=depart[i]*(1-Smooth((float)(returnTime[i]/.95)));
                }
                // Face carriages slide after their supporting side rails
                // extend. Retraction keeps the carrier extended last.
                if(!removing&&i>=3&&i<=4)target*=Smooth((position[i-2]-.18f)/.65f);
                if(!removing&&i==5)target*=Smooth((VisorWithoutChin()-.35f)/.6f);
                // Damped springs preserve velocity when input stops midway.
                // Small substeps make the same motion work at 15 or 30 fps.
                double remaining=step;
                while(remaining>0)
                {
                    float h=(float)Math.Min(1.0/120,remaining);remaining-=h;
                    velocity[i]+=(90*(target-position[i])-13*velocity[i])*h;
                    position[i]+=velocity[i]*h;
                }
            }
            bool settled=removing&&removeAge>1;
            for(int i=0;i<6&&settled;i++)settled=Math.Abs(position[i])<.002f&&Math.Abs(velocity[i])<.02f;
            if(settled)
            {
                age=-1;removing=false;nextAt=workTime+20;
                Array.Clear(position,0,6);Array.Clear(velocity,0,6);
            }
        }
        else if(working&&allowStart&&workTime>=nextAt)age=0;
    }
    float VisorWithoutChin(){return Math.Min(position[3],position[4]);}
}

internal sealed class LivingPetPortrait : IDisposable
{
    const int Width=300,Height=229;
    internal const int Padding=28;
    readonly Bitmap image=new Bitmap(Width+Padding*2,Height+Padding*2,PixelFormat.Format32bppPArgb);
    internal Bitmap Image { get { EnsureImage();return image; } }
    bool imageDirty=true;
    Matrix headPose,bodyPose;
    readonly int[] output=new int[(Width+Padding*2)*(Height+Padding*2)];
    bool hitPixelsDirty=true;
    internal readonly LayeredRigAssets Rig;
    internal int HairStyle { get; private set; }
    internal RigSprite HairCap { get { return HairStyle==PetHairStyles.Reference ? Rig.ReferenceHairCap : Rig.Parts["hair-cap"]; } }
    internal void SetHairStyle(int value)
    {
        value=PetHairStyles.Normalize(value);if(value==HairStyle)return;
        HairStyle=value;imageDirty=true;hitPixelsDirty=true;
    }
    internal float Activity,Tilt,Bob,Hair,Blink,Sway,GazeX,GazeY,Smile,IdleGesture,Breath;
    internal float CheekHairLeft,CheekHairRight,HairInertia;
    float cheekVelocityLeft,cheekVelocityRight,inertiaVelocity;
    PointF attentionTarget;
    float attentionStrength,nearAttention;
    double touchedAt=-100;
    readonly TypingHelmetAction helmet=new TypingHelmetAction();
    internal float HelmetBlend { get { return helmet.Blend; } }
    internal float HelmetVisor { get { return helmet.Visor; } }
    internal bool HelmetRemoving { get { return helmet.Removing; } }
    float HelmetHairCover { get { return Smooth((HelmetBlend-.28f)/.38f); } }
    internal float HelmetPart(int index){return helmet.Part(index);}
    internal WorkMood Mood;
    internal float MusicBlend { get; private set; }
    internal float MusicMotion { get; private set; }
    internal float MusicArmorBlend { get; private set; }
    internal float HeadphoneBandBlend { get { return MusicBlend*(1-Smooth(HelmetBlend/.32f)); } }
    internal float MusicEnergy { get { return musicEnergy; } }
    float musicEnergy;
    internal int MouthIndex;
    int previousMouth;
    float mouthMix=1,emotionLid,browLeft,browRight,accent;
    bool resting;
    double time;
    static readonly double[] BlinkStarts={2.8,6.7,7.08,12.4,17.6,22.3,22.7,28.8};
    static readonly string[] TorsoFace={"face","cheeks"},FrontHair={"hair-fringe-left","hair-fringe-right","hair-ahoge"};
    readonly GraphicsPath torsoSilhouette=TorsoSilhouette();
    readonly GraphicsPath lowerTorsoEdge=LowerTorsoEdge();
    // Tight lower garment contour excludes white fragments inherited from
    // the original long-hair atlas.
    internal static GraphicsPath TorsoSilhouette()
    {
        var p=new GraphicsPath();p.AddLines(new[] {new PointF(0,0),new PointF(300,0),new PointF(300,184),new PointF(181,184)});
        using(var edge=LowerTorsoEdge())p.AddPath(edge,true);
        p.AddLine(122,184,0,184);p.CloseFigure();return p;
    }
    static GraphicsPath LowerTorsoEdge()
    {
        var p=new GraphicsPath();
        p.AddBezier(181,184,177,191,176,196,176,199);
        p.AddBezier(176,199,178,204,181,209,185,212);
        p.AddBezier(185,212,174,222,132,224,119,212);
        p.AddBezier(119,212,125,205,129,201,128,196);
        p.AddBezier(128,196,128,191,124,187,122,184);
        return p;
    }
    static float Smooth(float x){x=Math.Max(0,Math.Min(1,x));return x*x*(3-2*x);}
    static float Clamp(float x,float limit){return Math.Max(-limit,Math.Min(limit,x));}
    internal void LookAt(PointF world,float strength)
    {
        PointF center=WorldPoint(new PointF(151,131));
        float dx=world.X-center.X,dy=world.Y-center.Y;
        attentionTarget=new PointF(Clamp(dx/140,1),Clamp(dy/110,1));
        float length=(float)Math.Sqrt(attentionTarget.X*attentionTarget.X+attentionTarget.Y*attentionTarget.Y);
        if(length>1)attentionTarget=new PointF(attentionTarget.X/length,attentionTarget.Y/length);
        attentionStrength=Math.Max(0,Math.Min(1,strength));
        nearAttention=Math.Max(0,1-(float)Math.Sqrt(dx*dx+dy*dy)/135)*attentionStrength;
    }
    internal void React(double now){touchedAt=now;}
    static void Spring(ref float position,ref float velocity,float target,double dt,float limit)
    {
        // Small substeps keep drag/slow-frame impulses stable and bounded.
        double remaining=Math.Min(.4,Math.Max(0,dt));
        while(remaining>0)
        {
            float step=(float)Math.Min(1.0/120,remaining);remaining-=step;
            velocity+=(70*(target-position)-13*velocity)*step;
            position+=velocity*step;
            if(Math.Abs(position)>limit){position=Clamp(position,limit);velocity=0;}
        }
    }
    internal LivingPetPortrait(KeyboardPetSprites sprites){Rig=sprites.Rig;Update(0,0,false,false,false);}
    internal static float BlinkAt(double now)
    {
        double phase=(now%32+32)%32;
        foreach(double start in BlinkStarts)
        {
            double age=phase-start;if(age<0||age>.33)continue;
            if(age<.095)return Smooth((float)(age/.095));if(age<.145)return 1;
            return 1-Smooth((float)((age-.145)/.185));
        }
        return 0;
    }
    internal static float Gesture(double phase,double start,double duration)
    {float t=(float)((phase-start)/duration);return t<=0||t>=1 ? 0 : Smooth(t/.24f)*(1-Smooth((t-.65f)/.35f));}
    // The bind poses retain the artwork proportions; all motion is applied
    // afterwards through the same torso root, including breathing.
    internal Matrix RootMatrix()
    {
        var m=new Matrix();m.Translate(-150,-229,MatrixOrder.Append);
        m.Rotate(Tilt*180/(float)Math.PI*.75f,MatrixOrder.Append);
        m.Translate(150+Sway,229+Bob+Breath*.35f,MatrixOrder.Append);return m;
    }
    internal Matrix HeadMatrix(){return headPose.Clone();}
    internal Matrix BodyMatrix(){return bodyPose.Clone();}
    Matrix BuildHeadMatrix()
    {
        // Only a quarter of the tilt bends the neck. The other three quarters
        // come from the torso, so the skull, collar and shoulders travel as
        // one structure. No independent head translation can stretch it.
        const float neckY=175+(173-164)*54f/65;
        var m=new Matrix();m.Translate(-150,-171,MatrixOrder.Append);m.Scale(1,175f/164,MatrixOrder.Append);
        m.Rotate(Tilt*180/(float)Math.PI*.25f,MatrixOrder.Append);
        m.Translate(150,neckY,MatrixOrder.Append);
        using(var root=RootMatrix())m.Multiply(root,MatrixOrder.Append);return m;
    }
    Matrix BuildBodyMatrix()
    {
        var m=new Matrix();m.Translate(-150,-164,MatrixOrder.Append);m.Scale(1,54f/65,MatrixOrder.Append);
        m.Translate(150,175,MatrixOrder.Append);
        using(var root=RootMatrix())m.Multiply(root,MatrixOrder.Append);return m;
    }
    internal Matrix LayerMatrix(string name)
    {
        RigSprite part=Rig.Parts[name];var m=new Matrix();float angle=0;
        if(name=="hair-fringe-left")angle=Clamp(Hair*.15f+HairInertia*.20f,1.1f);
        else if(name=="hair-fringe-right")angle=Clamp(Hair*.17f+HairInertia*.16f,1.1f);
        else if(name=="hair-ahoge")angle=Clamp(Hair*.68f+HairInertia*.85f,5);
        if(angle!=0)m.RotateAt(angle,part.Pivot,MatrixOrder.Append);
        m.Multiply(part.Parent=="body" ? bodyPose : headPose,MatrixOrder.Append);
        return m;
    }
    void DrawPart(Graphics g,string name,float opacity=1)
    {
        var saved=g.Save();
        if(name.StartsWith("hair-",StringComparison.Ordinal)&&name!="hair-cap"&&name!="hair-clip")
        {using(var m=LayerMatrix(name))g.MultiplyTransform(m);}
        else g.MultiplyTransform(Rig.Parts[name].Parent=="body" ? bodyPose : headPose);
        Rig.Parts[name].Draw(g,opacity);g.Restore(saved);
    }
    void DrawTorso(Graphics g)
    {
        var saved=g.Save();g.MultiplyTransform(bodyPose);
        // Back the lower cloth with a closed opaque contour, so neither key
        // legends nor background can appear through gaps in the old cutout.
        using(var cloth=(GraphicsPath)lowerTorsoEdge.Clone())
        using(var fill=new SolidBrush(Color.FromArgb(48,46,54)))
        {cloth.CloseFigure();g.FillPath(fill,cloth);}
        var clipped=g.Save();
        g.SetClip(torsoSilhouette,CombineMode.Intersect);
        Rig.Parts["body"].Draw(g);
        g.Restore(clipped);
        using(var ink=new Pen(Color.FromArgb(27,25,32),.8f))
        {ink.StartCap=ink.EndCap=LineCap.Round;g.DrawPath(ink,lowerTorsoEdge);}
        g.Restore(saved);
    }
    void DrawCheekHair(Graphics g)
    {
        // Shear the existing side locks below a fixed root; tuck the upper
        // volume against the scalp while the crown passes over it.
        RigSprite cap=HairCap;RectangleF b=cap.Bounds;
        const float root=105;
        var saved=g.Save();g.MultiplyTransform(headPose);
        const float crown=75;
        var upper=g.Save();
        float upperTuck=Smooth((HelmetBlend-.7f)/.3f);
        g.TranslateTransform(0,crown);g.ScaleTransform(1,1-.8f*upperTuck);g.TranslateTransform(0,-crown);
        DrawHairSection(g,cap,new RectangleF(b.Left,b.Top,b.Width,crown-b.Top),0,0);
        g.Restore(upper);
        DrawHairSection(g,cap,new RectangleF(b.Left,crown,b.Width,root-crown),0,0);
        for(int side=0;side<2;side++)
        {
            float left=side==0 ? b.Left : 150,right=side==0 ? 150 : b.Right;
            float offset=side==0 ? CheekHairLeft : CheekHairRight;
            // Two continuous affine segments bend the tips more than the roots.
            const float bend=140;
            DrawHairSection(g,cap,RectangleF.FromLTRB(left,root,right,bend),0,offset*.24f);
            DrawHairSection(g,cap,RectangleF.FromLTRB(left,bend,right,b.Bottom),offset*.24f,offset);
        }
        g.Restore(saved);
    }
    void DrawBackHair(Graphics g)
    {
        if(HairStyle!=PetHairStyles.Reference)return;
        var saved=g.Save();g.MultiplyTransform(headPose);
        // Keep the roots attached to the head; the lower locks bend behind
        // the clothing and arms, rather than painting over the desk or scarf.
        DrawHairSection(g,Rig.ReferenceBackLeft,Rig.ReferenceBackLeft.Bounds,0,CheekHairLeft*1.25f);
        DrawHairSection(g,Rig.ReferenceBackRight,Rig.ReferenceBackRight.Bounds,0,CheekHairRight*1.25f);
        g.Restore(saved);
    }
    static void DrawHairSection(Graphics g,RigSprite sprite,RectangleF area,float topOffset,float bottomOffset,float opacity=1)
    {
        if(opacity<=0)return;
        RectangleF b=sprite.Bounds;
        // GDI+ samples independently drawn affine strips at subpixel edges.
        // Overlap the same source artwork slightly so root/bend boundaries
        // stay opaque when the head is rotated between pixel centres.
        float slope=(bottomOffset-topOffset)/area.Height;
        float top=Math.Max(b.Top,area.Top-.6f),bottom=Math.Min(b.Bottom,area.Bottom+.6f);
        topOffset+=(top-area.Top)*slope;bottomOffset+=(bottom-area.Bottom)*slope;
        area=RectangleF.FromLTRB(area.Left,top,area.Right,bottom);
        var source=new RectangleF((area.X-b.X)*sprite.Image.Width/b.Width,(area.Y-b.Y)*sprite.Image.Height/b.Height,
            area.Width*sprite.Image.Width/b.Width,area.Height*sprite.Image.Height/b.Height);
        var corners=new[] {new PointF(area.Left+topOffset,area.Top),new PointF(area.Right+topOffset,area.Top),
            new PointF(area.Left+bottomOffset,area.Bottom)};
        if(opacity>=1)g.DrawImage(sprite.Image,corners,source,GraphicsUnit.Pixel);
        else using(var attributes=new ImageAttributes())
        {
            var color=new ColorMatrix();color.Matrix33=opacity;attributes.SetColorMatrix(color);
            g.DrawImage(sprite.Image,corners,source,GraphicsUnit.Pixel,attributes);
        }
    }
    void DrawNeck(Graphics g)
    {
        // Both ends overlap their parent artwork. The upper end follows the
        // jaw; the lower end stays tucked inside the scarf as the head moves.
        var top=new[] {new PointF(139,153),new PointF(163,153)};
        var bottom=new[] {new PointF(137,177),new PointF(166,177)};
        headPose.TransformPoints(top);bodyPose.TransformPoints(bottom);
        using(var neck=new GraphicsPath())
        {
            neck.AddLine(top[0],top[1]);
            neck.AddBezier(top[1],new PointF(top[1].X-1,top[1].Y+8),
                new PointF(bottom[1].X-3,bottom[1].Y-5),bottom[1]);
            neck.AddLine(bottom[1],bottom[0]);
            neck.AddBezier(bottom[0],new PointF(bottom[0].X+3,bottom[0].Y-5),
                new PointF(top[0].X+1,top[0].Y+8),top[0]);
            neck.CloseFigure();
            using(var skin=new LinearGradientBrush(neck.GetBounds(),Color.FromArgb(224,171,158),Color.FromArgb(255,234,220),90))g.FillPath(skin,neck);
            using(var edge=new Pen(Color.FromArgb(119,73,68),1.1f))g.DrawPath(edge,neck);
        }
    }
    internal static GraphicsPath EyeAperture(bool left,float close)
    {
        float x=left ? 97 : 164,y=left ? 111 : 116,c=14+7*close,u=-4+21*close,l=41-24*close;
        var p=new GraphicsPath();p.AddBezier(x,y+c,x+9,y+u,x+35,y+u,x+44,y+c);p.AddBezier(x+44,y+c,x+41,y+l,x+3,y+l,x,y+c);p.CloseFigure();return p;
    }
    void DrawEye(Graphics g,int eye)
    {
        bool left=eye==0;string side=left ? "left" : "right";float close=EyeClose(eye);
        if(close>.96f)close=1;
        var saved=g.Save();g.MultiplyTransform(headPose);
        if(close<1)
        {
            var apertureState=g.Save();g.SetClip(Rig.EyeClips[eye][(int)Math.Round(close*32)],CombineMode.Intersect);
            Rig.Parts["eye-"+side+"-white"].Draw(g);
            g.TranslateTransform(GazeX*4.2f,GazeY*2.5f);Rig.Parts["eye-"+side+"-iris"].Draw(g);
            g.Restore(apertureState);
        }
        g.Restore(saved);
    }
    void DrawEyelid(Graphics g,int eye)
    {
        float close=EyeClose(eye);
        if(close>.96f)close=1;
        var saved=g.Save();g.MultiplyTransform(headPose);
        Rig.Eyelids[eye][(int)Math.Round(close*32)].Draw(g);g.Restore(saved);
    }
    float EyeClose(int eye){return resting ? 1 : Math.Max(emotionLid,BlinkAt(time-(eye==0 ? 0 : .012)));}
    void DrawBrows(Graphics g)
    {
        for(int side=0;side<2;side++)
        {
            var saved=g.Save();g.MultiplyTransform(headPose);
            g.SetClip(new RectangleF(side==0 ? 0 : 150,0,150,Height),CombineMode.Intersect);
            using(var turn=new Matrix())
            {
                turn.RotateAt(side==0 ? browLeft : browRight,new PointF(side==0 ? 118 : 184,side==0 ? 104 : 108));
                g.MultiplyTransform(turn);Rig.Parts["eyebrows"].Draw(g);
            }
            g.Restore(saved);
        }
    }
    void DrawMouth(Graphics g)
    {
        var saved=g.Save();g.MultiplyTransform(headPose);
        if(mouthMix<1)Rig.Parts["mouth-"+previousMouth].Draw(g,1-mouthMix);
        Rig.Parts["mouth-"+MouthIndex].Draw(g,mouthMix);g.Restore(saved);
    }
    void DrawWorkAccents(Graphics g)
    {
        if(resting||Mood==WorkMood.Idle||accent<.02f)return;
        var saved=g.Save();g.MultiplyTransform(headPose);
        int alpha=(int)(220*accent);
        if(Mood==WorkMood.Thinking)
        {
            for(int i=0;i<3;i++)using(var dot=new SolidBrush(Color.FromArgb((int)(alpha*(.5+.5*Math.Sin(time*4-i))),136,101,126)))
                g.FillEllipse(dot,210+i*5,103,2.3f,2.3f);
        }
        else if(Mood==WorkMood.Rushing)
        {
            float drift=(float)((time*1.2)%1)*2;
            using(var drop=new GraphicsPath())
            {
                drop.AddBezier(211,121+drift,208.5f,125+drift,208.5f,127+drift,211,127+drift);
                drop.AddBezier(211,127+drift,213.5f,127+drift,213.5f,125+drift,211,121+drift);
                using(var fill=new SolidBrush(Color.FromArgb(alpha,148,214,240)))g.FillPath(fill,drop);
                using(var edge=new Pen(Color.FromArgb(alpha,73,127,165),.8f))g.DrawPath(edge,drop);
            }
        }
        else if(Mood==WorkMood.Turbo)
        {
            float pulse=.85f+(float)Math.Sin(time*3)*.15f;
            using(var sparkle=new GraphicsPath())
            {
                float x=213,y=116;
                sparkle.AddPolygon(new[] {new PointF(x,y-4*pulse),new PointF(x+1,y-1),new PointF(x+3*pulse,y),
                    new PointF(x+1,y+1),new PointF(x,y+4*pulse),new PointF(x-1,y+1),new PointF(x-3*pulse,y),new PointF(x-1,y-1)});
                using(var gold=new SolidBrush(Color.FromArgb(alpha,235,166,75)))g.FillPath(gold,sparkle);
            }
            using(var blush=new Pen(Color.FromArgb(alpha/2,218,116,132),.9f))
            {g.DrawLine(blush,101,144,102,146);g.DrawLine(blush,105,144,106,146);g.DrawLine(blush,197,148,198,150);g.DrawLine(blush,201,148,202,150);}
        }
        g.Restore(saved);
    }
    internal void Update(double now,double dt,bool resting,bool typing,bool mouseActive,WorkMood mood=WorkMood.Idle,float typingSpeed=0,bool musicPlaying=false,float musicLevel=0)
    {
        time=now;this.resting=resting;Mood=resting ? WorkMood.Idle : mood;
        // Music only starts a new deployment after sustained fast real typing.
        // An existing action keeps its normal lifetime when music starts or
        // typing slows; the audio modules follow the actual moving side plates.
        helmet.Update(now,dt,resting,Mood,!musicPlaying||Mood==WorkMood.Rushing||Mood==WorkMood.Turbo);
        float musicTarget=resting || !musicPlaying ? 0 : 1;
        MusicBlend+=(musicTarget-MusicBlend)*(float)(1-Math.Exp(-5*Math.Max(0,dt)));
        if(resting)MusicBlend=0;
        float audioArmorTarget=Smooth((Math.Min(helmet.Part(1),helmet.Part(2))-.15f)/.75f);
        MusicArmorBlend+=Clamp((audioArmorTarget-MusicArmorBlend)*(float)(1-Math.Exp(-8*Math.Max(0,dt))),
            (float)Math.Max(0,dt)*2);
        if(resting||HelmetBlend==0)MusicArmorBlend=0;
        musicEnergy+=((float)Math.Min(1,Math.Sqrt(Math.Max(0,musicLevel))*2.5)-musicEnergy)*(float)(1-Math.Exp(-7*Math.Max(0,dt)));
        MusicMotion=MusicBlend*(typing ? .3f+.15f*MusicArmorBlend : 1)*(.6f+musicEnergy*.4f);
        float target=resting ? 0 : typing ? (mood==WorkMood.Idle ? 1 : .35f+Math.Min(1,typingSpeed/480)*.65f) : mouseActive ? .65f : 0;
        Activity+=(target-Activity)*(float)(1-Math.Exp(-5*Math.Max(0,dt)));
        double phase=(now%32+32)%32;
        float curious=Gesture(phase,3.8,3.4),look=Gesture(phase,10,3.6),happy=Gesture(phase,19,3.8),nod=Gesture(phase,26,2.6);
        float idle=(1-Activity)*(resting ? 0 : 1)*(1-MusicBlend),amount=resting ? .035f : .28f+Activity;
        double touchAge=now-touchedAt;
        float touch=resting||touchAge<0||touchAge>1.6 ? 0 : (float)(Math.Sin(touchAge*7)*Math.Exp(-touchAge*2.8));
        float response=resting||touchAge<0 ? 0 : Gesture(touchAge,0,1.5);
        IdleGesture=(curious-look*.8f+happy*.7f)*idle;
        float settle=(float)(1-Math.Exp(-9*Math.Max(0,dt)));
        float lidTarget=Mood==WorkMood.Focused ? .15f : Mood==WorkMood.Rushing ? .23f : Mood==WorkMood.Turbo ? .03f : Mood==WorkMood.Relieved ? 1 : 0;
        if(Mood==WorkMood.Idle)lidTarget=MusicBlend*.5f;
        emotionLid+=(lidTarget-emotionLid)*settle;
        float leftTarget=Mood==WorkMood.Thinking ? -6 : Mood==WorkMood.Focused ? 5 : Mood==WorkMood.Rushing ? 6 : Mood==WorkMood.Turbo ? -3 : Mood==WorkMood.Relieved ? -4 : 0;
        float rightTarget=Mood==WorkMood.Thinking ? 3 : -leftTarget;
        browLeft+=(leftTarget-browLeft)*settle;browRight+=(rightTarget-browRight)*settle;
        accent+=((Mood==WorkMood.Idle ? 0 : 1)-accent)*settle;
        // A shared weight shift drives the whole upper body. Smaller nods
        // and a delayed hair response keep activity lively without whipping
        // the large head against an almost stationary collar.
        float oldTilt=Tilt;
        Tilt+=((float)(Math.Sin(now*1.65)*.038+Math.Sin(now*2.85+.7)*.008)*amount+IdleGesture*.045f+
            (resting ? 0 : attentionTarget.X*attentionStrength*.012f)+touch*.012f+(float)Math.Sin(now*3.6)*(.032f-.02f*MusicArmorBlend)*MusicMotion-Tilt)*settle;
        Bob+=((float)Math.Sin(now*3.3)*1.6f*amount+idle*(float)(Math.Sin(now*1.65)*.45-nod*1.6+happy*Math.Sin((phase-19)*4)*.6)+
            ((float)Math.Cos(now*7.2)*1.35f*(1-MusicArmorBlend)-(float)Math.Pow(.5+.5*Math.Sin(now*3.6),3)*2.8f*MusicArmorBlend)*MusicMotion-Bob)*settle;
        Sway+=((float)Math.Sin(now*1.65)*1.8f*amount+IdleGesture*1.2f+(float)Math.Sin(now*3.6)*(2-1.3f*MusicArmorBlend)*MusicMotion-Sway)*settle;
        Hair+=((float)(Math.Sin(now*1.65-.55)*4+Math.Sin(now*2.85-.4)) *amount+IdleGesture*2-Hair)*settle;
        float angularSpeed=dt<=0 ? 0 : Clamp((Tilt-oldTilt)/(float)dt,1);
        Spring(ref HairInertia,ref inertiaVelocity,-angularSpeed*10+touch*2.8f,dt,3);
        float hairAmount=resting ? .09f : .55f+Activity*.65f+nearAttention*.22f;
        Spring(ref CheekHairLeft,ref cheekVelocityLeft,Clamp((float)Math.Sin(now*1.9-.7)*hairAmount+Hair*.18f+HairInertia*.65f+touch*1.8f,2.8f),dt,2.8f);
        Spring(ref CheekHairRight,ref cheekVelocityRight,Clamp((float)Math.Sin(now*1.7-1.5)*hairAmount+Hair*.15f+HairInertia*.55f-touch*1.3f,2.8f),dt,2.8f);
        float gazeBlend=resting||Mood==WorkMood.Relieved ? 0 : attentionStrength*(typing&&nearAttention<.1f ? .5f : 1);
        float gazeX=resting ? 0 : (curious*.7f-look*.85f)*idle+(float)Math.Sin(now*.78)*.17f;
        float gazeY=resting ? 0 : -curious*.26f*idle+Activity*.42f;
        GazeX=Clamp(GazeX+(gazeX*(1-gazeBlend)+attentionTarget.X*gazeBlend-GazeX)*settle,1);
        GazeY=Clamp(GazeY+(gazeY*(1-gazeBlend)+attentionTarget.Y*gazeBlend-GazeY)*settle,1);
        Smile=resting ? .15f : .25f+happy*.7f*idle+Activity*.15f+response*.65f;
        int nextMouth=resting ? 7 : Mood==WorkMood.Thinking ? 1 : Mood==WorkMood.Focused ? 2 : Mood==WorkMood.Rushing ? 3 :
            Mood==WorkMood.Turbo ? 5 : Mood==WorkMood.Relieved ? 6 : MusicBlend>.35f ? 6 : Smile>.82f ? 4 : 0;
        if(nextMouth!=MouthIndex){previousMouth=MouthIndex;MouthIndex=nextMouth;mouthMix=0;}
        mouthMix=Math.Min(1,mouthMix+(float)Math.Max(0,dt)/.16f);
        Breath+=((float)Math.Sin(now*1.8)*(resting ? .65f : .85f)-Breath)*settle;Blink=resting ? 1 : BlinkAt(now);
        if(headPose!=null)headPose.Dispose();if(bodyPose!=null)bodyPose.Dispose();
        headPose=BuildHeadMatrix();bodyPose=BuildBodyMatrix();
        imageDirty=true;hitPixelsDirty=true;
    }
    void EnsureImage()
    {
        if(!imageDirty)return;
        using(var g=Graphics.FromImage(image))
        {
            g.Clear(Color.Transparent);g.TranslateTransform(Padding,Padding);
            DrawLayers(g);
        }
        imageDirty=false;
    }
    void DrawLayers(Graphics g,bool includeBackHair=true)
    {
        g.SmoothingMode=SmoothingMode.AntiAlias;g.InterpolationMode=InterpolationMode.Bilinear;g.PixelOffsetMode=PixelOffsetMode.HighQuality;
        if(includeBackHair)DrawBackHair(g);
        DrawHelmet(g,true);
        DrawNeck(g);
        DrawTorso(g);
        foreach(string name in TorsoFace)DrawPart(g,name);
        DrawBrows(g);
        DrawEye(g,0);DrawEye(g,1);
        DrawMouth(g);
        DrawCheekHair(g);
        // The tapered outer lash tips overlap the side locks;
        // the movable front bangs still naturally cover the upper lid.
        DrawEyelid(g,0);DrawEyelid(g,1);
        // Accessories physically tuck under the approaching crown. Alpha
        // fading left ghost hair visible through the metal on both passes.
        foreach(string name in FrontHair)
        {if(name=="hair-ahoge")DrawTuckedAccessory(g,name);else DrawPart(g,name);}
        DrawTuckedAccessory(g,"hair-clip");
        DrawWorkAccents(g);
        DrawHelmet(g,false);
        DrawMusic(g);
    }
    void DrawMusic(Graphics g)
    {
        if(MusicBlend<.01f)return;
        var saved=g.Save();g.MultiplyTransform(headPose);
        int alpha=(int)(255*MusicBlend*(1-MusicArmorBlend));
        int bandAlpha=(int)(255*HeadphoneBandBlend);
        float rise=(1-MusicBlend)*12;
        g.TranslateTransform(0,-rise);
        Color ink=Color.FromArgb(alpha,58,45,61),red=Color.FromArgb(alpha,164,49,70);
        // A silver outer arch and a soft dark underside follow the skull.
        using(var arch=new Pen(Color.FromArgb(bandAlpha,58,45,61),8))
        using(var silver=new Pen(Color.FromArgb(bandAlpha,234,229,239),4.5f))
        using(var seam=new Pen(Color.FromArgb(bandAlpha,164,49,70),1.2f))
        {
            arch.StartCap=arch.EndCap=silver.StartCap=silver.EndCap=LineCap.Round;
            g.DrawBezier(arch,70,124,58,33,232,28,239,126);
            g.DrawBezier(silver,70,124,58,33,232,28,239,126);
            g.DrawBezier(seam,69,105,76,39,222,34,237,108);
        }
        for(int side=0;side<2;side++)
        {
            float x=side==0 ? 59 : 228,y=109+(side==0 ? 0 : 3);
            using(var pad=BoundPetScene.Rounded(new RectangleF(x-3,y+3,19,35),7))
            using(var fill=new SolidBrush(ink))g.FillPath(fill,pad);
            using(var cup=BoundPetScene.Rounded(new RectangleF(x,y,17,34),6))
            using(var fill=new LinearGradientBrush(new RectangleF(x,y,17,34),Color.FromArgb(alpha,250,244,246),Color.FromArgb(alpha,181,174,194),0f))
            using(var edge=new Pen(ink,1.5f)) {g.FillPath(fill,cup);g.DrawPath(edge,cup);}
            using(var accentPen=new Pen(red,2))g.DrawLine(accentPen,x+5,y+8,x+5,y+25);
            using(var core=new SolidBrush(red))g.FillEllipse(core,x+8,y+13,5,7);
        }
        g.Restore(saved);
        saved=g.Save();g.MultiplyTransform(headPose);DrawArmoredMusic(g);g.Restore(saved);
        // Small hand-drawn notes stay inside the portrait/hit-test canvas.
        saved=g.Save();g.MultiplyTransform(headPose);
        for(int i=0;i<3;i++)
        {
            double phase=(time*.45+i/3.0)%1;
            float x=i%2==0 ? 43 : 258,y=91-(float)phase*29;
            int opacity=(int)(alpha*Math.Sin(phase*Math.PI)*.85);
            using(var note=new Pen(Color.FromArgb(opacity,164,49,70),1.8f))
            using(var fill=new SolidBrush(note.Color))
            {
                g.FillEllipse(fill,x,y,5,3.5f);g.DrawLine(note,x+4,y+1,x+4,y-10);
                g.DrawBezier(note,x+4,y-10,x+10,y-8,x+10,y-5,x+8,y-4);
            }
        }
        g.Restore(saved);
    }
    void DrawArmoredMusic(Graphics g)
    {
        float blend=MusicBlend*MusicArmorBlend;if(blend<.01f)return;
        int alpha=(int)(255*blend);
        float beat=(float)(.5+.5*Math.Sin(time*3.6)),energy=musicEnergy;
        // Compact audio housings attach to the temple carriages instead of
        // laying a second headband across the crown. Every detail inherits
        // its plate's deployment/retraction transform and the head pose.
        for(int side=0;side<2;side++)
        {
            float x=side==0 ? 78 : 226,y=108;
            var saved=g.Save();
            using(var pose=CurrentHelmetMatrix(side+1))g.MultiplyTransform(pose);
            using(var rim=new Pen(Color.FromArgb(alpha,28,27,31),1.5f))
            using(var shell=new SolidBrush(Color.FromArgb(alpha,237,237,239)))
            using(var pad=new SolidBrush(Color.FromArgb(alpha,39,37,43)))
            using(var ring=new Pen(Color.FromArgb((int)(alpha*(.45f+.45f*energy)),182,38,56),1.8f))
            {
                g.FillEllipse(shell,x-10,y-12,20,24);g.DrawEllipse(rim,x-10,y-12,20,24);
                g.FillEllipse(pad,x-7,y-8,14,16);
                g.DrawArc(ring,x-7,y-8,14,16,-100+beat*18,255);
                for(int bar=0;bar<3;bar++)
                {
                    float height=2+energy*(3+3*(float)(.5+.5*Math.Sin(time*5.2-bar*.9+side*.6)));
                    using(var led=new Pen(Color.FromArgb(alpha,244,110,125),1.4f))
                        g.DrawLine(led,x-3+bar*3,y-height/2,x-3+bar*3,y+height/2);
                }
            }
            // A restrained outward sound-wave pulse replaces floating notes.
            float wave=(float)((time*.6+side*.15)%1),radius=12+wave*5;
            using(var echo=new Pen(Color.FromArgb((int)(alpha*(1-wave)*energy*.55f),185,52,73),1))
                g.DrawArc(echo,x-radius,y-radius,radius*2,radius*2,side==0 ? 135 : -45,90);
            g.Restore(saved);

            // Tiny mirrored meters are printed on the moving cheek armor.
            float face=Smooth((helmet.Part(side+3)-.8f)/.2f);
            if(face<=0)continue;
            saved=g.Save();using(var pose=CurrentHelmetMatrix(side+3))g.MultiplyTransform(pose);
            for(int bar=0;bar<4;bar++)
            {
                float height=1.5f+energy*(2+3*(float)(.5+.5*Math.Sin(time*5.2-bar*.8)));
                float px=side==0 ? 113+bar*4 : 191-bar*4;
                using(var led=new Pen(Color.FromArgb((int)(alpha*face*.9f),181,37,54),1.6f))
                    g.DrawLine(led,px,125-height/2,px,125+height/2);
            }
            g.Restore(saved);
        }
    }
    void DrawTuckedAccessory(Graphics g,string name)
    {
        float tuck=name=="hair-clip" ? Smooth((HelmetBlend-.12f)/.28f) : HelmetHairCover;var saved=g.Save();
        using(var pose=LayerMatrix(name))g.MultiplyTransform(pose);
        if(name=="hair-ahoge")
        {g.TranslateTransform(0,76);g.ScaleTransform(1,1-.9f*tuck);g.TranslateTransform(0,-76);}
        else g.TranslateTransform(-24*tuck,-8*tuck);
        Rig.Parts[name].Draw(g);g.Restore(saved);
    }
    internal void DrawHelmet(Graphics g,bool rear)
    {
        if(HelmetBlend<=0)return;
        var saved=g.Save();g.MultiplyTransform(headPose);
        g.SetClip(new RectangleF(36,-8,232,183),CombineMode.Intersect);
        if(rear)
        {
            // Storage is inside the opaque scalp, not an oversized shell
            // behind it. Exposed rear corners looked like armor penetrating
            // the hair when a moving plate changed depth at the wrap edge.
            using(var scalp=new GraphicsPath())
            {scalp.AddEllipse(81,43,142,125);g.SetClip(scalp,CombineMode.Intersect);}
            DrawHelmetMechanism(g);g.Restore(saved);return;
        }
        // Visible armor always stays outside the scalp. Splitting one plate
        // between rear/front at a moving screen-space boundary sliced hair
        // across the metal rim, especially when reversing midway.
        foreach(int i in HelmetDrawOrder)
        {
            float progress=helmet.Part(i);
            if(progress<=0)continue;
            var part=g.Save();
            using(var pose=CurrentHelmetMatrix(i))g.MultiplyTransform(pose);
            var sprite=Rig.Parts[TypingHelmetAction.Parts[i]];
            // Stored armor must not appear in full behind the hair on the
            // first active frame. Each surface extends from its attachment,
            // using a growing footprint along the head's outer surface.
            using(var unfolding=HelmetDeploymentPath(i,progress,sprite.Bounds))g.SetClip(unfolding,CombineMode.Intersect);
            sprite.Draw(g);
            g.Restore(part);
        }
        DrawHelmetPower(g);
        g.Restore(saved);
    }
    internal static GraphicsPath HelmetDeploymentPath(int index,float progress,RectangleF bounds)
    {
        var path=new GraphicsPath();float p=Closed(progress);
        if(p<=0)return path;
        float cx=bounds.Left+bounds.Width/2,cy,rx,ry;
        if(index==3||index==4)
        {
            // Cheek armor feeds out of the temple housing toward the nose.
            float edge=bounds.Left+(bounds.Width+12)*p,bend=6*p;
            path.AddLine(bounds.Left-1,bounds.Top-1,edge-bend,bounds.Top-1);
            path.AddBezier(edge-bend,bounds.Top-1,edge+bend,bounds.Top+bounds.Height*.35f,
                edge+bend,bounds.Bottom,edge-bend,bounds.Bottom+1);
            path.AddLine(edge-bend,bounds.Bottom+1,bounds.Left-1,bounds.Bottom+1);path.CloseFigure();
            if(index==4)using(var mirror=new Matrix(-1,0,0,1,bounds.Left+bounds.Right,0))path.Transform(mirror);
            return path;
        }
        // Rounded growth follows the scalp: crown across the top, side armor
        // down from its hinge, chin up from the collar.
        cy=index==5 ? bounds.Bottom : bounds.Top;
        rx=bounds.Width*.9f*p;ry=bounds.Height*1.5f*p;
        path.AddEllipse(cx-rx,cy-ry,rx*2,ry*2);return path;
    }
    void DrawHelmetPower(Graphics g)
    {
        float power=Math.Max(0,Math.Min(1,(HelmetVisor-.92f)/.08f));if(power<=0)return;
        float pulse=helmet.Pulse,breath=.5f+.5f*(float)Math.Sin(time*2.4);
        float audio=MusicBlend*MusicArmorBlend*musicEnergy*(.65f+.35f*(float)Math.Sin(time*3.6));
        var eye=new[] {new PointF(95,89),new PointF(152,106),new PointF(210,89)};
        using(var glow=new Pen(Color.FromArgb((int)((20+15*breath+55*pulse+65*audio)*power),210,20,34),1.15f))
        using(var hot=new Pen(Color.FromArgb((int)(80*pulse*power),255,210,210),.35f))
        {glow.LineJoin=hot.LineJoin=LineJoin.Round;g.DrawLines(glow,eye);g.DrawLines(hot,eye);}
        // A short traveling lock highlight follows the V seam, then fades.
        if(pulse>.02f)
        {
            float t=(float)((time*1.9)%1),x=95+115*t,y=106-Math.Abs(t-.5f)*34;
            using(var spark=new SolidBrush(Color.FromArgb((int)(150*pulse*power),255,230,230)))g.FillEllipse(spark,x-1,y-1,2,2);
        }
    }
    Matrix CurrentHelmetMatrix(int index)
    {return HelmetPartMatrix(index,helmet.Part(0),helmet.Part(index==2||index==4 ? 2 : 1),helmet.Part(index));}
    internal static readonly int[] HelmetDrawOrder={3,4,1,2,0,5};
    static float Closed(float p){return Math.Max(0,Math.Min(1,p));}
    internal static Matrix HelmetCarrierMatrix(float progress)
    {
        // Keep the approaching crown outside the top of the hair. Bringing
        // the carrier up from inside the skull made its rim intersect the
        // scalp during both deployment and removal.
        var m=new Matrix();m.Translate(0,-10*(1-Closed(progress)));return m;
    }
    static Matrix TempleSlide(bool right,float progress)
    {
        float travel=1-Closed(progress);
        // Side housings approach from outside the hair silhouette. An inward
        // starting offset made the plates cut through the head as they opened.
        var m=new Matrix();m.Translate((right ? 1 : -1)*18*travel,8*travel);return m;
    }
    internal static Matrix HelmetPartMatrix(int index,float progress)
    {return HelmetPartMatrix(index,progress,progress,progress);}
    internal static Matrix HelmetPartMatrix(int index,float crown,float temple,float panel)
    {
        bool right=index==2||index==4;Matrix m;
        if(index==0)m=new Matrix();
        else if(index<=2)m=TempleSlide(right,temple);
        else if(index<=4)
        {
            // A face plate translates horizontally inside its parent's rail;
            // side-carriage motion is inherited without changing its shape.
            m=new Matrix();m.Translate((right ? 1 : -1)*38*(1-Closed(panel)),0);
            using(var parent=TempleSlide(right,temple))m.Multiply(parent,MatrixOrder.Append);
        }
        else {m=new Matrix();m.Translate(0,23*(1-Closed(panel)));}
        using(var carrier=HelmetCarrierMatrix(crown))m.Multiply(carrier,MatrixOrder.Append);
        return m;
    }
    static PointF HelmetPoint(Matrix matrix,PointF point)
    {var e=matrix.Elements;return new PointF(point.X*e[0]+point.Y*e[2]+e[4],point.X*e[1]+point.Y*e[3]+e[5]);}
    void DrawHelmetMechanism(Graphics g)
    {
        using(var carrier=HelmetCarrierMatrix(helmet.Part(0)))
        using(var dark=new Pen(Color.FromArgb(26,26,28),4.2f))
        using(var metal=new Pen(Color.FromArgb(166,166,166),1.6f))
        using(var piston=new Pen(Color.FromArgb(222,222,222),1.3f))
        using(var guide=new Pen(Color.FromArgb(225,225,225),.7f))
        {
            dark.StartCap=dark.EndCap=metal.StartCap=metal.EndCap=LineCap.Round;
            for(int side=0;side<2;side++)
            {
                bool right=side==1;float x=right ? 226 : 78,sign=right ? -1 : 1;
                var rail=new[] {new PointF(152+sign*-14,169),new PointF(x,151),new PointF(x,94),new PointF(152,66)};
                carrier.TransformPoints(rail);
                DrawExtendingRail(g,dark,rail,helmet.Part(0));DrawExtendingRail(g,metal,rail,helmet.Part(0));
                float sideProgress=Closed(helmet.Part(right ? 2 : 1));
                if(sideProgress<=0)continue;
                var mechanism=g.Save();
                // Pistons and white sliders emerge with their side housing,
                // rather than exposing the entire scaffold at activation.
                using(var unfolding=HelmetDeploymentPath(right ? 2 : 1,sideProgress,new RectangleF(x-32,82,64,70)))
                    g.SetClip(unfolding,CombineMode.Intersect);
                using(var temple=CurrentHelmetMatrix(right ? 2 : 1))
                using(var cheek=CurrentHelmetMatrix(right ? 4 : 3))
                {
                    PointF anchor=HelmetPoint(carrier,new PointF(x,94));
                    PointF elbow=HelmetPoint(temple,new PointF(x,94));
                    PointF trackStart=HelmetPoint(temple,new PointF(x-sign*12,119));
                    PointF trackEnd=HelmetPoint(temple,new PointF(x+sign*26,119));
                    PointF moving=HelmetPoint(cheek,new PointF(x+sign*26,119));
                    g.DrawLine(dark,anchor,elbow);g.DrawLine(piston,anchor,elbow);
                    g.DrawLine(dark,trackStart,trackEnd);g.DrawLine(piston,trackStart,trackEnd);
                    foreach(var point in new[] {elbow,moving})
                    {
                        g.FillRectangle(Brushes.DimGray,point.X-3,point.Y-2.5f,6,5);
                        g.DrawRectangle(guide,point.X-1.6f,point.Y-1.2f,3.2f,2.4f);
                    }
                }
                g.Restore(mechanism);
            }
        }
    }
    static void DrawExtendingRail(Graphics g,Pen pen,PointF[] points,float progress)
    {
        float length=0;for(int i=1;i<points.Length;i++)length+=Distance(points[i-1],points[i]);
        float remaining=length*Closed(progress);
        for(int i=1;i<points.Length&&remaining>0;i++)
        {
            PointF a=points[i-1],b=points[i];float segment=Distance(a,b),t=Math.Min(1,remaining/segment);
            g.DrawLine(pen,a,new PointF(a.X+(b.X-a.X)*t,a.Y+(b.Y-a.Y)*t));remaining-=segment;
        }
    }
    static float Distance(PointF a,PointF b)
    {float dx=b.X-a.X,dy=b.Y-a.Y;return (float)Math.Sqrt(dx*dx+dy*dy);}
    internal PointF SourcePoint(PointF p)
    {
        using(var m=p.Y>=175 ? BodyMatrix() : HeadMatrix()){m.Invert();var points=new[] {p};m.TransformPoints(points);return points[0];}
    }
    internal PointF WorldPoint(PointF source)
    {
        using(var m=source.Y>164 ? BodyMatrix() : HeadMatrix())
        {var p=new[] {source};m.TransformPoints(p);return ToWorld(p[0]);}
    }
    internal static PointF ToWorld(PointF p)
    {var r=BoundPetScene.CharacterBounds;return new PointF(r.X+p.X*r.Width/Width,r.Y+p.Y*r.Height/Height);}
    internal Matrix ToWorldMatrix(Matrix local)
    {
        var m=local.Clone();var r=BoundPetScene.CharacterBounds;m.Scale(r.Width/Width,r.Height/Height,MatrixOrder.Append);m.Translate(r.X,r.Y,MatrixOrder.Append);return m;
    }
    internal bool Hit(PointF local)
    {
        int x=(int)Math.Floor(local.X+Padding),y=(int)Math.Floor(local.Y+Padding);
        if(x<0||y<0||x>=image.Width||y>=image.Height)return false;
        EnsureImage();
        if(hitPixelsDirty)
        {
            var data=image.LockBits(new Rectangle(0,0,image.Width,image.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppPArgb);
            try{Marshal.Copy(data.Scan0,output,0,output.Length);}finally{image.UnlockBits(data);}
            hitPixelsDirty=false;
        }
        return ((uint)output[y*image.Width+x]>>24)>=32;
    }
    internal void DrawBehindDesk(Graphics g)
    {
        if(HairStyle!=PetHairStyles.Reference)return;
        var saved=g.Save();var r=BoundPetScene.CharacterBounds;
        g.TranslateTransform(r.X,r.Y);g.ScaleTransform(r.Width/Width,r.Height/Height);
        g.SmoothingMode=SmoothingMode.AntiAlias;g.InterpolationMode=InterpolationMode.Bilinear;g.PixelOffsetMode=PixelOffsetMode.HighQuality;
        DrawBackHair(g);g.Restore(saved);
    }
    internal void Draw(Graphics g,bool includeBackHair=true)
    {
        // Draw at the destination scale once. The old path composited a
        // whole intermediate portrait and bicubically rescaled it every
        // frame. Its alpha bitmap is now generated only for hit testing or
        // explicit previews, from exactly the same layer routine.
        var saved=g.Save();var r=BoundPetScene.CharacterBounds;
        g.TranslateTransform(r.X,r.Y);g.ScaleTransform(r.Width/Width,r.Height/Height);
        DrawLayers(g,includeBackHair);g.Restore(saved);
    }
    public void Dispose(){image.Dispose();torsoSilhouette.Dispose();lowerTorsoEdge.Dispose();if(headPose!=null)headPose.Dispose();if(bodyPose!=null)bodyPose.Dispose();}
}

internal static class LayeredRigChecks
{
    static void Check(bool value,string message){if(!value)throw new Exception("分层骨骼自检："+message);}
    static double Distance(PointF a,PointF b){return Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Y-b.Y)*(a.Y-b.Y));}
    static double Angle(Matrix matrix)
    {
        var e=matrix.Elements;return Math.Atan2(e[1],e[0]);
    }
    static PointF Transform(Matrix matrix,PointF p)
    {
        // GDI+ TransformPoints has a near-identity fast path that can omit
        // very small rotations. Check the actual affine coefficients here.
        var e=matrix.Elements;return new PointF(p.X*e[0]+p.Y*e[2]+e[4],p.X*e[1]+p.Y*e[3]+e[5]);
    }
    internal static void Run(BoundPetScene scene,KeyboardPetSprites sprites)
    {
        var rig=sprites.Rig;
        foreach(string name in new[] {"body","face","hair-cap","hair-fringe-left","hair-fringe-right","hair-ahoge","hair-clip",
            "eye-left-white","eye-left-iris","eye-right-white","eye-right-iris","eyebrows","cheeks","mouth-0","mouth-1","mouth-2","mouth-3","mouth-4","mouth-5","mouth-6","mouth-7"})
            Check(rig.Parts.ContainsKey(name),"独立 PNG 图层存在："+name);
        Check(rig.Parts.Values.All(p=>p.Bounds.Left>=0&&p.Bounds.Top>=0&&p.Bounds.Right<=300&&p.Bounds.Bottom<=229),"所有裁切图层保持统一画布内的注册偏移");
        Check(!rig.Parts.ContainsKey("hair-back-left")&&!rig.Parts.ContainsKey("hair-back-right"),"短发不加载两侧长后发");
        Check(rig.Parts["hair-cap"].Bounds.Bottom<170,"短发末端收在下颌附近，不延伸到肩膀和键盘");
        foreach(var p in new[] {new PointF(130,70),new PointF(145,70),new PointF(176,71),new PointF(184,76)})
            Check(rig.Parts["hair-cap"].Hit(p),"发根保留连续原画底层，没有硬裁切形成的横向虚线");
        // Probe the actual scarf alpha contour, including isolated upward
        // spikes that a centre-of-neck coverage check would miss.
        var torso=rig.Parts["body"];int previousTop=-1;
        int collarLeft=(int)((124-torso.Bounds.Left)*torso.Image.Width/torso.Bounds.Width);
        int collarRight=(int)((179-torso.Bounds.Left)*torso.Image.Width/torso.Bounds.Width);
        for(int x=collarLeft;x<=collarRight;x++)
        {
            int top=-1;
            for(int y=0;y<torso.Image.Height;y++)if(torso.Image.GetPixel(x,y).A>=160){top=y;break;}
            Check(top>=0,"围巾上沿没有透明缺口");
            if(previousTop>=0)Check(Math.Abs(top-previousTop)<=2,"围巾上沿连续，没有逐列抠图留下的竖刺与阶梯跳变");
            previousTop=top;
        }
        for(int eye=0;eye<2;eye++)
        {
            Check(rig.Eyelids[eye].Length==33,"左右眼分别包含 33 级真实眼睑帧");
            Check(rig.Eyelids[eye][32].Bounds.Height<8,"完全闭眼只保留单条眼睑，没有虹膜残缝或下眼线");
            var openLid=rig.Eyelids[eye][0].Bounds;
            Check(openLid.Height>rig.Eyelids[eye][32].Bounds.Height*1.5f,
                "睫毛轮廓随眨眼收拢，闭眼时没有残留外伸眼尾");
            var lid=rig.Eyelids[eye][0];
            int lashColumn=(int)(((eye==0 ? 119 : 186)-lid.Bounds.X)*lid.Image.Width/lid.Bounds.Width),lashWeight=0;
            for(int y=0;y<lid.Image.Height;y++)if(lid.Image.GetPixel(lashColumn,y).A>=180)lashWeight++;
            Check(lashWeight>=5,"睁眼上睫毛在显示纹理中保持加粗的连续实线");
            string side=eye==0 ? "left" : "right";
            foreach(PointF gaze in new[] {new PointF(-1,0),PointF.Empty,new PointF(1,0),new PointF(0,-1),new PointF(0,1),new PointF(.707f,.707f)})
            using(var sample=new Bitmap(300,229,PixelFormat.Format32bppArgb))
            {
                using(var g=Graphics.FromImage(sample))
                using(var aperture=LivingPetPortrait.EyeAperture(eye==0,0))
                {
                    g.SetClip(aperture);rig.Parts["eye-"+side+"-white"].Draw(g);
                    g.TranslateTransform(gaze.X*4.2f,gaze.Y*2.5f);rig.Parts["eye-"+side+"-iris"].Draw(g);
                }
                int white=0,red=0;
                for(int y=110;y<155;y++)for(int x=95;x<210;x++)
                {
                    Color c=sample.GetPixel(x,y);if(c.A<200)continue;
                    if(c.R>210&&c.G>205&&c.B>200)white++;
                    else if(c.R>c.G*1.35&&c.R>c.B*1.25)red++;
                }
                Check(red>200&&white>red*.45,"大面积红宝石虹膜与清晰眼白在左右张望时保持可见："+side);
            }
        }
        Check(!scene.Hit(new PointF(432,375),false,true)&&!scene.Hit(new PointF(444,334),false,true),"人物与桌面之外保持透明并允许点击穿透");
        var portrait=scene.Portrait;
        // Check the visible jaw in the final composite, including during
        // activity changes. Merely testing for opaque skin misses lost ink.
        for(int mode=0;mode<4;mode++)for(int frame=0;frame<=32;frame++)
        {
            portrait.Update(frame, .1,mode==3,mode==1,mode==2);
            var headRoot=new[] {new PointF(150,171)};
            var collarRoot=new[] {new PointF(150,173)};
            var jaw=new[] {new PointF(151,162),new PointF(138.67f,161.11f),new PointF(163.59f,161.12f)};
            using(var head=portrait.HeadMatrix()){head.TransformPoints(headRoot);head.TransformPoints(jaw);}
            using(var body=portrait.BodyMatrix())body.TransformPoints(collarRoot);
            Check(Distance(headRoot[0],collarRoot[0])<.2,"头部与围巾的实际绘制颈根在亚像素精度内重合");
            using(var head=portrait.HeadMatrix())using(var body=portrait.BodyMatrix())
            {
                Check(Distance(Transform(head,new PointF(150,171)),Transform(body,new PointF(150,173)))<.002,
                    "头部与围巾共用精确颈根，没有独立平移造成的拉伸");
                Check(Math.Abs(Angle(head)*.75-Angle(body))<.0001,
                    "身体承担四分之三的整体转动，头部继承躯干后只小幅弯颈");
            }
            foreach(var p in jaw)
            {
                bool ink=false;
                int cx=(int)Math.Round(p.X)+LivingPetPortrait.Padding,cy=(int)Math.Round(p.Y)+LivingPetPortrait.Padding;
                for(int y=cy-1;y<=cy+1;y++)for(int x=cx-1;x<=cx+1;x++)
                {
                    Color c=portrait.Image.GetPixel(x,y);
                    if(c.A>=200&&c.R<160&&c.G<125&&c.B<125)ink=true;
                }
                Check(ink,"待机、打字、鼠标跟随和休息时，下颌实线随脸部保持连续");
            }
            if(mode<3&&frame%4==0)
            {
                // Brows must remain visible in the final hair composite and
                // separate from the upper lid, including during head motion.
                var brows=new[] {new PointF(116,102.5f),new PointF(179,106.5f)};
                using(var head=portrait.HeadMatrix())head.TransformPoints(brows);
                foreach(var p in brows)
                {
                    int cx=(int)Math.Round(p.X)+LivingPetPortrait.Padding,cy=(int)Math.Round(p.Y)+LivingPetPortrait.Padding;
                    bool ink=false;
                    for(int y=cy-1;y<=cy+1;y++)for(int x=cx-1;x<=cx+1;x++)
                    {
                        Color c=portrait.Image.GetPixel(x,y);
                        if(c.A>=200&&c.R<180&&c.G<145&&c.B<150)ink=true;
                    }
                    Check(ink,"两眼上方的独立眉毛在刘海遮挡后仍然清晰可见");
                }
            }
        }
        // Probe the actual rendered bridge during a complete motion cycle.
        for(int frame=0;frame<=80;frame++)
        {
            portrait.Update(frame*.4,.4,false,true,false);
            var jaw=new[] {new PointF(151,160)};
            var collar=new[] {new PointF(151,173)};
            using(var head=portrait.HeadMatrix())head.TransformPoints(jaw);
            using(var body=portrait.BodyMatrix())body.TransformPoints(collar);
            int warmPixels=0;
            for(int step=0;step<=12;step++)
            {
                float t=step/12f;
                int x=(int)Math.Round(jaw[0].X*(1-t)+collar[0].X*t)+LivingPetPortrait.Padding;
                int y=(int)Math.Round(jaw[0].Y*(1-t)+collar[0].Y*t)+LivingPetPortrait.Padding;
                Color c=portrait.Image.GetPixel(x,y);
                // Bicubic sampling lightens skin beside the dark collar edge;
                // retain its warm hue while excluding neutral white hair/gaps.
                bool skinOrScarf=c.R>=c.G+4&&c.R>=c.B+12;
                // Include antialiased edge pixels along the scarf outline.
                bool outline=c.R<225&&c.G<215&&c.B<215;
                Check(c.A>=240,"摇头点头时下巴到围巾之间没有透明断层");
                if(skinOrScarf||outline)warmPixels++;
            }
            // Allow at most three filtered edge samples at the jaw/collar,
            // while rejecting a visible stretch of neutral white hair.
            Check(warmPixels>=10,"脖子连接处保持皮肤与围巾颜色，没有白色空档："+frame);
        }
        // Sample consecutive frames through idle, typing, sleep and mouse
        // activity. Static attachment checks alone do not catch pose snaps.
        var previous=new PointF[0];
        for(int frame=0;frame<=240;frame++)
        {
            double now=9+frame/60.0;
            portrait.Update(now,1/60.0,frame>=120&&frame<180,frame>=60&&frame<120,frame>=180);
            var headPoints=new[] {new PointF(150,20),new PointF(151,162)};
            var shoulders=new[] {BoundPetScene.LeftShoulderSource,BoundPetScene.RightShoulderSource};
            using(var head=portrait.HeadMatrix())head.TransformPoints(headPoints);
            using(var body=portrait.BodyMatrix())body.TransformPoints(shoulders);
            var points=new[] {headPoints[0],headPoints[1],shoulders[0],shoulders[1]};
            if(frame>0)for(int i=0;i<points.Length;i++)
                Check(Distance(previous[i],points[i])<2,"待机、打字、休息与鼠标活动切换时头颈和肩膀连续过渡");
            previous=points;
        }
        portrait.Update(0,0,false,false,false);
        Check(!rig.Parts.ContainsKey("sword")&&!rig.Parts.ContainsKey("sword-strap"),"背剑和背带不再加载");
        portrait.Update(0,0,false,false,false);
    }
}
