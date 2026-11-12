using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

internal enum WorkMood { Idle, Thinking, Focused, Rushing, Turbo, Relieved }

// Only short-lived press times are needed for tempo; no typed text is kept.
internal sealed class TypingWorkRhythm
{
    readonly Queue<double> taps=new Queue<double>();
    double sessionStart,lastTap=-100,pendingSince;
    int sessionCount;
    WorkMood pending;
    internal WorkMood Mood;
    internal float KeysPerMinute;
    internal int RecentCount { get { return taps.Count; } }
    internal static bool IsTypingKey(int code)
    {
        return code>=48&&code<=57 || code>=65&&code<=90 || code>=96&&code<=111 ||
            code>=186&&code<=192 || code>=219&&code<=222 || code==226 ||
            code==8||code==13||code==269||code==32;
    }
    internal void Press(double now)
    {
        if(now-lastTap>1.3||now<lastTap){taps.Clear();sessionStart=now;sessionCount=0;}
        lastTap=now;sessionCount++;taps.Enqueue(now);
        while(taps.Count>96||taps.Count>0&&now-taps.Peek()>=3)taps.Dequeue();
    }
    internal void Reset()
    {
        taps.Clear();lastTap=-100;sessionStart=pendingSince=0;sessionCount=0;
        KeysPerMinute=0;Mood=pending=WorkMood.Idle;
    }
    internal void Update(double now,double dt,bool resting)
    {
        if(resting){Reset();return;}
        // A drained input event can be a few microseconds newer than the
        // UI frame timestamp captured just before draining the queue.
        now=Math.Max(now,lastTap);
        while(taps.Count>0&&now-taps.Peek()>=3)taps.Dequeue();
        double age=now-lastTap;
        float target=age>1.3 ? 0 : (float)Math.Min(960,taps.Count*60/Math.Max(1,Math.Min(3,now-sessionStart)));
        KeysPerMinute+=(target-KeysPerMinute)*(float)(1-Math.Exp(-7*Math.Max(0,dt)));
        if(age>1.3)
        {
            Mood=sessionCount>=4&&age<3.4 ? WorkMood.Relieved : WorkMood.Idle;
            pending=Mood;return;
        }
        WorkMood next=KeysPerMinute>=480 ? WorkMood.Turbo : KeysPerMinute>=300 ? WorkMood.Rushing :
            KeysPerMinute>=120 ? WorkMood.Focused : WorkMood.Thinking;
        if(Mood==WorkMood.Turbo&&KeysPerMinute>=384)next=WorkMood.Turbo;
        else if(Mood==WorkMood.Rushing&&KeysPerMinute>=240&&next<WorkMood.Rushing)next=WorkMood.Rushing;
        else if(Mood==WorkMood.Focused&&KeysPerMinute>=96&&next<WorkMood.Focused)next=WorkMood.Focused;
        if(Mood==WorkMood.Idle||Mood==WorkMood.Relieved)Mood=WorkMood.Thinking;
        if(next!=pending){pending=next;pendingSince=now;}
        if(now-pendingSince>=.35)Mood=next;
    }
    internal string Caption
    {
        get
        {
            switch(Mood)
            {
                case WorkMood.Thinking:return "想一想…";
                case WorkMood.Focused:return "认真敲字";
                case WorkMood.Rushing:return "认真赶稿";
                case WorkMood.Turbo:return "火力全开！";
                case WorkMood.Relieved:return "歇一下～";
                default:return "";
            }
        }
    }
}

internal static class TypingWorkTests
{
    static void Check(bool value,string message){if(!value)throw new Exception("打字节奏自检："+message);}
    static void Feed(InputFeedbackState state,double rate,double start,double duration)
    {
        int tap=0;
        for(int frame=0;frame<=(int)(duration*100);frame++)
        {
            double now=start+frame*.01;
            while(start+tap/rate<=now+.00001)
            {
                int code=65+tap%20;state.Key(code,true,start+tap/rate);state.Key(code,false,start+tap/rate+.001);tap++;
            }
            state.Work.Update(now,.01,false);
        }
    }
    internal static void Run()
    {
        var s=new InputFeedbackState();
        double[] rates={1,4,6,10};
        var moods=new[] {WorkMood.Thinking,WorkMood.Focused,WorkMood.Rushing,WorkMood.Turbo};
        for(int i=0;i<rates.Length;i++)
        {
            s.Clear();Feed(s,rates[i],0,7);
            Check(s.Work.Mood==moods[i],"真实短按在不同速度下触发对应表情："+rates[i]+" / "+s.Work.Mood);
            Check(Math.Abs(s.Work.KeysPerMinute-rates[i]*60)<25,"按/分采用最近三秒的敲击频率");
        }
        Feed(s,4.95,7.1,4);
        Check(s.Work.Mood==WorkMood.Rushing,"速度在快打边界附近波动时不反复切换表情");
        Feed(s,2.5,11.2,4);
        Check(s.Work.Mood==WorkMood.Focused,"持续减速后能从赶工回到专注");
        s.Work.Update(16.6,.1,false);Check(s.Work.Mood==WorkMood.Relieved,"连续工作停打后松口气");
        s.Work.Update(19,.1,false);Check(s.Work.Mood==WorkMood.Idle&&s.Work.RecentCount==0,"停打后恢复待机并清理时间戳");
        s.Clear();s.Key(160,true,0);s.Key(65,true,.01);
        for(int i=1;i<100;i++)s.Key(65,true,.01+i*.02);
        Check(s.Work.RecentCount==1,"长按自动重复只统计一次实际敲击，Shift 不计入速度");
        s.Clear();s.Key(162,true,0);s.Key(83,true,.01);s.Key(37,true,.02);s.Button(1,true,.03);s.Move(0,0,.04);
        s.Work.Update(.1,.1,false);
        Check(s.Work.RecentCount==0&&s.Work.Mood==WorkMood.Idle,"快捷键、方向键和鼠标不会误触发打字表情");
        s.Clear();s.Key(65,true,0);s.Key(65,false,.01);s.Work.Update(.02,.02,false);
        Check(s.Work.Mood==WorkMood.Thinking,"单次输入先进入思考，不会突然极速");
        s.Work.Update(.1,.1,true);Check(s.Work.Mood==WorkMood.Idle&&s.Work.KeysPerMinute==0,"休息立即清空工作状态");
        s.Clear();s.Key(65,true,1.001);s.Work.Update(1,.02,false);
        Check(s.Work.RecentCount==1&&s.Work.Mood==WorkMood.Thinking,"当前帧稍晚入队的敲击不会被误当成时间倒退清空");
        for(int i=0;i<1200;i++)s.Work.Press(i*.0005);
        Check(s.Work.RecentCount<=96,"极速输入的时间戳队列有固定上限");
        s.Clear();Check(s.Work.RecentCount==0&&s.Work.KeysPerMinute==0,"暂停跟随或清空输入会清除速度");
    }
}

// Key names live only in this short-lived view model; nothing is written to disk.
internal sealed class InputFeedbackState
{
    internal sealed class FloatingKey { internal string Label; internal double Born; internal int Sequence; }
    readonly List<FloatingKey> floating = new List<FloatingKey>();
    readonly Dictionary<int, string> releaseLabels = new Dictionary<int, string>();
    readonly HashSet<int> consumedModifiers = new HashSet<int>();
    int bubbleSequence;
    internal bool BubbleMode { get; private set; }
    internal void SetBubbleMode(bool enabled)
    {
        if (BubbleMode == enabled) return;
        BubbleMode = enabled; floating.Clear(); releaseLabels.Clear(); consumedModifiers.Clear();
        if (enabled) foreach (int key in keys) CaptureBubble(key);
    }
    void CaptureBubble(int code)
    {
        releaseLabels[code] = String.Join(" + ", keys.Where(k => Rank(k) < 4 || k == code).OrderBy(Rank).ThenBy(k => k).Select(KeyName).Distinct());
        if (Rank(code) == 4) foreach (int modifier in keys.Where(k => Rank(k) < 4)) consumedModifiers.Add(modifier);
    }
    internal IEnumerable<FloatingKey> Floating(double now)
    {
        floating.RemoveAll(p => now - p.Born >= 2.4);
        return floating.ToArray();
    }
    readonly HashSet<int> keys = new HashSet<int>(), buttons = new HashSet<int>();
    readonly Dictionary<int, double> strikes = new Dictionary<int, double>();
    internal readonly TypingWorkRhythm Work=new TypingWorkRhythm();
    internal event Action<DateTime> TypingPressed;
    int lastX, lastY;
    string displayLabel = "";
    double displayUntil;
    bool havePointer;
    double keyPulseUntil, buttonUntil, wheelUntil, moveUntil;
    readonly double[] buttonFeedbackUntil = new double[6];
    readonly double[] buttonPressedAt = new double[6];
    internal bool WheelHorizontal;
    internal long Revision;
    internal int LastStrike, WheelDelta;
    internal double LastStrikeAt = -100, WheelAt = -100, MouseGain = .35;
    internal PointF MouseOffset;
    internal IEnumerable<int> HeldKeys { get { return keys.ToArray(); } }
    internal IEnumerable<KeyValuePair<int, double>> KeyboardStrikes { get { return strikes.ToArray(); } }
    internal IEnumerable<int> HeldButtons { get { return buttons.ToArray(); } }
    internal bool HasKeys { get { return keys.Count > 0; } }
    internal string LiveChord { get { return String.Join(" + ", keys.OrderBy(Rank).ThenBy(k => k).Select(KeyName).Distinct()); } }
    internal bool HasKeyboardActivity(double now) { return HasKeys || now < keyPulseUntil; }
    internal bool HasMouse(double now) { return buttons.Count > 0 || now < buttonUntil || now < wheelUntil || now < moveUntil; }
    internal bool IsButtonDown(int button) { return buttons.Contains(button); }
    internal float ButtonFeedback(int button, double now)
    {
        if (button < 1 || button > 5) return 0;
        return buttons.Contains(button) ? 1 : (float)Math.Max(0, Math.Min(1, (buttonFeedbackUntil[button] - now) / .18));
    }
    internal float WheelFeedback(double now)
    {
        return now < WheelAt ? 0 : (float)Math.Max(0, Math.Min(1, (wheelUntil - now) / .25));
    }
    internal double ButtonAge(int button,double now) { return Math.Max(0,now-buttonPressedAt[button]); }
    internal float ButtonMotion(int button,double now)
    {
        double age=ButtonAge(button,now);
        return ButtonFeedback(button,now)*(float)((1-Math.Exp(-55*age))*(1+.18*Math.Sin(age*26)*Math.Exp(-age*10)));
    }
    internal bool HasMouseFeedback(double now)
    {
        for (int button = 1; button <= 5; button++) if (ButtonFeedback(button, now) > 0) return true;
        return WheelFeedback(now) > 0;
    }
    internal IEnumerable<int> LitSources(double now) { return keys.Concat(strikes.Where(p => now - p.Value < .16).Select(p => p.Key)).Distinct().ToArray(); }
    // Keep only the latest chord, briefly retaining short taps after release.
    internal string KeyDisplay(double now) { return HasKeys || now < displayUntil ? displayLabel : ""; }
    static int Rank(int code)
    {
        if (code == 17 || code == 162 || code == 163) return 0;
        if (code == 16 || code == 160 || code == 161) return 1;
        if (code == 18 || code == 164 || code == 165) return 2;
        if (code == 91 || code == 92) return 3;
        return 4;
    }
    internal static string KeyName(int code)
    {
        if (code >= 65 && code <= 90 || code >= 48 && code <= 57) return ((char)code).ToString();
        if (code >= 112 && code <= 135) return "F" + (code - 111);
        if (code >= 96 && code <= 105) return "Num" + (code - 96);
        switch (code)
        {
            case 8: return "Backspace"; case 9: return "Tab"; case 12: return "NumClear";
            case 13: return "Enter"; case 269: return "NumEnter";
            case 16: case 160: case 161: return "Shift";
            case 17: case 162: case 163: return "Ctrl";
            case 18: case 164: case 165: return "Alt";
            case 19: return "Pause"; case 20: return "CapsLock";
            case 27: return "Esc"; case 32: return "Space";
            case 33: return "PageUp"; case 34: return "PageDown"; case 35: return "End"; case 36: return "Home";
            case 37: return "←"; case 38: return "↑"; case 39: return "→"; case 40: return "↓";
            case 44: return "PrintScreen"; case 45: return "Insert"; case 46: return "Delete";
            case 91: case 92: return "Win"; case 93: return "Menu";
            case 106: return "Num*"; case 107: return "Num+"; case 108: return "NumSeparator";
            case 109: return "Num-"; case 110: return "Num."; case 111: return "Num/";
            case 144: return "NumLock"; case 145: return "ScrollLock";
            case 166: return "浏览器后退"; case 167: return "浏览器前进"; case 168: return "刷新";
            case 173: return "静音"; case 174: return "音量−"; case 175: return "音量+";
            case 176: return "下一曲"; case 177: return "上一曲"; case 178: return "停止"; case 179: return "播放/暂停";
            case 186: return ";"; case 187: return "="; case 188: return ","; case 189: return "-";
            case 190: return "."; case 191: return "/"; case 192: return "`";
            case 219: return "["; case 220: return "\\"; case 221: return "]"; case 222: return "'";
            case 226: return "OEM102";
            default: return "VK " + code.ToString("X2");
        }
    }

    internal void Key(int code, bool down, double now, DateTime? localAt = null, bool recordTyping = true)
    {
        if (code == 0) return;
        Revision++; keyPulseUntil = now + .24;
        if (down)
        {
            bool fresh = keys.Add(code); strikes[code] = now;
            if (Rank(code) == 4 || !keys.Any(k => Rank(k) == 4)) { LastStrike = code; LastStrikeAt = now; }
            if (fresh)
            {
                if(TypingWorkRhythm.IsTypingKey(code)&&!keys.Any(k=>Rank(k)==0||Rank(k)==2||Rank(k)==3))
                {
                    Work.Press(now);
                    if(recordTyping && TypingPressed!=null)TypingPressed(localAt ?? DateTime.Now);
                }
                displayLabel = LiveChord;
                if (BubbleMode) CaptureBubble(code);
            }
            displayUntil = now + .9;
        }
        else if (keys.Remove(code))
        {
            displayUntil = now + .9;
            string label;
            if (BubbleMode && releaseLabels.TryGetValue(code, out label) && !consumedModifiers.Contains(code))
            {
                Floating(now);
                floating.Add(new FloatingKey { Label = label, Born = now, Sequence = bubbleSequence++ });
                while (floating.Count > 4) floating.RemoveAt(0);
            }
            releaseLabels.Remove(code); consumedModifiers.Remove(code);
        }
    }

    internal void Button(int button, bool down, double now)
    {
        if (button < 1 || button > 5) return;
        Revision++;
        if (down)
        {
            if(buttons.Add(button))buttonPressedAt[button]=now;
            buttonFeedbackUntil[button] = now + .36;
        }
        else if (buttons.Remove(button))
            buttonFeedbackUntil[button] = Math.Max(buttonFeedbackUntil[button], now + .18);
        buttonUntil = Math.Max(buttonUntil, buttonFeedbackUntil[button]);
    }
    internal void Wheel(int delta, bool horizontal, double now)
    {
        if (delta == 0) return;
        Revision++; WheelDelta = delta; WheelHorizontal = horizontal; WheelAt = now; wheelUntil = now + .45;
    }
    internal void Move(int x, int y, double now)
    {
        Revision++;
        if (havePointer && (x != lastX || y != lastY))
        {
            MouseOffset = new PointF((float)Math.Max(-43, Math.Min(43, MouseOffset.X + ((double)x - lastX) * MouseGain)),
                                    (float)Math.Max(-25, Math.Min(25, MouseOffset.Y + ((double)y - lastY) * MouseGain)));
            moveUntil = now + .18;
        }
        havePointer = true; lastX = x; lastY = y;
    }
    internal void Clear()
    {
        Revision++; keys.Clear(); buttons.Clear(); strikes.Clear(); displayLabel = ""; displayUntil = 0;
        Array.Clear(buttonFeedbackUntil, 0, buttonFeedbackUntil.Length); WheelHorizontal = false;
        Array.Clear(buttonPressedAt, 0, buttonPressedAt.Length);
        floating.Clear(); releaseLabels.Clear(); consumedModifiers.Clear();
        Work.Reset();
        LastStrike = WheelDelta = 0; LastStrikeAt = WheelAt = -100; MouseOffset = PointF.Empty;
        keyPulseUntil = buttonUntil = wheelUntil = moveUntil = 0; havePointer = false;
    }
}

// Windows waits for low-level hooks before delivering input to other apps.
// Keep them on a dedicated message-pump thread, entirely separate from drawing.
// The UI owns the view model and drains queued events without holding the lock.
internal sealed class GlobalInputFeedback : IDisposable
{
    delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] struct KeyboardData { internal uint Key, Scan, Flags, Time; internal UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] struct MouseData { internal int X, Y; internal uint Data, Flags, Time; internal UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] struct NativeMessage
    { internal IntPtr Window; internal uint Message; internal UIntPtr WParam; internal IntPtr LParam; internal uint Time; internal int X,Y; internal uint Private; }
    struct PendingInput { internal int Message; internal KeyboardData Key; internal MouseData Pointer; internal double At; internal DateTime LocalAt; }
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int id, HookProc callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("kernel32.dll", CharSet = CharSet.Auto)] static extern IntPtr GetModuleHandle(string module);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern bool PeekMessage(out NativeMessage message,IntPtr window,uint min,uint max,uint remove);
    [DllImport("user32.dll")] static extern int GetMessage(out NativeMessage message,IntPtr window,uint min,uint max);
    [DllImport("user32.dll", SetLastError=true)] static extern bool PostThreadMessage(uint thread,uint message,UIntPtr wParam,IntPtr lParam);
    readonly InputFeedbackState state;
    readonly Func<double> now;
    readonly HookProc keyboardCallback, mouseCallback;
    IntPtr keyboardHook, mouseHook;
    readonly object pendingGate=new object();
    List<PendingInput> pending=new List<PendingInput>(128),draining=new List<PendingInput>(128);
    Thread worker;
    volatile uint workerId;
    volatile bool enabled;
    volatile int probeResponses;
    bool overflowed;
    Exception startupError;
    internal bool Enabled { get { return enabled; } }
    internal int PendingCount { get { lock(pendingGate)return pending.Count; } }
    internal GlobalInputFeedback(InputFeedbackState state, Func<double> now)
    {
        this.state = state; this.now = now; keyboardCallback = Keyboard; mouseCallback = Mouse;
    }
    internal void Enable()
    {
        if (Enabled) return;
        Dispose();startupError=null;
        using(var ready=new ManualResetEvent(false))
        {
            worker=new Thread(()=>Pump(ready)) {IsBackground=true,Name="DesktopPet input hooks"};
            worker.Start();ready.WaitOne();
        }
        if(startupError!=null){Dispose();throw new Win32Exception("无法启动独立输入监听线程。",startupError);}
        SeedHeld();
    }
    void Pump(ManualResetEvent ready)
    {
        bool signaled=false;
        try
        {
            workerId=GetCurrentThreadId();NativeMessage message;
            PeekMessage(out message,IntPtr.Zero,0,0,0);
            IntPtr module=GetModuleHandle(null);
            keyboardHook=SetWindowsHookEx(13,keyboardCallback,module,0);
            if(keyboardHook==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error());
            mouseHook=SetWindowsHookEx(14,mouseCallback,module,0);
            if(mouseHook==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error());
            enabled=true;ready.Set();signaled=true;
            while(GetMessage(out message,IntPtr.Zero,0,0)>0)
                if(message.Message==0x8001)probeResponses++;
        }
        catch(Exception error){startupError=error;}
        finally
        {
            enabled=false;
            if(keyboardHook!=IntPtr.Zero){UnhookWindowsHookEx(keyboardHook);keyboardHook=IntPtr.Zero;}
            if(mouseHook!=IntPtr.Zero){UnhookWindowsHookEx(mouseHook);mouseHook=IntPtr.Zero;}
            if(!signaled)ready.Set();
        }
    }
    void SeedHeld()
    {
        // Seed already-held keys once; subsequent input is event driven.
        double t = now();
        for (int key = 8; key < 255; key++)
            if (key != 16 && key != 17 && key != 18 && (GetAsyncKeyState(key) & 0x8000) != 0) state.Key(key, true, t, recordTyping:false);
        foreach (int button in new[] { 1, 2, 3, 4, 5 })
        {
            int vk = button == 1 ? 1 : button == 2 ? 2 : button == 3 ? 4 : button == 4 ? 5 : 6;
            if ((GetAsyncKeyState(vk) & 0x8000) != 0) state.Button(button, true, t);
        }
        Point pointer=System.Windows.Forms.Cursor.Position;state.Move(pointer.X,pointer.Y,t);
    }
    void Enqueue(PendingInput item)
    {
        lock(pendingGate)
        {
            int count=pending.Count;
            if(item.Message==0x200&&count>0&&pending[count-1].Message==0x200)pending[count-1]=item;
            else
            {
                if(count>=4096){pending.Clear();overflowed=true;}
                pending.Add(item);
            }
        }
    }
    internal bool DrainPending()
    {
        bool overflow;
        lock(pendingGate)
        {
            overflow=overflowed;overflowed=false;
            var empty=draining;draining=pending;pending=empty;
        }
        if(overflow){draining.Clear();state.Clear();SeedHeld();return true;}
        bool changed=draining.Count>0;
        foreach(var item in draining)
        {
            int m=item.Message;double t=item.At;
            if(m==0x100||m==0x101||m==0x104||m==0x105)ApplyKey(item.Key,m,t,item.LocalAt);
            else
            {
                var input=item.Pointer;
                if(m==0x200)state.Move(input.X,input.Y,t);
                else if(m==0x201||m==0x202)state.Button(1,m==0x201,t);
                else if(m==0x204||m==0x205)state.Button(2,m==0x204,t);
                else if(m==0x207||m==0x208)state.Button(3,m==0x207,t);
                else if(m==0x20B||m==0x20C)state.Button((input.Data>>16)==1 ? 4 : 5,m==0x20B,t);
                else if(m==0x20A||m==0x20E)state.Wheel(unchecked((short)(input.Data>>16)),m==0x20E,t);
            }
        }
        draining.Clear();return changed;
    }
    // A private-thread message must be serviced even while the caller is
    // blocked here, without pumping any messages on the rendering/UI thread.
    internal bool ProbePump()
    {
        int before=probeResponses;
        if(!Enabled||!PostThreadMessage(workerId,0x8001,UIntPtr.Zero,IntPtr.Zero))return false;
        for(int i=0;i<250;i++){if(probeResponses!=before)return true;Thread.Sleep(1);}
        return false;
    }
    IntPtr Keyboard(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0)
        {
            int m = (int)message.ToInt64();
            if (m == 0x100 || m == 0x101 || m == 0x104 || m == 0x105)
            {
                Enqueue(new PendingInput {Message=m,Key=(KeyboardData)Marshal.PtrToStructure(data,typeof(KeyboardData)),At=now(),LocalAt=DateTime.Now});
            }
        }
        return CallNextHookEx(IntPtr.Zero, code, message, data);
    }
    void ApplyKey(KeyboardData input,int m,double t,DateTime localAt)
    {
                if (input.Key == 0) return;
                int key = (int)input.Key;
                if (key == 13 && (input.Flags & 1) != 0) key = 269;
                if ((input.Flags & 1) == 0)
                {
                    // Numpad keys retain their physical targets when NumLock is off.
                    switch (input.Scan)
                    {
                        case 0x47: key = 103; break; case 0x48: key = 104; break; case 0x49: key = 105; break;
                        case 0x4B: key = 100; break; case 0x4C: key = 101; break; case 0x4D: key = 102; break;
                        case 0x4F: key = 97; break; case 0x50: key = 98; break; case 0x51: key = 99; break;
                        case 0x52: key = 96; break; case 0x53: key = 110; break;
                    }
                }
                state.Key(key, m == 0x100 || m == 0x104, t, localAt, (input.Flags & 0x10) == 0);
    }
    IntPtr Mouse(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0)
        {
            int m = (int)message.ToInt64();
            var input = (MouseData)Marshal.PtrToStructure(data, typeof(MouseData));
            Enqueue(new PendingInput {Message=m,Pointer=input,At=now()});
        }
        return CallNextHookEx(IntPtr.Zero, code, message, data);
    }
    internal void ReconcileReleased()
    {
        double t = now();
        foreach (int key in state.HeldKeys)
        {
            int vk = key == 269 ? 13 : key;
            int nav = key == 103 ? 36 : key == 104 ? 38 : key == 105 ? 33 : key == 100 ? 37 : key == 101 ? 12 :
                key == 102 ? 39 : key == 97 ? 35 : key == 98 ? 40 : key == 99 ? 34 : key == 96 ? 45 : key == 110 ? 46 : vk;
            if ((GetAsyncKeyState(vk) & 0x8000) == 0 && (GetAsyncKeyState(nav) & 0x8000) == 0) state.Key(key, false, t);
        }
        foreach (int button in state.HeldButtons)
        {
            int vk = button == 1 ? 1 : button == 2 ? 2 : button == 3 ? 4 : button == 4 ? 5 : 6;
            if ((GetAsyncKeyState(vk) & 0x8000) == 0) state.Button(button, false, t);
        }
    }
    internal void StopAndDrain() { Stop(true); }
    public void Dispose() { Stop(false); }
    void Stop(bool drain)
    {
        if(worker!=null)
        {
            if(worker.IsAlive){PostThreadMessage(workerId,0x12,UIntPtr.Zero,IntPtr.Zero);worker.Join();}
            worker=null;workerId=0;
        }
        enabled=false;
        if(drain)DrainPending();
        lock(pendingGate){pending.Clear();overflowed=false;}
        draining.Clear();
    }
}

internal static class InputFeedbackTests
{
    [StructLayout(LayoutKind.Sequential)] struct TestKey { internal uint Key, Scan, Flags, Time; internal UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] struct TestMouse { internal int X, Y; internal uint Data, Flags, Time; internal UIntPtr Extra; }
    static void Check(bool condition, string message) { if (!condition) throw new Exception("实时输入自检：" + message); }
    static void Dispatch(object hook, string method, int message, object value,bool drain=true)
    {
        IntPtr data = Marshal.AllocHGlobal(Marshal.SizeOf(value));
        try
        {
            Marshal.StructureToPtr(value, data, false);
            hook.GetType().GetMethod(method, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(hook, new object[] { 0, new IntPtr(message), data });
            if(drain)((GlobalInputFeedback)hook).DrainPending();
        }
        finally { Marshal.FreeHGlobal(data); }
    }
    internal static void Run()
    {
        TypingWorkTests.Run();
        var s = new InputFeedbackState();
        s.Key(0,true,0);s.Key(0,false,.01);
        Check(!s.HasKeys&&!s.HasKeyboardActivity(.02)&&s.KeyDisplay(.02)=="","无效零键码不产生按键显示或动画");
        s.Key(112,true,.1);s.Key(0,true,.11);s.Key(112,false,.12);s.Key(0,false,.95);
        Check(s.KeyDisplay(.2)=="F1"&&s.KeyDisplay(1.1)=="","零键码不污染 F1 或延长显示时间");
        s.Clear();s.SetBubbleMode(true);
        s.Key(0,true,0);s.Key(112,true,.1);s.Key(112,false,.12);s.Key(0,false,.2);
        Check(s.Floating(.3).Single().Label=="F1","冒泡过滤零键码，正常 F1 气泡保留");
        s.SetBubbleMode(false);s.Clear();
        s.Key(65, true, 0); s.Key(65, false, .001);
        Check(s.LiveChord == "" && s.KeyDisplay(.016) == "A" && s.HasKeyboardActivity(.016), "1ms 点按释放后仍在固定框短暂显示");
        s.Clear(); s.Key(162, true, .1); s.Key(160, true, .11); s.Key(83, true, .12);
        Check(s.LiveChord == "Ctrl + Shift + S", "实时组合键顺序");
        s.Key(83, true, .14); s.Key(83, false, .16); s.Key(162, false, .17); s.Key(160, false, .18);
        Check(s.LiveChord == "" && s.KeyDisplay(.2) == "Ctrl + Shift + S", "松开后保留完整组合键，不被修饰键释放覆盖");
        s.Key(162, true, .3); s.Key(163, true, .31); s.Key(162, false, .32);
        Check(s.LiveChord == "Ctrl", "左右修饰键分别释放");
        s.Clear(); Check(s.LiveChord == "" && s.KeyDisplay(1) == "", "暂停清空全部输入");
        for (int i = 0; i < 6; i++) { s.Key(65 + i, true, 1 + i * .1); s.Key(65 + i, false, 1.01 + i * .1); }
        Check(s.KeyDisplay(1.6) == "F", "快速连按只显示最新按键");
        Check(s.KeyDisplay(4) == "", "松开后固定框到时隐藏");
        Check(!s.Floating(4).Any(), "固定框模式不积累冒泡记录");
        s.Clear(); s.SetBubbleMode(true);
        s.Key(162,true,4); s.Key(160,true,4.01); s.Key(83,true,4.02);
        s.Key(83,true,4.03); s.Key(83,false,4.04); s.Key(162,false,4.05); s.Key(160,false,4.06);
        Check(s.Floating(4.1).Single().Label=="Ctrl + Shift + S", "冒泡模式组合键只生成一次，不重复记录修饰键或长按");
        for(int i=0;i<6;i++){s.Key(65+i,true,4.2+i*.1);s.Key(65+i,false,4.21+i*.1);}
        Check(String.Join("",s.Floating(4.8).Select(p=>p.Label))=="CDEF", "冒泡模式最多保留四条短期记录");
        s.Key(65,true,4.9); s.SetBubbleMode(false);
        Check(s.HasKeys&&!s.Floating(4.9).Any()&&s.KeyDisplay(4.9)=="A", "切换为固定框保留输入并清除冒泡");
        s.SetBubbleMode(true); s.Key(65,false,4.91);
        Check(s.Floating(4.92).Single().Label=="A", "按住按键时切换冒泡，释放仍正常生成气泡");
        s.SetBubbleMode(false); s.Clear();
        Check(InputFeedbackState.KeyName(269) == "NumEnter" && InputFeedbackState.KeyName(13) == "Enter" &&
            InputFeedbackState.KeyName(112) == "F1" && InputFeedbackState.KeyName(186) == ";", "数字区、功能键与标点键");
        s.Button(1, true, 6); s.Button(2, true, 6.01); s.Wheel(240, false, 6.02);
        Check(s.IsButtonDown(1) && s.IsButtonDown(2) && s.WheelDelta == 240 && s.LiveChord == "", "鼠标状态独立且不写入按键框");
        s.Button(1, false, 6.04); Check(!s.IsButtonDown(1) && s.IsButtonDown(2), "独立释放鼠标键");
        s.Clear(); s.Move(10, 10, 7); s.Move(15, 5, 7.01);
        Check(s.MouseOffset.X > 0 && s.MouseOffset.Y < 0, "鼠标真实移动方向");
        s.Wheel(-120, true, 8); Check(s.WheelDelta == -120, "滚轮带符号数据");
        Check(s.WheelHorizontal && s.WheelFeedback(8.1)>0 && s.WheelFeedback(8.5)==0,"水平滚轮方向保留，反馈按时淡出");
        s.Clear();s.Button(1,true,9);s.Button(1,false,9.005);
        Check(!s.IsButtonDown(1)&&s.ButtonFeedback(1,9.08)==1&&s.HasMouse(9.08),"帧间极短左键点击仍可见并保持活动刷新");
        s.Button(2,true,9.1);
        Check(s.ButtonFeedback(1,9.2)>0&&s.ButtonFeedback(2,9.2)==1,"左右键反馈同时保留");
        Check(s.ButtonFeedback(1,9.4)==0&&s.ButtonFeedback(2,12)==1,"短按按时结束，长按持续点亮");
        s.Button(2,false,12);Check(s.ButtonFeedback(2,12.08)>0&&s.ButtonFeedback(2,12.2)==0,"长按释放后淡出");
        s.Wheel(120,false,13);Check(!s.WheelHorizontal&&s.WheelDelta>0,"垂直滚轮更新方向和轴向");
        s.Clear();Check(!s.HasMouseFeedback(13.01)&&!s.HasMouse(13.01),"暂停清空全部点击和滚轮余光");
        s.Clear();
        using (var hook = new GlobalInputFeedback(s, () => 10))
        {
            Dispatch(hook,"Keyboard",0x100,new TestKey {Key=0,Scan=0x47});
            Dispatch(hook,"Keyboard",0x101,new TestKey {Key=0,Scan=0x47});
            Check(!s.HasKeys&&s.KeyDisplay(10)=="","原生零键码在扫描码映射之前丢弃");
            Dispatch(hook, "Keyboard", 0x104, new TestKey { Key = 164 });
            Dispatch(hook, "Keyboard", 0x100, new TestKey { Key = 13, Flags = 1 });
            Check(s.LiveChord == "Alt + NumEnter", "系统按键与扩展 Enter 解码");
            Dispatch(hook, "Keyboard", 0x101, new TestKey { Key = 13, Flags = 1 });
            Dispatch(hook, "Keyboard", 0x105, new TestKey { Key = 164 });
            Check(!s.HasKeys, "系统键释放");
            Dispatch(hook, "Keyboard", 0x100, new TestKey { Key = 36, Scan = 0x47 });
            Check(s.LiveChord == "Num7", "NumLock 关闭时保留物理数字键位");
            Dispatch(hook, "Keyboard", 0x101, new TestKey { Key = 36, Scan = 0x47 });
            Dispatch(hook, "Mouse", 0x20A, new TestMouse { Data = unchecked((uint)(-120 << 16)) });
            Dispatch(hook, "Mouse", 0x20B, new TestMouse { Data = 2u << 16 });
            Check(s.WheelDelta == -120 && s.IsButtonDown(5), "滚轮及侧键消息解码");
            s.Clear();long revision=s.Revision;
            Dispatch(hook,"Keyboard",0x100,new TestKey {Key=65},false);
            Dispatch(hook,"Keyboard",0x101,new TestKey {Key=65},false);
            Check(s.Revision==revision&&hook.PendingCount==2,"输入回调只入队，不跨线程修改动画状态");
            hook.DrainPending();
            Check(!s.HasKeys&&s.KeyDisplay(10.1)=="A","一帧之间的短按和松开按原顺序保留");
            s.Clear();
            foreach(int message in new[] {0x201,0x202,0x204,0x205})Dispatch(hook,"Mouse",message,new TestMouse(),false);
            hook.DrainPending();
            Check(!s.IsButtonDown(1)&&!s.IsButtonDown(2)&&s.ButtonFeedback(1,10.05)>0&&s.ButtonFeedback(2,10.05)>0&&s.KeyDisplay(10.05)=="","队列同帧处理左右短按，不污染键盘显示");
            s.Clear();s.Move(0,0,9);
            for(int i=0;i<1000;i++)Dispatch(hook,"Mouse",0x200,new TestMouse {X=i,Y=i},false);
            Check(hook.PendingCount==1,"高回报率鼠标的连续位移合并为一个待处理事件");
            hook.DrainPending();Check(s.MouseOffset.X>0&&s.MouseOffset.Y>0,"合并后保留实际鼠标位移");
            for(int i=0;i<4100;i++)Dispatch(hook,"Keyboard",0x100,new TestKey {Key=65},false);
            Check(hook.PendingCount<=4096,"长时间阻塞时输入队列有容量上限");
            hook.Dispose();Check(hook.PendingCount==0,"暂停时丢弃待处理事件，不遗留旧输入");
            s.Clear(); hook.Enable(); Check(hook.Enabled, "原生键盘与鼠标监听安装");
            Check(hook.ProbePump(),"绘制线程停止泵消息时，独立输入线程仍能及时响应");
            hook.Enable(); hook.Dispose(); Check(!hook.Enabled, "重复启用及监听释放");
            hook.Enable();Check(hook.ProbePump(),"暂停、恢复后重新建立独立输入线程");hook.Dispose();
        }
        s.Clear();
    }
}
