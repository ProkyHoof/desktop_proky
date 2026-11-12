using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.Windows.Forms;

internal sealed class WardrobePreview : Control
{
    internal BoundPetScene Scene;
    internal double Time;
    internal bool Resting;
    internal WardrobePreview(){DoubleBuffered=true;ResizeRedraw=true;BackColor=KnightUi.Surface;AccessibleName="发型、键鼠、头饰、音乐样式与桌饰的动态预览";}
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);if(Scene==null)return;
        var g=e.Graphics;
        using(var fill=new LinearGradientBrush(ClientRectangle,Color.FromArgb(249,249,252),Color.FromArgb(232,237,242),90))g.FillRectangle(fill,ClientRectangle);
        float scale=Math.Max(.1f,Math.Min((Width-28)/352f,(Height-24)/306f));
        var saved=g.Save();g.TranslateTransform((Width-352*scale)/2,(Height-306*scale)/2-110*scale);g.ScaleTransform(scale,scale);
        Scene.Draw(g,Time,Resting,true);g.Restore(saved);
    }
}

internal sealed class PetWardrobeWindow : KnightWindow
{
    readonly KeyboardPetSprites sprites;
    readonly BoundPetScene scene;
    readonly InputFeedbackState input=new InputFeedbackState();
    readonly WardrobePreview preview=new WardrobePreview();
    readonly ComboBox hair=new ComboBox(),keyboard=new ComboBox(),mouse=new ComboBox(),pad=new ComboBox(),headwear=new ComboBox(),musicStyle=new ComboBox(),listening=new ComboBox(),ornament=new ComboBox(),palette=new ComboBox(),mode=new ComboBox();
    readonly Label status=new Label();
    readonly KnightButton[] outfitButtons=new KnightButton[3];
    readonly Font uiFont=new Font("Microsoft YaHei UI",14,FontStyle.Regular,GraphicsUnit.Pixel);
    readonly Action<int,int,int,int,int,int,int,int,int> apply;
    readonly bool readOnly;
    readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
    readonly Stopwatch clock=Stopwatch.StartNew();
    bool released;
    int swordBeat;
    double previous,nextTap;
    internal PetWardrobeWindow(string root,int hairStyle,Action<int> apply,bool readOnly=false)
        : this(root,hairStyle,0,0,0,0,0,0,0,(h,k,m,d,w,p,a,l)=>apply(h),readOnly) {}
    internal PetWardrobeWindow(string root,int hairStyle,int keyboardStyle,int mouseStyle,int padStyle,int headwearStyle,int colorPalette,int musicBackdrop,int listeningStyle,Action<int,int,int,int,int,int,int,int> apply,bool readOnly=false)
        : this(root,hairStyle,keyboardStyle,mouseStyle,padStyle,headwearStyle,colorPalette,musicBackdrop,listeningStyle,0,(h,k,m,d,w,p,a,l,o)=>apply(h,k,m,d,w,p,a,l),readOnly) {}
    internal PetWardrobeWindow(string root,int hairStyle,int keyboardStyle,int mouseStyle,int padStyle,int headwearStyle,int colorPalette,int musicBackdrop,int listeningStyle,int ornamentStyle,Action<int,int,int,int,int,int,int,int,int> apply,bool readOnly=false)
    {
        this.apply=apply;this.readOnly=readOnly;Font=uiFont;Text="皮肤";
        ClientSize=new Size(860,670);MinimumSize=new Size(800,630);StartPosition=FormStartPosition.CenterScreen;
        sprites=new KeyboardPetSprites(root);scene=new BoundPetScene(sprites,new PetInputBindings(),input);scene.SetViewWidth(BoundPetScene.CompactWidth);
        preview.Scene=scene;
        var layout=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Padding=new Padding(18,14,18,16)};
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,324));layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        var left=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=6,Padding=new Padding(0,0,14,0)};
        foreach(float height in new[]{32f,52f,396f,0f,48f,48f})left.RowStyles.Add(height==0 ? new RowStyle(SizeType.Percent,100) : new RowStyle(SizeType.Absolute,height));
        var presets=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=4,RowCount=1,Margin=Padding.Empty};
        presets.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,70));
        presets.Controls.Add(new Label {Text="整体套装",Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,ForeColor=KnightUi.Ink,Margin=Padding.Empty},0,0);
        for(int i=0;i<3;i++)
        {
            presets.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100f/3));int outfit=i;
            var button=new KnightButton {Text=new[]{"经典","海盗","赛博"}[i],Dock=DockStyle.Fill,Margin=new Padding(3,0,3,2),AccessibleName="预览"+new[]{"经典","海盗","赛博"}[i]+"套装"};
            button.Click+=(s,e)=>SelectOutfit(outfit);outfitButtons[i]=button;presets.Controls.Add(button,i+1,0);
        }
        left.Controls.Add(presets,0,0);
        left.Controls.Add(new Label {Text="整体配色统一基调，造型独立搭配。\n持续打字时戴上所选头饰。",Dock=DockStyle.Fill,ForeColor=KnightUi.Steel},0,1);
        var fields=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=2,RowCount=9,Margin=Padding.Empty};
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,70));fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        AddChoice(fields,0,"整体配色",palette,new object[]{"经典","海盗","赛博"},PaletteIndex(colorPalette));
        AddChoice(fields,1,"发型",hair,new object[]{"利落短发","层次长发","蓬松双马尾"},PetHairStyles.Normalize(hairStyle));
        AddChoice(fields,2,"键盘",keyboard,new object[]{"骑士键盘","羊皮卷","悬浮投影键盘"},KeyboardIndex(keyboardStyle));
        AddChoice(fields,3,"鼠标",mouse,new object[]{"骑士鼠标","骑士剑鼠标","全息投影"},MouseIndex(mouseStyle));
        AddChoice(fields,4,"鼠标垫",pad,new object[]{"骑士鼠标垫","航海图","全息电路桌垫"},PetPadStyles.Normalize(padStyle));
        AddChoice(fields,5,"打字头饰",headwear,new object[]{"骑士头盔","海盗帽","战术全息目镜"},PetHeadwearStyles.Normalize(headwearStyle));
        AddChoice(fields,6,"音律",musicStyle,new object[]{"节奏光柱","海浪波纹","黑客代码流"},PetMusicStyles.Normalize(musicBackdrop));
        AddChoice(fields,7,"听歌配饰",listening,new object[]{"原版耳机","头巾","神经音频模块"},PetListeningStyles.Normalize(listeningStyle));
        AddChoice(fields,8,"桌饰",ornament,new object[]{"无桌饰","原版骑士","海盗船","高达"},PetDeskOrnaments.Normalize(ornamentStyle));
        foreach(var choice in new[]{hair,keyboard,mouse,pad,headwear,palette,musicStyle,listening,ornament})choice.SelectedIndexChanged+=(s,e)=>UpdatePreview();
        left.Controls.Add(fields,0,2);
        var buttons=new FlowLayoutPanel {Dock=DockStyle.Fill,WrapContents=false,Margin=Padding.Empty};
        var wear=new KnightButton {Text="应用并保存",Primary=true,Size=new Size(142,36),Margin=new Padding(0,4,8,0),Enabled=!readOnly};
        wear.Click+=(s,e)=>ApplySelection();var close=new KnightButton {Text="关闭",Size=new Size(142,36),Margin=new Padding(0,4,0,0)};close.Click+=(s,e)=>Close();
        buttons.Controls.AddRange(new Control[]{wear,close});left.Controls.Add(buttons,0,4);
        status.Dock=DockStyle.Fill;status.ForeColor=KnightUi.Steel;status.Text=readOnly ? "预览模式，不保存个人设置。" : "预览满意后点击“应用并保存”。";left.Controls.Add(status,0,5);
        var right=new TableLayoutPanel {Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(8,0,0,0)};
        right.RowStyles.Add(new RowStyle(SizeType.Absolute,32));right.RowStyles.Add(new RowStyle(SizeType.Percent,100));right.RowStyles.Add(new RowStyle(SizeType.Absolute,43));
        right.Controls.Add(new Label {Text="动态预览 · 自由搭配",Dock=DockStyle.Fill,ForeColor=KnightUi.Steel},0,0);
        preview.Dock=DockStyle.Fill;right.Controls.Add(preview,0,1);
        mode.DropDownStyle=ComboBoxStyle.DropDownList;mode.Dock=DockStyle.Fill;mode.Items.AddRange(new object[]{"预览：待机","预览：打字与头饰","预览：音乐律动","预览：休息","预览：鼠标动作 / 剑光","预览：边打字边听音乐"});mode.SelectedIndex=0;
        mode.SelectedIndexChanged+=(s,e)=>{input.Clear();swordBeat=0;nextTap=clock.Elapsed.TotalSeconds;input.Move(0,0,nextTap);};right.Controls.Add(mode,0,2);
        layout.Controls.Add(left,0,0);layout.Controls.Add(right,1,0);Controls.Add(layout);layout.BringToFront();
        UpdatePreview();timer.Interval=50;timer.Tick+=Tick;timer.Start();
    }
    static void AddChoice(TableLayoutPanel fields,int row,string label,ComboBox choice,object[] items,int selected)
    {
        fields.RowStyles.Add(new RowStyle(SizeType.Percent,100f/fields.RowCount));
        choice.DropDownStyle=ComboBoxStyle.DropDownList;choice.Dock=DockStyle.Fill;choice.Margin=new Padding(3,10,3,0);
        choice.Items.AddRange(items);choice.AccessibleName="选择"+label;choice.SelectedIndex=selected;
        fields.Controls.Add(new Label {Text=label,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,row);fields.Controls.Add(choice,1,row);
    }
    void UpdatePreview()
    {
        scene.Portrait.SetHairStyle(hair.SelectedIndex);scene.SetKeyboardStyle(SelectedKeyboardStyle);scene.SetMouseStyle(SelectedMouseStyle);scene.SetPadStyle(pad.SelectedIndex);scene.Portrait.SetHeadwearStyle(headwear.SelectedIndex);scene.SetTone(SelectedPalette);scene.SetMusicStyle(musicStyle.SelectedIndex);scene.Portrait.SetListeningStyle(listening.SelectedIndex);scene.SetOrnamentStyle(ornament.SelectedIndex);preview.Invalidate();
        status.Text=readOnly ? "预览模式，不保存个人设置。" : "正在预览搭配；点击“应用并保存”后生效。";
        status.ForeColor=KnightUi.Steel;
    }
    internal void SelectHairStyle(int style){hair.SelectedIndex=PetHairStyles.Normalize(style);}
    static int KeyboardIndex(int style){style=PetKeyboardStyles.Normalize(style);return style==PetKeyboardStyles.Cyberpunk ? 2 : style==PetKeyboardStyles.Parchment ? 1 : 0;}
    int SelectedKeyboardStyle { get { return keyboard.SelectedIndex==2 ? PetKeyboardStyles.Cyberpunk : keyboard.SelectedIndex==1 ? PetKeyboardStyles.Parchment : PetKeyboardStyles.Original; } }
    internal void SelectKeyboardStyle(int style){keyboard.SelectedIndex=KeyboardIndex(style);}
    internal void SelectCyberpunk()
    {SelectOutfit(2);}
    internal void SelectOutfit(int outfit)
    {
        bool pirate=outfit==1,cyber=outfit==2;
        SelectHairStyle(cyber ? PetHairStyles.TwinPonytails : pirate ? PetHairStyles.Short : PetHairStyles.Reference);
        SelectPalette(cyber ? PetSkinStyles.Cyber : pirate ? PetSkinStyles.Pirate : PetSkinStyles.Classic);
        SelectKeyboardStyle(cyber ? PetKeyboardStyles.Cyberpunk : pirate ? PetKeyboardStyles.Parchment : PetKeyboardStyles.Original);
        SelectMouseStyle(cyber ? PetMouseStyles.Cyberpunk : pirate ? PetMouseStyles.KnightSword : PetMouseStyles.Original);
        SelectPadStyle(cyber ? PetPadStyles.Cyberpunk : pirate ? PetPadStyles.NavigationChart : PetPadStyles.Original);
        SelectHeadwearStyle(cyber ? PetHeadwearStyles.CyberVisor : pirate ? PetHeadwearStyles.PirateHat : PetHeadwearStyles.Helmet);
        SelectMusicStyle(cyber ? PetMusicStyles.HackerCode : pirate ? PetMusicStyles.OceanWaves : PetMusicStyles.LevelBars);
        SelectListeningStyle(cyber ? PetListeningStyles.NeuralAudio : pirate ? PetListeningStyles.Bandana : PetListeningStyles.Headphones);
        SelectOrnamentStyle(cyber ? PetDeskOrnaments.Gundam : pirate ? PetDeskOrnaments.PirateShip : PetDeskOrnaments.Knight);
    }
    internal void SelectPadStyle(int style){pad.SelectedIndex=PetPadStyles.Normalize(style);}
    internal void SelectHeadwearStyle(int style){headwear.SelectedIndex=PetHeadwearStyles.Normalize(style);}
    static int MouseIndex(int style){style=PetMouseStyles.Normalize(style);return style==PetMouseStyles.Cyberpunk ? 2 : style;}
    int SelectedMouseStyle { get { return mouse.SelectedIndex==2 ? PetMouseStyles.Cyberpunk : mouse.SelectedIndex; } }
    internal void SelectMouseStyle(int style){mouse.SelectedIndex=MouseIndex(style);}
    internal void SelectMusicStyle(int style){musicStyle.SelectedIndex=PetMusicStyles.Normalize(style);}
    internal void SelectListeningStyle(int style){listening.SelectedIndex=PetListeningStyles.Normalize(style);}
    internal void SelectOrnamentStyle(int style){ornament.SelectedIndex=PetDeskOrnaments.Normalize(style);}
    static int PaletteIndex(int style){style=PetSkinStyles.Normalize(style);return style==PetSkinStyles.Cyber ? 2 : style;}
    int SelectedPalette { get { return palette.SelectedIndex==2 ? PetSkinStyles.Cyber : palette.SelectedIndex; } }
    internal void SelectPalette(int style){palette.SelectedIndex=PaletteIndex(style);}
    internal void ApplySelection()
    {
        if(readOnly)return;
        try {apply(hair.SelectedIndex,SelectedKeyboardStyle,SelectedMouseStyle,pad.SelectedIndex,headwear.SelectedIndex,SelectedPalette,musicStyle.SelectedIndex,listening.SelectedIndex,ornament.SelectedIndex);status.Text="已应用并保存当前搭配。";status.ForeColor=KnightUi.Steel;}
        catch(Exception error){status.Text="未保存："+error.Message;status.ForeColor=KnightUi.Red;}
    }
    void Tick(object sender,EventArgs e)
    {
        double now=clock.Elapsed.TotalSeconds,dt=Math.Min(.1,Math.Max(0,now-previous));previous=now;
        bool typing=mode.SelectedIndex==1||mode.SelectedIndex==5,resting=mode.SelectedIndex==3;
        if(typing&&now>=nextTap){int code=(int)(now*7)%2==0 ? 65 : 74;input.Key(code,true,now);input.Key(code,false,now+.003);nextTap=now+(mode.SelectedIndex==5 ? .1 : .14);}
        if(mode.SelectedIndex==4)
        {
            input.Move((int)(Math.Sin(now*1.8)*80),(int)(Math.Cos(now*1.8)*45),now);
            if(now>=nextTap)
            {
                int beat=swordBeat++%6;
                if(beat<2||beat==4){int button=beat==4 ? 3 : beat+1;input.Button(button,true,now);input.Button(button,false,now+.003);}
                else input.Wheel(beat==3 ? -120 : 120,beat==5,now);
                nextTap=now+.65;
            }
        }
        scene.MusicPlaying=mode.SelectedIndex==2||mode.SelectedIndex==5;scene.MusicLevel=scene.MusicPlaying ? (float)(.16+.09*Math.Sin(now*3.2)) : 0;
        scene.Advance(now,dt,resting);preview.Time=now;preview.Resting=resting;preview.Invalidate();
    }
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);var work=Screen.FromControl(this).WorkingArea;
        int width=Math.Min(Width,work.Width-24),height=Math.Min(Height,work.Height-24);
        if(width!=Width||height!=Height)
        {
            MinimumSize=new Size(Math.Min(MinimumSize.Width,width),Math.Min(MinimumSize.Height,height));Size=new Size(width,height);
            Location=new Point(work.Left+(work.Width-Width)/2,work.Top+(work.Height-Height)/2);
        }
    }
    protected override void Dispose(bool disposing)
    {
        if(disposing&&!released){released=true;timer.Stop();timer.Dispose();preview.Scene=null;scene.Dispose();sprites.Dispose();}
        base.Dispose(disposing);if(disposing)uiFont.Dispose();
    }
}
