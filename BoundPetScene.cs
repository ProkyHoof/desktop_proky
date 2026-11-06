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
        g.DrawImage(Image,corners,new RectangleF(0,0,Image.Width,Image.Height),GraphicsUnit.Pixel);
    }
    public void Dispose() {Image.Dispose();}
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
    readonly Bitmap mouseTexture=CreateMouseTexture();
    readonly Font keyFont = new Font("Arial", 3.5f, FontStyle.Bold, GraphicsUnit.Point);
    readonly Font inputFont = new Font("Microsoft YaHei UI", 11, FontStyle.Bold);
    readonly Font floatingFont = new Font("Microsoft YaHei UI", 10, FontStyle.Bold);
    readonly Dictionary<int, float> lifts = new Dictionary<int, float>();
    sealed class TrailPoint { internal PointF Point; internal double At; }
    readonly List<TrailPoint> trail = new List<TrailPoint>();
    readonly CoordinatedPetHands hands;
    PointF mouse { get { return hands.Mouse; } }
    float mouseTilt { get { return 20+(hands.Mouse.X-74)*.1f; } }
    long lastRevision = -1;
    bool lastRest, hadMouseFeedback;
    readonly LivingPetPortrait portrait;
    double renderTime;
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
        using (var g = Graphics.FromImage(devices))
        {
            g.Clear(Color.Transparent); g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.ScaleTransform(DeviceScale,DeviceScale);
            g.TranslateTransform(-DeviceBounds.X,-DeviceBounds.Y);
            using (var pad = PetDeskProjection.Polygon(PetDeskProjection.MousePad))
            using (var fill = new SolidBrush(Color.FromArgb(41, 41, 49)))
            using (var edge = new Pen(Color.FromArgb(163, 38, 53), 2)) { g.FillPath(fill, pad); g.DrawPath(edge, pad); }
            DrawKeyboardShell(g);
            foreach (var cap in PetKeyboardLayout.Keys) DrawCap(g, cap, false, 1);
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
    static void DrawKeyboardShell(Graphics g)
    {
        PointF[] corners=PetDeskProjection.Keyboard;
        PointF[] basePoints=corners.Select(p=>new PointF(p.X,p.Y+4.3f)).ToArray();
        Color ink=Color.FromArgb(54,44,59);
        using(var shape=SoftPolygon(basePoints,3))
        using(var fill=new SolidBrush(Color.FromArgb(164,157,179)))
        using(var edge=new Pen(ink,1.25f)) {g.FillPath(fill,shape);g.DrawPath(edge,shape);}
        using(var shape=SoftPolygon(corners,3))
        using(var silver=new LinearGradientBrush(new RectangleF(103,355-PetDeskProjection.Lift,188,48),Color.FromArgb(251,246,249),Color.FromArgb(206,200,218),90))
        using(var edge=new Pen(ink,1.25f)) {g.FillPath(silver,shape);g.DrawPath(edge,shape);}
        PointF center=new PointF(corners.Average(p=>p.X),corners.Average(p=>p.Y));
        PointF[] tray=corners.Select(p=>new PointF(center.X+(p.X-center.X)*.974f,center.Y+(p.Y-center.Y)*.91f)).ToArray();
        using(var shape=SoftPolygon(tray,2))
        using(var fill=new SolidBrush(Color.FromArgb(110,101,121)))g.FillPath(fill,shape);
        // A scarf-red seam and a small silver sword echo the knight's accessories.
        using(var seam=new Pen(Color.FromArgb(151,49,72),1.1f))
        {
            seam.StartCap=seam.EndCap=LineCap.Round;
            g.DrawLine(seam,PetDeskProjection.DeskPoint(111,399.5f),PetDeskProjection.DeskPoint(283,405.8f));
        }
        using(var sword=new Pen(Color.FromArgb(250,241,240),.65f))
        {g.DrawLine(sword,PetDeskProjection.DeskPoint(198,402),PetDeskProjection.DeskPoint(198,405));
            g.DrawLine(sword,PetDeskProjection.DeskPoint(196.5f,402.8f),PetDeskProjection.DeskPoint(199.5f,402.9f));}
    }
    internal PointF TargetFor(int source) { return hands.TargetFor(source); }
    internal bool Advance(double now, double dt, bool resting)
    {
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
        PointF[] p = cap.Corners;
        bool accent = cap.Code == 27 || cap.Code == 13 || cap.Code == 32;
        bool modifier = cap.Code == 8 || cap.Code == 9 || cap.Code == 20 || cap.Code >= 160 && cap.Code <= 165 || cap.Code >= 91 && cap.Code <= 93;
        Color normal = accent ? Color.FromArgb(193,48,73) : modifier ? Color.FromArgb(209,201,220) : Color.FromArgb(250,245,247);
        Color fillColor = lit ? Color.FromArgb((int)(255 * opacity), 247, 158, 173) : normal;
        Color outline=Color.FromArgb(92,77,99);
        PointF[] lower=p.Select(v=>new PointF(v.X,v.Y+1.05f)).ToArray();
        using(var baseShape=SoftPolygon(lower,.8f))
        using(var wall=new SolidBrush(accent ? Color.FromArgb(119,40,62) : Color.FromArgb(159,147,174)))
        using(var edge=new Pen(outline,.55f)) {g.FillPath(wall,baseShape);g.DrawPath(edge,baseShape);}
        if(lit)p=p.Select(v=>new PointF(v.X,v.Y+.65f)).ToArray();
        using(var shape=SoftPolygon(p,.8f))
        using(var fill=new SolidBrush(fillColor))
        using(var edge=new Pen(outline,.55f)) {g.FillPath(fill,shape);g.DrawPath(edge,shape);}
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
            using (var shape = SoftPolygon(p,.8f))
            using (var ink = new SolidBrush(accent && !lit ? Color.FromArgb(255,234,235) : Color.FromArgb(111,92,116)))
            using (var format = new StringFormat(StringFormat.GenericTypographic) { Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center,FormatFlags=StringFormatFlags.NoWrap })
            {
                g.SetClip(shape,CombineMode.Intersect); g.MultiplyTransform(transform);
                g.DrawString(label,keyFont,ink,new RectangleF(0,0,w,9),format);
            }
        }
        finally { g.Restore(saved); }
    }
    static Bitmap CreateMouseTexture()
    {
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
        return image;
    }
    internal void DrawMouse(Graphics g,double now)
    {
        for (int i=1;i<trail.Count;i++)
        {
            int a=(int)(70*Math.Max(0,1-(now-trail[i].At)/.22));
            using (var pen=new Pen(Color.FromArgb(a,239,109,128),2)) g.DrawLine(pen,trail[i-1].Point,trail[i].Point);
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
                using(var fill=new SolidBrush(Color.FromArgb((int)(205*strength),249,125,151)))
                using(var rim=new Pen(Color.FromArgb((int)(255*strength),185,43,77),1.2f))
                {
                    float sink=-2.6f*input.ButtonMotion(button,now);
                    plate.AddPolygon(new[] {new PointF(side*3,-19+sink),new PointF(side*9,-17+sink),
                        new PointF(side*11.5f,-12+sink),new PointF(side*11,-1+sink),new PointF(side*3,-3+sink)});
                    g.FillPath(fill,plate);g.DrawPath(rim,plate);
                }
                using(var click=new Pen(Color.FromArgb((int)(245*strength),249,84,110),2.2f))
                    g.DrawArc(click,-17,-25,34,44,button==1 ? 160 : 275,105);
            }
            float extra=Math.Max(input.ButtonFeedback(3,now),Math.Max(input.ButtonFeedback(4,now),input.ButtonFeedback(5,now)));
            if(extra>0)using(var click=new Pen(Color.FromArgb((int)(245*extra),249,84,110),2.2f))g.DrawEllipse(click,-16,-24,32,46);
            float wheelStrength=input.WheelFeedback(now);
            float scroll=wheelStrength*(float)Math.Sin((now-input.WheelAt)*26)*Math.Sign(input.WheelDelta)*2.2f;
            using(var wheel=Rounded(new RectangleF(-1.8f,-14+scroll,3.6f,8),1.2f))
            using(var ruby=new SolidBrush(Color.FromArgb(210,37,69)))g.FillPath(ruby,wheel);
            using(var shine=new Pen(Color.FromArgb(255,194,199),.7f))g.DrawLine(shine,-.8f,-12+scroll,.8f,-12+scroll);
            if(wheelStrength>0)
            using(var glow=new Pen(Color.FromArgb((int)(245*wheelStrength),255,117,148),1.6f))
                g.DrawRectangle(glow,-3.5f,-16,7,12);
        }
        finally {g.Restore(saved);}
    }
    // Motion marks sit outside the glove, where neither button is hidden by it.
    internal void DrawMouseFeedback(Graphics g,double now)
    {
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
                using(var echo=new Pen(Color.FromArgb((int)(185*pulse),245,113,143),1.7f))
                using(var spark=new Pen(Color.FromArgb((int)(245*pulse),255,190,200),2))
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
            using(var arrow=new Pen(Color.FromArgb((int)(245*wheel),248,125,153),2.1f))
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
    }
    void DrawMusicBackdrop(Graphics g,double now)
    {
        float blend=portrait.MusicBlend;
        if(blend<.01f)return;
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
            using(var fill=new LinearGradientBrush(bounds,Color.FromArgb(alpha,188,71,104),Color.FromArgb(alpha/3,126,102,167),90f))
            using(var tip=new Pen(Color.FromArgb((int)(alpha*.8f),204,111,140),1.8f))
            {
                Color end=Color.FromArgb(alpha/2,126,102,167);
                fill.InterpolationColors=new ColorBlend
                {
                    Positions=new[] {0f,.5f,1f},
                    Colors=new[] {end,Color.FromArgb(alpha,188,71,104),end}
                };
                g.FillPath(fill,shape);
                tip.StartCap=tip.EndCap=LineCap.Round;
                g.DrawLine(tip,bounds.X+3,bounds.Y+3.5f,bounds.Right-3,bounds.Y+3.5f);
                g.DrawLine(tip,bounds.X+3,bounds.Bottom-3.5f,bounds.Right-3,bounds.Bottom-3.5f);
            }
        }
    }
    internal void Draw(Graphics g, double now, bool resting, bool follow)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias; g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        DrawMusicBackdrop(g,now);
        portrait.DrawBehindDesk(g);
        portrait.Draw(g,false);
        // The raised desk covers the lower clothing and hair. Both hands
        // render last so they continue to rest on top of the keyboard/mouse.
        DrawDesk(g,now);
        DrawMouse(g, now);
        PointF leftTip=new PointF(hands.LeftPalm.X,hands.LeftPalm.Y+9);
        PointF rightTip=new PointF(hands.RightPalm.X,hands.RightPalm.Y+9);
        sprites.Arms[0].Draw(g,Shoulder(true),leftTip);
        sprites.Arms[1].Draw(g,Shoulder(false),rightTip);
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
    public void Dispose() { portrait.Dispose(); devices.Dispose(); mouseTexture.Dispose(); keyFont.Dispose(); inputFont.Dispose(); floatingFont.Dispose(); }
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
