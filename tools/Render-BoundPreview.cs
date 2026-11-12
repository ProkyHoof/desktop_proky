using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

internal static class BoundPreview
{
    static void HackerCodeFrames(string root,string output)
    {
        Directory.CreateDirectory(output);
        using(var sprites=new KeyboardPetSprites(root))
        using(var image=new Bitmap(704,420,PixelFormat.Format32bppArgb))
        using(var g=Graphics.FromImage(image))
        using(var font=new Font("Microsoft YaHei UI",12,FontStyle.Bold))
        {
            var input=new InputFeedbackState();
            using(var scene=new BoundPetScene(sprites,new PetInputBindings(),input))
            {
                scene.SetViewWidth(BoundPetScene.CompactWidth);scene.SetTone(3);scene.SetKeyboardStyle(4);scene.SetMouseStyle(3);scene.SetPadStyle(2);
                scene.SetMusicStyle(PetMusicStyles.HackerCode);scene.Portrait.SetHeadwearStyle(2);scene.Portrait.SetListeningStyle(2);scene.Portrait.SetHairStyle(2);
                scene.MusicPlaying=true;scene.MusicLevel=.2f;
                for(int frame=0;frame<360;frame++)
                {
                    double now=frame/30.0;
                    if(frame%3==0){input.Key(65,true,now);input.Key(65,false,now+.001);}
                    scene.Advance(now,1/30.0,false);
                }
                for(int frame=0;frame<100;frame++)
                {
                    double now=12+frame*.05;
                    if(frame%2==0){input.Key(65,true,now);input.Key(65,false,now+.001);}
                    scene.MusicLevel=(float)(.15+.1*Math.Sin(now*2));scene.Advance(now,.05,false);
                    g.Clear(Color.FromArgb(241,245,248));
                    using(var dark=new SolidBrush(Color.FromArgb(19,26,41)))g.FillRectangle(dark,352,0,352,420);
                    for(int column=0;column<2;column++)
                    {
                        g.DrawString(column==0 ? "黑客代码流 · 浅色背景" : "黑客代码流 · 深色背景",font,column==0 ? Brushes.DarkSlateGray : Brushes.WhiteSmoke,column*352+14,12);
                        var state=g.Save();g.TranslateTransform(column*352,0);scene.Draw(g,now,false,false);g.Restore(state);
                    }
                    image.Save(Path.Combine(output,frame.ToString("D3")+".png"),ImageFormat.Png);
                    if(frame==20)image.Save(Path.Combine(root,"assets","previews","hacker-code-flow.png"),ImageFormat.Png);
                }
            }
        }
    }
    static void OceanFrames(string root,string output)
    {
        Directory.CreateDirectory(output);
        using(var sprites=new KeyboardPetSprites(root))
        using(var image=new Bitmap(704,420,PixelFormat.Format32bppArgb))
        using(var g=Graphics.FromImage(image))
        using(var font=new Font("Microsoft YaHei UI",12,FontStyle.Bold))
        using(var shortHair=new BoundPetScene(sprites,new PetInputBindings(),new InputFeedbackState()))
        using(var longHair=new BoundPetScene(sprites,new PetInputBindings(),new InputFeedbackState()))
        {
            var scenes=new[]{shortHair,longHair};
            for(int i=0;i<2;i++)
            {
                var scene=scenes[i];scene.SetViewWidth(BoundPetScene.CompactWidth);scene.Portrait.SetHairStyle(i);
                scene.SetMusicStyle(PetMusicStyles.OceanWaves);scene.Portrait.SetListeningStyle(PetListeningStyles.Bandana);
                scene.MusicPlaying=true;scene.MusicLevel=.2f;
                for(int frame=0;frame<90;frame++)scene.Advance(frame/30.0,1/30.0,false);
            }
            for(int frame=0;frame<100;frame++)
            {
                double now=3+frame*.05;
                g.Clear(Color.FromArgb(243,248,250));
                using(var dark=new SolidBrush(Color.FromArgb(32,41,56)))g.FillRectangle(dark,352,0,352,420);
                for(int i=0;i<2;i++)
                {
                    var scene=scenes[i];scene.MusicLevel=(float)(.15+.1*Math.Sin(now*1.7));scene.Advance(now,.05,false);
                    g.DrawString(i==0 ? "海浪 · 浅色背景" : "海浪 · 深色背景",font,i==0 ? Brushes.DarkSlateGray : Brushes.WhiteSmoke,i*352+15,12);
                    var saved=g.Save();g.TranslateTransform(i*352,-18);scene.Draw(g,now,false,false);g.Restore(saved);
                }
                image.Save(Path.Combine(output,frame.ToString("D3")+".png"),ImageFormat.Png);
                if(frame==20)image.Save(Path.Combine(root,"assets","previews","ocean-waves-soft.png"),ImageFormat.Png);
            }
        }
    }
    static void ArmoredMusicFrames(string root,string output)
    {
        Directory.CreateDirectory(output);
        using(var sprites=new KeyboardPetSprites(root))
        using(var image=new Bitmap(528,370,PixelFormat.Format32bppArgb))
        using(var g=Graphics.FromImage(image))
        using(var sheet=new Bitmap(1120,780,PixelFormat.Format32bppArgb))
        using(var sg=Graphics.FromImage(sheet))
        using(var font=new Font("Microsoft YaHei UI",10,FontStyle.Bold))
        {
            sg.Clear(Color.FromArgb(250,247,249));
            var inputs=new[] {new InputFeedbackState(),new InputFeedbackState()};
            using(var shortHair=new BoundPetScene(sprites,new PetInputBindings(),inputs[0]))
            using(var longHair=new BoundPetScene(sprites,new PetInputBindings(),inputs[1]))
            {
                var scenes=new[] {shortHair,longHair};
                for(int i=0;i<2;i++){scenes[i].Portrait.SetHairStyle(i);scenes[i].SetViewWidth(BoundPetScene.CompactWidth);}
                int column=0;
                for(int frame=0;frame<400;frame++)
                {
                    double now=frame*.05;g.Clear(Color.FromArgb(250,247,249));
                    bool snapshot=frame==50||frame==170||frame==250||frame==390;
                    for(int style=0;style<2;style++)
                    {
                        if(now>=1&&now<13&&frame%2==0)
                        {int key=frame%4==0 ? 65 : 74;inputs[style].Key(key,true,now);inputs[style].Key(key,false,now+.005);}
                        var scene=scenes[style];scene.MusicPlaying=now>=.5;
                        scene.MusicLevel=(float)(.12+.1*Math.Sin(now*3.6));scene.Advance(now,.05,false);
                        var pose=scene.Portrait;
                        string phase=pose.HelmetBlend<=0 ? "耳机听音乐" : pose.HelmetRemoving ? "装甲退回 · 恢复耳机" : pose.HelmetVisor<.99f ? "音频模块接入" : "装甲音频 · 律动点头";
                        g.DrawString((style==0 ? "短发 · " : "长发 · ")+phase,font,Brushes.DarkSlateGray,style*264+8,6);
                        var saved=g.Save();g.TranslateTransform(style*264,25);g.ScaleTransform(.75f,.75f);scene.Draw(g,now,false,true);g.Restore(saved);
                        if(snapshot)
                        {
                            int x=column*280,y=style*390;
                            string label=new[] {"耳机听音乐 / 快速打字","头梁收起 / 装甲接入","耳侧灯环与音量条","机甲退下 / 恢复耳机"}[column];
                            sg.DrawString((style==0 ? "短发 · " : "长发 · ")+label,font,Brushes.DarkSlateGray,x+8,y+12);
                            saved=sg.Save();sg.TranslateTransform(x+8,y+32);sg.ScaleTransform(.75f,.75f);scene.Draw(sg,now,false,true);sg.Restore(saved);
                        }
                    }
                    if(snapshot)column++;
                    image.Save(Path.Combine(output,frame.ToString("D3")+".png"),ImageFormat.Png);
                }
                sheet.Save(Path.Combine(root,"assets","previews","current-armored-music.png"),ImageFormat.Png);
            }
        }
    }
    static void MusicFrames(string root,string output)
    {
        Directory.CreateDirectory(output);
        using(var sprites=new KeyboardPetSprites(root))
        using(var image=new Bitmap(704,420,PixelFormat.Format32bppArgb))
        using(var g=Graphics.FromImage(image))
        using(var font=new Font("Microsoft YaHei UI",11,FontStyle.Bold))
        {
            var inputs=new[] {new InputFeedbackState(),new InputFeedbackState()};
            using(var shortHair=new BoundPetScene(sprites,new PetInputBindings(),inputs[0]))
            using(var longHair=new BoundPetScene(sprites,new PetInputBindings(),inputs[1]))
            {
                var scenes=new[] {shortHair,longHair};
                for(int i=0;i<2;i++){scenes[i].Portrait.SetHairStyle(i);scenes[i].SetViewWidth(BoundPetScene.CompactWidth);}
                for(int frame=0;frame<180;frame++)
                {
                    double now=frame*.05;
                    g.Clear(Color.FromArgb(250,247,249));
                    for(int style=0;style<2;style++)
                    {
                        bool typing=now>=4&&now<6;
                        if(typing&&frame%4==0)
                        {int key=frame%8==0 ? 65 : 74;inputs[style].Key(key,true,now);inputs[style].Key(key,false,now+.005);}
                        scenes[style].MusicPlaying=now>=1&&now<7;
                        scenes[style].MusicLevel=(float)(.12+.1*Math.Sin(now*7.2));
                        scenes[style].Advance(now,.05,false);
                        string phase=now<1 ? "等待播放" : now>=7 ? "暂停后回到待机" : typing ? "边听边工作" : "戴上耳机 · 双向律动";
                        g.DrawString((style==0 ? "短发" : "长发")+" · "+phase,font,Brushes.DarkSlateGray,style*352+12,12);
                        var saved=g.Save();g.TranslateTransform(style*352,-18);
                        scenes[style].Draw(g,now,false,true);g.Restore(saved);
                    }
                    image.Save(Path.Combine(output,frame.ToString("D3")+".png"),ImageFormat.Png);
                    if(frame==65)image.Save(Path.Combine(root,"assets","previews","current-music.png"),ImageFormat.Png);
                }
            }
        }
    }
    static void HelmetStills(KeyboardPetSprites sprites,string prefix)
    {
        using(var sheet=new Bitmap(1120,1520,PixelFormat.Format32bppArgb))
        using(var settled=new Bitmap(720,350,PixelFormat.Format32bppArgb))
        using(var settledGraphics=Graphics.FromImage(settled))
        using(var g=Graphics.FromImage(sheet))
        using(var font=new Font("Microsoft YaHei UI",12,FontStyle.Bold))
        {
            g.Clear(Color.FromArgb(250,247,249));
            settledGraphics.Clear(Color.FromArgb(250,247,249));
            for(int style=0;style<2;style++)
            {
                var state=new InputFeedbackState();
                using(var scene=new BoundPetScene(sprites,new PetInputBindings(),state))
                {
                    scene.Portrait.SetHairStyle(style);scene.SetViewWidth(BoundPetScene.CompactWidth);
                    int column=0;
                    for(int frame=0;frame<=550;frame++)
                    {
                        double now=frame/30.0;
                        if(frame%6==0){int key=frame%12==0 ? 65 : 74;state.Key(key,true,now);state.Key(key,false,now+.005);}
                        scene.Advance(now,1/30.0,false);
                        if(frame==315)
                        {
                            settledGraphics.DrawString(style==0 ? "短发 · 银白骑士盔" : "长发 · 银白骑士盔",font,Brushes.DarkSlateGray,style*360+14,10);
                            var snapshot=settledGraphics.Save();settledGraphics.TranslateTransform(style*360+6,-80);
                            scene.Draw(settledGraphics,now,false,false);settledGraphics.Restore(snapshot);
                        }
                        if(frame!=120&&frame!=165&&frame!=195&&frame!=225&&frame!=255&&frame!=315&&frame!=435&&frame!=550)continue;
                        int x=column%4*280,y=(style*2+column/4)*380;
                        g.DrawString((style==0 ? "短发 · " : "长发 · ")+new[] {"正常打字","顶甲开始铺展","顶甲贴合头顶","侧甲贴合两侧","下颌护边贴合","露面头盔锁定","护边打开 · 侧甲退回","顶甲收回"}[column],font,Brushes.DarkSlateGray,x+12,y+12);
                        var saved=g.Save();g.TranslateTransform(x+7,y+32);g.ScaleTransform(.75f,.75f);
                        scene.Draw(g,now,false,true);g.Restore(saved);column++;
                    }
                }
            }
            sheet.Save(prefix+"-typing-helmet.png",ImageFormat.Png);
            settled.Save(prefix+"-helmet-settled.png",ImageFormat.Png);
        }
    }
    static void HelmetFrames(string root,string output)
    {
        Directory.CreateDirectory(output);
        using(var sprites=new KeyboardPetSprites(root))
        using(var image=new Bitmap(528,370,PixelFormat.Format32bppArgb))
        using(var g=Graphics.FromImage(image))
        using(var font=new Font("Microsoft YaHei UI",10,FontStyle.Bold))
        {
            var inputs=new[] {new InputFeedbackState(),new InputFeedbackState()};
            using(var shortHair=new BoundPetScene(sprites,new PetInputBindings(),inputs[0]))
            using(var longHair=new BoundPetScene(sprites,new PetInputBindings(),inputs[1]))
            {
                var scenes=new[] {shortHair,longHair};
                for(int i=0;i<2;i++){scenes[i].Portrait.SetHairStyle(i);scenes[i].SetViewWidth(BoundPetScene.CompactWidth);}
                for(int frame=0;frame<=360;frame++)
                {
                    double now=frame*.05;
                    g.Clear(Color.FromArgb(250,247,249));
                    for(int style=0;style<2;style++)
                    {
                        if(frame%4==0)
                        {int key=frame%8==0 ? 65 : 74;inputs[style].Key(key,true,now);inputs[style].Key(key,false,now+.005);}
                        scenes[style].Advance(now,.05,false);
                        var pose=scenes[style].Portrait;
                        string phase=pose.HelmetBlend<=0 ? "打字" : pose.HelmetRemoving ? "沿头部外侧退回" : pose.HelmetVisor<.99f ? "沿头部外侧展开" : "完整头盔锁定";
                        g.DrawString((style==0 ? "短发" : "长发")+" · "+phase,font,Brushes.DarkSlateGray,style*264+12,4);
                        var saved=g.Save();g.TranslateTransform(style*264,25);g.ScaleTransform(.75f,.75f);
                        scenes[style].Draw(g,now,false,true);g.Restore(saved);
                    }
                    if(frame>=80)image.Save(Path.Combine(output,(frame-80).ToString("D3")+".png"),ImageFormat.Png);
                }
            }
        }
    }
    static void MouseFeedbackStills(string root)
    {
        using(var sprites=new KeyboardPetSprites(root))
        using(var sheet=new Bitmap(1250,560,PixelFormat.Format32bppArgb))
        using(var g=Graphics.FromImage(sheet))
        using(var font=new Font("Microsoft YaHei UI",12,FontStyle.Bold))
        {
            g.Clear(Color.FromArgb(250,247,249));
            for(int mode=0;mode<5;mode++)
            {
                var state=new InputFeedbackState();
                using(var scene=new BoundPetScene(sprites,new PetInputBindings(),state))
                {
                    scene.SetViewWidth(BoundPetScene.CompactWidth);scene.SetMouseStyle(PetMouseStyles.KnightSword);
                    for(int frame=0;frame<90;frame++)scene.Advance(frame/30.0,1/30.0,false);
                    if(mode==1||mode==2){state.Button(mode,true,3);state.Button(mode,false,3.005);}
                    if(mode==3||mode==4)state.Wheel(mode==3 ? 120 : -120,false,3);
                    scene.Advance(3.08,.08,false);
                    g.DrawString(new[] {"待机","左键 · 向左挥剑","右键 · 向右挥剑","滚轮 · 向剑尖流光","滚轮 · 向剑柄流光"}[mode],font,Brushes.DarkSlateGray,mode*250+12,12);
                    var saved=g.Save();g.TranslateTransform(mode*250+5,46);g.ScaleTransform(.68f,.68f);g.TranslateTransform(0,-100);
                    scene.Draw(g,3.08,false,true);g.Restore(saved);
                    saved=g.Save();g.SetClip(new Rectangle(mode*250+8,305,234,235));
                    using(var bg=new SolidBrush(Color.FromArgb(49,43,57)))g.FillRectangle(bg,mode*250+8,305,234,235);
                    g.TranslateTransform(mode*250+8,305);g.ScaleTransform(1.8f,1.8f);g.TranslateTransform(-10,-300);
                    scene.Draw(g,3.08,false,true);g.Restore(saved);
                }
            }
            sheet.Save(Path.Combine(root,"assets/previews/current-sword-feedback.png"),ImageFormat.Png);
        }
        Console.WriteLine("Rendered mouse click and scroll animation stills.");
    }
    static void MouseFeedbackFrames(string root,string output)
    {
        Directory.CreateDirectory(output);
        using(var sprites=new KeyboardPetSprites(root))
        using(var sceneImage=new Bitmap(352,460,PixelFormat.Format32bppArgb))
        using(var sg=Graphics.FromImage(sceneImage))
        using(var frameImage=new Bitmap(700,440,PixelFormat.Format32bppArgb))
        using(var g=Graphics.FromImage(frameImage))
        {
            var state=new InputFeedbackState();
            using(var scene=new BoundPetScene(sprites,new PetInputBindings(),state))
            {
                scene.SetViewWidth(BoundPetScene.CompactWidth);scene.SetMouseStyle(PetMouseStyles.KnightSword);
                for(int frame=0;frame<110;frame++)
                {
                    double now=frame*.05;
                    if(frame==6||frame==22||frame==38)
                    {
                        int button=frame==22 ? 2 : 1;state.Button(button,true,now);state.Button(button,false,now+.005);
                        if(frame==38){state.Button(2,true,now);state.Button(2,false,now+.005);}
                    }
                    if(frame==54||frame==64)state.Wheel(frame==54 ? 120 : -120,false,now);
                    if(frame==76)state.Button(1,true,now);
                    if(frame==92)state.Button(1,false,now);
                    scene.Advance(now,.05,false);
                    sg.Clear(Color.Transparent);scene.Draw(sg,now,false,true);
                    g.Clear(Color.FromArgb(49,43,57));
                    g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(sceneImage,new Rectangle(0,-55,352,460),0,0,352,460,GraphicsUnit.Pixel);
                    g.DrawImage(sceneImage,new Rectangle(370,105,310,288),new Rectangle(10,300,135,130),GraphicsUnit.Pixel);
                    frameImage.Save(Path.Combine(output,frame.ToString("D4")+".png"),ImageFormat.Png);
                }
            }
        }
    }
    static void KeyDisplayStills(KeyboardPetSprites sprites,string prefix)
    {
        using(var sheet=new Bitmap(960,500,PixelFormat.Format32bppArgb))
        using(var g=Graphics.FromImage(sheet))
        using(var font=new Font("Microsoft YaHei UI",12,FontStyle.Bold))
        {
            g.Clear(Color.FromArgb(250,247,249));
            for(int mode=0;mode<2;mode++)
            {
                var state=new InputFeedbackState(); state.SetBubbleMode(mode==1);
                using(var scene=new BoundPetScene(sprites,new PetInputBindings(),state))
                {
                    scene.SetViewWidth(BoundPetScene.CompactWidth);
                    for(int frame=0;frame<=48;frame++)
                    {
                        double now=4+frame/60.0;
                        if(frame==0){state.Key(162,true,now);state.Key(65,true,now);}
                        if(frame==2){state.Key(65,false,now);state.Key(162,false,now);}
                        if(frame==16||frame==30)state.Key(frame==16 ? 66 : 67,true,now);
                        if(frame==18||frame==32)state.Key(frame==18 ? 66 : 67,false,now);
                        if(frame==44)state.Key(68,true,now);
                        scene.Advance(now,1/60.0,false);
                    }
                    g.DrawString(mode==0 ? "固定框显示" : "冒泡显示",font,Brushes.DarkSlateGray,mode*480+20,8);
                    var saved=g.Save();g.TranslateTransform(mode*480+64,28);
                    scene.Draw(g,4.8,false,true);g.Restore(saved);
                }
            }
            sheet.Save(prefix+"-key-display-modes.png",ImageFormat.Png);
        }
    }
    static void LongHairDeskStills(KeyboardPetSprites sprites,string prefix)
    {
        using(var sheet=new Bitmap(1200,260,PixelFormat.Format32bppArgb))
        using(var review=Graphics.FromImage(sheet))
        using(var font=new Font("Microsoft YaHei UI",12,FontStyle.Bold))
        {
            review.Clear(Color.FromArgb(250,247,249));
            for(int mode=0;mode<3;mode++)
            {
                var input=new InputFeedbackState();
                using(var scene=new BoundPetScene(sprites,new PetInputBindings(),input))
                {
                    scene.Portrait.SetHairStyle(PetHairStyles.Reference);
                    for(int frame=0;frame<=120;frame++)
                    {
                        double now=frame/30.0;
                        if(mode==1&&frame%3==0){int key=65+frame%20;input.Key(key,true,now);input.Key(key,false,now+.001);}
                        scene.Advance(now,1/30.0,mode==2);
                    }
                    review.DrawString(new[] {"长发 · 待机","长发 · 打字","长发 · 休息"}[mode],font,Brushes.DarkSlateGray,mode*400+12,10);
                    var saved=review.Save();review.SetClip(new Rectangle(mode*400+7,40,386,210));
                    review.TranslateTransform(mode*400+7,40);review.ScaleTransform(386f/165,210f/90);
                    review.TranslateTransform(-115,-315);scene.Draw(review,4,mode==2,true);review.Restore(saved);
                }
            }
            sheet.Save(prefix+"-longhair-desk.png",ImageFormat.Png);
        }
    }
    static void KeyboardStills(KeyboardPetSprites sprites,string prefix)
    {
        using(var sheet=new Bitmap(1000,550,PixelFormat.Format32bppArgb))
        using(var review=Graphics.FromImage(sheet))
        using(var font=new Font("Microsoft YaHei UI",11,FontStyle.Bold))
        using(var small=new Font("Microsoft YaHei UI",9))
        {
            review.Clear(Color.FromArgb(250,247,249));
            review.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            for(int mode=0;mode<2;mode++)
            {
                var state=new InputFeedbackState();
                using(var scene=new BoundPetScene(sprites,new PetInputBindings(),state))
                using(var image=new Bitmap(480,460,PixelFormat.Format32bppArgb))
                using(var g=Graphics.FromImage(image))
                {
                    scene.Portrait.SetHairStyle(mode);
                    if(mode==1){state.Key(65,true,0);state.Key(13,true,0);}
                    for(int frame=0;frame<=30;frame++)scene.Advance(frame/30.0,1/30.0,false);
                    scene.Draw(g,1,false,false);
                    int x=mode*500;
                    review.DrawString(mode==0 ? "银白小骑士键盘 · 待机" : "围巾红按键 · 敲击反馈",font,Brushes.DarkSlateGray,x+25,15);
                    review.DrawImage(image,new Rectangle(x+15,48,470,340),new Rectangle(0,108,415,300),GraphicsUnit.Pixel);
                    using(var background=new SolidBrush(mode==0 ? Color.FromArgb(239,233,242) : Color.FromArgb(49,43,57)))
                        review.FillRectangle(background,x+25,410,450,112);
                    var saved=review.Save();review.SetClip(new Rectangle(x+25,410,450,112));
                    review.TranslateTransform(x+25,410);review.ScaleTransform(450f/195,112f/49);review.TranslateTransform(-99,-355+PetDeskProjection.Lift);
                    scene.Draw(review,1,false,false);review.Restore(saved);
                }
            }
            review.DrawString("圆润银白外壳 / 灰紫阴影 / 奶白键帽 / 深色描边 / 红围巾点缀",small,Brushes.DarkSlateGray,25,528);
            sheet.Save(prefix+"-keyboard.png",ImageFormat.Png);
        }
    }
    static void HairClipStills(KeyboardPetSprites sprites,string prefix)
    {
        using(var sheet=new Bitmap(900,310,PixelFormat.Format32bppArgb))
        using(var review=Graphics.FromImage(sheet))
        using(var font=new Font("Microsoft YaHei UI",12,FontStyle.Bold))
        {
            review.Clear(Color.FromArgb(250,247,249));
            review.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            for(int style=0;style<2;style++)
            using(var scene=new BoundPetScene(sprites,new PetInputBindings(),new InputFeedbackState()))
            {
                scene.Portrait.SetHairStyle(style);scene.Advance(0,0,false);
                review.DrawString(style==0 ? "字样发夹 · 短发" : "字样发夹 · 长发",font,Brushes.DarkSlateGray,style*450+15,10);
                var saved=review.Save();review.SetClip(new Rectangle(style*450+8,45,434,260));
                review.TranslateTransform(style*450+8,45);review.ScaleTransform(434f/103,260f/62);
                review.TranslateTransform(-183,-155);scene.Draw(review,0,false,true);review.Restore(saved);
            }
            sheet.Save(prefix+"-hair-clip.png",ImageFormat.Png);
        }
    }
    static void HairstyleStills(KeyboardPetSprites sprites,string prefix)
    {
        for(int mode=0;mode<2;mode++)
        using(var sheet=new Bitmap(960,500,PixelFormat.Format32bppArgb))
        using(var g=Graphics.FromImage(sheet))
        using(var font=new Font("Microsoft YaHei UI",12,FontStyle.Bold))
        {
            g.Clear(Color.FromArgb(250,247,249));
            for(int style=0;style<2;style++)
            {
                var state=new InputFeedbackState();
                using(var scene=new BoundPetScene(sprites,new PetInputBindings(),state))
                {
                    scene.Portrait.SetHairStyle(style);
                    for(int frame=0;frame<=120;frame++)
                    {
                        double now=frame/30.0;
                        if(mode==1&&frame%3==0){int key=65+frame%20;state.Key(key,true,now);state.Key(key,false,now+.001);}
                        scene.Advance(now,1/30.0,false);
                    }
                    var saved=g.Save();g.TranslateTransform(style*480,32);
                    scene.Draw(g,4,false,true);g.Restore(saved);
                    g.DrawString(style==0 ? "利落短发" : "参考图长发 · 层次外翘",font,Brushes.DarkSlateGray,style*480+20,8);
                }
            }
            sheet.Save(prefix+(mode==0 ? "-hairstyles.png" : "-hairstyles-typing.png"),ImageFormat.Png);
        }
    }
    static void InteractionFrames(string root,string output)
    {
        Directory.CreateDirectory(output);
        var state=new InputFeedbackState();
        using(var sprites=new KeyboardPetSprites(root))
        using(var scene=new BoundPetScene(sprites,new PetInputBindings(),state))
        using(var image=new Bitmap(480,490,PixelFormat.Format32bppArgb))
        using(var sheet=new Bitmap(1080,620,PixelFormat.Format32bppArgb))
        using(var g=Graphics.FromImage(image))using(var review=Graphics.FromImage(sheet))
        using(var font=new Font("Microsoft YaHei UI",10,FontStyle.Bold))
        using(var marker=new Pen(Color.FromArgb(246,171,182),1.5f))
        {
            review.Clear(Color.FromArgb(45,42,52));int tap=0,panel=0;
            int[] samples={8,40,80,108,143,175};
            for(int frame=0;frame<192;frame++)
            {
                double now=frame*.05;int phase=frame/48;
                PointF face=scene.Portrait.WorldPoint(new PointF(151,131));
                float x=phase==0 ? -110+frame*220/47f : phase==1 ? (float)Math.Cos((frame-48)/48.0*Math.PI*2)*100 : -85;
                float y=phase==1 ? (float)Math.Sin((frame-48)/48.0*Math.PI*2)*85 : -15;
                PointF cursor=new PointF(face.X+x,face.Y+y);
                scene.ObservePointer(cursor,now,true);
                if(frame==96)scene.Interact(scene.Portrait.WorldPoint(new PointF(151,144)),now);
                if(phase==3)while(tap*.1<=now-7.2+.00001)
                {double at=7.2+tap*.1;int key=65+tap%20;state.Key(key,true,at);state.Key(key,false,at+.001);tap++;}
                scene.Advance(now,.05,false);
                g.Clear(Color.FromArgb(45,42,52));g.ResetTransform();
                string label=new[] {"鼠标左右移动 · 眼珠平滑跟随","上下张望 · 虹膜保持在眼眶内","轻点脸庞 · 微笑与短发回弹","连续敲字 · 短发、刘海与呆毛轻摆"}[phase];
                g.DrawString(label,font,Brushes.White,10,5);
                g.TranslateTransform(0,30);scene.Draw(g,now,false,true);
                g.DrawEllipse(marker,cursor.X-5,cursor.Y-5,10,10);g.DrawLine(marker,cursor.X-8,cursor.Y,cursor.X+8,cursor.Y);
                image.Save(Path.Combine(output,frame.ToString("D3")+".png"),ImageFormat.Png);
                if(panel<samples.Length&&frame==samples[panel])
                {
                    int px=panel%3*360,py=panel/3*310;
                    review.DrawString(new[] {"视线向左","视线向右","上下张望","点击脸庞","发丝回弹","敲字联动"}[panel],font,Brushes.White,px+10,py+5);
                    review.DrawImage(image,new Rectangle(px+5,py+27,350,280),new Rectangle(24,145,322,258),GraphicsUnit.Pixel);
                    panel++;
                }
            }
            string prefix=Path.Combine(Path.GetDirectoryName(output),Path.GetFileName(output).Replace("-frames",""));
            sheet.Save(prefix+".png",ImageFormat.Png);
        }
        Console.WriteLine("Rendered 192 cursor/hair/click/typing interaction frames and review sheet.");
    }
    static void DetailStills(KeyboardPetSprites sprites,string prefix)
    {
        using(var hairSheet=new Bitmap(1050,320,PixelFormat.Format32bppArgb))
        using(var mouseSheet=new Bitmap(900,330,PixelFormat.Format32bppArgb))
        using(var hair=Graphics.FromImage(hairSheet))
        using(var mice=Graphics.FromImage(mouseSheet))
        using(var font=new Font("Microsoft YaHei UI",10,FontStyle.Bold))
        {
            hair.Clear(Color.FromArgb(45,42,52));mice.Clear(Color.FromArgb(250,247,249));
            hair.InterpolationMode=mice.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            for(int mode=0;mode<3;mode++)
            {
                var state=new InputFeedbackState();
                using(var scene=new BoundPetScene(sprites,new PetInputBindings(),state))
                using(var image=new Bitmap(BoundPetScene.WorldWidth,BoundPetScene.WorldHeight,PixelFormat.Format32bppArgb))
                using(var g=Graphics.FromImage(image))
                {
                    int tap=0;
                    for(int frame=0;frame<=108;frame++)
                    {
                        double now=frame*.05;
                        if(mode==1)while(tap*.1<=now+.00001)
                        {int code=65+tap%20;state.Key(code,true,tap*.1);state.Key(code,false,tap*.1+.001);tap++;}
                        scene.Advance(now,.05,mode==2);
                    }
                    g.Clear(Color.FromArgb(45,42,52));scene.Draw(g,5.4,mode==2,true);
                    hair.DrawImage(image,new Rectangle(mode*350,28,350,286),new Rectangle(38,108,300,245),GraphicsUnit.Pixel);
                    hair.DrawString(new[] {"短发 · 发根与下颌轮廓","极速输入 · 短发联动","休息 · 短发轮廓完整"}[mode],font,Brushes.White,mode*350+12,3);
                    state.Clear();state.Key(65,true,6);state.Key(74,true,6);
                    for(int frame=0;frame<40;frame++)scene.Advance(6+frame*.02,.02,false);
                    if(mode==1)state.Button(1,true,6.8);
                    if(mode==2)state.Wheel(120,false,6.8);
                    scene.Advance(6.85,.07,false);
                    var saved=mice.Save();mice.SetClip(new Rectangle(mode*300+14,40,272,280));
                    mice.TranslateTransform(mode*300+14,40);mice.ScaleTransform(3,3);mice.TranslateTransform(-24,-327);
                    scene.DrawSword(mice,6.85);mice.Restore(saved);
                    mice.DrawString(new[] {"骑士剑 · 银刃与红宝石","点击 · 剑刃亮起","滚轮 · 剑身流光"}[mode],font,Brushes.DarkSlateGray,mode*300+10,8);
                }
            }
            hairSheet.Save(prefix+"-hair.png",ImageFormat.Png);mouseSheet.Save(prefix+"-sword.png",ImageFormat.Png);
        }
    }
    static void WorkStills(KeyboardPetSprites sprites,string prefix)
    {
        using(var sheet=new Bitmap(1260,720,PixelFormat.Format32bppArgb))
        using(var review=Graphics.FromImage(sheet))
        using(var font=new Font("Microsoft YaHei UI",11,FontStyle.Bold))
        using(var subFont=new Font("Microsoft YaHei UI",8))
        {
            review.Clear(Color.FromArgb(250,247,249));
            string[] labels={"待机 · 轻松小猫嘴","慢打 · 抿嘴思考","稳定输入 · 专注","快速输入 · 认真赶稿","极速输入 · 火力全开","停下来 · 放松一下"};
            double[] rates={0,1,4,6,10,6};
            var expected=new[] {WorkMood.Idle,WorkMood.Thinking,WorkMood.Focused,WorkMood.Rushing,WorkMood.Turbo,WorkMood.Relieved};
            for(int mode=0;mode<rates.Length;mode++)
            {
                var state=new InputFeedbackState();
                using(var scene=new BoundPetScene(sprites,new PetInputBindings(),state))
                using(var image=new Bitmap(BoundPetScene.WorldWidth,BoundPetScene.WorldHeight,PixelFormat.Format32bppArgb))
                using(var g=Graphics.FromImage(image))
                {
                    int tap=0;double until=mode==5 ? 7.1 : 5.4;
                    for(int frame=0;frame<=(int)(until*60);frame++)
                    {
                        double now=frame/60.0;
                        if(rates[mode]>0)while(tap/rates[mode]<=Math.Min(now,5.4)+.00001)
                        {
                            double at=tap/rates[mode];int key=65+tap%20;
                            state.Key(key,true,at);state.Key(key,false,at+.001);tap++;
                        }
                        scene.Advance(now,1/60.0,false);
                    }
                    if(scene.Portrait.Mood!=expected[mode])throw new Exception("Work preview did not reach "+expected[mode]+": "+scene.Portrait.Mood);
                    g.Clear(Color.FromArgb(250,247,249));scene.Draw(g,until,false,true);
                    int x=mode%3*420,y=mode/3*360;
                    review.DrawString(labels[mode],font,Brushes.DarkSlateGray,x+15,y+8);
                    review.DrawString(mode==0 ? "没有输入时自然待机" : mode==5 ? "闭眼轻笑，放松后自然回到待机" : Math.Round(state.Work.KeysPerMinute)+" 按/分",subFont,Brushes.SlateGray,x+15,y+34);
                    review.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    review.DrawImage(image,new Rectangle(x+5,y+60,410,295),new Rectangle(5,110,410,295),GraphicsUnit.Pixel);
                    if(mode==2)image.Save(prefix+"-typing.png",ImageFormat.Png);
                }
            }
            sheet.Save(prefix+"-work-expressions.png",ImageFormat.Png);
        }
    }
    static void EyeDetail(Bitmap source,string path)
    {
        using(var detail=new Bitmap(600,330,PixelFormat.Format32bppArgb))
        using(var g=Graphics.FromImage(detail))
        {
            g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(source,new Rectangle(0,0,600,330),new Rectangle(85,215,200,110),GraphicsUnit.Pixel);
            detail.Save(path,ImageFormat.Png);
        }
    }
    static void Stills(string root,string output)
    {
        string prefix=Path.Combine(Path.GetDirectoryName(output),Path.GetFileName(output).Replace("-frames",""));
        var state=new InputFeedbackState();
        using(var sprites=new KeyboardPetSprites(root))
        using(var scene=new BoundPetScene(sprites,new PetInputBindings(),state))
        using(var image=new Bitmap(BoundPetScene.WorldWidth,BoundPetScene.WorldHeight,PixelFormat.Format32bppArgb))
        using(var g=Graphics.FromImage(image))
        using(var font=new Font("Consolas",10))
        {
            scene.Advance(0,0,false);g.Clear(Color.FromArgb(250,247,249));scene.Draw(g,0,false,true);
            image.Save(prefix+"-idle.png",ImageFormat.Png);
            EyeDetail(image,prefix+"-eyes.png");
            state.Key(65,true,9);state.Key(74,true,9);
            for(int i=0;i<40;i++)scene.Advance(9+i*.016,.016,false);
            g.Clear(Color.FromArgb(250,247,249));scene.Draw(g,9.624,false,true);
            image.Save(prefix+"-typing.png",ImageFormat.Png);
            using(var strip=new Bitmap(900,380,PixelFormat.Format32bppArgb))
            using(var review=Graphics.FromImage(strip))
            {
                review.Clear(Color.FromArgb(250,247,249));
                double[] times={0,3.3,5,1.4,3.3,5};
                for(int i=0;i<times.Length;i++)
                {
                    state.Clear();
                    if(i>=3){state.Key(65,true,times[i]-1);state.Key(74,true,times[i]-1);}
                    for(int step=0;step<=30;step++)scene.Advance(times[i]-1+step/30.0,1/30.0,false);
                    g.Clear(Color.FromArgb(250,247,249));scene.Draw(g,times[i],false,true);
                    int x=i%3*300,y=i/3*190;
                    review.DrawImage(image,new Rectangle(x,y+20,300,170),new Rectangle(100,270,170,96),GraphicsUnit.Pixel);
                    review.DrawString((i<3 ? "Idle " : "Typing ")+times[i].ToString("0.0")+"s",font,Brushes.DarkSlateGray,x+10,y+2);
                }
                strip.Save(prefix+"-neck.png",ImageFormat.Png);
            }
            state.Clear();
            using(var strip=new Bitmap(7*240,270,PixelFormat.Format32bppArgb))
            using(var review=Graphics.FromImage(strip))
            {
                review.Clear(Color.FromArgb(250,247,249));
                double[] times={2.8,2.84,2.89,2.93,2.98,3.06,3.14};
                for(int i=0;i<times.Length;i++)
                {
                    scene.Advance(times[i],.05,false);g.Clear(Color.Transparent);scene.Draw(g,times[i],false,true);
                    review.DrawImage(image,new Rectangle(i*240,20,240,240),new Rectangle(8,112,340,340),GraphicsUnit.Pixel);
                    review.DrawString(times[i].ToString("0.00")+"s",font,Brushes.DarkSlateGray,i*240+12,5);
                }
                strip.Save(prefix+"-blink.png",ImageFormat.Png);
            }
            WorkStills(sprites,prefix);
            HelmetStills(sprites,prefix);
            DetailStills(sprites,prefix);
            HairstyleStills(sprites,prefix);
            HairClipStills(sprites,prefix);
            LongHairDeskStills(sprites,prefix);
            KeyboardStills(sprites,prefix);
            KeyDisplayStills(sprites,prefix);
        }
        Console.WriteLine("Rendered idle, typing, blink, neck, eyes, work expressions, complete hair and personalised mouse details.");
    }
    [STAThread] static int Main(string[] args)
    {
        try
        {
            string root = args[0], output = args[1];
            if(args.Length>2&&args[2]=="--hacker-code"){HackerCodeFrames(root,output);return 0;}
            if(args.Length>2&&args[2]=="--ocean-waves"){OceanFrames(root,output);return 0;}
            if(args.Length>2&&args[2]=="--armored-music"){ArmoredMusicFrames(root,output);return 0;}
            if(args.Length>2&&args[2]=="--music"){MusicFrames(root,output);return 0;}
            if(args.Length>2&&args[2]=="--helmet")
            {using(var sprites=new KeyboardPetSprites(root))HelmetStills(sprites,Path.Combine(root,"assets","previews","current"));HelmetFrames(root,output);return 0;}
            if(args.Length>2&&args[2]=="--mouse-feedback"){MouseFeedbackStills(root);MouseFeedbackFrames(root,output);return 0;}
            if(args.Length>2&&args[2]=="--stills"){Stills(root,output);return 0;}
            if(args.Length>2&&args[2]=="--interaction"){InteractionFrames(root,output);return 0;}
            Directory.CreateDirectory(output);
            var state = new InputFeedbackState(); var bindings = new PetInputBindings();
            using (var sprites = new KeyboardPetSprites(root))
            using (var scene = new BoundPetScene(sprites, bindings, state))
            using (var image = new Bitmap(BoundPetScene.WorldWidth, BoundPetScene.WorldHeight, PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(image))
            {
                var renderClock=System.Diagnostics.Stopwatch.StartNew();
                var current = new HashSet<int>();
                const int frameCount=640;
                for (int frame = 0; frame < frameCount; frame++)
                {
                    double now = frame * .05;
                    int[] next = frame >= 160 && frame < 180 ? new[] {65,74} : frame >= 180 && frame < 200 ? new[] {83,75} :
                        frame >= 200 && frame < 220 ? new[] {68,76} : frame >= 220 && frame < 240 ? new[] {13,70} :
                        frame >= 240 && frame < 260 ? new[] {162,160,83} : new int[0];
                    foreach (int code in current.Where(k => !next.Contains(k)).ToArray()) { state.Key(code,false,now); current.Remove(code); }
                    foreach (int code in next) if(current.Add(code)) state.Key(code,true,now);
                    if(frame>=280&&frame<320)
                        state.Move(1000 + (int)(Math.Sin(now * 1.4) * 115), 1000 + (int)(Math.Cos(now * 1.1) * 65), now);
                    if (frame == 12) state.Button(1, true, now);
                    if (frame == 15) state.Button(1, false, now);
                    if (frame == 100) state.Wheel(120, false, now);
                    scene.Advance(now, .05, false);
                    g.Clear(Color.FromArgb(250, 247, 249)); scene.Draw(g, now, false, true);
                    image.Save(Path.Combine(output, frame.ToString("D3") + ".png"), ImageFormat.Png);
                    if(frame==0||frame==190)
                        image.Save(Path.Combine(Path.GetDirectoryName(output),Path.GetFileName(output).Replace("-frames","")+(frame==0 ? "-idle.png" : "-typing.png")),ImageFormat.Png);
                    if(frame==0)EyeDetail(image,Path.Combine(Path.GetDirectoryName(output),Path.GetFileName(output).Replace("-frames","")+"-eyes.png"));
                    if(frame==58)
                        image.Save(Path.Combine(Path.GetDirectoryName(output),Path.GetFileName(output).Replace("-frames","")+
                            "-closed.png"),ImageFormat.Png);
                    if(frame==150)image.Save(Path.Combine(Path.GetDirectoryName(output),Path.GetFileName(output).Replace("-frames","")+"-hair.png"),ImageFormat.Png);
                }
                Console.WriteLine(frameCount+" animation frames including PNG export: " + renderClock.ElapsedMilliseconds + "ms");
                state.Clear();
                using(var strip=new Bitmap(7*240,270,PixelFormat.Format32bppArgb))
                using(var review=Graphics.FromImage(strip))
                using(var font=new Font("Consolas",11))
                {
                    review.Clear(Color.FromArgb(250,247,249));
                    double[] times={2.8,2.84,2.89,2.93,2.98,3.06,3.14};
                    for(int i=0;i<times.Length;i++)
                    {
                        scene.Advance(times[i],.05,false);g.Clear(Color.Transparent);scene.Draw(g,times[i],false,true);
                        review.DrawImage(image,new Rectangle(i*240,20,240,240),new Rectangle(8,112,340,340),GraphicsUnit.Pixel);
                        review.DrawString(times[i].ToString("0.00")+"s",font,Brushes.DarkSlateGray,i*240+12,5);
                    }
                    strip.Save(Path.Combine(Path.GetDirectoryName(output),Path.GetFileName(output).Replace("-frames","")+"-blink.png"),ImageFormat.Png);
                }
            }
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
