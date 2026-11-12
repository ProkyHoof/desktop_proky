using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

internal static class PetDeskProjection
{
    internal const float Lift = 14;
    internal static PointF DeskPoint(float x,float y) { return new PointF(x,y-Lift); }
    internal static readonly PointF[] Keyboard = { DeskPoint(115,355), DeskPoint(282,361), DeskPoint(291,402), DeskPoint(103,395) };
    internal static readonly PointF[] MousePad = { DeskPoint(35,340), DeskPoint(147,346), DeskPoint(124,399), DeskPoint(8,392) };
    internal static PointF Project(float x, float y)
    {
        // A real 180-degree desk rotation: the spacebar faces the character.
        float u = 1 - x / 15.7f, v = 1 - y / 6.65f;
        return new PointF((1-u)*(1-v)*Keyboard[0].X + u*(1-v)*Keyboard[1].X + u*v*Keyboard[2].X + (1-u)*v*Keyboard[3].X,
            (1-u)*(1-v)*Keyboard[0].Y + u*(1-v)*Keyboard[1].Y + u*v*Keyboard[2].Y + (1-u)*v*Keyboard[3].Y);
    }
    internal static GraphicsPath Polygon(PointF[] points)
    {
        var path = new GraphicsPath(); path.AddPolygon(points); return path;
    }
}

internal sealed class PetKeyCap
{
    internal int Code;
    internal string Label;
    internal float X, Y, W, H;
    internal PointF[] Corners
    {
        get
        {
            float left = X + .28f, top = Y + .16f, right = left + W - .13f, bottom = top + H - .19f;
            return new[] { PetDeskProjection.Project(left, top), PetDeskProjection.Project(right, top),
                PetDeskProjection.Project(right, bottom), PetDeskProjection.Project(left, bottom) };
        }
    }
    internal PointF Center
    {
        get { PointF[] p = Corners; return new PointF(p.Average(v => v.X), p.Average(v => v.Y)); }
    }
}

internal static class PetKeyboardLayout
{
    internal static readonly PetKeyCap[] Keys = Create();
    static PetKeyCap[] Create()
    {
        var keys = new List<PetKeyCap>();
        Action<int, string, float, float, float> add = (code, label, x, y, w) =>
            keys.Add(new PetKeyCap { Code = code, Label = label, X = x, Y = y, W = w, H = 1 });
        add(27, "Esc", 0, 0, 1);
        for (int i = 0; i < 12; i++) add(112 + i, "F" + (i + 1), 1.45f + i * 1.13f, 0, 1);
        int[][] rows = {
            new[] {192,49,50,51,52,53,54,55,56,57,48,189,187,8},
            new[] {9,81,87,69,82,84,89,85,73,79,80,219,221,220},
            new[] {20,65,83,68,70,71,72,74,75,76,186,222,13},
            new[] {160,90,88,67,86,66,78,77,188,190,191,161,38},
            new[] {162,91,164,32,165,92,93,163,37,40,39}
        };
        float[] bottomWidths = {1.25f,1,1.25f,4.75f,1,1,1,1,1,1,1};
        for (int row = 0; row < rows.Length; row++)
        {
            float x = 0;
            for (int n = 0; n < rows[row].Length; n++)
            {
                int code = rows[row][n]; float w = 1;
                if (row == 0 && code == 8) w = 2;
                if (row == 1 && (code == 9 || code == 220)) w = 1.5f;
                if (row == 2 && code == 20) w = 1.75f;
                if (row == 2 && code == 13) w = 2.25f;
                if (row == 3 && code == 160) w = 2.25f;
                if (row == 4) w = bottomWidths[n];
                string label = code == 8 ? "Back" : code == 20 ? "Caps" : code == 32 ? "Space" :
                    code == 160 ? "LShift" : code == 161 ? "RShift" : code == 162 ? "LCtrl" : code == 163 ? "RCtrl" :
                    code == 164 ? "LAlt" : code == 165 ? "RAlt" : InputFeedbackState.KeyName(code);
                add(code, label, x, 1.25f + row * 1.05f, w); x += w;
            }
        }
        return keys.ToArray();
    }
    internal static PetKeyCap Find(int code) { return Keys.FirstOrDefault(k => k.Code == code); }
    // Sources from a physical full-size keyboard can still drive the compact visual.
    internal static int CompactTarget(int code)
    {
        if (Find(code) != null || code == 0) return code;
        if (code >= 96 && code <= 105) return 48 + code - 96;
        switch (code)
        {
            case 269: return 13; case 110: return 190; case 111: return 191;
            case 106: return 56; case 107: return 187; case 109: return 189;
            case 44: case 145: case 19: case 45: case 36: case 33: case 46: case 35: case 34: case 144: return 0;
            default: return -1;
        }
    }
}

internal sealed class PetInputBindings
{
    internal readonly Dictionary<int, int> Map = new Dictionary<int, int>();
    internal double MouseGain = .35;
    internal PetInputBindings()
    {
        foreach (PetKeyCap key in PetKeyboardLayout.Keys) Map[key.Code] = key.Code;
        Map[16] = 160; Map[17] = 162; Map[18] = 164;
        for (int code = 96; code <= 111; code++) if (PetKeyboardLayout.CompactTarget(code) >= 0) Map[code] = PetKeyboardLayout.CompactTarget(code);
        Map[269] = 13;
        foreach (int code in new[] { 44,145,19,45,36,33,46,35,34,144 }) Map[code] = 0;
    }
    internal int Resolve(int source) { int target; return Map.TryGetValue(source, out target) ? target : 0; }
    internal static PetInputBindings Load(string path)
    {
        var result = new PetInputBindings();
        if (!File.Exists(path)) return result;
        int lineNumber = 0;
        foreach (string raw in File.ReadAllLines(path))
        {
            lineNumber++; string line = raw.Trim();
            if (line.StartsWith("# mouse_gain="))
            {
                double gain;
                if (!Double.TryParse(line.Substring(13), NumberStyles.Float, CultureInfo.InvariantCulture, out gain) || gain < .05 || gain > 1)
                    throw new InvalidDataException("鼠标绑定灵敏度应为 0.05 到 1。");
                result.MouseGain = gain; continue;
            }
            if (line.Length == 0 || line.StartsWith("#")) continue;
            string[] parts = line.Split('\t'); int source, target;
            if (parts.Length < 2 || !Int32.TryParse(parts[0], out source) || !Int32.TryParse(parts[1], out target) ||
                source < 8 || source > 269 || PetKeyboardLayout.CompactTarget(target) < 0)
                throw new InvalidDataException("按键绑定第 " + lineNumber + " 行不正确。");
            result.Map[source] = PetKeyboardLayout.CompactTarget(target);
        }
        return result;
    }
    internal void Save(string path)
    {
        var lines = new List<string> { "# Pet visual bindings only: source VK<TAB>target VK. 0 = unbound.",
            "# mouse_gain=" + MouseGain.ToString("0.00", CultureInfo.InvariantCulture) };
        lines.AddRange(Map.OrderBy(p => p.Key).Select(p => p.Key + "\t" + p.Value + "\t# " + InputFeedbackState.KeyName(p.Key)));
        string temp = path + ".tmp";
        try
        {
            File.WriteAllLines(temp, lines.ToArray(), System.Text.Encoding.UTF8);
            if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

internal sealed class PetBindingEditor : KnightDialog
{
    sealed class Choice { public int Code { get; set; } public string Label { get; set; } }
    readonly DataGridView grid = new DataGridView();
    readonly KnightSlider gain = new KnightSlider();
    readonly Label gainLabel = new Label();
    readonly PetInputBindings bindings;
    readonly string path;
    internal PetBindingEditor(PetInputBindings bindings, string path)
    {
        this.bindings = bindings; this.path = path;
        Text = Caption = "按键绑定与鼠标反馈";
        Subtitle = "仅改变桌宠动作，不影响系统按键";
        ClientSize = new Size(620, 676);
        var note = new Label { Text = "键位映射    /    实际按键 → 桌宠手部落点", BackColor = Color.Transparent, AutoSize = false, Bounds = new Rectangle(26, 101, 568, 26) };
        Controls.Add(note);
        grid.Bounds = new Rectangle(26, 134, 568, 340); grid.AllowUserToAddRows = grid.AllowUserToDeleteRows = false;
        grid.RowHeadersVisible = false; grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.BackgroundColor = KnightUi.Paper; grid.BorderStyle = BorderStyle.None; grid.GridColor = KnightUi.Silver;
        grid.EnableHeadersVisualStyles = false; grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing; grid.ColumnHeadersHeight = 36;
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = KnightUi.Ink, ForeColor = KnightUi.OnDark,
            SelectionBackColor = KnightUi.Ink, SelectionForeColor = KnightUi.OnDark, Padding = new Padding(10, 0, 0, 0) };
        grid.DefaultCellStyle = new DataGridViewCellStyle { BackColor = KnightUi.Paper, ForeColor = KnightUi.Ink,
            SelectionBackColor = KnightUi.Ink, SelectionForeColor = KnightUi.OnDark, Padding = new Padding(10, 2, 4, 2) };
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(243, 243, 242);
        grid.RowTemplate.Height = 30; grid.AllowUserToResizeRows = false;
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "source", HeaderText = "实际按键", ReadOnly = true });
        var choices = new List<Choice> { new Choice { Code = 0, Label = "不绑定" } };
        choices.AddRange(PetKeyboardLayout.Keys.Select(k => new Choice { Code = k.Code, Label = k.Label + "  [" + k.Code + "]" }));
        grid.Columns.Add(new DataGridViewComboBoxColumn { Name = "target", HeaderText = "桌宠键位 / 手部落点", DataSource = choices,
            ValueMember = "Code", DisplayMember = "Label", FlatStyle = FlatStyle.Flat });
        grid.EditingControlShowing += (s, e) =>
        {
            var combo = e.Control as ComboBox;
            if (combo != null) { combo.BackColor = KnightUi.Paper; combo.ForeColor = KnightUi.Ink; combo.FlatStyle = FlatStyle.Flat; }
        };
        foreach (int code in bindings.Map.Keys.OrderBy(k => k))
        {
            PetKeyCap key = PetKeyboardLayout.Find(code);
            int row = grid.Rows.Add((key == null ? InputFeedbackState.KeyName(code) : key.Label) + "  [" + code + "]", bindings.Resolve(code));
            grid.Rows[row].Tag = code;
        }
        Controls.Add(grid);
        gainLabel.Bounds = new Rectangle(26, 506, 568, 26); gainLabel.BackColor = Color.Transparent; Controls.Add(gainLabel);
        gain.Bounds = new Rectangle(26, 541, 568, 34); gain.Minimum = 5; gain.Maximum = 100;
        gain.Value = (int)Math.Round(bindings.MouseGain * 100); gain.ValueChanged += (s, e) => UpdateGainLabel(); Controls.Add(gain); UpdateGainLabel();
        var reset = new KnightButton { Text = "恢复默认", Bounds = new Rectangle(26, 618, 112, 36) };
        reset.Click += (s, e) => { var defaults = new PetInputBindings(); foreach (DataGridViewRow row in grid.Rows) row.Cells[1].Value = defaults.Resolve((int)row.Tag); gain.Value = 35; };
        var save = new KnightButton { Text = "保存绑定", Primary = true, Bounds = new Rectangle(348, 618, 118, 36) };
        save.Click += (s, e) =>
        {
            grid.EndEdit(); var candidate = new PetInputBindings { MouseGain = gain.Value / 100.0 };
            foreach (DataGridViewRow row in grid.Rows) candidate.Map[(int)row.Tag] = Convert.ToInt32(row.Cells[1].Value);
            try { candidate.Save(path); }
            catch (IOException error) { KnightUi.ShowError(this, "保存绑定失败", error.Message); return; }
            catch (UnauthorizedAccessException error) { KnightUi.ShowError(this, "保存绑定失败", error.Message); return; }
            bindings.Map.Clear(); foreach (var pair in candidate.Map) bindings.Map[pair.Key] = pair.Value;
            bindings.MouseGain = candidate.MouseGain; DialogResult = DialogResult.OK; Close();
        };
        var cancel = new KnightButton { Text = "取消", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(480, 618, 114, 36) };
        Controls.AddRange(new Control[] { reset, save, cancel }); AcceptButton = save; CancelButton = cancel;
        // Follow the visual order when navigating the custom chrome with Tab.
        grid.TabIndex = 0; gain.TabIndex = 1; reset.TabIndex = 2; save.TabIndex = 3; cancel.TabIndex = 4;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);float scale=DeviceDpi/96f;var state=e.Graphics.Save();e.Graphics.ScaleTransform(scale,scale);
        KnightUi.Armor(e.Graphics,new RectangleF(18,126,584,356),255,false,8);
        KnightUi.Armor(e.Graphics,new RectangleF(18,496,584,90),255,false,8);
        e.Graphics.Restore(state);
    }
    void UpdateGainLabel() { gainLabel.Text = "鼠标位移增幅：" + (gain.Value / 100.0).ToString("0.00") + "（越大，鼠标手移动越明显）"; }
}

internal sealed class WholeArmSprite : IDisposable
{
    internal readonly Bitmap Image;
    Bitmap tonedImage;
    int tone;
    internal void SetTone(int value)
    {
        if(value==tone)return;
        var replacement=value==0 ? null : PetSkinStyles.ApplyTone(Image,value);
        if(tonedImage!=null)tonedImage.Dispose();tonedImage=replacement;tone=value;
    }
    internal readonly PointF Shoulder, Fingertip;
    internal const float WorldWidth = 44;
    internal WholeArmSprite(Bitmap image,bool left)
    {
        Image=image;
        Shoulder=new PointF(image.Width*.50f,image.Height*.08f);
        Fingertip=new PointF(image.Width*(left ? .46f : .54f),image.Height*.98f);
    }
    static Rectangle ContentBounds(Bitmap atlas,Rectangle tile)
    {
        int x0=tile.Width,y0=tile.Height,x1=-1,y1=-1;
        BitmapData data=atlas.LockBits(tile,ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
        try
        {
            byte[] row=new byte[tile.Width*4];
            for(int y=0;y<tile.Height;y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(IntPtr.Add(data.Scan0,y*data.Stride),row,0,row.Length);
                for(int x=0;x<tile.Width;x++) if(row[x*4+3]>32)
                {x0=Math.Min(x0,x);x1=Math.Max(x1,x);y0=Math.Min(y0,y);y1=Math.Max(y1,y);}
            }
        }
        finally {atlas.UnlockBits(data);}
        if(x1<x0)throw new InvalidDataException("一体手臂素材为空。");
        x0=Math.Max(0,x0-2);y0=Math.Max(0,y0-2);x1=Math.Min(tile.Width-1,x1+2);y1=Math.Min(tile.Height-1,y1+2);
        return new Rectangle(tile.X+x0,tile.Y+y0,x1-x0+1,y1-y0+1);
    }
    internal static WholeArmSprite[] LoadPair(string root)
    {
        var pair=new WholeArmSprite[2];
        try
        {
            using(var atlas=new Bitmap(Path.Combine(root,"assets","keyboard-knight","soft-style-arms.png")))
            for(int i=0;i<2;i++)
            {
                int x=i*atlas.Width/2, right=(i+1)*atlas.Width/2;
                Rectangle bounds=ContentBounds(atlas,new Rectangle(x,0,right-x,atlas.Height));
                // Arms render about 44 scene pixels wide. Cache a 3x premultiplied
                // texture once instead of shrinking the large atlas every frame.
                int width=Math.Min(bounds.Width,132),height=Math.Max(1,(int)Math.Round(bounds.Height*width/(double)bounds.Width));
                var texture=new Bitmap(width,height,PixelFormat.Format32bppPArgb);
                try
                {
                    using(var g=Graphics.FromImage(texture))
                    {
                        g.InterpolationMode=InterpolationMode.HighQualityBicubic;
                        g.DrawImage(atlas,new Rectangle(0,0,width,height),bounds,GraphicsUnit.Pixel);
                    }
                    pair[i]=new WholeArmSprite(texture,i==0);
                }
                catch{texture.Dispose();throw;}
            }
            return pair;
        }
        catch {foreach(var arm in pair)if(arm!=null)arm.Dispose();throw;}
    }
    // One affine transform drives the complete shoulder-to-finger silhouette.
    // There is no intermediate joint or separately transformed glove.
    internal PointF Map(PointF point,PointF shoulder,PointF fingertip)
    {
        double sx=Fingertip.X-Shoulder.X,sy=Fingertip.Y-Shoulder.Y,sourceLength=Math.Sqrt(sx*sx+sy*sy);
        double dx=fingertip.X-shoulder.X,dy=fingertip.Y-shoulder.Y,targetLength=Math.Max(1,Math.Sqrt(dx*dx+dy*dy));
        double sourceAX=sx/sourceLength,sourceAY=sy/sourceLength,targetAX=dx/targetLength,targetAY=dy/targetLength;
        double px=point.X-Shoulder.X,py=point.Y-Shoulder.Y;
        double across=(px*sourceAY-py*sourceAX)*WorldWidth/Image.Width;
        double along=(px*sourceAX+py*sourceAY)*targetLength/sourceLength;
        return new PointF((float)(shoulder.X+across*targetAY+along*targetAX),(float)(shoulder.Y-across*targetAX+along*targetAY));
    }
    internal void Draw(Graphics g,PointF shoulder,PointF fingertip)
    {
        PointF[] corners={Map(PointF.Empty,shoulder,fingertip),Map(new PointF(Image.Width,0),shoulder,fingertip),Map(new PointF(0,Image.Height),shoulder,fingertip)};
        g.DrawImage(tonedImage??Image,corners,new RectangleF(0,0,Image.Width,Image.Height),GraphicsUnit.Pixel);
    }
    public void Dispose() {if(tonedImage!=null)tonedImage.Dispose();Image.Dispose();}
}

internal sealed class CoordinatedPetHands
{
    internal static readonly PointF MouseHome = PetDeskProjection.DeskPoint(74,371), RightRest = PetDeskProjection.DeskPoint(241,375);
    readonly PetInputBindings bindings;
    readonly InputFeedbackState input;
    HashSet<int> previousHeld = new HashSet<int>();
    double releasedAt = -100;
    internal PointF Mouse = MouseHome, LeftContact = MouseHome, RightContact = RightRest;
    internal PointF LeftPalm, RightPalm;
    internal float LeftLift = 0, RightLift = 3, TypingBlend;
    internal float MousePressLeft, MousePressRight, MouseScroll;
    internal bool Typing;
    internal int LeftSource, RightSource;
    internal CoordinatedPetHands(PetInputBindings bindings, InputFeedbackState input)
    {
        this.bindings=bindings; this.input=input; UpdatePalms();
    }
    internal static PointF Approach(PointF from,PointF to,double rate,double dt)
    {
        float ease=(float)(1-Math.Exp(-rate*Math.Max(0,dt)));
        PointF result=new PointF(from.X+(to.X-from.X)*ease,from.Y+(to.Y-from.Y)*ease);
        return Math.Abs(result.X-to.X)+Math.Abs(result.Y-to.Y)<.06 ? to : result;
    }
    static float Approach(float from,float to,double rate,double dt)
    {
        float next=from+(to-from)*(float)(1-Math.Exp(-rate*Math.Max(0,dt)));
        return Math.Abs(next-to)<.025 ? to : next;
    }
    internal PointF TargetFor(int source)
    {
        PetKeyCap cap=PetKeyboardLayout.Find(bindings.Resolve(source));
        return cap==null ? PetKeyboardLayout.Find(70).Center : cap.Center;
    }
    internal bool UsesLeft(int source) { return TargetFor(source).X < PetDeskProjection.Project(7.85f,3.3f).X; }
    static double Distance(PointF a,PointF b) { return Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Y-b.Y)*(a.Y-b.Y)); }
    int SourceFor(bool left,double now,HashSet<int> held)
    {
        var candidates=input.KeyboardStrikes.Where(p => bindings.Resolve(p.Key)!=0 && UsesLeft(p.Key)==left &&
            (held.Contains(p.Key)||now-p.Value<.20)).OrderByDescending(p => p.Value).ToArray();
        return candidates.Length==0 ? 0 : candidates[0].Key;
    }
    double StrikeAge(int source,double now)
    {
        foreach(var p in input.KeyboardStrikes) if(p.Key==source) return now-p.Value;
        return 100;
    }
    void UpdatePalms()
    {
        float spread=Math.Max(0,(48-(RightContact.X-LeftContact.X))/2)*TypingBlend;
        LeftPalm=new PointF(LeftContact.X-spread,LeftContact.Y-9-LeftLift);
        // The pet faces the viewer: its left mouse button is on screen-right.
        LeftPalm.X+=(MousePressLeft-MousePressRight)*1.8f;
        LeftPalm.Y+=Math.Max(MousePressLeft,MousePressRight)*2.8f+MouseScroll;
        RightPalm=new PointF(RightContact.X+spread,RightContact.Y-9-RightLift);
    }
    internal bool Advance(double now,double dt,bool resting)
    {
        dt=Math.Max(0,Math.Min(.1,dt));
        PointF oldMouse=Mouse,oldLeft=LeftContact,oldRight=RightContact;
        float oldBlend=TypingBlend,oldLL=LeftLift,oldRL=RightLift;
        float oldML=MousePressLeft,oldMR=MousePressRight,oldScroll=MouseScroll;
        bool oldTyping=Typing;
        var held=new HashSet<int>(input.HeldKeys);
        var mapped=input.KeyboardStrikes.Where(p => bindings.Resolve(p.Key)!=0).ToArray();
        if(previousHeld.Except(held).Any(k => bindings.Resolve(k)!=0)) releasedAt=now;
        previousHeld=held;
        Typing=!resting && (held.Any(k => bindings.Resolve(k)!=0)||mapped.Any(p => now-p.Value<.32)||now-releasedAt<.32);
        TypingBlend=Approach(TypingBlend,Typing ? 1 : 0,16,dt);
        // The mouse stays parked while both hands are on the keyboard.
        if(!Typing && TypingBlend<.45f)
            Mouse=Approach(Mouse,new PointF(MouseHome.X+input.MouseOffset.X*.28f,MouseHome.Y+input.MouseOffset.Y*.20f),16,dt);
        LeftSource=SourceFor(true,now,held); RightSource=SourceFor(false,now,held);
        PointF leftKey=LeftSource==0 ? PetKeyboardLayout.Find(75).Center : TargetFor(LeftSource);
        PointF rightKey=RightSource==0 ? PetKeyboardLayout.Find(70).Center : TargetFor(RightSource);
        float arch=(float)Math.Sin(TypingBlend*Math.PI)*15;
        PointF leftTarget=new PointF(Mouse.X+(leftKey.X-Mouse.X)*TypingBlend,
            Mouse.Y+(leftKey.Y-Mouse.Y)*TypingBlend-arch);
        PointF rightTarget=new PointF(RightRest.X+(rightKey.X-RightRest.X)*TypingBlend,
            RightRest.Y+(rightKey.Y-RightRest.Y)*TypingBlend-arch*.3f);
        LeftContact=Approach(LeftContact,leftTarget,30,dt); RightContact=Approach(RightContact,rightTarget,34,dt);
        bool pressLeft=LeftSource!=0 && (held.Contains(LeftSource)||StrikeAge(LeftSource,now)<.10);
        bool pressRight=RightSource!=0 && (held.Contains(RightSource)||StrikeAge(RightSource,now)<.10);
        float liftLeft=pressLeft && StrikeAge(LeftSource,now)>.055 ? 0 : (float)Math.Min(7,Math.Sqrt(Distance(LeftContact,leftTarget))*.6)+(pressLeft ? 0 : 2.5f);
        float liftRight=pressRight && StrikeAge(RightSource,now)>.055 ? 0 : (float)Math.Min(7,Math.Sqrt(Distance(RightContact,rightTarget))*.6)+(pressRight ? 0 : 2.5f);
        // A small wrist stretch during the quiet nod, cancelled as typing resumes.
        float stretch=resting ? 0 : LivingPetPortrait.Gesture((now%32+32)%32,26,2.6)*3.5f;
        LeftLift=Approach(LeftLift,liftLeft*TypingBlend,30,dt);
        RightLift=Approach(RightLift,(3+stretch)*(1-TypingBlend)+liftRight*TypingBlend,34,dt);
        float mouseWeight=resting ? 0 : 1-TypingBlend;
        MousePressLeft=Approach(MousePressLeft,input.ButtonMotion(1,now)*mouseWeight,40,dt);
        MousePressRight=Approach(MousePressRight,input.ButtonMotion(2,now)*mouseWeight,40,dt);
        MouseScroll=mouseWeight*input.WheelFeedback(now)*(float)Math.Sin((now-input.WheelAt)*36)*Math.Sign(input.WheelDelta)*1.2f;
        UpdatePalms();
        return oldTyping!=Typing||Mouse!=oldMouse||LeftContact!=oldLeft||RightContact!=oldRight||
            oldBlend!=TypingBlend||oldLL!=LeftLift||oldRL!=RightLift||oldML!=MousePressLeft||oldMR!=MousePressRight||oldScroll!=MouseScroll;
    }
}

internal sealed class BoundPetScene : IDisposable
{
    internal const int WorldWidth = 480, WorldHeight = 460;
    internal static readonly RectangleF CharacterBounds = new RectangleF(12, 120, 332, 248);
    internal static readonly RectangleF KeyDisplayFrame = new RectangleF(44, 66, 268, 40);
    static readonly RectangleF BubbleBounds = new RectangleF(266, 209, 192, 44);
    static readonly Rectangle DeviceBounds=Rectangle.FromLTRB(
        (int)Math.Floor(PetDeskProjection.MousePad.Concat(PetDeskProjection.Keyboard).Min(p=>p.X))-3,
        (int)Math.Floor(PetDeskProjection.MousePad.Concat(PetDeskProjection.Keyboard).Min(p=>p.Y))-3,
        (int)Math.Ceiling(PetDeskProjection.MousePad.Concat(PetDeskProjection.Keyboard).Max(p=>p.X))+3,
        (int)Math.Ceiling(PetDeskProjection.MousePad.Concat(PetDeskProjection.Keyboard).Max(p=>p.Y))+11);
    // The original canvas reserves empty space to the right of the desk.
    // Native windows use the permanent artwork extent with a small margin;
    // previews can retain the full canvas.
    internal static int CompactWidth { get { return (int)Math.Ceiling(Math.Max(CharacterBounds.Right,DeviceBounds.Right))+8; } }
    internal int ViewWidth { get; private set; }
    internal bool DialogueActive;
    internal RectangleF CurrentKeyFrame { get { var r = KeyDisplayFrame; if (DialogueActive) r.Y = 408; return r; } }
    internal void SetViewWidth(int width){ViewWidth=Math.Max(CompactWidth,Math.Min(WorldWidth,width));}
    // Centers of the two black sleeve sockets in each 1024 x 768 atlas cell.
    internal static readonly PointF LeftShoulderSource = new PointF(405*300f/1024,590*229f/768),
        RightShoulderSource = new PointF(635*300f/1024,590*229f/768);
    readonly KeyboardPetSprites sprites;
    readonly PetInputBindings bindings;
    readonly InputFeedbackState input;
    // Supersample the small illustrated props once, retaining soft contours
    // at every window size without adding paths to the animation loop.
    const int DeviceScale = 3;
    readonly Bitmap devices = new Bitmap(DeviceBounds.Width*DeviceScale,DeviceBounds.Height*DeviceScale,PixelFormat.Format32bppPArgb);
    static readonly RectangleF SwordBounds=new RectangleF(-21,-20,42,87);
    Bitmap swordTexture,mouseTexture;
    Bitmap ornamentTexture,ornamentHitMask;
    internal static readonly RectangleF DeskOrnamentBounds=new RectangleF(294,320,50,75);
    internal int OrnamentStyle { get; private set; }
    internal void SetOrnamentStyle(int value)
    {
        value=PetDeskOrnaments.Normalize(value);if(value==OrnamentStyle)return;
        var replacement=PetDeskOrnaments.Create(value);
        Bitmap mask=null;
        try
        {
            if(replacement!=null)
            {
                mask=new Bitmap((int)DeskOrnamentBounds.Width,(int)DeskOrnamentBounds.Height,PixelFormat.Format32bppPArgb);
                using(var g=Graphics.FromImage(mask))
                {g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(replacement,new RectangleF(0,0,mask.Width,mask.Height),new RectangleF(0,0,replacement.Width,replacement.Height),GraphicsUnit.Pixel);}
            }
        }
        catch{if(replacement!=null)replacement.Dispose();if(mask!=null)mask.Dispose();throw;}
        if(ornamentTexture!=null)ornamentTexture.Dispose();if(ornamentHitMask!=null)ornamentHitMask.Dispose();
        ornamentTexture=replacement;ornamentHitMask=mask;OrnamentStyle=value;
    }
    internal void DrawOrnament(Graphics g)
    {
        if(ornamentTexture==null)return;var saved=g.Save();
        try{g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.DrawImage(ornamentTexture,DeskOrnamentBounds,new RectangleF(0,0,ornamentTexture.Width,ornamentTexture.Height),GraphicsUnit.Pixel);}
        finally{g.Restore(saved);}
    }
    bool HitOrnament(PointF p)
    {
        var r=DeskOrnamentBounds;if(ornamentHitMask==null||!r.Contains(p))return false;
        return ornamentHitMask.GetPixel((int)(p.X-r.X),(int)(p.Y-r.Y)).A>=32;
    }
    internal int MouseStyle { get; private set; }
    internal int PadStyle { get; private set; }
    internal int KeyboardStyle { get; private set; }
    internal int Tone { get; private set; }
    Color KeyboardColor(Color color){return PetSkinStyles.ToneColor(Tone,color);}
    Color MouseColor(Color color){return MouseStyle==PetMouseStyles.Cyberpunk&&color.R>color.G+24 ? PetSkinStyles.CyberPrimary(Tone,color.A) : PetSkinStyles.ToneColor(Tone,color);}
    Color MouseButtonColor(Color color,int button)
    {return MouseStyle==PetMouseStyles.Cyberpunk ? (button==1 ? PetSkinStyles.CyberPrimary(Tone,color.A) : PetSkinStyles.CyberSecondary(Tone,color.A)) : MouseColor(color);}
    internal void SetTone(int value)
    {
        value=PetSkinStyles.Normalize(value);if(Tone==value)return;
        var replacement=CreateSwordTexture(value);
        swordTexture.Dispose();swordTexture=replacement;
        mouseTexture.Dispose();mouseTexture=CreateMouseTexture(value,MouseStyle==PetMouseStyles.Cyberpunk);Tone=value;
        portrait.SetTone(value);foreach(var arm in sprites.Arms)arm.SetTone(value);RenderDevices();
    }
    internal void SetKeyboardStyle(int keyboard)
    {
        keyboard=PetKeyboardStyles.Normalize(keyboard);
        if(KeyboardStyle==keyboard)return;
        KeyboardStyle=keyboard;RenderDevices();
    }
    internal void SetPadStyle(int value)
    {value=PetPadStyles.Normalize(value);if(PadStyle==value)return;PadStyle=value;RenderDevices();}
    internal void SetMouseStyle(int mouseStyle)
    {
        mouseStyle=PetMouseStyles.Normalize(mouseStyle);if(mouseStyle==MouseStyle)return;
        var replacement=CreateMouseTexture(Tone,mouseStyle==PetMouseStyles.Cyberpunk);
        mouseTexture.Dispose();mouseTexture=replacement;MouseStyle=mouseStyle;
    }
    readonly Font keyFont = new Font("Arial", 3.5f, FontStyle.Bold, GraphicsUnit.Point);
    readonly Font inputFont = new Font("Microsoft YaHei UI", 11, FontStyle.Bold);
    readonly Font floatingFont = new Font("Microsoft YaHei UI", 10, FontStyle.Bold);
    readonly Font codeFont = new Font("Consolas",11,FontStyle.Bold,GraphicsUnit.Pixel);
    double codeScroll;
    static readonly string[] HackerCodeLines={
        "// uplink ready", "port = 0xF4;", "fn connect() {", "  net.open();",
        "  key=hash(id);", "  if (auth) {", "    read();", "    sync(key);",
        "  } else retry;", "  return OK;", "}", "root@neon:~$ run",
        "fn decode(b) {", "  for (i=0;", "    i<n; ++i) {", "    ptr+=0x10;",
        "    emit(b[i]);", "  }", "  return crc;", "}",
        "// encrypted", "link = ONLINE;", "await packet();", "buffer.flush();"
    };
    readonly Dictionary<int, float> lifts = new Dictionary<int, float>();
    sealed class TrailPoint { internal PointF Point; internal double At; }
    readonly List<TrailPoint> trail = new List<TrailPoint>();
    readonly CoordinatedPetHands hands;
    PointF mouse { get { return hands.Mouse; } }
    float mouseTilt { get { return 20+(hands.Mouse.X-74)*.1f; } }
    float swordTilt { get { return 28+(hands.Mouse.X-74)*.1f+(hands.MousePressLeft-hands.MousePressRight)*14; } }
    long lastRevision = -1;
    bool lastRest, hadMouseFeedback;
    readonly LivingPetPortrait portrait;
    double renderTime;
    internal int MusicStyle { get; private set; }
    internal void SetMusicStyle(int value){MusicStyle=PetMusicStyles.Normalize(value);}
    internal bool MusicPlaying;
    internal float MusicLevel;
    PointF pointer;
    bool trackingPointer;
    double pointerMovedAt=-100;
    internal void ObservePointer(PointF world,double now,bool enabled)
    {
        if(enabled&&(!trackingPointer||Math.Abs(world.X-pointer.X)+Math.Abs(world.Y-pointer.Y)>.5f))pointerMovedAt=now;
        pointer=world;trackingPointer=enabled;
    }
    internal LivingPetPortrait Portrait { get { return portrait; } }
    internal PointF Shoulder(bool left)
    {
        return portrait.WorldPoint(left ? LeftShoulderSource : RightShoulderSource);
    }
    internal CoordinatedPetHands Hands { get { return hands; } }
    internal PointF MousePosition { get { return hands.Mouse; } }
    internal BoundPetScene(KeyboardPetSprites sprites, PetInputBindings bindings, InputFeedbackState input)
    {
        ViewWidth=WorldWidth;
        this.sprites = sprites; this.bindings = bindings; this.input = input;
        hands=new CoordinatedPetHands(bindings,input);
        portrait=new LivingPetPortrait(sprites);
        swordTexture=CreateSwordTexture(0);mouseTexture=CreateMouseTexture(0);RenderDevices();
    }
    void RenderDevices()
    {
        using (var g = Graphics.FromImage(devices))
        {
            g.Clear(Color.Transparent); g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.ScaleTransform(DeviceScale,DeviceScale);
            g.TranslateTransform(-DeviceBounds.X,-DeviceBounds.Y);
            if(PadStyle==PetPadStyles.Cyberpunk)DrawCyberPad(g);
            else if(PadStyle==PetPadStyles.NavigationChart)DrawNavigationChart(g);
            else
            using (var pad = PetDeskProjection.Polygon(PetDeskProjection.MousePad))
            using (var fill = new SolidBrush(MouseColor(Color.FromArgb(41, 41, 49))))
            using (var edge = new Pen(MouseColor(Color.FromArgb(163, 38, 53)), 2)) { g.FillPath(fill, pad); g.DrawPath(edge, pad); }
            DrawKeyboardShell(g);
            foreach (var cap in PetKeyboardLayout.Keys) DrawCap(g, cap, false, 1);
        }
    }
    static PointF ChartPoint(float u,float v)
    {
        var p=PetDeskProjection.MousePad;
        return new PointF((1-u)*(1-v)*p[0].X+u*(1-v)*p[1].X+u*v*p[2].X+(1-u)*v*p[3].X,
            (1-u)*(1-v)*p[0].Y+u*(1-v)*p[1].Y+u*v*p[2].Y+(1-u)*v*p[3].Y);
    }
    void DrawCyberPad(Graphics g)
    {
        using(var pad=PetDeskProjection.Polygon(PetDeskProjection.MousePad))
        using(var surface=new LinearGradientBrush(new RectangleF(8,326,139,60),Color.FromArgb(28,39,65),Color.FromArgb(15,20,35),25f))
        using(var edge=new Pen(Color.FromArgb(11,16,29),2.5f))
        {
            g.FillPath(surface,pad);g.DrawPath(edge,pad);
            var saved=g.Save();g.SetClip(pad,CombineMode.Intersect);
            using(var grid=new Pen(PetSkinStyles.CyberPrimary(Tone,35),.55f))
            {
                for(int i=1;i<12;i++)g.DrawLine(grid,ChartPoint(i/12f,0),ChartPoint(i/12f,1));
                for(int i=1;i<7;i++)g.DrawLine(grid,ChartPoint(0,i/7f),ChartPoint(1,i/7f));
            }
            for(int i=0;i<4;i++)
            {
                float v=.14f+i*.19f;
                CyberpunkArt.Trace(g,PetSkinStyles.CyberPrimary(Tone,105),.7f,ChartPoint(0,v),ChartPoint(.18f+i*.05f,v),ChartPoint(.28f+i*.05f,v+.09f),ChartPoint(.56f,v+.09f));
                PointF node=ChartPoint(.56f,v+.09f);
                using(var via=new Pen(PetSkinStyles.CyberPrimary(Tone,180),.7f))g.DrawEllipse(via,node.X-1,node.Y-.65f,2,1.3f);
            }
            PointF center=ChartPoint(.73f,.53f);
            using(var target=new Pen(PetSkinStyles.CyberSecondary(Tone,160),.85f))
            {
                g.DrawEllipse(target,center.X-13,center.Y-7,26,14);g.DrawEllipse(target,center.X-9,center.Y-4.8f,18,9.6f);
                g.DrawLine(target,center.X-17,center.Y,center.X-11,center.Y);g.DrawLine(target,center.X+11,center.Y,center.X+17,center.Y);
            }
            CyberpunkArt.Trace(g,PetSkinStyles.CyberSecondary(Tone),1.4f,ChartPoint(.04f,.24f),ChartPoint(.04f,.06f),ChartPoint(.32f,.06f));
            CyberpunkArt.Trace(g,PetSkinStyles.CyberPrimary(Tone),1.4f,ChartPoint(.69f,.94f),ChartPoint(.96f,.94f),ChartPoint(.96f,.71f));
            using(var caution=new Pen(Color.FromArgb(231,206,112),1))
                for(int i=0;i<4;i++)g.DrawLine(caution,ChartPoint(.08f+i*.025f,.91f),ChartPoint(.1f+i*.025f,.84f));
            g.Restore(saved);
            using(var rim=new Pen(PetSkinStyles.CyberPrimary(Tone,145),.9f))g.DrawPath(rim,pad);
        }
    }
    void DrawCyberKeyboard(Graphics g)
    {
        // The opaque emitter deck sits below the separate translucent keys;
        // key coordinates remain on the hand contact plane.
        var corners=PetDeskProjection.Keyboard;
        using(var wall=SoftPolygon(corners.Select(p=>new PointF(p.X,p.Y+8)).ToArray(),1))
        using(var metal=new SolidBrush(Color.FromArgb(13,21,34)))g.FillPath(metal,wall);
        using(var deck=SoftPolygon(corners.Select(p=>new PointF(p.X,p.Y+5)).ToArray(),1))
        using(var surface=new LinearGradientBrush(new RectangleF(103,341,188,55),Color.FromArgb(35,54,74),Color.FromArgb(16,26,43),90f))
        using(var rim=new Pen(PetSkinStyles.CyberPrimary(Tone,190),.8f))
        {g.FillPath(surface,deck);g.DrawPath(rim,deck);}
        var saved=g.Save();g.TranslateTransform(0,5);
        try
        {
            CyberpunkArt.Trace(g,PetSkinStyles.CyberPrimary(Tone,230),1,ScrollPoint(.01f,.18f),ScrollPoint(.01f,.01f),ScrollPoint(.08f,.01f));
            CyberpunkArt.Trace(g,PetSkinStyles.CyberSecondary(Tone,230),1,ScrollPoint(.92f,.99f),ScrollPoint(.99f,.99f),ScrollPoint(.99f,.82f));
        }
        finally{g.Restore(saved);}
    }
    static Bitmap CreateCyberMouseTexture(int tone)
    {
        var image=new Bitmap(48,36,PixelFormat.Format32bppPArgb);
        using(var g=Graphics.FromImage(image))
        {
            g.ScaleTransform(3,3);g.TranslateTransform(8,6);g.SmoothingMode=SmoothingMode.AntiAlias;
            using(var body=CyberpunkArt.Panel(new RectangleF(-6,-4,12,8),2))
            using(var metal=new SolidBrush(Color.FromArgb(245,19,31,46)))
            using(var rim=new Pen(PetSkinStyles.CyberPrimary(tone,210),.65f))
            {g.FillPath(metal,body);g.DrawPath(rim,body);}
            using(var lens=new SolidBrush(PetSkinStyles.CyberPrimary(tone,235)))g.FillEllipse(lens,-2,-2,4,4);
            using(var ring=new Pen(PetSkinStyles.CyberSecondary(tone,200),.6f))g.DrawEllipse(ring,-3,-3,6,6);
        }
        return image;
    }
    static readonly RectangleF CyberProjectionFace=new RectangleF(-35,-65,70,39);
    internal PointF CyberMouseEmitter
    {
        get
        {
            var arm=sprites.Arms[0];
            return arm.Map(new PointF(arm.Image.Width*.50f,arm.Image.Height*.80f),Shoulder(true),new PointF(hands.LeftPalm.X,hands.LeftPalm.Y+9));
        }
    }
    float CyberProjectionBlend { get { return Math.Max(0,1-hands.TypingBlend); } }
    internal RectangleF CyberMouseScreen
    { get { var p=CyberMouseEmitter;float s=CyberProjectionBlend;var r=CyberProjectionFace;return new RectangleF(p.X+r.X*s,p.Y+r.Y*s,r.Width*s,r.Height*s); } }
    GraphicsState BeginCyberProjection(Graphics g)
    {var saved=g.Save();var p=CyberMouseEmitter;g.TranslateTransform(p.X,p.Y);g.ScaleTransform(CyberProjectionBlend,CyberProjectionBlend);return saved;}
    internal void DrawCyberMouseInstrument(Graphics g)
    {
        if(MouseStyle!=PetMouseStyles.Cyberpunk)return;
        var arm=sprites.Arms[0];var p=CyberMouseEmitter;
        var across=arm.Map(new PointF(arm.Image.Width*.65f,arm.Image.Height*.80f),Shoulder(true),new PointF(hands.LeftPalm.X,hands.LeftPalm.Y+9));
        var saved=g.Save();
        try
        {
            g.TranslateTransform(p.X,p.Y);g.RotateTransform((float)(Math.Atan2(across.Y-p.Y,across.X-p.X)*180/Math.PI));
            g.DrawImage(mouseTexture,new RectangleF(-8,-6,16,12),new RectangleF(0,0,mouseTexture.Width,mouseTexture.Height),GraphicsUnit.Pixel);
        }
        finally{g.Restore(saved);}
    }
    void DrawCyberMouse(Graphics g,double now)
    {
        if(CyberProjectionBlend<.02f)return;
        var saved=BeginCyberProjection(g);
        try
        {
            var r=CyberProjectionFace;
            using(var beam=new GraphicsPath())
            using(var light=new LinearGradientBrush(new RectangleF(r.X,r.Top,r.Width,-r.Top),PetSkinStyles.CyberPrimary(Tone,12),PetSkinStyles.CyberPrimary(Tone,75),90f))
            using(var edge=new Pen(PetSkinStyles.CyberPrimary(Tone,90),.6f))
            {
                beam.AddLine(PointF.Empty,new PointF(r.Left,r.Top+r.Height/2));beam.AddArc(r,180,180);beam.CloseFigure();
                g.FillPath(light,beam);g.DrawPath(edge,beam);
            }
            using(var panel=new GraphicsPath())
            using(var tint=new LinearGradientBrush(r,Color.FromArgb(100,17,54,76),Color.FromArgb(65,34,26,67),90f))
            {
                panel.AddEllipse(r);
                g.FillPath(tint,panel);CyberpunkArt.Glow(g,panel,PetSkinStyles.CyberPrimary(Tone,190),.8f);
                var state=g.Save();
                try
                {
                    g.SetClip(panel,CombineMode.Intersect);
                    using(var grid=new Pen(PetSkinStyles.CyberPrimary(Tone,35),.5f))
                    {
                        for(int x=10;x<70;x+=10)g.DrawLine(grid,r.Left+x,r.Top+10,r.Left+x,r.Bottom-8);
                        for(int y=14;y<32;y+=7)g.DrawLine(grid,r.Left+4,r.Top+y,r.Right-4,r.Top+y);
                    }
                    using(var text=new SolidBrush(PetSkinStyles.CyberPrimary(Tone,225)))g.DrawString("HOLO / POINTER",keyFont,text,r.Left+16,r.Top+6);
                    using(var scan=new Pen(PetSkinStyles.CyberPrimary(Tone,70),.65f))
                    {float y=r.Top+11+(float)((now*.27)%1)*19;g.DrawLine(scan,r.Left+4,y,r.Right-4,y);}
                    PointF cursor=CyberMouseLocalCursor;
                    // Map the bounded world-space motion history onto the projection.
                    for(int i=1;i<trail.Count;i++)
                    using(var line=new Pen(PetSkinStyles.CyberPrimary(Tone,(int)(125*Math.Max(0,1-(now-trail[i].At)/.22))),.8f))
                        g.DrawLine(line,ProjectMouseTrail(trail[i-1].Point),ProjectMouseTrail(trail[i].Point));
                    using(var arrow=PetDeskProjection.Polygon(new[]{cursor,new PointF(cursor.X+2,cursor.Y+8),new PointF(cursor.X+4,cursor.Y+5),new PointF(cursor.X+8,cursor.Y+4)}))
                    using(var fill=new SolidBrush(PetSkinStyles.CyberPrimary(Tone,240)))g.FillPath(fill,arrow);
                    using(var text=new SolidBrush(PetSkinStyles.CyberSecondary(Tone,180)))g.DrawString("L  M  R  /  X1 X2",keyFont,text,r.Left+12,r.Bottom-12);
                }
                finally{g.Restore(state);}
            }
        }
        finally{g.Restore(saved);}
    }
    PointF CyberMouseLocalCursor { get { var r=CyberProjectionFace;return new PointF(r.Left+29+Math.Max(-17,Math.Min(17,input.MouseOffset.X*.45f)),r.Top+17+Math.Max(-3,Math.Min(6,input.MouseOffset.Y*.18f))); } }
    internal PointF CyberMouseCursor { get { var p=CyberMouseEmitter;var c=CyberMouseLocalCursor;float s=CyberProjectionBlend;return new PointF(p.X+c.X*s,p.Y+c.Y*s); } }
    PointF ProjectMouseTrail(PointF point)
    {var r=CyberProjectionFace;return new PointF(r.Left+29+Math.Max(-17,Math.Min(17,(point.X-CoordinatedPetHands.MouseHome.X)*1.6f)),r.Top+17+Math.Max(-3,Math.Min(6,(point.Y-CoordinatedPetHands.MouseHome.Y)*.9f)));}
    void DrawCyberMouseFeedback(Graphics g,double now)
    {
        if(!input.HasMouseFeedback(now)||CyberProjectionBlend<.02f)return;
        var r=CyberProjectionFace;var saved=BeginCyberProjection(g);
        try
        {
            using(var panel=new GraphicsPath()){panel.AddEllipse(r);g.SetClip(panel,CombineMode.Intersect);}
            PointF cursor=CyberMouseLocalCursor;
            for(int button=1;button<=5;button++)
            {
                float strength=input.ButtonFeedback(button,now);if(strength<=0)continue;
                Color color=button==2||button==5 ? PetSkinStyles.CyberSecondary(Tone,(int)(245*strength)) : PetSkinStyles.CyberPrimary(Tone,(int)(245*strength));
                float radius=3+(float)Math.Min(1,input.ButtonAge(button,now)/.36)*7;
                using(var ripple=new Pen(color,1.1f))g.DrawEllipse(ripple,cursor.X-radius,cursor.Y-radius,radius*2,radius*2);
                float x=r.Left+12+(button==1 ? 0 : button==3 ? 7 : button==2 ? 14 : button==4 ? 30 : 38);
                using(var fill=new SolidBrush(Color.FromArgb((int)(85*strength),color)))g.FillRectangle(fill,x-1,r.Bottom-13,button<4 ? 6 : 9,7);
                using(var text=new SolidBrush(color))g.DrawString(button==1 ? "L" : button==2 ? "R" : button==3 ? "M" : button==4 ? "X1" : "X2",keyFont,text,x,r.Bottom-12);
            }
            float wheel=input.WheelFeedback(now);
            if(wheel>0)
            {
                int direction=-Math.Sign(input.WheelDelta);
                float flow=(float)((Math.Max(0,now-input.WheelAt)*5)%1)*3;
                var state=g.Save();g.TranslateTransform(r.Right-13,r.Top+20);if(input.WheelHorizontal)g.RotateTransform(90);
                for(int i=0;i<2;i++)
                {
                    float y=direction*(flow+i*5-4);
                    CyberpunkArt.Trace(g,PetSkinStyles.CyberPrimary(Tone,(int)(225*wheel)),1.2f,new PointF(-5,y-direction*3),new PointF(0,y),new PointF(5,y-direction*3));
                }
                g.Restore(state);
            }
        }
        finally{g.Restore(saved);}
    }
    void DrawNavigationChart(Graphics g)
    {
        using(var pad=PetDeskProjection.Polygon(PetDeskProjection.MousePad))
        using(var paper=new LinearGradientBrush(new RectangleF(8,326,139,60),Color.FromArgb(244,226,183),Color.FromArgb(211,192,148),90f))
        using(var edge=new Pen(Color.FromArgb(107,79,50),1.5f))
        {
            g.FillPath(paper,pad);g.DrawPath(edge,pad);
            var saved=g.Save();g.SetClip(pad,CombineMode.Intersect);
            using(var grid=new Pen(Color.FromArgb(75,154,124,81),.5f))
            {
                for(int i=1;i<8;i++)g.DrawLine(grid,ChartPoint(i/8f,0),ChartPoint(i/8f,1));
                for(int i=1;i<5;i++)g.DrawLine(grid,ChartPoint(0,i/5f),ChartPoint(1,i/5f));
            }
            using(var land=PetDeskProjection.Polygon(new[]{ChartPoint(-.02f,.02f),ChartPoint(.28f,0),ChartPoint(.33f,.14f),ChartPoint(.25f,.22f),ChartPoint(.3f,.36f),ChartPoint(.18f,.44f),ChartPoint(.23f,.6f),ChartPoint(.1f,.72f),ChartPoint(-.08f,.66f)}))
            using(var island=PetDeskProjection.Polygon(new[]{ChartPoint(.43f,.7f),ChartPoint(.54f,.62f),ChartPoint(.62f,.72f),ChartPoint(.6f,.85f),ChartPoint(.47f,.89f),ChartPoint(.4f,.8f)}))
            using(var green=new SolidBrush(Color.FromArgb(155,157,166,123)))
            using(var coast=new Pen(Color.FromArgb(109,118,85),.85f))
            {g.FillPath(green,land);g.DrawPath(coast,land);g.FillPath(green,island);g.DrawPath(coast,island);}
            using(var route=new Pen(MouseColor(Color.FromArgb(174,137,49,49)),.9f))
            {
                route.DashPattern=new[]{2f,2f};
                g.DrawBezier(route,ChartPoint(.12f,.83f),ChartPoint(.29f,.57f),ChartPoint(.32f,.1f),ChartPoint(.69f,.27f));
            }
            PointF cross=ChartPoint(.12f,.83f),compass=ChartPoint(.69f,.13f);
            using(var ink=new Pen(Color.FromArgb(111,77,47),.7f))
            using(var north=new SolidBrush(MouseColor(Color.FromArgb(165,47,53))))
            using(var south=new SolidBrush(Color.FromArgb(118,95,66)))
            {
                g.DrawLine(ink,cross.X-2,cross.Y-2,cross.X+2,cross.Y+2);g.DrawLine(ink,cross.X+2,cross.Y-2,cross.X-2,cross.Y+2);
                g.DrawEllipse(ink,compass.X-5,compass.Y-5,10,10);
                g.DrawLine(ink,compass.X-8,compass.Y,compass.X+8,compass.Y);
                g.FillPolygon(north,new[]{new PointF(compass.X,compass.Y-8),new PointF(compass.X-2,compass.Y+1),new PointF(compass.X+2,compass.Y+1)});
                g.FillPolygon(south,new[]{new PointF(compass.X,compass.Y+7),new PointF(compass.X-2,compass.Y),new PointF(compass.X+2,compass.Y)});
            }
            g.Restore(saved);
        }
    }
    internal static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var p = new GraphicsPath(); float d = radius * 2;
        p.AddArc(r.Left, r.Top, d, d, 180, 90); p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure(); return p;
    }
    // Round in scene space so foreshortened keys keep the same soft ink corners.
    static GraphicsPath SoftPolygon(PointF[] points,float radius)
    {
        var path=new GraphicsPath();
        for(int i=0;i<points.Length;i++)
        {
            PointF previous=points[(i+points.Length-1)%points.Length],corner=points[i],next=points[(i+1)%points.Length];
            float before=(float)Math.Sqrt(Math.Pow(previous.X-corner.X,2)+Math.Pow(previous.Y-corner.Y,2));
            float after=(float)Math.Sqrt(Math.Pow(next.X-corner.X,2)+Math.Pow(next.Y-corner.Y,2));
            float a=Math.Min(.35f,radius/before),b=Math.Min(.35f,radius/after);
            PointF entry=new PointF(corner.X+(previous.X-corner.X)*a,corner.Y+(previous.Y-corner.Y)*a);
            PointF leave=new PointF(corner.X+(next.X-corner.X)*b,corner.Y+(next.Y-corner.Y)*b);
            if(i==0)path.StartFigure();else path.AddLine(path.GetLastPoint(),entry);
            path.AddBezier(entry,new PointF(entry.X+(corner.X-entry.X)*.67f,entry.Y+(corner.Y-entry.Y)*.67f),
                new PointF(leave.X+(corner.X-leave.X)*.67f,leave.Y+(corner.Y-leave.Y)*.67f),leave);
        }
        path.CloseFigure();return path;
    }
    void DrawKeyboardShell(Graphics g)
    {
        if(KeyboardStyle==PetKeyboardStyles.Cyberpunk){DrawCyberKeyboard(g);return;}
        if(KeyboardStyle==PetKeyboardStyles.Parchment){DrawParchment(g);return;}
        PointF[] corners=PetDeskProjection.Keyboard;
        PointF[] basePoints=corners.Select(p=>new PointF(p.X,p.Y+4.3f)).ToArray();
        Color ink=KeyboardColor(Color.FromArgb(54,44,59));
        using(var shape=SoftPolygon(basePoints,3))
        using(var fill=new SolidBrush(KeyboardColor(Color.FromArgb(164,157,179))))
        using(var edge=new Pen(ink,1.25f)) {g.FillPath(fill,shape);g.DrawPath(edge,shape);}
        using(var shape=SoftPolygon(corners,3))
        using(var silver=new LinearGradientBrush(new RectangleF(103,355-PetDeskProjection.Lift,188,48),Color.FromArgb(251,246,249),Color.FromArgb(206,200,218),90))
        using(var edge=new Pen(ink,1.25f)) {g.FillPath(silver,shape);g.DrawPath(edge,shape);}
        PointF center=new PointF(corners.Average(p=>p.X),corners.Average(p=>p.Y));
        PointF[] tray=corners.Select(p=>new PointF(center.X+(p.X-center.X)*.974f,center.Y+(p.Y-center.Y)*.91f)).ToArray();
        using(var shape=SoftPolygon(tray,2))
        using(var fill=new SolidBrush(KeyboardColor(Color.FromArgb(110,101,121))))g.FillPath(fill,shape);
        // A scarf-red seam and a small silver sword echo the knight's accessories.
        using(var seam=new Pen(KeyboardColor(Color.FromArgb(151,49,72)),1.1f))
        {
            seam.StartCap=seam.EndCap=LineCap.Round;
            g.DrawLine(seam,PetDeskProjection.DeskPoint(111,399.5f),PetDeskProjection.DeskPoint(283,405.8f));
        }
        using(var sword=new Pen(KeyboardColor(Color.FromArgb(250,241,240)),.65f))
        {g.DrawLine(sword,PetDeskProjection.DeskPoint(198,402),PetDeskProjection.DeskPoint(198,405));
            g.DrawLine(sword,PetDeskProjection.DeskPoint(196.5f,402.8f),PetDeskProjection.DeskPoint(199.5f,402.9f));}
    }
    static PointF ScrollPoint(float u,float v)
    {
        var p=PetDeskProjection.Keyboard;
        return new PointF((1-u)*(1-v)*p[0].X+u*(1-v)*p[1].X+u*v*p[2].X+(1-u)*v*p[3].X,
            (1-u)*(1-v)*p[0].Y+u*(1-v)*p[1].Y+u*v*p[2].Y+(1-u)*v*p[3].Y);
    }
    void DrawParchment(Graphics g)
    {
        // Keep the paper and rolls inside the existing desk extent and leave
        // every key/contact point in its established projection.
        using(var paper=new GraphicsPath())
        {
            paper.AddBezier(ScrollPoint(.02f,0),ScrollPoint(.26f,-.08f),ScrollPoint(.7f,.06f),ScrollPoint(.98f,0));
            paper.AddLine(ScrollPoint(.98f,0),ScrollPoint(.98f,1));
            paper.AddBezier(ScrollPoint(.98f,1),ScrollPoint(.73f,.95f),ScrollPoint(.3f,1.12f),ScrollPoint(.02f,1));
            paper.CloseFigure();
            var saved=g.Save();g.TranslateTransform(0,2.2f);
            using(var shadow=new SolidBrush(Color.FromArgb(110,74,49,35)))g.FillPath(shadow,paper);
            g.Restore(saved);
            using(var parchment=new LinearGradientBrush(new RectangleF(103,338,188,55),Color.FromArgb(251,233,184),Color.FromArgb(200,158,98),90f))
            using(var edge=new Pen(Color.FromArgb(101,69,44),1.1f))
            {g.FillPath(parchment,paper);g.DrawPath(edge,paper);}
            saved=g.Save();g.SetClip(paper,CombineMode.Intersect);
            using(var fiber=new Pen(Color.FromArgb(34,132,90,51),.45f))
                for(int i=0;i<36;i++)
                {
                    float u=.07f+(i*37%89)/100f,v=(i*19%97)/100f;
                    g.DrawLine(fiber,ScrollPoint(u,v),ScrollPoint(Math.Min(.95f,u+.025f+(i%3)*.012f),v+.006f));
                }
            using(var border=new Pen(Color.FromArgb(125,145,97,51),.65f))
            {
                g.DrawLine(border,ScrollPoint(.07f,.025f),ScrollPoint(.93f,.025f));
                g.DrawLine(border,ScrollPoint(.07f,.97f),ScrollPoint(.93f,.97f));
            }
            g.Restore(saved);
        }
        foreach(float u in new[]{.015f,.985f})
        {
            PointF top=ScrollPoint(u,-.045f),bottom=ScrollPoint(u,1.04f);
            using(var shadow=new Pen(Color.FromArgb(85,57,41),6.5f))
            using(var roll=new Pen(Color.FromArgb(187,137,76),4.6f))
            using(var highlight=new Pen(Color.FromArgb(250,222,162),1.5f))
            {
                shadow.StartCap=shadow.EndCap=roll.StartCap=roll.EndCap=LineCap.Round;
                g.DrawLine(shadow,top,bottom);g.DrawLine(roll,top,bottom);
                g.DrawLine(highlight,new PointF(top.X-1,top.Y+.7f),new PointF(bottom.X-1,bottom.Y-.7f));
            }
            using(var end=new SolidBrush(Color.FromArgb(112,75,44)))g.FillEllipse(end,bottom.X-2.4f,bottom.Y-1.4f,4.8f,2.8f);
        }
        PointF seal=ScrollPoint(.5f,1.02f);
        using(var wax=new SolidBrush(KeyboardColor(Color.FromArgb(155,39,52))))
        using(var rim=new Pen(KeyboardColor(Color.FromArgb(247,158,129)),.7f))
        {g.FillEllipse(wax,seal.X-3,seal.Y-1.5f,6,3);g.DrawEllipse(rim,seal.X-2,seal.Y-1,4,2);}
    }
    internal PointF TargetFor(int source) { return hands.TargetFor(source); }
    internal bool Advance(double now, double dt, bool resting)
    {
        if(now<renderTime)codeScroll=0;
        renderTime=now;
        bool changed = input.Revision != lastRevision || lastRest != resting;
        lastRevision = input.Revision; lastRest = resting;
        PointF oldMouse=hands.Mouse;
        changed |= hands.Advance(now,dt,resting);
        input.Work.Update(now,dt,resting);
        PointF face=portrait.WorldPoint(new PointF(151,131));
        float dx=pointer.X-face.X,dy=pointer.Y-face.Y;
        float attention=!trackingPointer ? 0 : dx*dx+dy*dy<180*180 ? 1 : (float)Math.Exp(-Math.Max(0,now-pointerMovedAt-1.2)*1.8);
        portrait.LookAt(pointer,attention);
        portrait.Update(now,dt,resting,hands.Typing,input.HasMouse(now),input.Work.Mood,input.Work.KeysPerMinute,MusicPlaying,MusicLevel);
        // Integrate positive velocity instead of multiplying the clock by
        // volume: volume changes must never jump or reverse the code stream.
        if(!resting&&portrait.MusicBlend>.01f)
            codeScroll+=Math.Max(0,Math.Min(.4,dt))*(18+portrait.MusicEnergy*22);
        changed=true;
        if(hands.Mouse!=oldMouse)
        {
            if(trail.Count==0||Math.Abs(oldMouse.X-trail[trail.Count-1].Point.X)+Math.Abs(oldMouse.Y-trail[trail.Count-1].Point.Y)>1.3)
                trail.Add(new TrailPoint {Point=oldMouse,At=now});
            while(trail.Count>10)trail.RemoveAt(0);
        }
        trail.RemoveAll(p => now-p.At>=.22); changed |= trail.Count>0;
        if (input.BubbleMode)
        {
            var particles = input.Floating(now).ToArray();
            var current = new HashSet<int>(particles.Select(p => p.Sequence));
            foreach (int old in lifts.Keys.Where(k => !current.Contains(k)).ToArray()) lifts.Remove(old);
            for (int i = 0; i < particles.Length; i++)
            {
                float lift; int id = particles[i].Sequence; lifts.TryGetValue(id, out lift);
                lifts[id] = lift + ((particles.Length - 1 - i) * 40 - lift) * (float)(1 - Math.Exp(-10 * Math.Max(0, dt)));
            }
            changed |= particles.Length > 0;
        }
        else lifts.Clear();
        bool mouseFeedback = input.HasMouseFeedback(now);
        changed |= now - input.LastStrikeAt < .20 || mouseFeedback || hadMouseFeedback;
        hadMouseFeedback = mouseFeedback;
        return changed;
    }
    void DrawCap(Graphics g, PetKeyCap cap, bool lit, float opacity)
    {
        if(KeyboardStyle==PetKeyboardStyles.Cyberpunk)
        {
            DrawCyberKey(g,cap,lit,opacity);
            return;
        }
        PointF[] p = cap.Corners;
        bool accent = cap.Code == 27 || cap.Code == 13 || cap.Code == 32;
        bool modifier = cap.Code == 8 || cap.Code == 9 || cap.Code == 20 || cap.Code >= 160 && cap.Code <= 165 || cap.Code >= 91 && cap.Code <= 93;
        bool parchment=KeyboardStyle==PetKeyboardStyles.Parchment;
        Color normal = parchment ? Color.FromArgb(238,216,166) : accent ? KeyboardColor(Color.FromArgb(193,48,73)) : modifier ? Color.FromArgb(209,201,220) : Color.FromArgb(250,245,247);
        Color fillColor = lit ? KeyboardColor(Color.FromArgb((int)(255 * opacity), 247, 158, 173)) : normal;
        Color outline=parchment ? Color.FromArgb(130,151,111,64) : KeyboardColor(Color.FromArgb(92,77,99));
        PointF[] lower=p.Select(v=>new PointF(v.X,v.Y+1.05f)).ToArray();
        float corner=.8f;
        if(!parchment)
        using(var baseShape=SoftPolygon(lower,corner))
        using(var wall=new SolidBrush(accent ? KeyboardColor(Color.FromArgb(119,40,62)) : KeyboardColor(Color.FromArgb(159,147,174))))
        using(var edge=new Pen(outline,.55f)) {g.FillPath(wall,baseShape);g.DrawPath(edge,baseShape);}
        if(lit)p=p.Select(v=>new PointF(v.X,v.Y+.65f)).ToArray();
        using(var shape=SoftPolygon(p,corner))
        using(var fill=new SolidBrush(parchment&&!lit ? Color.FromArgb(70,fillColor) : fillColor))
        using(var edge=new Pen(outline,parchment ? .4f : .55f)) {g.FillPath(fill,shape);g.DrawPath(edge,shape);}
        // Single, quiet legends survive the desktop scale; long modifier names
        // previously turned into dense stripes that fought the portrait's linework.
        string label = cap.Code == 32 ? "" : cap.Code == 27 ? "•" : cap.Code == 13 ? "↵" :
            cap.Code == 8 ? "‹" : cap.Code == 9 ? "↔" : modifier ? "·" :
            cap.Code >= 112 && cap.Code <= 123 ? "·" : cap.Label;
        var saved = g.Save();
        try
        {
            float w = Math.Max(6,cap.W*14.2f-1.8f);
            using (var transform = new Matrix(new RectangleF(0,0,w,9),new[] {p[0],p[1],p[3]}))
            using (var shape = SoftPolygon(p,corner))
            using (var ink = new SolidBrush(parchment&&!lit ? (accent ? KeyboardColor(Color.FromArgb(142,40,45)) : Color.FromArgb(92,65,39)) : accent && !lit ? KeyboardColor(Color.FromArgb(255,234,235)) : Color.FromArgb(111,92,116)))
            using (var format = new StringFormat(StringFormat.GenericTypographic) { Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center,FormatFlags=StringFormatFlags.NoWrap })
            {
                g.SetClip(shape,CombineMode.Intersect); g.MultiplyTransform(transform);
                g.DrawString(label,keyFont,ink,new RectangleF(0,0,w,9),format);
            }
        }
        finally { g.Restore(saved); }
    }
    void DrawCyberKey(Graphics g,PetKeyCap cap,bool lit,float opacity)
    {
        var saved=g.Save();
        try
        {
            var p=cap.Corners;
            bool accent=cap.Code==27||cap.Code==13||cap.Code==32;
            Color color=accent ? PetSkinStyles.CyberSecondary(Tone) : PetSkinStyles.CyberPrimary(Tone);
            using(var key=SoftPolygon(p,.6f))
            using(var light=new SolidBrush(Color.FromArgb((int)((lit ? 85 : 16)*opacity),color)))
            using(var edge=new Pen(Color.FromArgb((int)((lit ? 250 : 205)*opacity),color),lit ? .95f : .65f))
            {
                g.FillPath(light,key);
                using(var contrast=new Pen(Color.FromArgb((int)(90*opacity),color.R/4,color.G/4,color.B/4),1.1f))g.DrawPath(contrast,key);
                g.DrawPath(edge,key);
                if(lit)CyberpunkArt.Glow(g,key,Color.FromArgb((int)(210*opacity),color),.7f);
                g.SetClip(key,CombineMode.Intersect);
                float w=Math.Max(6,cap.W*14.2f-1.8f);
                using(var transform=new Matrix(new RectangleF(0,0,w,9),new[]{p[0],p[1],p[3]}))g.MultiplyTransform(transform);
                using(var ink=new SolidBrush(Color.FromArgb((int)(235*opacity),color)))
                using(var format=new StringFormat(StringFormat.GenericTypographic){Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center,FormatFlags=StringFormatFlags.NoWrap})
                {
                    string label=cap.Code==32 ? "—" : cap.Code==13 ? "↵" : cap.Code==8 ? "‹" : cap.Code==9 ? "↔" : cap.Label.Length>3 ? "·" : cap.Label;
                    g.DrawString(label,keyFont,ink,new RectangleF(0,0,w,9),format);
                }
            }
        }
        finally{g.Restore(saved);}
    }
    static Bitmap CreateMouseTexture(int tone,bool cyber=false)
    {
        if(cyber)return CreateCyberMouseTexture(tone);
        // Cache the silver-knight shell at 2x. Only button/scroll feedback
        // changes per frame, so the extra detail has no per-frame path cost.
        var image=new Bitmap(88,108,PixelFormat.Format32bppPArgb);
        using(var g=Graphics.FromImage(image))
        {
            g.ScaleTransform(2,2);g.TranslateTransform(22,28);g.SmoothingMode=SmoothingMode.AntiAlias;
            using(var shell=new GraphicsPath())
            {
                shell.AddBezier(0,-22,-10,-23,-14,-17,-14,-7);
                shell.AddBezier(-14,-7,-16,11,-8,22,0,23);
                shell.AddBezier(0,23,8,22,16,11,14,-7);
                shell.AddBezier(14,-7,14,-17,10,-23,0,-22);shell.CloseFigure();
                using(var baseFill=new SolidBrush(Color.FromArgb(37,35,47)))g.FillPath(baseFill,shell);
                var saved=g.Save();g.ScaleTransform(.92f,.94f);g.TranslateTransform(0,-.8f);
                using(var metal=new LinearGradientBrush(new RectangleF(-14,-22,28,45),Color.FromArgb(253,249,248),Color.FromArgb(163,166,188),25))g.FillPath(metal,shell);
                g.Restore(saved);
                using(var outline=new Pen(Color.FromArgb(40,33,46),1.4f))g.DrawPath(outline,shell);
            }
            for(int side=-1;side<=1;side+=2)
            using(var plate=new GraphicsPath())
            {
                plate.AddPolygon(new[] {new PointF(side*3,-19),new PointF(side*9,-17),new PointF(side*11.5f,-12),
                    new PointF(side*11,-1),new PointF(side*3,-3)});
                using(var fill=new SolidBrush(side<0 ? Color.FromArgb(249,246,251) : Color.FromArgb(220,218,233)))g.FillPath(fill,plate);
                using(var rim=new Pen(Color.FromArgb(139,136,157),.7f))g.DrawPath(rim,plate);
            }
            using(var recess=Rounded(new RectangleF(-3.2f,-16,6.4f,13),2))g.FillPath(Brushes.DimGray,recess);
            // A red scarf wraps the armour; the small tail and shield echo
            // the pet's scarf and sword even with a hand over the buttons.
            using(var scarf=new GraphicsPath())
            {
                scarf.AddBezier(-13,3,-6,6,7,6,13,1);
                scarf.AddLine(13,1,12,8);scarf.AddBezier(12,8,3,12,-6,10,-13,7);scarf.CloseFigure();
                using(var fill=new SolidBrush(Color.FromArgb(199,30,58)))g.FillPath(fill,scarf);
                using(var edge=new Pen(Color.FromArgb(102,28,49),.8f))g.DrawPath(edge,scarf);
                using(var tail=new GraphicsPath())
                {
                    tail.AddPolygon(new[] {new PointF(10,6),new PointF(18,10),new PointF(14,11),new PointF(15,15),new PointF(8,9)});
                    using(var red=new SolidBrush(Color.FromArgb(222,42,67)))g.FillPath(red,tail);
                    using(var edge=new Pen(Color.FromArgb(102,28,49),.8f))g.DrawPath(edge,tail);
                }
            }
            using(var shield=new GraphicsPath())
            {
                shield.AddPolygon(new[] {new PointF(-4.5f,12),new PointF(4.5f,12),new PointF(3.7f,18),new PointF(0,21),new PointF(-3.7f,18)});
                using(var ruby=new SolidBrush(Color.FromArgb(147,27,52)))g.FillPath(ruby,shield);
                using(var silver=new Pen(Color.FromArgb(250,237,230),.9f))g.DrawPath(silver,shield);
                using(var sword=new Pen(Color.FromArgb(255,247,238),1))
                {g.DrawLine(sword,0,13,0,18.5f);g.DrawLine(sword,-2,15,2,15);}
            }
            using(var rivet=new SolidBrush(Color.FromArgb(247,238,225)))
            {g.FillEllipse(rivet,-11,11,1.5f,1.5f);g.FillEllipse(rivet,9.5f,11,1.5f,1.5f);}
        }
        if(tone==0)return image;
        try{return PetSkinStyles.ApplyTone(image,tone);}finally{image.Dispose();}
    }
    internal void DrawMouse(Graphics g,double now)
    {
        if(MouseStyle==PetMouseStyles.Cyberpunk){DrawCyberMouse(g,now);return;}
        for (int i=1;i<trail.Count;i++)
        {
            int a=(int)(70*Math.Max(0,1-(now-trail[i].At)/.22));
            using (var pen=new Pen(MouseColor(Color.FromArgb(a,239,109,128)),2)) g.DrawLine(pen,trail[i-1].Point,trail[i].Point);
        }
        var saved=g.Save();
        try
        {
            float left=input.ButtonMotion(1,now),right=input.ButtonMotion(2,now),press=Math.Max(left,right);
            g.TranslateTransform(mouse.X,mouse.Y+press*1.3f);
            // Buttons point toward the fingertips/viewer; the rounded heel
            // belongs under the palm, toward the character's wrist.
            g.RotateTransform(mouseTilt+(left-right)*3.5f+180);g.ScaleTransform(1,.82f*(1-press*.065f));
            g.DrawImage(mouseTexture,new RectangleF(-22,-28,44,54),new RectangleF(0,0,mouseTexture.Width,mouseTexture.Height),GraphicsUnit.Pixel);
            for (int button=1;button<=2;button++)
            {
                float strength=input.ButtonFeedback(button,now);
                if(strength<=0)continue;
                int side=button==1 ? -1 : 1;
                using(var plate=new GraphicsPath())
                using(var fill=new SolidBrush(MouseButtonColor(Color.FromArgb((int)(205*strength),249,125,151),button)))
                using(var rim=new Pen(MouseButtonColor(Color.FromArgb((int)(255*strength),185,43,77),button),1.2f))
                {
                    float sink=-2.6f*input.ButtonMotion(button,now);
                    plate.AddPolygon(new[] {new PointF(side*3,-19+sink),new PointF(side*9,-17+sink),
                        new PointF(side*11.5f,-12+sink),new PointF(side*11,-1+sink),new PointF(side*3,-3+sink)});
                    g.FillPath(fill,plate);g.DrawPath(rim,plate);
                }
                using(var click=new Pen(MouseButtonColor(Color.FromArgb((int)(245*strength),249,84,110),button),2.2f))
                    g.DrawArc(click,-17,-25,34,44,button==1 ? 160 : 275,105);
            }
            float extra=Math.Max(input.ButtonFeedback(3,now),Math.Max(input.ButtonFeedback(4,now),input.ButtonFeedback(5,now)));
            if(extra>0)using(var click=new Pen(MouseColor(Color.FromArgb((int)(245*extra),249,84,110)),2.2f))g.DrawEllipse(click,-16,-24,32,46);
            float wheelStrength=input.WheelFeedback(now);
            float scroll=wheelStrength*(float)Math.Sin((now-input.WheelAt)*26)*Math.Sign(input.WheelDelta)*2.2f;
            using(var wheel=Rounded(new RectangleF(-1.8f,-14+scroll,3.6f,8),1.2f))
            using(var ruby=new SolidBrush(MouseColor(Color.FromArgb(210,37,69))))g.FillPath(ruby,wheel);
            using(var shine=new Pen(MouseColor(Color.FromArgb(255,194,199)),.7f))g.DrawLine(shine,-.8f,-12+scroll,.8f,-12+scroll);
            if(wheelStrength>0)
            using(var glow=new Pen(MouseColor(Color.FromArgb((int)(245*wheelStrength),255,117,148)),1.6f))
                g.DrawRectangle(glow,-3.5f,-16,7,12);
        }
        finally {g.Restore(saved);}
    }
    // Motion marks sit outside the glove, where neither button is hidden by it.
    internal void DrawMouseFeedback(Graphics g,double now)
    {
        if(MouseStyle==PetMouseStyles.Cyberpunk){DrawCyberMouseFeedback(g,now);return;}
        if(MouseStyle==PetMouseStyles.KnightSword){DrawSwordFeedback(g,now);return;}
        if(!input.HasMouseFeedback(now))return;
        var saved=g.Save();
        try
        {
            g.TranslateTransform(mouse.X,mouse.Y);g.RotateTransform(mouseTilt);
            for(int button=1;button<=2;button++)
            {
                float pulse=input.ButtonFeedback(button,now);
                if(pulse<=0)continue;
                float travel=(float)Math.Min(1,input.ButtonAge(button,now)/.30)*6;
                int side=button==1 ? 1 : -1;
                using(var echo=new Pen(MouseColor(Color.FromArgb((int)(185*pulse),245,113,143)),1.7f))
                using(var spark=new Pen(MouseColor(Color.FromArgb((int)(245*pulse),255,190,200)),2))
                {
                    echo.StartCap=echo.EndCap=spark.StartCap=spark.EndCap=LineCap.Round;
                    g.DrawArc(echo,-26-travel,-29-travel/2,52+travel*2,48+travel,button==1 ? -65 : 145,100);
                    float x=side*(28+travel);
                    for(int ray=-1;ray<=1;ray++)
                        g.DrawLine(spark,x,-10+ray*6,x+side*4,-10+ray*9);
                }
            }
            float wheel=input.WheelFeedback(now);
            if(wheel>0)
            using(var arrow=new Pen(MouseColor(Color.FromArgb((int)(245*wheel),248,125,153)),2.1f))
            {
                arrow.StartCap=arrow.EndCap=LineCap.Round;
                int direction=Math.Sign(input.WheelDelta)*(input.WheelHorizontal ? 1 : -1);
                float travel=(float)(((now-input.WheelAt)*9)%1)*5;
                for(int i=0;i<2;i++)
                {
                    float offset=direction*(i*7+travel);
                    if(input.WheelHorizontal)
                        g.DrawLines(arrow,new[] {new PointF(offset-direction*3,32),new PointF(offset,35),new PointF(offset-direction*3,38)});
                    else
                        g.DrawLines(arrow,new[] {new PointF(-4,33+offset-direction*3),new PointF(0,33+offset),new PointF(4,33+offset-direction*3)});
                }
            }
        }
        finally {g.Restore(saved);}
    }
    static GraphicsPath SwordBlade()
    {
        return PetDeskProjection.Polygon(new[] {new PointF(-6,15),new PointF(6,15),new PointF(6,47),
            new PointF(0,63),new PointF(-6,47)});
    }
    static Bitmap CreateSwordTexture(int tone)
    {
        // One supersampled illustration, anchored at the glove's grip.
        // The guard and long blade stay visible when the fingers cover the hilt.
        var image=new Bitmap((int)SwordBounds.Width*DeviceScale,(int)SwordBounds.Height*DeviceScale,PixelFormat.Format32bppPArgb);
        using(var g=Graphics.FromImage(image))
        {
            g.ScaleTransform(DeviceScale,DeviceScale);g.TranslateTransform(-SwordBounds.X,-SwordBounds.Y);
            g.SmoothingMode=SmoothingMode.AntiAlias;
            using(var blade=SwordBlade())
            {
                var saved=g.Save();g.TranslateTransform(2.5f,3);
                using(var shadow=new SolidBrush(Color.FromArgb(28,26,23,34)))g.FillPath(shadow,blade);
                g.Restore(saved);
                using(var silver=new LinearGradientBrush(new RectangleF(-6,15,12,48),Color.FromArgb(255,254,249),Color.FromArgb(159,168,184),0f))g.FillPath(silver,blade);
                using(var facet=PetDeskProjection.Polygon(new[] {new PointF(0,16),new PointF(4.5f,16),new PointF(4.5f,46),new PointF(0,60)}))
                using(var shade=new SolidBrush(Color.FromArgb(188,196,208)))g.FillPath(shade,facet);
                using(var ink=new Pen(Color.FromArgb(43,39,49),1.25f))g.DrawPath(ink,blade);
                using(var ridge=new Pen(Color.FromArgb(255,255,250),1))g.DrawLine(ridge,0,17,0,58);
                using(var fuller=new Pen(Color.FromArgb(112,122,141),.65f))g.DrawLine(fuller,1.2f,20,1.2f,46);
            }
            using(var grip=Rounded(new RectangleF(-4,-10,8,20),2))
            using(var leather=new LinearGradientBrush(new RectangleF(-4,-10,8,20),Color.FromArgb(67,57,66),Color.FromArgb(29,28,36),0f))
            using(var edge=new Pen(Color.FromArgb(34,29,40),1.1f))
            {g.FillPath(leather,grip);g.DrawPath(edge,grip);}
            using(var wrap=new Pen(Color.FromArgb(130,115,126),.85f))
                for(int y=-7;y<8;y+=4)g.DrawLine(wrap,-3,y,3,y+2);
            using(var pommel=PetDeskProjection.Polygon(new[] {new PointF(0,-18),new PointF(6,-13),new PointF(4,-9),new PointF(-4,-9),new PointF(-6,-13)}))
            using(var silver=new SolidBrush(Color.FromArgb(225,229,235)))
            using(var ink=new Pen(Color.FromArgb(43,39,49),1.2f))
            {g.FillPath(silver,pommel);g.DrawPath(ink,pommel);}
            // A curved crossguard, silver bevels and a scarf-red oath ribbon.
            using(var ribbon=PetDeskProjection.Polygon(new[] {new PointF(-10,10),new PointF(-17,16),new PointF(-14,17),new PointF(-18,23),new PointF(-10,19),new PointF(-6,12)}))
            using(var red=new SolidBrush(Color.FromArgb(194,31,56)))
            using(var edge=new Pen(Color.FromArgb(100,29,45),.8f))
            {g.FillPath(red,ribbon);g.DrawPath(edge,ribbon);}
            using(var guard=PetDeskProjection.Polygon(new[] {new PointF(-17,7),new PointF(-10,8),new PointF(-5,10),new PointF(5,10),
                new PointF(10,8),new PointF(17,7),new PointF(15,13),new PointF(7,15),new PointF(-7,15),new PointF(-15,13)}))
            using(var silver=new LinearGradientBrush(new RectangleF(-17,7,34,8),Color.FromArgb(253,251,244),Color.FromArgb(143,151,168),90))
            using(var edge=new Pen(Color.FromArgb(43,39,49),1.2f))
            {g.FillPath(silver,guard);g.DrawPath(edge,guard);}
            using(var gem=PetDeskProjection.Polygon(new[] {new PointF(0,7),new PointF(4,11),new PointF(0,17),new PointF(-4,11)}))
            using(var ruby=new SolidBrush(Color.FromArgb(194,31,56)))
            using(var edge=new Pen(Color.FromArgb(89,32,50),.8f))
            {g.FillPath(ruby,gem);g.DrawPath(edge,gem);}
            using(var shine=new Pen(Color.FromArgb(255,218,214),.8f))g.DrawLine(shine,-1,10,0,8.5f);
        }
        if(tone==0)return image;
        try{return PetSkinStyles.ApplyTone(image,tone);}finally{image.Dispose();}
    }
    Matrix SwordTransform()
    {
        var transform=new Matrix();
        transform.Translate(mouse.X,mouse.Y+Math.Max(hands.MousePressLeft,hands.MousePressRight)*1.3f);
        transform.Rotate(swordTilt);transform.Scale(1,.82f);
        return transform;
    }
    internal void DrawSword(Graphics g,double now)
    {
        for(int i=1;i<trail.Count;i++)
        {
            int a=(int)(70*Math.Max(0,1-(now-trail[i].At)/.22));
            using(var pen=new Pen(MouseColor(Color.FromArgb(a,239,109,128)),2))g.DrawLine(pen,trail[i-1].Point,trail[i].Point);
        }
        var saved=g.Save();
        try
        {
            using(var transform=SwordTransform())g.MultiplyTransform(transform);
            g.DrawImage(swordTexture,SwordBounds,new RectangleF(0,0,swordTexture.Width,swordTexture.Height),GraphicsUnit.Pixel);
            float weight=1-hands.TypingBlend;
            for(int button=1;button<=2;button++)
            {
                float pulse=input.ButtonFeedback(button,now)*weight;
                if(pulse<=0)continue;
                int side=button==1 ? -1 : 1;
                using(var glow=new Pen(MouseColor(Color.FromArgb((int)(230*pulse),250,113,139)),2))
                {
                    glow.StartCap=glow.EndCap=LineCap.Round;
                    g.DrawLines(glow,new[] {new PointF(side*6,18),new PointF(side*6,47),new PointF(0,62)});
                }
            }
            float wheel=input.WheelFeedback(now)*weight;
            if(wheel>0)
            {
                float phase=(float)Math.Max(0,Math.Min(1,(now-input.WheelAt)/.45));
                float y=19+37*(input.WheelDelta>0 ? phase : 1-phase);
                using(var light=new Pen(MouseColor(Color.FromArgb((int)(230*wheel),255,117,148)),2.1f))
                using(var shine=new Pen(Color.FromArgb((int)(230*wheel),255,248,239),.8f))
                {
                    if(input.WheelHorizontal)
                    {
                        float x=(input.WheelDelta>0 ? phase : 1-phase)*24-12;
                        g.DrawLine(light,x,8,x,15);g.DrawLine(shine,x-1,9,x-1,14);
                    }
                    else
                    {g.DrawLine(light,-4,y,4,y);g.DrawLine(shine,-3,y-1,3,y-1);}
                }
            }
        }
        finally {g.Restore(saved);}
    }
    // Sword trails sit beyond the fingers so quick clicks remain readable.
    void DrawSwordFeedback(Graphics g,double now)
    {
        if(!input.HasMouseFeedback(now))return;
        var saved=g.Save();
        try
        {
            using(var transform=SwordTransform())g.MultiplyTransform(transform);
            float weight=1-hands.TypingBlend;
            for(int button=1;button<=2;button++)
            {
                float pulse=input.ButtonFeedback(button,now)*weight;
                if(pulse<=0)continue;
                float travel=(float)Math.Min(1,input.ButtonAge(button,now)/.30)*6;
                int side=button==1 ? -1 : 1;
                using(var echo=new Pen(MouseColor(Color.FromArgb((int)(185*pulse),245,113,143)),1.7f))
                using(var spark=new Pen(MouseColor(Color.FromArgb((int)(245*pulse),255,190,200)),1.5f))
                {
                    echo.StartCap=echo.EndCap=spark.StartCap=spark.EndCap=LineCap.Round;
                    g.DrawArc(echo,-24-travel,19,48+travel*2,43,button==1 ? 100 : -20,100);
                    float x=side*(18+travel);
                    g.DrawLine(spark,x,46,x+side*4,50);g.DrawLine(spark,x+side*2,43,x+side*6,43);
                }
            }
            float extra=Math.Max(input.ButtonFeedback(3,now),Math.Max(input.ButtonFeedback(4,now),input.ButtonFeedback(5,now)))*weight;
            if(extra>0)
            using(var ring=new Pen(MouseColor(Color.FromArgb((int)(225*extra),249,125,151)),1.7f))g.DrawEllipse(ring,-21,0,42,24);
        }
        finally {g.Restore(saved);}
    }
    bool HitSword(PointF point)
    {
        var points=new[]{point};
        using(var transform=SwordTransform()){transform.Invert();transform.TransformPoints(points);}
        point=points[0];if(!SwordBounds.Contains(point))return false;
        int x=(int)((point.X-SwordBounds.X)*DeviceScale),y=(int)((point.Y-SwordBounds.Y)*DeviceScale);
        // Bicubic presentation blends neighbouring supersampled texels.
        // Include that soft edge in dragging without blocking empty corners.
        for(int dy=-2;dy<=2;dy++)for(int dx=-2;dx<=2;dx++)
            if(x+dx>=0&&x+dx<swordTexture.Width&&y+dy>=0&&y+dy<swordTexture.Height&&swordTexture.GetPixel(x+dx,y+dy).A>=32)return true;
        return false;
    }
    void DrawKeyDisplay(Graphics g, string label)
    {
        var r = CurrentKeyFrame;
        using (var ink = new SolidBrush(KnightUi.Ink))
        using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter })
        {
            KnightUi.Armor(g,r,252,false,7);
            using(var red=new Pen(KnightUi.Red,2))g.DrawLine(red,r.Left+8,r.Top+r.Height/2-5,r.Left+8,r.Top+r.Height/2+5);
            r.Inflate(-19, -3); g.DrawString(label, inputFont, ink, r, format);
        }
    }
    float BubbleWidth(string label, Font font)
    {
        return Math.Min(BubbleBounds.Width, Math.Max(58, TextRenderer.MeasureText(label, font, Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width + 26));
    }
    internal RectangleF LiveBubble
    {
        get { float width = BubbleWidth(input.LiveChord, inputFont); return new RectangleF(Math.Min(BubbleBounds.X, ViewWidth-width-8), BubbleBounds.Y, width, BubbleBounds.Height); }
    }
    internal RectangleF FloatingBubble(InputFeedbackState.FloatingKey particle, double now)
    {
        double age = Math.Max(0, now-particle.Born); float lift; lifts.TryGetValue(particle.Sequence, out lift);
        float width = BubbleWidth(particle.Label, floatingFont);
        return new RectangleF(Math.Min(ViewWidth-width-8, BubbleBounds.X+22+(float)age*18+lift*.48f),
            Math.Max(DialogueActive ? 126 : 6, BubbleBounds.Y-48-(float)age*38-lift), width, 34);
    }
    void DrawBubble(Graphics g, RectangleF r, string label, int alpha, bool tail, Font font)
    {
        using (var ink = new SolidBrush(KnightUi.Alpha(KnightUi.Ink,alpha)))
        using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter })
        {
            if (tail)
            {
                KnightUi.Gem(g,new RectangleF(r.Left+5,r.Bottom+5,9,7),alpha);
                KnightUi.Gem(g,new RectangleF(r.Left-4,r.Bottom+17,4,3),alpha);
            }
            KnightUi.Armor(g,r,alpha,false,7);
            using(var red=new Pen(KnightUi.Alpha(KnightUi.Red,alpha),2))g.DrawLine(red,r.Left+10,r.Bottom-5,r.Left+24,r.Bottom-5);
            r.Inflate(-7,-3); g.DrawString(label,font,ink,r,format);
        }
    }
    internal void DrawDesk(Graphics g,double now)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.DrawImage(devices,DeviceBounds,0,0,devices.Width,devices.Height,GraphicsUnit.Pixel);
        foreach (int target in input.LitSources(now).Select(bindings.Resolve).Where(k => k != 0).Distinct())
        {
            PetKeyCap cap = PetKeyboardLayout.Find(target); if (cap != null) DrawCap(g, cap, true, 1);
        }
        DrawOrnament(g);
    }
    void DrawMusicBackdrop(Graphics g,double now)
    {
        float blend=portrait.MusicBlend;
        if(blend<.01f)return;
        // 70% transparency leaves 30% opacity; keep the music fade on top.
        const float themedOpacity=.30f;
        if(MusicStyle==PetMusicStyles.OceanWaves){DrawOceanWaves(g,now,blend*themedOpacity);return;}
        if(MusicStyle==PetMusicStyles.HackerCode){DrawHackerCode(g,blend*themedOpacity);return;}
        float energy=portrait.MusicEnergy;
        float baseline=portrait.WorldPoint(new PointF(150,112)).Y;
        // Staggered level bars remain behind the character and desk, with
        // real output energy driving their height and brightness. They are
        // decorative level animation rather than individual frequency bins.
        // Center both ends on the head so louder output expands upwards and
        // downwards equally, with the midpoint following its gentle bob.
        for(int bar=0;bar<23;bar++)
        {
            float pulse=(float)(.5+.325*Math.Sin(now*5.2-bar*.77)+.175*Math.Sin(now*3.1+bar*1.13));
            float height=(44+pulse*24+energy*(78+pulse*60))*(.4f+.6f*blend);
            var bounds=new RectangleF(22+bar*13.5f,baseline-height*.5f,8.5f,height);
            int alpha=(int)(blend*(48+energy*64));
            using(var shape=Rounded(bounds,4.25f))
            using(var fill=new LinearGradientBrush(bounds,KeyboardColor(Color.FromArgb(alpha,188,71,104)),PetSkinStyles.Tint(Tone,Color.FromArgb(alpha/3,126,102,167)),90f))
            using(var tip=new Pen(KeyboardColor(Color.FromArgb((int)(alpha*.8f),204,111,140)),1.8f))
            {
                Color end=PetSkinStyles.Tint(Tone,Color.FromArgb(alpha/2,126,102,167));
                fill.InterpolationColors=new ColorBlend
                {
                    Positions=new[] {0f,.5f,1f},
                    Colors=new[] {end,KeyboardColor(Color.FromArgb(alpha,188,71,104)),end}
                };
                g.FillPath(fill,shape);
                tip.StartCap=tip.EndCap=LineCap.Round;
                g.DrawLine(tip,bounds.X+3,bounds.Y+3.5f,bounds.Right-3,bounds.Y+3.5f);
                g.DrawLine(tip,bounds.X+3,bounds.Bottom-3.5f,bounds.Right-3,bounds.Bottom-3.5f);
            }
        }
    }
    void DrawHackerCode(Graphics g,float blend)
    {
        // Use the space above the hair as well as the exposed sides. At small
        // pet sizes the center is almost entirely hidden by the portrait.
        var area=new RectangleF(14,78,ViewWidth-28,310);
        var saved=g.Save();g.SetClip(area,CombineMode.Intersect);
        g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
        const float lineHeight=16;
        float loopHeight=HackerCodeLines.Length*lineHeight,columnWidth=area.Width/3;
        float energy=portrait.MusicEnergy;
        using(var format=new StringFormat(StringFormat.GenericTypographic) {FormatFlags=StringFormatFlags.NoWrap})
        for(int column=0;column<3;column++)
        {
            var columnState=g.Save();
            g.SetClip(new RectangleF(area.Left+column*columnWidth,area.Top,columnWidth-3,area.Height),CombineMode.Intersect);
            double offset=codeScroll*(.9+column*.12)+column*73;
            for(int row=0;row<HackerCodeLines.Length;row++)
            {
                float y=area.Top+(float)(((row*lineHeight-offset)%loopHeight+loopHeight)%loopHeight)-lineHeight;
                float fade=Math.Max(0,Math.Min(1,Math.Min((y+lineHeight-area.Top)/25,(area.Bottom-y)/25)));
                int index=(row+column*7)%HackerCodeLines.Length;
                bool prompt=HackerCodeLines[index].StartsWith("root"),comment=HackerCodeLines[index].StartsWith("//");
                int alpha=(int)(blend*fade*(prompt ? 215+energy*35 : 175+energy*65));
                Color color=prompt ? PetSkinStyles.CyberSecondary(Tone,alpha) : comment ? Color.FromArgb(alpha,102,231,165) : PetSkinStyles.CyberPrimary(Tone,alpha);
                float x=area.Left+column*columnWidth+2;
                // A dark offset keeps the neon letters legible on pale desktops.
                using(var shadow=new SolidBrush(Color.FromArgb((int)(blend*fade*(150+energy*40)),8,29,46)))
                using(var ink=new SolidBrush(color))
                {
                    g.DrawString(HackerCodeLines[index],codeFont,shadow,new PointF(x+.8f,y+1),format);
                    g.DrawString(HackerCodeLines[index],codeFont,ink,new PointF(x,y),format);
                }
                if(prompt)
                using(var cursor=new SolidBrush(PetSkinStyles.CyberPrimary(Tone,alpha)))
                    g.FillRectangle(cursor,x+columnWidth-9,y+3,4,9);
            }
            g.Restore(columnState);
        }
        g.Restore(saved);
    }
    void DrawOceanWaves(Graphics g,double now,float blend)
    {
        float energy=portrait.MusicEnergy,center=portrait.WorldPoint(new PointF(150,115)).Y;
        var saved=g.Save();g.SetClip(new RectangleF(10,82,ViewWidth-20,306),CombineMode.Intersect);
        // Broad, translucent swells overlap rather than stacking solid ribbons.
        // Curved crests, feathered volume and broken foam share a slow phase.
        for(int layer=0;layer<4;layer++)
        {
            float baseline=center-126+layer*58,amplitude=15+energy*(18+layer*2);
            var points=new PointF[97];var lower=new PointF[points.Length];
            double drift=now*(.98+layer*.14)-layer*1.7;
            for(int i=0;i<points.Length;i++)
            {
                float u=i/(points.Length-1f),x=14+(ViewWidth-28)*u;
                double phase=u*Math.PI*(4.2+layer*.5)-drift;
                float swell=(float)(Math.Sin(phase)+.2*Math.Sin(2*phase-.7));
                points[i]=new PointF(x,baseline+swell*amplitude);
                lower[points.Length-1-i]=new PointF(x,points[i].Y+34+energy*10+(float)Math.Sin(phase-1.1)*4);
            }
            int alpha=(int)(blend*(85+energy*65));
            int crestAlpha=(int)(blend*(175+energy*65));
            using(var water=new GraphicsPath())
            using(var crest=new GraphicsPath())
            {
                crest.AddCurve(points,.48f);water.AddCurve(points,.48f);
                water.AddCurve(lower,.48f);water.CloseFigure();
                // A vertical fade avoids the triangulated seams that GDI+
                // path gradients produce inside a strongly curved water band.
                using(var depth=new LinearGradientBrush(water.GetBounds(),Color.Transparent,Color.Transparent,90f))
                {
                    Color sea=Color.FromArgb(alpha,40,167+layer*7,199+layer*6);
                    depth.InterpolationColors=new ColorBlend
                    {
                        Positions=new[]{0f,.25f,.6f,1f},
                        Colors=new[]{Color.FromArgb(0,sea),sea,Color.FromArgb(alpha/2,sea),Color.FromArgb(0,sea)}
                    };
                    g.FillPath(depth,water);
                }
                using(var light=new LinearGradientBrush(new RectangleF(14,0,ViewWidth-28,1),Color.Transparent,Color.Transparent,0f))
                {
                    Color shadow=Color.FromArgb((int)(blend*(90+energy*35)),17,100,135);
                    light.InterpolationColors=new ColorBlend {Positions=new[]{0f,.12f,.88f,1f},Colors=new[]{Color.FromArgb(0,shadow),shadow,shadow,Color.FromArgb(0,shadow)}};
                    using(var edge=new Pen(light,4.5f))
                    {edge.StartCap=edge.EndCap=LineCap.Round;edge.LineJoin=LineJoin.Round;g.DrawPath(edge,crest);}
                    foreach(float width in new[]{15f,8f,3.5f,1.8f})
                    {
                        float strength=width>10 ? .1f : width>5 ? .2f : width>2 ? .55f : 1f;
                        Color sea=Color.FromArgb((int)(crestAlpha*strength),width>2 ? 45 : 153,211,231);
                        light.InterpolationColors=new ColorBlend {Positions=new[]{0f,.12f,.88f,1f},Colors=new[]{Color.FromArgb(0,sea),sea,sea,Color.FromArgb(0,sea)}};
                        using(var edge=new Pen(light,width))
                        {edge.StartCap=edge.EndCap=LineCap.Round;edge.LineJoin=LineJoin.Round;g.DrawPath(edge,crest);}
                    }
                }
                // Short foam threads collect around the rounded wave tops.
                // Their length and opacity breathe without a repeating dash pattern.
                for(int i=3;i<points.Length-5;i+=5)
                {
                    float u=i/(points.Length-1f),envelope=(float)Math.Pow(Math.Sin(u*Math.PI),.55);
                    double phase=u*Math.PI*(4.2+layer*.5)-drift;
                    float foam=(float)Math.Max(0,-Math.Sin(phase)-.1);
                    if(foam<.08f)continue;
                    int opacity=(int)(alpha*1.65f*foam*envelope);
                    using(var pen=new Pen(Color.FromArgb(opacity,229,251,252),1.2f+foam*.8f))
                    {
                        pen.StartCap=pen.EndCap=LineCap.Round;
                        g.DrawCurve(pen,new[]{points[i],points[i+1],points[i+2],points[i+3]},.5f);
                    }
                    using(var sparkle=new SolidBrush(Color.FromArgb((int)(opacity*.65f),242,255,255)))
                        g.FillEllipse(sparkle,points[i+2].X+1,points[i+2].Y-3-foam*2,1.1f+foam,1.1f);
                }
                // Faint refracted traces give the body of each swell depth.
                var reflection=new PointF[points.Length];
                for(int i=0;i<points.Length;i++)reflection[i]=new PointF(points[i].X,points[i].Y+8+(float)Math.Sin(i*.15+layer)*2);
                using(var shimmer=new Pen(Color.FromArgb((int)(alpha*.42f),75,187,219),1.1f))
                {shimmer.StartCap=shimmer.EndCap=LineCap.Round;g.DrawCurve(shimmer,reflection,.48f);}
            }
        }
        g.Restore(saved);
    }
    internal void Draw(Graphics g, double now, bool resting, bool follow)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        DrawMusicBackdrop(g,now);
        portrait.DrawBehindDesk(g);
        portrait.Draw(g,false);
        // The raised desk covers the lower clothing and hair. Both hands
        // render last so they continue to rest on top of the keyboard and selected mouse prop.
        DrawDesk(g,now);
        if(MouseStyle==PetMouseStyles.KnightSword)DrawSword(g,now);else DrawMouse(g,now);
        PointF leftTip=new PointF(hands.LeftPalm.X,hands.LeftPalm.Y+9);
        PointF rightTip=new PointF(hands.RightPalm.X,hands.RightPalm.Y+9);
        sprites.Arms[0].Draw(g,Shoulder(true),leftTip);
        sprites.Arms[1].Draw(g,Shoulder(false),rightTip);
        DrawCyberMouseInstrument(g);
        if (!follow || resting) return;
        DrawMouseFeedback(g,now);
        if (input.BubbleMode)
        {
            if (input.LiveChord.Length > 0) DrawBubble(g,LiveBubble,input.LiveChord,252,true,inputFont);
            foreach (var particle in input.Floating(now))
            {
                double age = Math.Max(0,now-particle.Born);
                int alpha = (int)(230*Math.Max(0,1-Math.Pow(age/2.4,1.2)));
                DrawBubble(g,FloatingBubble(particle,now),particle.Label,alpha,false,floatingFont);
            }
        }
        else
        {
            string label = input.KeyDisplay(now);
            if (label.Length > 0) DrawKeyDisplay(g, label);
        }
    }

    internal bool Hit(PointF p, bool resting, bool follow)
    {
        if (follow && !resting && (input.BubbleMode
            ? input.LiveChord.Length > 0 && LiveBubble.Contains(p)
            : input.KeyDisplay(renderTime).Length > 0 && CurrentKeyFrame.Contains(p))) return true;
        if(MouseStyle==PetMouseStyles.KnightSword&&HitSword(p))return true;
        if(HitOrnament(p))return true;
        using (var pad = PetDeskProjection.Polygon(PetDeskProjection.MousePad)) if (pad.IsVisible(p)) return true;
        using (var keyboard = PetDeskProjection.Polygon(PetDeskProjection.Keyboard)) if (keyboard.IsVisible(p)) return true;
        if(portrait.Hit(new PointF((p.X-CharacterBounds.X)*300/CharacterBounds.Width,(p.Y-CharacterBounds.Y)*229/CharacterBounds.Height)))return true;
        for(int i=0;i<sprites.Arms.Length;i++)
        {
            // Use the same affine mapping for the raised shoulder silhouette.
            var arm=sprites.Arms[i];
            PointF tip=i==0 ? new PointF(hands.LeftPalm.X,hands.LeftPalm.Y+9) : new PointF(hands.RightPalm.X,hands.RightPalm.Y+9);
            PointF shoulder=Shoulder(i==0);
            PointF a=arm.Map(PointF.Empty,shoulder,tip), b=arm.Map(new PointF(arm.Image.Width,0),shoulder,tip),
                c=arm.Map(new PointF(0,arm.Image.Height),shoulder,tip);
            float ux=b.X-a.X,uy=b.Y-a.Y,vx=c.X-a.X,vy=c.Y-a.Y,det=ux*vy-uy*vx;
            if(Math.Abs(det)<.001)continue;
            float px=p.X-a.X,py=p.Y-a.Y,u=(px*vy-py*vx)/det,v=(ux*py-uy*px)/det;
            if(u>=0&&u<1&&v>=0&&v<1&&arm.Image.GetPixel((int)(u*arm.Image.Width),(int)(v*arm.Image.Height)).A>=32)return true;
        }
        return false;
    }
    internal bool Interact(PointF p,double now)
    {
        if(p.Y<CharacterBounds.Y+CharacterBounds.Height*.8f&&
            portrait.Hit(new PointF((p.X-CharacterBounds.X)*300/CharacterBounds.Width,(p.Y-CharacterBounds.Y)*229/CharacterBounds.Height)))
        {portrait.React(now);return true;}
        return false;
    }
    public void Dispose() { portrait.Dispose(); devices.Dispose(); swordTexture.Dispose(); mouseTexture.Dispose(); if(ornamentTexture!=null)ornamentTexture.Dispose(); if(ornamentHitMask!=null)ornamentHitMask.Dispose(); keyFont.Dispose(); inputFont.Dispose(); floatingFont.Dispose(); codeFont.Dispose(); }
}

internal static class PetDeskOrnaments
{
    internal const int None=0,Knight=1,PirateShip=2,Gundam=3;
    internal static int Normalize(int value){return value>=None&&value<=Gundam ? value : None;}
    static readonly Color Ink=Color.FromArgb(33,36,47),White=Color.FromArgb(240,244,247),Silver=Color.FromArgb(171,185,201),
        Dark=Color.FromArgb(48,58,75),Red=Color.FromArgb(204,48,64),Gold=Color.FromArgb(244,195,77),Blue=Color.FromArgb(40,104,183);
    static void Part(Graphics g,Color color,params float[] xy)
    {
        var points=new PointF[xy.Length/2];for(int i=0;i<points.Length;i++)points[i]=new PointF(xy[i*2],xy[i*2+1]);
        using(var fill=new SolidBrush(color))using(var edge=new Pen(Ink,1.15f)){edge.LineJoin=LineJoin.Round;g.FillPolygon(fill,points);g.DrawPolygon(edge,points);}
    }
    static void Oval(Graphics g,Color color,float x,float y,float w,float h)
    {using(var fill=new SolidBrush(color))using(var edge=new Pen(Ink,.9f)){g.FillEllipse(fill,x,y,w,h);g.DrawEllipse(edge,x,y,w,h);}}
    static void Line(Graphics g,Color color,float width,params float[] xy)
    {
        var points=new PointF[xy.Length/2];for(int i=0;i<points.Length;i++)points[i]=new PointF(xy[i*2],xy[i*2+1]);
        using(var pen=new Pen(color,width)){pen.StartCap=pen.EndCap=LineCap.Round;pen.LineJoin=LineJoin.Round;g.DrawLines(pen,points);}
    }
    internal static Bitmap Create(int style)
    {
        style=Normalize(style);if(style==None)return null;
        var image=new Bitmap(192,288,PixelFormat.Format32bppPArgb);
        try
        {
            using(var g=Graphics.FromImage(image))
            {
                g.ScaleTransform(3,3);g.SmoothingMode=SmoothingMode.AntiAlias;
                Oval(g,style==Gundam ? Dark : Silver,4,86,56,8);
                Line(g,style==Gundam ? Color.FromArgb(53,237,244) : White,1,9,89,54,89);
                if(style==Knight)DrawKnight(g);else if(style==PirateShip)DrawShip(g);else DrawGundam(g);
            }
            return image;
        }
        catch{image.Dispose();throw;}
    }
    static void DrawKnight(Graphics g)
    {
        // The original knight's closed silver helmet, scarf, shield and sword.
        Part(g,Red,20,39,43,40,48,78,33,75,16,80);
        Part(g,Dark,23,64,42,64,42,80,23,80);
        Part(g,Silver,23,65,31,65,30,83,21,83);
        Part(g,White,34,65,42,65,44,83,34,83);
        Part(g,Dark,20,82,31,82,32,87,18,87);
        Part(g,Dark,34,82,44,82,47,87,33,87);
        Part(g,Silver,21,41,43,41,44,60,39,68,24,67,19,57);
        Part(g,White,23,42,32,41,32,63,23,60);
        Line(g,Dark,1,32,43,32,62);Line(g,Silver,1,23,49,30,52);
        Part(g,White,16,41,23,43,21,51,13,50);
        Part(g,Silver,42,43,49,42,52,51,44,53);
        Part(g,Dark,14,49,21,51,19,64,12,62);
        Part(g,Dark,45,51,51,50,54,64,47,66);
        Part(g,White,51,39,54,34,57,39,56,69,51,69);
        Line(g,Silver,.7f,54,39,54,67);Line(g,Gold,2,48,69,59,69);Line(g,Dark,3,54,72,54,80);
        Part(g,Silver,18,19,25,13,39,13,46,21,44,36,37,42,25,40,18,33);
        Part(g,White,20,20,29,15,32,19,31,38,24,36,20,31);
        Part(g,Color.FromArgb(111,129,153),34,19,41,18,44,23,42,35,35,39);
        Part(g,Dark,21,25,43,25,42,29,22,29);
        Line(g,Gold,.8f,24,27,29,27,32,28,37,27,40,27);
        Line(g,Silver,1,32,19,32,37);Line(g,Dark,.7f,36,32,39,33,36,35);
        Part(g,Red,27,14,27,8,34,4,42,6,36,10,33,15);
        Part(g,Red,23,38,34,41,43,37,43,43,32,47,22,43);
        Part(g,Red,38,43,47,44,45,52,40,49);
        Part(g,White,6,54,23,51,24,69,16,78,7,70);
        Part(g,Red,9,57,20,55,21,68,16,74,10,68);
        Line(g,Gold,1.3f,15,58,16,70);Line(g,Gold,1.3f,12,62,20,61);
    }
    static void DrawShip(Graphics g)
    {
        Oval(g,Color.FromArgb(78,145,166),7,80,51,9);
        Part(g,Color.FromArgb(100,59,40),4,66,16,69,49,68,61,63,53,79,40,85,17,83,9,77);
        Part(g,Color.FromArgb(174,111,62),9,69,51,69,56,67,49,77,38,81,18,79);
        Line(g,Gold,1.3f,7,67,18,71,48,70,59,65);
        Line(g,Color.FromArgb(82,48,37),1,14,75,47,75);
        for(int i=0;i<4;i++)Oval(g,Gold,17+i*8,73,3,3);
        Line(g,Ink,2.6f,31,17,31,69);Line(g,Color.FromArgb(173,111,66),1.2f,31,18,31,68);
        Line(g,Ink,1.6f,12,28,49,28);Line(g,Ink,1.5f,16,57,47,57);
        using(var sail=new GraphicsPath())
        {
            sail.AddBezier(14,29,19,34,16,48,12,55);sail.AddBezier(12,55,25,59,40,59,49,54);
            sail.AddBezier(49,54,44,46,45,35,49,29);sail.CloseFigure();
            using(var fill=new SolidBrush(Color.FromArgb(244,225,182)))using(var edge=new Pen(Ink,1.2f)){g.FillPath(fill,sail);g.DrawPath(edge,sail);}
        }
        Line(g,Color.FromArgb(198,172,130),.65f,20,32,21,52);Line(g,Color.FromArgb(198,172,130),.65f,42,32,41,52);
        Oval(g,Ink,26,37,12,10);Oval(g,White,28,39,3,3);Oval(g,White,33,39,3,3);
        Line(g,Ink,2,29,47,34,47);Line(g,Ink,1.2f,25,49,38,53);Line(g,Ink,1.2f,25,53,38,49);
        Part(g,Red,47,42,58,59,47,58);
        Part(g,Ink,32,16,32,7,48,10,42,14,48,18);
        Line(g,White,.8f,35,11,40,14);Line(g,White,.8f,35,14,40,11);
        Line(g,Color.FromArgb(178,145,99),.8f,30,20,8,66);Line(g,Color.FromArgb(178,145,99),.8f,33,20,57,64);
        Part(g,Color.FromArgb(122,74,46),42,61,50,61,50,68,42,68);
        Line(g,White,.7f,13,84,22,85,27,83);Line(g,White,.7f,41,86,49,84,55,85);
    }
    static void DrawGundam(Graphics g)
    {
        Part(g,Dark,21,43,43,43,47,68,18,68);
        Part(g,Silver,19,67,30,67,29,83,19,84);Part(g,White,34,67,44,67,46,84,35,84);
        Part(g,White,20,73,29,73,27,84,19,83);Part(g,Silver,36,73,44,73,46,84,37,84);
        Part(g,Red,17,83,29,83,30,88,13,88);Part(g,Red,35,83,47,83,51,88,34,88);
        Part(g,White,17,59,31,57,31,70,18,69);Part(g,White,33,58,46,60,46,69,33,71);
        Part(g,Red,27,60,37,60,38,69,32,73,26,68);
        Part(g,Blue,21,40,43,40,45,58,38,64,25,62,19,56);
        Part(g,White,8,40,21,38,26,45,21,53,6,49);Part(g,White,43,38,56,40,59,50,44,53,39,46);
        Part(g,Silver,8,49,19,51,18,61,9,62);Part(g,Silver,46,51,56,50,56,63,47,63);
        Part(g,White,8,59,17,58,18,70,7,70);Part(g,White,47,60,56,60,58,71,48,72);
        Part(g,Gold,23,45,29,45,29,53,23,52);Part(g,Gold,35,45,41,45,41,52,35,53);
        Line(g,Dark,.6f,25,47,28,47);Line(g,Dark,.6f,36,47,39,47);
        Part(g,Red,29,54,35,54,38,60,32,64,27,59);
        Part(g,White,22,23,27,18,38,18,43,24,41,37,34,42,25,38);
        Part(g,Silver,36,24,42,25,40,37,35,39);
        Part(g,Dark,25,28,40,28,39,32,25,32);
        Line(g,Color.FromArgb(64,242,228),1,27,30,30,30);Line(g,Color.FromArgb(64,242,228),1,35,30,38,30);
        Part(g,White,27,32,37,32,36,37,32,40,28,37);
        Line(g,Dark,.7f,30,35,34,35);Line(g,Dark,.7f,30,37,34,37);
        Part(g,Red,30,39,35,39,34,43,31,43);
        Part(g,Gold,31,26,12,8,23,28,30,30);Part(g,Gold,33,26,52,8,42,28,34,30);
        Part(g,White,30,18,35,18,35,28,30,28);Part(g,Red,31,21,34,21,34,26,31,26);
        Part(g,Dark,4,59,10,57,13,62,12,80,7,82,7,69,3,67);
        Line(g,Silver,1,6,61,10,61);
        Part(g,White,48,56,60,58,60,77,54,83,48,76);
        Part(g,Red,51,60,57,61,57,75,54,79,51,74);
        Line(g,Gold,1,52,66,57,66);Line(g,Gold,1,54,63,54,71);
    }
}

internal static class BoundPetTests
{
    static void Check(bool value, string message) { if (!value) throw new Exception("绑定动画自检：" + message); }
    static double Distance(PointF a, PointF b) { return Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y)); }
    static int AlphaWidth(Bitmap image,float at)
    {
        int first=image.Width,last=-1,y=(int)(image.Height*at);
        for(int x=0;x<image.Width;x++)if(image.GetPixel(x,y).A>64){first=Math.Min(first,x);last=x;}
        return Math.Max(0,last-first+1);
    }
    static bool ClearEdges(Bitmap image)
    {
        for(int x=0;x<image.Width;x++)if(image.GetPixel(x,0).A!=0||image.GetPixel(x,image.Height-1).A!=0)return false;
        for(int y=0;y<image.Height;y++)if(image.GetPixel(0,y).A!=0||image.GetPixel(image.Width-1,y).A!=0)return false;
        return true;
    }
    static void Frames(BoundPetScene scene,double start,int count)
    {
        for(int i=1;i<=count;i++)
        {
            scene.Advance(start+i*.016,.016,false);
            Check(scene.Hands.LeftPalm.X+30<=scene.Hands.RightPalm.X,"双手全程不交叉或重叠");
        }
    }
    static void CheckWorkExpressions(KeyboardPetSprites sprites)
    {
        double[] rates={1,4,6,10};
        var expected=new[] {WorkMood.Thinking,WorkMood.Focused,WorkMood.Rushing,WorkMood.Turbo};
        int[] mouths={1,2,3,5};
        for(int mode=0;mode<rates.Length;mode++)
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
                    while(tap/rates[mode]<=now+.00001)
                    {
                        double at=tap/rates[mode];int code=65+tap%20;
                        state.Key(code,true,at);state.Key(code,false,at+.001);tap++;
                    }
                    scene.Advance(now,.05,false);
                    Check(ClearEdges(scene.Portrait.Image),"速度表情、汗滴与动作不越出人物绘制边缘");
                }
                Check(scene.Portrait.Mood==expected[mode]&&scene.Portrait.MouthIndex==mouths[mode],
                    "实际键盘事件驱动完整人物表情与嘴形："+rates[mode]);
                g.Clear(Color.Transparent);scene.Draw(g,5.4,false,true);
                PointF formerOrnament=new PointF(353,350);
                Check(image.GetPixel((int)formerOrnament.X,(int)formerOrnament.Y).A==0&&!scene.Hit(formerOrnament,false,true),"移除摆件后原位置完全透明且点击穿透");
                Check(!scene.Hit(formerOrnament,false,false),"关闭跟随时原摆件位置仍点击穿透");
                Check(!scene.Hit(new PointF(438,435),false,true),"旧悬浮状态条位置不再拦截桌面");
                Check(!scene.Hit(new PointF(407,308),false,true),"原摆件边缘不拦截桌面");
                for(int frame=109;frame<=146;frame++)scene.Advance(frame*.05,.05,false);
                Check(scene.Portrait.Mood==WorkMood.Relieved&&scene.Portrait.MouthIndex==6,"停打后整个人物进入闭眼轻笑的放松表情");
                scene.Advance(10,.1,false);
                Check(scene.Portrait.Mood==WorkMood.Idle&&!scene.Hit(formerOrnament,false,true),"放松结束恢复待机，原摆件位置保持透明");
                scene.Advance(10.1,.1,true);
                Check(scene.Portrait.MouthIndex==7&&scene.Portrait.Blink==1,"休息使用放松闭眼和独立睡眠嘴形");
                foreach(int width in new[] {252,336,420})for(int display=0;display<3;display++)
                {
                    g.ResetTransform();g.Clear(Color.Transparent);float scale=width/336f;g.ScaleTransform(scale,scale);
                    scene.Draw(g,10.1,display==1,display!=2);
                    Check(image.GetPixel((int)(formerOrnament.X*scale),(int)(formerOrnament.Y*scale)).A==0,
                        "三种尺寸下待机、休息及暂停跟随均不绘制摆件");
                }
            }
        }
    }
    internal static void Run(string root)
    {
        Check(LivingPetPortrait.BlinkAt(2.8)==0&&LivingPetPortrait.BlinkAt(2.84)>0&&LivingPetPortrait.BlinkAt(2.84)<.5&&
            LivingPetPortrait.BlinkAt(2.90)==1&&LivingPetPortrait.BlinkAt(2.94)==1&&
            LivingPetPortrait.BlinkAt(3.03)>0&&LivingPetPortrait.BlinkAt(3.03)<.6&&LivingPetPortrait.BlinkAt(3.14)==0,
            "眨眼快速落睑、短暂停留、稍慢睁开，持续约 0.33 秒");
        Check(LivingPetPortrait.BlinkAt(6.82)==1&&LivingPetPortrait.BlinkAt(7.05)==0&&LivingPetPortrait.BlinkAt(7.20)==1,
            "偶尔双眨，中间完整睁眼，间隔并非等长");
        for(int i=1;i<=3200;i++)
        {
            float blink=LivingPetPortrait.BlinkAt(i*.01);
            Check(blink>=0&&blink<=1&&Math.Abs(blink-LivingPetPortrait.BlinkAt(i*.01-.01))<.17,
                "眼睑开合连续，双眨及循环边界没有跳帧");
        }
        Check(PetKeyboardLayout.Keys.Length==78&&PetKeyboardLayout.Keys.Select(k=>k.Code).Distinct().Count()==78,"78 个独立键位");
        Check(PetKeyboardLayout.Find(269)==null&&PetKeyboardLayout.Find(38)!=null&&PetKeyboardLayout.Find(123)!=null,"紧凑功能行及方向键");
        PointF[] plane=PetDeskProjection.Keyboard;
        Check(plane[2].X-plane[3].X<200&&plane[1].X-plane[0].X<175,"键盘进一步缩小");
        Check(plane[0].Y<=341&&plane[1].Y<=347&&PetDeskProjection.MousePad[0].Y<=326,"键鼠整体上移并覆盖衣身下沿");
        Check(PetKeyboardLayout.Find(32).Center.Y+25<PetKeyboardLayout.Find(27).Center.Y,"空格靠角色，功能行靠观察者");
        PointF[] space=PetKeyboardLayout.Find(32).Corners;
        Check(space[0].X>space[1].X&&space[0].Y>space[3].Y,"键盘与键帽整体转向角色");
        var bindings=new PetInputBindings();
        Check(bindings.Resolve(65)==65&&bindings.Resolve(269)==13&&bindings.Resolve(103)==55,"78 键与数字区兼容绑定");
        var releaseState=new InputFeedbackState();
        var releasePose=new CoordinatedPetHands(bindings,releaseState);
        releaseState.Key(65,true,0);releasePose.Advance(0,.016,false);releasePose.Advance(1,.1,false);
        releaseState.Key(65,false,1.01);releasePose.Advance(1.02,.02,false);
        Check(releasePose.Typing,"长按松开后保留收势时间");
        releasePose.Advance(1.38,.1,false);Check(!releasePose.Typing,"停止输入后退出打字姿态");
        string file=Path.GetTempFileName();
        try
        {
            bindings.Map[65]=76;bindings.MouseGain=.6;bindings.Save(file);
            var loaded=PetInputBindings.Load(file);
            Check(loaded.Resolve(65)==76&&loaded.MouseGain==.6,"自定义绑定保存 / 读取");
            File.WriteAllText(file,"269\t269\n65\t103\n36\t36\n");
            loaded=PetInputBindings.Load(file);
            Check(loaded.Resolve(269)==13&&loaded.Resolve(65)==55&&loaded.Resolve(36)==0,"旧绑定迁移");
        }
        finally {File.Delete(file);}
        bindings=new PetInputBindings();var state=new InputFeedbackState();
        using(var sprites=new KeyboardPetSprites(root))
        using(var scene=new BoundPetScene(sprites,bindings,state))
        using(var image=new Bitmap(BoundPetScene.WorldWidth,BoundPetScene.WorldHeight,PixelFormat.Format32bppArgb))
        using(var g=Graphics.FromImage(image))
        {
            LayeredRigChecks.Run(scene,sprites);
            CheckWorkExpressions(sprites);
            Check(sprites.Arms.Length==2,"左右肩甲到手套的完整素材");
            PointF leftShoulder=scene.Shoulder(true),rightShoulder=scene.Shoulder(false);
            Check(rightShoulder.X-leftShoulder.X<85&&leftShoulder.Y<PetDeskProjection.Keyboard[0].Y-16,
                "窄肩位于颈部两侧、桌面上方，双臂向前搭桌");
            foreach(PointF source in new[] {BoundPetScene.LeftShoulderSource,BoundPetScene.RightShoulderSource})
            {
                var body=sprites.Rig.Parts["body"];
                Color fabric=body.Image.GetPixel((int)((source.X-body.Bounds.X)*body.Image.Width/body.Bounds.Width),
                    (int)((source.Y-body.Bounds.Y)*body.Image.Height/body.Bounds.Height));
                Check(fabric.A>240&&fabric.G<110&&fabric.B<140,"肩部定位在黑色衣身的袖口内，而非白发中");
            }
            using(var motion=new LivingPetPortrait(sprites))
            {
                for(int i=1;i<=50;i++)motion.Update(i*.016,.016,false,true,false);
                Check(motion.Activity>.95,"连续打字带动头部与发梢");
                float tilt=motion.Tilt,hair=motion.Hair;
                motion.Update(1.2,.1,false,true,false);
                Check(Math.Abs(motion.Tilt-tilt)>.002&&Math.Abs(motion.Hair-hair)>.1,"头部与发梢错开相位轻摆");
                float minTilt=1,maxTilt=-1,minBob=10,maxBob=-10;
                for(int i=1;i<=50;i++)
                {
                    motion.Update(1.2+i*.08,.08,false,true,false);
                    minTilt=Math.Min(minTilt,motion.Tilt);maxTilt=Math.Max(maxTilt,motion.Tilt);
                    minBob=Math.Min(minBob,motion.Bob);maxBob=Math.Max(maxBob,motion.Bob);
                    Check(ClearEdges(motion.Image),"身体带动头颈摆动时人物和头发没有裁切");
                    foreach(PointF source in new[] {BoundPetScene.LeftShoulderSource,BoundPetScene.RightShoulderSource})
                    {
                        PointF world=motion.WorldPoint(source);
                        PointF local=new PointF((world.X-BoundPetScene.CharacterBounds.X)*300/BoundPetScene.CharacterBounds.Width,
                            (world.Y-BoundPetScene.CharacterBounds.Y)*229/BoundPetScene.CharacterBounds.Height);
                        // GDI+ matrix inversion can round by a few hundredths
                        // of a rig unit; .05 is still less than .06 screen px.
                        Check(Distance(motion.SourcePoint(local),source)<.05,"摇头点头时肩部连接与衣身坐标保持重合");
                    }
                }
                Check(maxTilt-minTilt>.06&&maxTilt-minTilt<.13&&maxBob-minBob>3&&maxBob-minBob<4.5,
                    "操作时保留可见轻摆，限制大头甩动与上下颠簸幅度");
                for(int y=0;y<motion.Image.Height;y+=7)
                for(int x=0;x<motion.Image.Width;x+=7)
                    Check(motion.Hit(new PointF(x-LivingPetPortrait.Padding,y-LivingPetPortrait.Padding))==(motion.Image.GetPixel(x,y).A>=32),"人物命中跟随变形后的透明轮廓");
                for(int i=1;i<=100;i++)motion.Update(1.2+i*.016,.016,false,false,false);
                Check(motion.Activity<.001,"停止操作后轻摆自然减弱");
                float minIdle=1,maxIdle=-1,minGaze=1,maxGaze=-1;
                for(int i=0;i<160;i++)
                {
                    motion.Update(i*.2,.2,false,false,false);
                    minIdle=Math.Min(minIdle,motion.Tilt);maxIdle=Math.Max(maxIdle,motion.Tilt);
                    minGaze=Math.Min(minGaze,motion.GazeX);maxGaze=Math.Max(maxGaze,motion.GazeX);
                    Check(ClearEdges(motion.Image),"待机歪头、左右张望和点头完整保留透明边缘");
                }
                Check(maxIdle-minIdle>.06&&maxIdle-minIdle<.13&&maxGaze-minGaze>1,"无输入也会小幅歪头停顿、左右张望，视线随姿态变化");
                motion.Update(20.5,.5,false,false,false);Check(motion.Smile>.8,"待机偶尔露出更明显的笑容");
                motion.Update(20.5,.5,true,false,false);Check(motion.Blink==1&&motion.IdleGesture==0,"休息关闭张望和待机小动作");
            }
            for(int i=0;i<sprites.Arms.Length;i++)
            {
                var arm=sprites.Arms[i];
                Check(AlphaWidth(arm.Image,.84f)>AlphaWidth(arm.Image,.15f)*1.5,"手套宽大，肩甲明显小于手掌");
                PointF shoulder=scene.Shoulder(i==0);
                foreach(var cap in PetKeyboardLayout.Keys)
                {
                    PointF tip=cap.Center;
                    Check(Distance(arm.Map(arm.Shoulder,shoulder,tip),shoulder)<.001&&
                        Distance(arm.Map(arm.Fingertip,shoulder,tip),tip)<.001,"肩与指尖共用一个整体变换并保持绑定");
                    PointF middle=new PointF((arm.Shoulder.X+arm.Fingertip.X)/2,(arm.Shoulder.Y+arm.Fingertip.Y)/2);
                    PointF expected=new PointF((shoulder.X+tip.X)/2,(shoulder.Y+tip.Y)/2);
                    Check(Distance(arm.Map(middle,shoulder,tip),expected)<.001,"整条手臂轴线保持直线，没有肘部折弯");
                }
                Check(arm.Image.GetPixel(0,0).A<32,"一体手臂保留透明边缘");
            }
            Check(Distance(scene.TargetFor(65),scene.TargetFor(13))>100,"不同键位保持独立落点");
            bindings.Map[65]=76;
            Check(Distance(scene.TargetFor(65),scene.TargetFor(76))==0,"自定义绑定改变手部落点");bindings.Map[65]=65;
            scene.Advance(0,0,false);
            Check(!scene.Hands.Typing&&Distance(scene.Hands.LeftContact,scene.MousePosition)<1&&
                Distance(scene.Hands.RightContact,CoordinatedPetHands.RightRest)<1,"不打字时一手鼠标、一手自然放松");
            PointF before=scene.Hands.LeftContact;
            state.Key(65,true,0);scene.Advance(.016,.016,false);
            Check(scene.Hands.Typing&&scene.Hands.TypingBlend>0&&scene.Hands.TypingBlend<.5&&
                Distance(before,scene.Hands.LeftContact)<20,"进入双手打字时平滑抬手");
            Frames(scene,.016,39);
            Check(scene.Hands.TypingBlend>.99&&Distance(scene.Hands.RightContact,scene.TargetFor(65))<2&&
                scene.Hands.RightLift<1&&scene.Hands.LeftContact.X>150,"按 A 时右侧手落键，另一手到键盘准备");
            PointF parked=scene.MousePosition;
            state.Move(100,100,.65);state.Move(300,0,.66);scene.Advance(.67,.03,false);
            Check(Distance(parked,scene.MousePosition)==0&&scene.Hands.Typing,"双手打字时不同时拉动鼠标");
            state.Key(74,true,.70);Frames(scene,.70,40);
            Check(scene.Hands.LeftSource==74&&scene.Hands.RightSource==65&&
                Distance(scene.Hands.LeftContact,scene.TargetFor(74))<2&&
                Distance(scene.Hands.RightContact,scene.TargetFor(65))<2,"A 与 J 由双手分别准确落键");
            state.Key(65,false,1.36);state.Key(74,false,1.36);
            state.Key(72,true,1.37);state.Key(74,true,1.37);Frames(scene,1.37,40);
            Check(scene.Hands.RightPalm.X-scene.Hands.LeftPalm.X>=34,"相邻中间键避免双手相撞");
            state.Key(72,false,2.02);state.Key(74,false,2.02);
            state.Key(68,true,2.03);state.Key(68,false,2.04);scene.Advance(2.08,.07,false);
            Check(scene.Hands.Typing,"快速点按间隙保持打字姿态");
            Frames(scene,2.4,40);
            Check(!scene.Hands.Typing&&scene.Hands.TypingBlend==0&&
                Distance(scene.Hands.LeftContact,scene.MousePosition)<2&&
                Distance(scene.Hands.RightContact,CoordinatedPetHands.RightRest)<2,"停打后协调收手、回到鼠标与待机位");
            state.Clear();state.Move(100,100,3.1);state.Move(180,40,3.11);Frames(scene,3.1,20);
            Check(scene.MousePosition.X>80&&scene.MousePosition.X<87&&scene.MousePosition.Y<CoordinatedPetHands.MouseHome.Y-3,"小幅鼠标保持真实右上方向");
            state.Move(-100,200,3.5);Frames(scene,3.5,20);
            Check(scene.MousePosition.X>61&&scene.MousePosition.X<64&&scene.MousePosition.Y>CoordinatedPetHands.MouseHome.Y+4&&scene.MousePosition.Y<CoordinatedPetHands.MouseHome.Y+6,"鼠标反向且限制较小幅度");
            state.Clear();state.Button(1,true,4);state.Wheel(120,false,4);
            g.Clear(Color.Transparent);scene.Draw(g,4,false,true);
            int frameX=(int)BoundPetScene.KeyDisplayFrame.Right-12,frameY=(int)BoundPetScene.KeyDisplayFrame.Y+20;
            Check(image.GetPixel(frameX,frameY).A==0,"鼠标动作不写入按键框");
            state.Key(162,true,5);state.Key(160,true,5);state.Key(65,true,5);Frames(scene,5,40);
            g.Clear(Color.Transparent);scene.Draw(g,5.64,false,true);
            Check(image.GetPixel(frameX,frameY).A>240&&state.KeyDisplay(5.64)=="Ctrl + Shift + A","固定框显示组合键");
            state.Key(65,false,5.8);state.Key(162,false,5.8);state.Key(160,false,5.8);scene.Advance(5.9,.1,false);
            g.Clear(Color.Transparent);scene.Draw(g,5.9,false,true);
            Check(image.GetPixel(frameX,frameY).A>240&&scene.Hit(new PointF(frameX,frameY),false,true),"松开后在原位置短暂保留且显示与命中一致");
            scene.Advance(6.9,.1,false);g.Clear(Color.Transparent);scene.Draw(g,6.9,false,true);
            Check(image.GetPixel(frameX,frameY).A==0&&!scene.Hit(new PointF(frameX,frameY),false,true),"按键框到时隐藏并恢复点击穿透");
            state.Clear();state.Key(65,true,9);state.Key(74,true,9.1);Frames(scene,9.1,40);
            g.Clear(Color.Transparent);scene.Draw(g,9.74,false,true);
            state.Clear();Frames(scene,10,60);
            g.Clear(Color.Transparent);scene.Draw(g,10.96,false,true);
            state.Key(36,true,11);Frames(scene,11,30);
            Check(!scene.Hands.Typing&&!state.LitSources(11.48).Any(k=>bindings.Resolve(k)!=0),"未绑定按键不触发假敲打");
        }
    }
}
