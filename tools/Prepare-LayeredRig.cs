using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;

// Registered cutouts of the existing artwork. Every part stays on the same
// 900 x 687 canvas; coordinates and joints below are in 300 x 229 rig units.
internal static class PrepareLayeredRig
{
    const int W=900,H=687;
    static string output;
    static Bitmap clean,original;
    static readonly List<string> manifest=new List<string>();
    static float Clamp(float x) {return Math.Max(0,Math.Min(1,x));}
    static Bitmap Canvas() {return new Bitmap(W,H,PixelFormat.Format32bppArgb);}
    static Graphics Painter(Bitmap bitmap)
    {
        var g=Graphics.FromImage(bitmap);g.ScaleTransform(3,3);g.SmoothingMode=SmoothingMode.AntiAlias;
        g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.PixelOffsetMode=PixelOffsetMode.HighQuality;
        return g;
    }
    static int[] Read(Bitmap image)
    {
        var data=image.LockBits(new Rectangle(0,0,W,H),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
        var result=new int[W*H];try {Marshal.Copy(data.Scan0,result,0,result.Length);}finally {image.UnlockBits(data);}return result;
    }
    static Bitmap Pixels(int[] pixels)
    {
        var image=Canvas();var data=image.LockBits(new Rectangle(0,0,W,H),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
        try {Marshal.Copy(pixels,0,data.Scan0,pixels.Length);}finally {image.UnlockBits(data);}return image;
    }
    static GraphicsPath Polygon(params float[] xy)
    {
        var points=new PointF[xy.Length/2];for(int i=0;i<points.Length;i++)points[i]=new PointF(xy[i*2],xy[i*2+1]);
        var path=new GraphicsPath();path.AddPolygon(points);return path;
    }
    static byte[] Mask(GraphicsPath path,float overlap=0)
    {
        using(var image=Canvas())
        {
            using(var g=Painter(image))
            {
                g.FillPath(Brushes.White,path);
                if(overlap>0)using(var edge=new Pen(Color.White,overlap*2)){edge.LineJoin=LineJoin.Round;g.DrawPath(edge,path);}
            }
            var p=Read(image);var result=new byte[p.Length];for(int i=0;i<p.Length;i++)result[i]=(byte)((uint)p[i]>>24);return result;
        }
    }
    static void Save(string name,Bitmap image,string parent,float px,float py,int z)
    {
        string file=name+".png";image.Save(Path.Combine(output,file),ImageFormat.Png);
        manifest.Add(String.Join("\t",new[] {name,file,parent,px.ToString(CultureInfo.InvariantCulture),py.ToString(CultureInfo.InvariantCulture),z.ToString()}));
    }
    static int Alpha(int color,float coverage)
    {
        int a=(int)(((uint)color>>24)*Clamp(coverage));return a==0 ? 0 : (color&0xFFFFFF)|(a<<24);
    }
    static void Cut(string name,int[] source,byte[] mask,string parent,float px,float py,int z)
    {
        var p=new int[source.Length];for(int i=0;i<p.Length;i++)p[i]=Alpha(source[i],mask[i]/255f);
        using(var image=Pixels(p))Save(name,image,parent,px,py,z);
    }
    static Bitmap LoadCell(string path)
    {
        using(var atlas=new Bitmap(path))using(var cell=atlas.Clone(new Rectangle(0,0,atlas.Width/2,atlas.Height),PixelFormat.Format32bppArgb))
        {var image=Canvas();using(var g=Graphics.FromImage(image)){g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(cell,new Rectangle(0,0,W,H));}return image;}
    }
    static GraphicsPath CollarEdge(float offset)
    {
        // Follow the original close-fitting collar: only the centre dips
        // around the narrow neck, rather than opening across the whole jaw.
        var edge=new GraphicsPath();
        edge.AddBezier(116,161+offset,128,159.5f+offset,135,161.3f+offset,140,162.2f+offset);
        edge.AddBezier(140,162.2f+offset,144,165+offset,146,169+offset,151,168.8f+offset);
        edge.AddBezier(151,168.8f+offset,158,169+offset,163,165.8f+offset,168,162.8f+offset);
        edge.AddBezier(168,162.8f+offset,176,162.2f+offset,182,160.2f+offset,188,161.2f+offset);
        return edge;
    }
    static void PrepareBodyAndHair()
    {
        var source=Read(clean);var orig=Read(original);
        var face=new byte[source.Length];var hair=new byte[source.Length];var body=new byte[source.Length];
        byte[] lowerFace;
        using(var jaw=new GraphicsPath())
        {
            // Only skin and the old baked jaw occupy this region. Leave
            // the pointed side locks outside it, but do not let fragments
            // of the old chin travel with independently swaying back hair.
            jaw.AddBezier(107,149,119,155,137,158,151,158);
            jaw.AddBezier(151,158,168,158,187,153,197,148);
            jaw.AddBezier(197,148,193,159,169,164,151,164);
            jaw.AddBezier(151,164,129,164,109,159,107,149);
            jaw.CloseFigure();lowerFace=Mask(jaw);
        }
        // A continuous neckline replaces the column-by-column red threshold,
        // which retained isolated dark spikes and stair steps in the collar.
        // The fitted curve follows the scarf, leaving the neck to its rig.
        byte[] garmentMask;
        using(var garment=new GraphicsPath())
        {
            using(var edge=CollarEdge(0))garment.AddPath(edge,false);
            garment.AddBezier(188,161.2f,188,163,187,166,187,168);
            garment.AddBezier(187,168,193,170,194,177,187,180);
            garment.AddBezier(187,180,181,185,179,193,177,198);
            garment.AddBezier(177,198,179,202,185,208,189,211);
            garment.AddBezier(189,211,180,222,127,222,117,212);
            garment.AddBezier(117,212,123,205,129,200,128,196);
            garment.AddBezier(128,196,126,188,121,183,117,180);
            garment.AddBezier(117,180,112,178,112,171,116,168);
            garment.AddBezier(116,168,118,166,116,163,116,161);garment.CloseFigure();
            garmentMask=Mask(garment);
        }
        for(int y=0;y<H;y++)for(int x=0;x<W;x++)
        {
            int i=y*W+x,c=source[i],r=(c>>16)&255,green=(c>>8)&255,b=c&255;float xx=x/3f,yy=y/3f;
            bool faceArea=xx>94&&xx<211&&yy>83&&yy<166;
            float skin=faceArea&&green>170&&b>145 ? Clamp((r-b-4)/13f)*Clamp((r-165)/45f) : 0;
            face[i]=(byte)(255*skin);
            body[i]=garmentMask[i];
            // Exclude the actual curved clothing silhouette, not a rectangle
            // spanning the side locks. White hair below either cheek belongs
            // to the head; previously it was cut off and scaled with the body.
            bool oldNeck=xx>134&&xx<170&&yy>158&&yy<177;
            hair[i]=oldNeck ? (byte)0 : (byte)(255*(1-skin)*(1-lowerFace[i]/255f)*(1-garmentMask[i]/255f));
            // Keep the exact original outer alpha; faint generated background is never imported.
            source[i]=(source[i]&0xFFFFFF)|(orig[i]&unchecked((int)0xFF000000));
        }
        using(var torso=Pixels(CutPixels(orig,body)))
        {
            using(var g=Painter(torso))
            using(var edge=CollarEdge(0))
            using(var band=new GraphicsPath())
            {
                band.AddPath(edge,false);
                using(var inside=CollarEdge(1.2f)){inside.Reverse();band.AddPath(inside,true);}band.CloseFigure();
                using(var fill=new LinearGradientBrush(new RectangleF(116,160,72,15),Color.FromArgb(171,15,32),Color.FromArgb(232,26,39),90))g.FillPath(fill,band);
                using(var ink=new Pen(Color.FromArgb(53,27,35),.8f))
                {ink.StartCap=ink.EndCap=LineCap.Round;g.DrawPath(ink,edge);}
            }
            Save("body",torso,"body",150,190,20);
        }
        using(var skin=Canvas())
        {
            using(var g=Painter(skin))
            using(var contour=new GraphicsPath())
            {
                // The broad, shallow jaw follows the original artwork;
                // an ellipse pinches the cheeks inside the side-hair tips.
                // The forehead is backing only, never a second hairline.
                contour.AddBezier(99,122,102,96,119,76,151,76);
                contour.AddBezier(151,76,181,76,203,96,207,128);
                contour.AddBezier(207,128,203,145,200,150,189,155);
                contour.AddBezier(189,155,179,160,162,162,151,162);
                contour.AddBezier(151,162,134,162,115,158,108,151);
                contour.AddBezier(108,151,102,146,100,136,99,122);contour.CloseFigure();
                using(var fill=new LinearGradientBrush(new RectangleF(98,76,111,88),Color.FromArgb(255,237,225),Color.FromArgb(255,231,221),90))g.FillPath(fill,contour);
                // Preserve the source skin's warm shading at the bangs.
                // Features are absent from this clean plate; eyes and lips
                // remain separate animated layers.
                var saved=g.Save();g.SetClip(contour);
                using(var shading=Pixels(CutPixels(source,face)))g.DrawImage(shading,new RectangleF(0,0,300,229));
                g.Restore(saved);
                using(var jaw=new GraphicsPath())
                using(var edge=new Pen(Color.FromArgb(66,38,34),1.25f))
                {
                    jaw.AddBezier(99,125,100,137,102,146,108,151);
                    jaw.AddBezier(108,151,115,158,134,162,151,162);
                    jaw.AddBezier(151,162,162,162,179,160,189,155);
                    jaw.AddBezier(189,155,200,150,203,145,207,128);
                    edge.StartCap=edge.EndCap=LineCap.Round;g.DrawPath(edge,jaw);
                }
            }
            Save("face",skin,"head",150,164,30);
        }
        // Cheek-framing locks belong to the skull. Start independent sway
        // below the jaw, with an overlap hidden in the longer back hair;
        // otherwise the cheek tips slide against the face and draw twice.
        var backL=new byte[hair.Length];var backR=new byte[hair.Length];var cap=new byte[hair.Length];
        for(int y=0;y<H;y++)for(int x=0;x<W;x++)
        {
            int i=y*W+x;float yy=y/3f,xx=x/3f;
            float lower=Clamp((yy-156)/12);
            if(xx<150)backL[i]=(byte)(hair[i]*lower);else backR[i]=(byte)(hair[i]*lower);
            cap[i]=(byte)(hair[i]*(1-Clamp((yy-168)/12)));
        }
        Cut("hair-back-left",source,backL,"head",83,163,0);
        Cut("hair-back-right",source,backR,"head",219,163,1);
        byte[] ahoge,fringeL,fringeR;
        using(var path=new GraphicsPath())
        {
            path.AddBezier(94,34,92,10,128,-4,148,5);path.AddBezier(148,5,165,10,166,29,164,43);
            path.AddLine(164,43,159,39);path.AddBezier(159,39,151,13,124,9,94,34);path.CloseFigure();ahoge=Mask(path);
        }
        using(var path=Polygon(121,70,151,68,169,87,165,131,162,125,164,136,145,130,129,117,122,94))fringeL=Mask(path);
        using(var path=Polygon(170,66,187,70,204,92,207,123,192,112,181,99,174,83))fringeR=Mask(path);
        var ahogeMask=new byte[hair.Length];var leftMask=new byte[hair.Length];var rightMask=new byte[hair.Length];
        for(int i=0;i<hair.Length;i++)
        {
            float yy=i/W/3f;
            float leftRoot=Clamp((yy-78)/18),rightRoot=Clamp((yy-77)/16);
            leftRoot=leftRoot*leftRoot*(3-2*leftRoot);rightRoot=rightRoot*rightRoot*(3-2*rightRoot);
            ahogeMask[i]=(byte)(hair[i]*ahoge[i]/255);
            leftMask[i]=(byte)(hair[i]*fringeL[i]/255*leftRoot);rightMask[i]=(byte)(hair[i]*fringeR[i]/255*rightRoot);
            // Keep the original painted roots intact on the scalp, with a
            // feathered overlap below them. Hard polygon cuts at y=66..70
            // left the dotted transverse seams visible on the white hair.
            float leftCut=Clamp((yy-96)/8),rightCut=Clamp((yy-93)/8);
            leftCut=leftCut*leftCut*(3-2*leftCut);rightCut=rightCut*rightCut*(3-2*rightCut);
            cap[i]=(byte)(cap[i]*(1-ahoge[i]/255f)*(1-fringeL[i]/255f*leftCut)*(1-fringeR[i]/255f*rightCut));
        }
        // Root backing is hidden under the movable locks, keeping small turns
        // from exposing transparent cracks through the scalp.
        using(var scalp=Canvas())
        {
            using(var g=Painter(scalp))using(var fill=new SolidBrush(Color.FromArgb(250,249,253)))
            {
                using(var backing=Polygon(124,70,163,70,164,101,129,102))g.FillPath(fill,backing);
                using(var backing=Polygon(173,67,185,72,196,94,179,92))g.FillPath(fill,backing);
            }
            using(var capImage=Pixels(CutPixels(source,cap)))
            using(var g=Graphics.FromImage(scalp))g.DrawImageUnscaled(capImage,0,0);
            Save("hair-cap",scalp,"head",150,66,50);
        }
        Cut("hair-fringe-left",source,leftMask,"head",140,77,51);
        Cut("hair-fringe-right",source,rightMask,"head",177,74,52);
        Cut("hair-ahoge",source,ahogeMask,"head",162,40,55);
    }
    static int[] CutPixels(int[] source,byte[] mask)
    {var result=new int[source.Length];for(int i=0;i<result.Length;i++)result[i]=Alpha(source[i],mask[i]/255f);return result;}
    static GraphicsPath Aperture(bool left,float close)
    {
        float x=left ? 97 : 164,y=left ? 111 : 116,corner=14+7*close,upper=-4+21*close,lower=41-24*close;
        var p=new GraphicsPath();p.AddBezier(x,y+corner,x+9,y+upper,x+35,y+upper,x+44,y+corner);
        p.AddBezier(x+44,y+corner,x+41,y+lower,x+3,y+lower,x,y+corner);p.CloseFigure();return p;
    }
    static void PrepareEyesAndMouth()
    {
        for(int eye=0;eye<2;eye++)
        {
            bool left=eye==0;string side=left ? "left" : "right";float ox=left ? 97 : 164,oy=left ? 111 : 116;
            using(var white=Canvas())
            {
                using(var g=Painter(white))using(var aperture=Aperture(left,0))
                using(var fill=new LinearGradientBrush(new RectangleF(ox,oy,44,35),Color.FromArgb(240,226,234),Color.FromArgb(255,253,247),90))g.FillPath(fill,aperture);
                Save("eye-"+side+"-white",white,"head",ox+22,oy+18,35);
            }
            using(var iris=Canvas())
            {
                using(var g=Painter(iris))
                {
                    // Broad ruby irises, round pupils and layered reflections
                    // give the enlarged eyes depth at desktop display size.
                    g.TranslateTransform(ox+9,oy+2);
                    using(var fill=new LinearGradientBrush(new RectangleF(0,0,26,33),Color.Black,Color.White,90))
                    {
                        fill.InterpolationColors=new ColorBlend {
                            Positions=new[] {0f,.35f,.72f,1f},
                            Colors=new[] {Color.FromArgb(65,20,42),Color.FromArgb(148,36,69),Color.FromArgb(230,82,110),Color.FromArgb(255,172,159)}};
                        g.FillEllipse(fill,0,0,26,33);
                    }
                    using(var rim=new Pen(Color.FromArgb(105,31,54),.9f))g.DrawEllipse(rim,0,0,26,33);
                    using(var fiber=new Pen(Color.FromArgb(115,255,198,171),.55f))
                    for(int i=1;i<=12;i++)
                    {
                        double a=i*Math.PI/13;
                        g.DrawLine(fiber,13+(float)Math.Cos(a)*7.8f,17+(float)Math.Sin(a)*10,
                            13+(float)Math.Cos(a)*10.8f,17+(float)Math.Sin(a)*13);
                    }
                    using(var glow=new Pen(Color.FromArgb(210,255,208,177),1.25f))g.DrawArc(glow,3,19,20,10,12,156);
                    using(var pupil=new SolidBrush(Color.FromArgb(52,18,38)))g.FillEllipse(pupil,9,6,8,17);
                    using(var highlight=new SolidBrush(Color.FromArgb(255,255,251)))
                    {
                        g.FillEllipse(highlight,4,4,6.2f,7.2f);
                        g.FillEllipse(highlight,18.5f,22,3.3f,3.3f);
                        g.FillEllipse(highlight,19,8,1.7f,1.7f);
                        using(var sparkle=Polygon(7,24,7.7f,25.8f,9.5f,26.5f,7.7f,27.2f,7,29,6.3f,27.2f,4.5f,26.5f,6.3f,25.8f))g.FillPath(highlight,sparkle);
                    }
                }
                Save("eye-"+side+"-iris",iris,"head",ox+22,oy+18,36);
            }
            Directory.CreateDirectory(Path.Combine(output,"eyelid-"+side));
            for(int step=0;step<=32;step++)using(var lid=Canvas())
            {
                float close=step/32f,open=1-close,corner=14+7*close,upper=-4+21*close;
                using(var g=Painter(lid))
                using(var band=new GraphicsPath())
                using(var ink=new LinearGradientBrush(new RectangleF(-4,-9,54,52),Color.FromArgb(71,40,50),Color.FromArgb(104,53,64),90))
                {
                    g.TranslateTransform(ox,oy);
                    // A fuller upper lash band tapers into the inner corner
                    // and short lifted outer tip. Weight fades continuously
                    // into a single closed lid through all 33 blink frames.
                    if(left){g.TranslateTransform(44,0);g.ScaleTransform(-1,1);}
                    float inner=-.4f*open;
                    band.AddBezier(inner,corner+.1f*open,9,upper-1.1f-3.3f*open,
                        35,upper-1.1f-4.1f*open,44-2*open,corner-4.2f*open);
                    band.AddBezier(44-2*open,corner-4.2f*open,44+.5f*open,corner-2.6f*open,
                        44+2.1f*open,corner-2.6f*open,44+3.7f*open,corner-3.4f*open);
                    band.AddBezier(44+3.7f*open,corner-3.4f*open,44+1.2f*open,corner+.5f*open,
                        44-.6f*open,corner+1.3f*open,44-1.5f*open,corner+.3f*open);
                    band.AddBezier(44-1.5f*open,corner+.3f*open,35,upper+.2f,9,upper+.2f,inner,corner+.1f*open);
                    band.CloseFigure();g.FillPath(ink,band);
                }
                lid.Save(Path.Combine(output,"eyelid-"+side,step.ToString("D2")+".png"),ImageFormat.Png);
            }
            manifest.Add("eyelid-"+side+"\teyelid-"+side+"/{00..32}.png\thead\t"+(ox+22)+"\t"+(oy+18)+"\t37");
        }
        using(var brows=Canvas())
        {
            using(var g=Painter(brows))
            using(var ink=new SolidBrush(Color.FromArgb(112,82,91)))
            using(var left=new GraphicsPath())
            using(var right=new GraphicsPath())
            {
                // Separate tapered brows sit above the open eyelids and stay
                // attached to the head throughout blinking. Hair masks them.
                left.AddBezier(101,106,109,100,124,100,135,106);
                left.AddBezier(135,106,123,103,111,103,101,107);left.CloseFigure();
                right.AddBezier(168,110,176,103,191,104,201,110);
                right.AddBezier(201,110,189,106,178,106,168,111);right.CloseFigure();
                g.FillPath(ink,left);g.FillPath(ink,right);
            }
            Save("eyebrows",brows,"head",150,105,33);
        }
        // Registered at one lip centre: relaxed, thinking, focused, determined,
        // delighted, energetic, contented and sleeping. Keep every contour
        // above the jaw and avoid the previous deep U-shaped smile.
        for(int i=0;i<8;i++)using(var mouth=Canvas())
        {
            using(var g=Painter(mouth))
            using(var pen=new Pen(Color.FromArgb(146,66,79),1.25f))
            {
                g.TranslateTransform(150,153);pen.StartCap=pen.EndCap=LineCap.Round;
                if(i==0)
                {
                    g.DrawBezier(pen,-5,-.4f,-3,2,-1,2,0,.6f);
                    g.DrawBezier(pen,0,.6f,1,2,3,2,5,-.4f);
                }
                else if(i==1)
                {
                    g.DrawBezier(pen,-4,.4f,-1,-1.6f,1,1.8f,4,-.2f);
                    using(var lip=new Pen(Color.FromArgb(100,195,112,122),.7f))g.DrawBezier(lip,-1,3,0,3.3f,1,3.3f,2,2.8f);
                }
                else if(i==2)g.DrawBezier(pen,-4.5f,.5f,-1,.1f,1,.1f,4.5f,-.2f);
                else if(i==3)g.DrawBezier(pen,-5,.6f,-1,2.6f,2.8f,.7f,5.1f,-1);
                else if(i==6)g.DrawBezier(pen,-4.7f,-.1f,-2.2f,2.7f,2.2f,2.7f,4.7f,-.1f);
                else if(i==7)g.DrawBezier(pen,-3.5f,0,-1,1.1f,1,1.1f,3.5f,0);
                else using(var shape=new GraphicsPath())
                {
                    if(i==4)
                    {
                        shape.AddBezier(-6,-2,-3,-.5f,3,-.5f,6,-2);
                        shape.AddBezier(6,-2,5,8,-5,8,-6,-2);shape.CloseFigure();
                    }
                    else
                    {
                        shape.AddBezier(-6.2f,-1.7f,-2,-.3f,2,-.3f,6.2f,-1.7f);
                        shape.AddBezier(6.2f,-1.7f,4.6f,6.2f,-4.2f,6.8f,-6.2f,-1.7f);shape.CloseFigure();
                    }
                    using(var fill=new SolidBrush(Color.FromArgb(117,43,61)))g.FillPath(fill,shape);
                    var saved=g.Save();g.SetClip(shape);
                    if(i==4||i==5)using(var teeth=new SolidBrush(Color.FromArgb(255,247,235)))
                        g.FillRectangle(teeth,-8,-3,16,i==5 ? 3.4f : 3.8f);
                    using(var tongue=new SolidBrush(Color.FromArgb(233,126,143)))g.FillEllipse(tongue,-3,i==5 ? 2.5f : 3,7,4);
                    g.Restore(saved);g.DrawPath(pen,shape);
                }
            }
            Save("mouth-"+i,mouth,"head",150,153,40);
        }
        using(var cheeks=Canvas())
        {
            using(var g=Painter(cheeks))foreach(var r in new[] {new RectangleF(98,141,16,8),new RectangleF(195,145,15,7)})
            using(var p=new GraphicsPath())
            {p.AddEllipse(r);using(var blush=new PathGradientBrush(p)){blush.CenterColor=Color.FromArgb(45,255,112,131);blush.SurroundColors=new[] {Color.FromArgb(0,255,160,165)};g.FillPath(blush,p);}}
            Save("cheeks",cheeks,"head",150,145,34);
        }
        PrepareHairClip();
    }
    static void PrepareHairClip()
    {
        using(var clip=Canvas())
        {
            using(var g=Painter(clip))
            {
                g.TranslateTransform(172,55);g.RotateTransform(16.7f);
                // An open metal barrette: the name itself forms the front,
                // with transparent gaps between individual enamel letters.
                // The narrow spring jaw and hinge remain visible underneath.
                using(var jaw=new GraphicsPath())
                {
                    jaw.AddBezier(-1,2.2f,11,4.6f,44,5.5f,59,3.2f);
                    jaw.AddBezier(59,3.2f,62,2.7f,62,1,59.5f,.7f);
                    using(var edge=new Pen(Color.FromArgb(13,14,18),2.1f))
                    using(var silver=new Pen(Color.FromArgb(40,43,50),1.35f))
                    using(var shine=new Pen(Color.FromArgb(105,110,121),.45f))
                    {
                        edge.StartCap=edge.EndCap=silver.StartCap=silver.EndCap=LineCap.Round;
                        g.DrawPath(edge,jaw);g.DrawPath(silver,jaw);
                        g.DrawBezier(shine,1,2.1f,17,4,44,4.8f,58,3);
                    }
                }
                using(var metal=new LinearGradientBrush(new RectangleF(-3,-1,5,6),Color.FromArgb(74,79,89),Color.FromArgb(17,19,24),90))
                using(var edge=new Pen(Color.FromArgb(10,12,16),.65f))
                {
                    g.FillEllipse(metal,-3,-.7f,5,5);g.DrawEllipse(edge,-3,-.7f,5,5);
                    using(var inset=new SolidBrush(Color.FromArgb(15,17,22)))g.FillEllipse(inset,-1.5f,.8f,2,2);
                    using(var glint=new SolidBrush(Color.FromArgb(119,126,139)))g.FillEllipse(glint,-1.25f,.9f,.6f,.6f);
                }
                using(var letters=new GraphicsPath())
                using(var family=new FontFamily("Segoe UI"))
                using(var format=(StringFormat)StringFormat.GenericTypographic.Clone())
                {
                    letters.AddString("ProkyHoof",family,(int)FontStyle.Bold,12,PointF.Empty,format);
                    RectangleF bounds=letters.GetBounds();
                    using(var fit=new Matrix())
                    {
                        fit.Translate(-bounds.X,-bounds.Y,MatrixOrder.Append);
                        fit.Scale(54/bounds.Width,8.8f/bounds.Height,MatrixOrder.Append);
                        fit.Translate(3.7f,-6.2f,MatrixOrder.Append);letters.Transform(fit);
                    }
                    using(var shadow=(GraphicsPath)letters.Clone())
                    using(var offset=new Matrix())
                    using(var dark=new Pen(Color.FromArgb(13,15,20),1.05f))
                    {
                        offset.Translate(.25f,.65f);shadow.Transform(offset);
                        dark.LineJoin=LineJoin.Round;g.DrawPath(dark,shadow);
                    }
                    using(var rim=new Pen(Color.FromArgb(12,14,18),1.3f))
                    using(var silver=new Pen(Color.FromArgb(117,123,135),.7f))
                    using(var enamel=new LinearGradientBrush(new RectangleF(3,-7,55,11),Color.FromArgb(46,49,57),Color.FromArgb(12,14,19),90))
                    {
                        rim.LineJoin=silver.LineJoin=LineJoin.Round;
                        g.DrawPath(rim,letters);g.DrawPath(silver,letters);g.FillPath(enamel,letters);
                    }
                }
            }
            Save("hair-clip",clip,"head",172,55,60);
        }
    }
    static void PrepareHelmet(string root)
    {
        using(var source=new Bitmap(Path.Combine(root,"assets","keyboard-knight","helmet-ink-black-white-source.png")))
        {
            int l=source.Width,t=source.Height,r=-1,b=-1;
            for(int y=0;y<source.Height;y++)for(int x=0;x<source.Width;x++)
                if(source.GetPixel(x,y).A>=8){l=Math.Min(l,x);r=Math.Max(r,x);t=Math.Min(t,y);b=Math.Max(b,y);}
            if(r<l)throw new InvalidDataException("Helmet source is empty.");
            using(var cut=source.Clone(Rectangle.FromLTRB(l,t,r+1,b+1),PixelFormat.Format32bppArgb))
            using(var layer=Canvas())
            {
                // Register the complete design once, then cut along its panel
                // seams. Shared source coordinates keep all six parts aligned.
                using(var g=Painter(layer))g.DrawImage(cut,new RectangleF(44,1,216,172));
                layer.Save(Path.Combine(output,"helmet-assembled-reference.png"),ImageFormat.Png);
                var pixels=Read(layer);
                HelmetCut("helmet",pixels,new float[] {-.1f,-.1f,1.1f,-.1f,1.1f,.23f,.82f,.29f,.778f,.45f,.793f,.46f,.5f,.582f,.207f,.46f,.222f,.45f,.18f,.29f,-.1f,.23f},false);
                var temple=new float[] {-.1f,.23f,.18f,.29f,.222f,.45f,.159f,.61f,.136f,.69f,.194f,.811f,.382f,.915f,.40f,1.1f,-.1f,1.1f};
                HelmetCut("helmet-temple-left",pixels,temple,false);HelmetCut("helmet-temple-right",pixels,temple,true);
                var face=new float[] {.222f,.45f,.207f,.46f,.5f,.582f,.5f,.795f,.39f,.865f,.40f,1.1f,.382f,.915f,.194f,.811f,.136f,.69f,.159f,.61f};
                HelmetCut("helmet-face-left",pixels,face,false);HelmetCut("helmet-face-right",pixels,face,true);
                HelmetCut("helmet-chin",pixels,new float[] {.5f,.795f,.61f,.865f,.60f,1.1f,.40f,1.1f,.39f,.865f},false);
            }
        }
    }
    static void HelmetCut(string name,int[] pixels,float[] normalized,bool mirror)
    {
        var xy=new float[normalized.Length];
        for(int i=0;i<xy.Length;i+=2)
        {xy[i]=44+216*(mirror ? 1-normalized[i] : normalized[i]);xy[i+1]=1+172*normalized[i+1];}
        using(var path=Polygon(xy))Cut(name,pixels,Mask(path,.65f),"head",152,90,70);
    }
    static void RegisterShortHair(string root)
    {
        string source=Path.Combine(root,"assets","keyboard-knight","short-hair-cap-source.png");
        if(!File.Exists(source))return;
        using(var generated=new Bitmap(source))using(var registered=Canvas())
        {
            using(var g=Graphics.FromImage(registered))
            {
                g.CompositingMode=CompositingMode.SourceCopy;
                g.InterpolationMode=InterpolationMode.HighQualityBicubic;
                g.DrawImage(generated,new Rectangle(0,0,W,H));
            }
            registered.Save(Path.Combine(output,"short-hair-cap.png"),ImageFormat.Png);
        }
    }
    [STAThread] static int Main(string[] args)
    {
        try
        {
            string root=args[0];output=Path.Combine(root,"assets","keyboard-knight","rig");Directory.CreateDirectory(output);
            if(args.Length==2&&args[1]=="--hair-clip-only")
            {PrepareHairClip();Console.WriteLine("Prepared openwork ProkyHoof metal hair clip.");return 0;}
            if(args.Length==2&&args[1]=="--helmet-only")
            {
                PrepareHelmet(root);
                var lines=new List<string>(File.ReadAllLines(Path.Combine(output,"layers.tsv")));
                lines.RemoveAll(line=>line.StartsWith("helmet\t",StringComparison.Ordinal)||line.StartsWith("helmet-",StringComparison.Ordinal));lines.AddRange(manifest);
                File.WriteAllLines(Path.Combine(output,"layers.tsv"),lines.ToArray());return 0;
            }
            original=LoadCell(Path.Combine(root,"assets","keyboard-knight","rounded-original-body.png"));
            clean=LoadCell(Path.Combine(root,"assets","keyboard-knight","clean-face-plate.png"));
            PrepareBodyAndHair();PrepareEyesAndMouth();PrepareHelmet(root);
            RegisterShortHair(root);
            if(File.Exists(Path.Combine(output,"short-hair-cap.png")))
            {
                manifest.RemoveAll(line=>line.StartsWith("hair-back-",StringComparison.Ordinal));
                for(int i=0;i<manifest.Count;i++)if(manifest[i].StartsWith("hair-cap\t",StringComparison.Ordinal))
                    manifest[i]=manifest[i].Replace("\thair-cap.png\t","\tshort-hair-cap.png\t");
            }
            File.WriteAllLines(Path.Combine(output,"layers.tsv"),manifest.ToArray());
            original.Dispose();clean.Dispose();
            Console.WriteLine("Prepared registered 900x687 cutouts, independent eyes/iris/lids and eight mouth states.");return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
