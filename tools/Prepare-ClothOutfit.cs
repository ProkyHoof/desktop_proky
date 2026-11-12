using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

// Register imagegen clothing textures against the existing sprite masks.
// Original alpha, canvas, shoulder/fingertip anchors and runtime scale stay exact.
internal static class PrepareClothOutfit
{
    static int[] Read(Bitmap image)
    {
        var data=image.LockBits(new Rectangle(Point.Empty,image.Size),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
        var pixels=new int[image.Width*image.Height];
        try {for(int y=0;y<image.Height;y++)Marshal.Copy(IntPtr.Add(data.Scan0,y*data.Stride),pixels,y*image.Width,image.Width);}
        finally {image.UnlockBits(data);}return pixels;
    }
    static Rectangle Bounds(Bitmap image,Rectangle tile)
    {
        var pixels=Read(image);int l=tile.Right,t=tile.Bottom,r=-1,b=-1;
        for(int y=tile.Top;y<tile.Bottom;y++)for(int x=tile.Left;x<tile.Right;x++)
            if(((uint)pixels[y*image.Width+x]>>24)>32){l=Math.Min(l,x);t=Math.Min(t,y);r=Math.Max(r,x);b=Math.Max(b,y);}
        if(r<l)throw new InvalidDataException("Empty sprite.");return Rectangle.FromLTRB(l,t,r+1,b+1);
    }
    static void Register(string originalFile,string generatedFile,string output,int columns)
    {
        using(var original=new Bitmap(originalFile))using(var source=new Bitmap(generatedFile))
        using(var result=new Bitmap(original.Width,original.Height,PixelFormat.Format32bppArgb))
        {
            using(var g=Graphics.FromImage(result))
            {
                g.InterpolationMode=InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode=PixelOffsetMode.HighQuality;
                for(int i=0;i<columns;i++)
                {
                    var targetTile=new Rectangle(i*original.Width/columns,0,original.Width/columns,original.Height);
                    var sourceTile=new Rectangle(i*source.Width/columns,0,source.Width/columns,source.Height);
                    var target=Bounds(original,targetTile);var from=Bounds(source,sourceTile);
                    g.DrawImage(source,target,from,GraphicsUnit.Pixel);
                    Console.WriteLine(Path.GetFileName(output)+" part "+i+": "+from+" -> "+target);
                }
            }
            var pixels=Read(result);var mask=Read(original);
            // Extend nearest generated edge color beneath the original mask,
            // avoiding transparent holes or fringes where the drawings differ.
            var queue=new int[pixels.Length];var known=new bool[pixels.Length];int start=0,end=0;
            for(int i=0;i<pixels.Length;i++)if(((uint)pixels[i]>>24)>32){known[i]=true;queue[end++]=i;}
            while(start<end)
            {
                int index=queue[start++],x=index%result.Width,y=index/result.Width;
                if(x>0)Extend(index,index-1,pixels,known,queue,ref end);
                if(x+1<result.Width)Extend(index,index+1,pixels,known,queue,ref end);
                if(y>0)Extend(index,index-result.Width,pixels,known,queue,ref end);
                if(y+1<result.Height)Extend(index,index+result.Width,pixels,known,queue,ref end);
            }
            for(int i=0;i<pixels.Length;i++)pixels[i]=((uint)mask[i]>>24)==0 ? 0 : (mask[i]&unchecked((int)0xFF000000))|(pixels[i]&0xFFFFFF);
            var data=result.LockBits(new Rectangle(Point.Empty,result.Size),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
            try {for(int y=0;y<result.Height;y++)Marshal.Copy(pixels,y*result.Width,IntPtr.Add(data.Scan0,y*data.Stride),result.Width);}
            finally {result.UnlockBits(data);}
            result.Save(output,ImageFormat.Png);
            using(var saved=new Bitmap(output))
            {
                var actual=Read(saved);
                for(int i=0;i<actual.Length;i++)if(((uint)actual[i]>>24)!=((uint)mask[i]>>24))throw new Exception("Sprite alpha changed.");
            }
            Console.WriteLine("PASS: exact "+original.Width+"x"+original.Height+" canvas and original alpha: "+output);
        }
    }
    static void Extend(int from,int to,int[] pixels,bool[] known,int[] queue,ref int end)
    {if(known[to])return;known[to]=true;pixels[to]=pixels[from];queue[end++]=to;}
    static int Main(string[] args)
    {
        try
        {
            string root=Path.GetFullPath(args[0]),assets=Path.Combine(root,"assets","keyboard-knight"),design=Path.Combine(assets,"outfit-design");
            Register(Path.Combine(design,"original-body.png"),Path.Combine(design,"cloth-body-source.png"),Path.Combine(assets,"rig","body.png"),1);
            Register(Path.Combine(design,"original-arms.png"),Path.Combine(design,"cloth-arms-source.png"),Path.Combine(assets,"soft-style-arms.png"),2);
            return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
