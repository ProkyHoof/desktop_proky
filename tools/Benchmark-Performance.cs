using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;

internal static class PerformanceBenchmark
{
    [STAThread] static int Main(string[] args)
    {
        try
        {
            int frames=args.Length>1 ? Int32.Parse(args[1]) : 180;
            var input=new InputFeedbackState();
            using(var sprites=new KeyboardPetSprites(args[0]))
            using(var scene=new BoundPetScene(sprites,new PetInputBindings(),input))
            using(var image=new Bitmap(480,460,PixelFormat.Format32bppPArgb))
            using(var g=Graphics.FromImage(image))
            using(var process=Process.GetCurrentProcess())
            {
                foreach(string mode in new[] {"idle","typing","turbo","rest"})
                {
                    input.Clear();double nextTap=0;int tap=0;
                    for(int i=0;i<8;i++){scene.Advance(i/30.0,1/30.0,mode=="rest");g.Clear(Color.Transparent);scene.Draw(g,i/30.0,mode=="rest",true);}
                    GC.Collect();GC.WaitForPendingFinalizers();
                    TimeSpan cpu=process.TotalProcessorTime;int gen0=GC.CollectionCount(0);
                    var watch=Stopwatch.StartNew();
                    long updateTicks=0,drawTicks=0;
                    for(int i=0;i<frames;i++)
                    {
                        double now=1+i/30.0;
                        if(mode=="typing"||mode=="turbo")while(nextTap<=now)
                        {
                            int code=65+tap++%20;input.Key(code,true,nextTap);input.Key(code,false,nextTap+.001);
                            nextTap+=mode=="turbo" ? .1 : .25;
                        }
                        long tick=Stopwatch.GetTimestamp();scene.Advance(now,1/30.0,mode=="rest");
                        long updated=Stopwatch.GetTimestamp();g.Clear(Color.Transparent);scene.Draw(g,now,mode=="rest",true);
                        updateTicks+=updated-tick;drawTicks+=Stopwatch.GetTimestamp()-updated;
                    }
                    watch.Stop();process.Refresh();
                    Console.WriteLine("{0}: frames={1}, wall_ms_per_frame={2:F2}, cpu_ms_per_frame={3:F2}, gen0={4}, private_MiB={5:F1}",
                        mode,frames,watch.Elapsed.TotalMilliseconds/frames,(process.TotalProcessorTime-cpu).TotalMilliseconds/frames,
                        GC.CollectionCount(0)-gen0,process.PrivateMemorySize64/1048576.0);
                    Console.WriteLine("  portrait_update_ms={0:F2}, scene_draw_ms={1:F2}",updateTicks*1000.0/Stopwatch.Frequency/frames,drawTicks*1000.0/Stopwatch.Frequency/frames);
                }
            }
            return 0;
        }
        catch(Exception error){Console.Error.WriteLine(error);return 1;}
    }
}
