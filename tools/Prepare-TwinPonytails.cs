using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Collections.Generic;

// Register the generated loose ponytail pair without repainting its alpha.
internal static class PrepareTwinPonytails
{
    static Rectangle AlphaBounds(Bitmap image,int side)
    {
        int start=side*image.Width/2,end=(side+1)*image.Width/2;
        int l=end,t=image.Height,r=-1,b=-1;
        for(int y=0;y<image.Height;y++)for(int x=start;x<end;x++)
            if(image.GetPixel(x,y).A>=8){l=Math.Min(l,x);t=Math.Min(t,y);r=Math.Max(r,x);b=Math.Max(b,y);}
        if(r<l)throw new InvalidDataException("Empty ponytail");
        return Rectangle.FromLTRB(l,t,r+1,b+1);
    }
    static int Main(string[] args)
    {
        try
        {
            string assets=Path.Combine(Path.GetFullPath(args[0]),"assets","keyboard-knight");
            string rig=Path.Combine(assets,"rig");
            using(var source=new Bitmap(Path.Combine(assets,"hairstyle-design","twin-ponytails-source.png")))
            for(int side=0;side<2;side++)
            using(var result=new Bitmap(900,687,PixelFormat.Format32bppArgb))
            {
                using(var g=Graphics.FromImage(result))
                {
                    g.InterpolationMode=InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode=PixelOffsetMode.HighQuality;
                    g.DrawImage(source,new Rectangle(side==0 ? 18 : 660,162,222,486),AlphaBounds(source,side),GraphicsUnit.Pixel);
                }
                result.Save(Path.Combine(rig,"hair-ponytail-"+(side==0 ? "left" : "right")+".png"),ImageFormat.Png);
            }
            string manifest=Path.Combine(rig,"layers.tsv");
            var lines=new List<string>(File.ReadAllLines(manifest));
            lines.RemoveAll(line=>line.StartsWith("hair-ponytail-",StringComparison.Ordinal));
            lines.Add("hair-ponytail-left\thair-ponytail-left.png\thead\t76\t72\t0");
            lines.Add("hair-ponytail-right\thair-ponytail-right.png\thead\t224\t72\t1");
            File.WriteAllLines(manifest,lines.ToArray());
            return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
