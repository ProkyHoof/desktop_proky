using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

// PCM16 mono, 16 kHz. Keep a short pre-roll, require actual voiced frames,
// end a turn on silence, and bound each upload to twenty seconds.
internal sealed class PetVoiceSegmenter
{
    readonly int threshold, silenceBytes;
    readonly Queue<byte[]> preRoll = new Queue<byte[]>();
    readonly MemoryStream utterance = new MemoryStream();
    int quietBytes, voicedBytes;
    internal float Level { get; private set; }
    internal bool HasSpeech { get { return utterance.Length > 0; } }
    internal PetVoiceSegmenter(int threshold, int silenceMilliseconds)
    { this.threshold = threshold; silenceBytes = silenceMilliseconds * 32; }
    internal byte[] Feed(byte[] pcm)
    {
        double sum = 0;
        for (int i = 0; i + 1 < pcm.Length; i += 2) { short sample = (short)(pcm[i] | pcm[i + 1] << 8); sum += (double)sample * sample; }
        double rms = Math.Sqrt(sum / Math.Max(1, pcm.Length / 2)); Level = (float)Math.Min(1, rms / 6000);
        bool voiced = rms >= threshold;
        if (utterance.Length == 0)
        {
            preRoll.Enqueue(pcm); while (preRoll.Count > 3) preRoll.Dequeue();
            if (!voiced) return null;
            foreach (var frame in preRoll) utterance.Write(frame, 0, frame.Length);
            preRoll.Clear();
        }
        else utterance.Write(pcm, 0, pcm.Length);
        if (voiced) { voicedBytes += pcm.Length; quietBytes = 0; } else quietBytes += pcm.Length;
        if (quietBytes < silenceBytes && utterance.Length < 20 * 32000) return null;
        byte[] result = voicedBytes >= 8000 ? ToWav(utterance.ToArray()) : null;
        utterance.SetLength(0); quietBytes = voicedBytes = 0;
        return result;
    }
    internal static byte[] ToWav(byte[] pcm)
    {
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream, Encoding.ASCII))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + pcm.Length); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(16000); writer.Write(32000);
            writer.Write((short)2); writer.Write((short)16); writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(pcm.Length); writer.Write(pcm);
            return stream.ToArray();
        }
    }
}

// Poll native headers instead of running managed work inside a wave callback.
// Reset the device before freeing headers, including cancellation/error paths.
internal sealed class PetMicrophone : IDisposable
{
    [StructLayout(LayoutKind.Sequential, Pack = 2)] struct WaveFormat
    { public ushort Format, Channels; public uint SampleRate, BytesPerSecond; public ushort BlockAlign, Bits, Extra; }
    [StructLayout(LayoutKind.Sequential)] struct WaveHeader
    { public IntPtr Data; public uint Length, Recorded; public UIntPtr User; public uint Flags, Loops; public IntPtr Next; public UIntPtr Reserved; }
    sealed class Buffer { internal IntPtr Data, Header; internal bool Prepared; }
    [DllImport("winmm.dll")] static extern uint waveInOpen(out IntPtr handle, uint device, ref WaveFormat format, IntPtr callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll")] static extern uint waveInPrepareHeader(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll")] static extern uint waveInUnprepareHeader(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll")] static extern uint waveInAddBuffer(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll")] static extern uint waveInStart(IntPtr handle);
    [DllImport("winmm.dll")] static extern uint waveInReset(IntPtr handle);
    [DllImport("winmm.dll")] static extern uint waveInClose(IntPtr handle);
    readonly List<Buffer> buffers = new List<Buffer>();
    readonly uint headerSize = (uint)Marshal.SizeOf(typeof(WaveHeader));
    IntPtr device;
    static void Check(uint result) { if (result != 0) throw new InvalidOperationException("无法打开或读取默认麦克风（Windows 音频错误 " + result + "）。请检查麦克风连接与 Windows 麦克风隐私权限。"); }
    internal PetMicrophone()
    {
        try
        {
            var format = new WaveFormat { Format = 1, Channels = 1, SampleRate = 16000, BytesPerSecond = 32000, BlockAlign = 2, Bits = 16 };
            Check(waveInOpen(out device, UInt32.MaxValue, ref format, IntPtr.Zero, IntPtr.Zero, 0));
            for (int i = 0; i < 4; i++)
            {
                var b = new Buffer(); buffers.Add(b); b.Data = Marshal.AllocHGlobal(3200); b.Header = Marshal.AllocHGlobal((int)headerSize);
                Marshal.StructureToPtr(new WaveHeader { Data = b.Data, Length = 3200 }, b.Header, false);
                Check(waveInPrepareHeader(device, b.Header, headerSize)); b.Prepared = true;
                Check(waveInAddBuffer(device, b.Header, headerSize));
            }
            Check(waveInStart(device));
        }
        catch { Dispose(); throw; }
    }
    internal static Task<byte[]> ListenAsync(PetChatSettings settings, Action<float, bool> meter, CancellationToken token)
    {
        return Task.Run(async delegate
        {
            token.ThrowIfCancellationRequested();
            using (var microphone = new PetMicrophone())
            {
                var segmenter = new PetVoiceSegmenter(settings.VoiceThreshold, settings.SilenceMilliseconds);
                int nextBuffer = 0;
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    for (int pending = 0; pending < microphone.buffers.Count; pending++)
                    {
                        var b = microphone.buffers[nextBuffer];
                        var h = (WaveHeader)Marshal.PtrToStructure(b.Header, typeof(WaveHeader));
                        if ((h.Flags & 1) == 0) break;
                        if (h.Recorded > h.Length) throw new InvalidOperationException("麦克风缓冲数据无效。");
                        if (h.Recorded > 0)
                        {
                            var pcm = new byte[h.Recorded]; Marshal.Copy(b.Data, pcm, 0, pcm.Length);
                            byte[] result = segmenter.Feed(pcm);
                            if (meter != null) meter(segmenter.Level, segmenter.HasSpeech);
                            if (result != null) return result;
                        }
                        Check(waveInAddBuffer(microphone.device, b.Header, microphone.headerSize));
                        nextBuffer = (nextBuffer + 1) % microphone.buffers.Count;
                    }
                    await Task.Delay(20, token).ConfigureAwait(false);
                }
            }
        }, token);
    }
    public void Dispose()
    {
        if (device != IntPtr.Zero) waveInReset(device);
        foreach (var b in buffers)
        {
            if (b.Prepared) waveInUnprepareHeader(device, b.Header, headerSize);
            if (b.Header != IntPtr.Zero) Marshal.FreeHGlobal(b.Header);
            if (b.Data != IntPtr.Zero) Marshal.FreeHGlobal(b.Data);
        }
        buffers.Clear();
        if (device != IntPtr.Zero) { waveInClose(device); device = IntPtr.Zero; }
    }
}

// Play from memory through waveOut; cancellation resets playback immediately.
// No audio files or recording history are written to disk.
internal static class PetVoicePlayback
{
    [StructLayout(LayoutKind.Sequential)] struct Header
    { public IntPtr Data; public uint Length, Recorded; public UIntPtr User; public uint Flags, Loops; public IntPtr Next; public UIntPtr Reserved; }
    [DllImport("winmm.dll")] static extern uint waveOutOpen(out IntPtr handle, uint device, IntPtr format, IntPtr callback, IntPtr instance, uint flags);
    [DllImport("winmm.dll")] static extern uint waveOutPrepareHeader(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll")] static extern uint waveOutWrite(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll")] static extern uint waveOutReset(IntPtr handle);
    [DllImport("winmm.dll")] static extern uint waveOutUnprepareHeader(IntPtr handle, IntPtr header, uint size);
    [DllImport("winmm.dll")] static extern uint waveOutClose(IntPtr handle);
    internal static void ReadWav(byte[] wav, out byte[] format, out byte[] samples)
    {
        format = samples = null;
        if (wav.Length < 44 || Encoding.ASCII.GetString(wav, 0, 4) != "RIFF" || Encoding.ASCII.GetString(wav, 8, 4) != "WAVE") throw new InvalidOperationException("无效的 WAV 音频。");
        for (int pos = 12; pos + 8 <= wav.Length; )
        {
            string tag = Encoding.ASCII.GetString(wav, pos, 4); uint declaredSize = BitConverter.ToUInt32(wav, pos + 4); pos += 8;
            // Some HTTP speech providers use an unknown data length in a
            // streamed WAV header. The HTTP response is already size-bounded.
            if (tag == "data" && declaredSize == UInt32.MaxValue) declaredSize = (uint)(wav.Length - pos);
            if (declaredSize > wav.Length - pos) throw new InvalidOperationException("WAV 数据不完整。");
            int size = (int)declaredSize;
            if (tag == "fmt ") { format = new byte[Math.Max(18, size)]; Buffer.BlockCopy(wav, pos, format, 0, size); }
            if (tag == "data") { samples = new byte[size]; Buffer.BlockCopy(wav, pos, samples, 0, size); }
            pos += size + (size % 2);
        }
        if (format == null || format.Length < 18 || BitConverter.ToInt16(format, 0) != 1 || samples == null || samples.Length == 0)
            throw new InvalidOperationException("语音服务须返回未压缩 PCM WAV 音频。");
    }
    static void Check(uint value) { if (value != 0) throw new InvalidOperationException("无法播放语音（Windows 音频错误 " + value + "），请检查默认扬声器和 WAV 格式。"); }
    internal static Task PlayAsync(byte[] wav, CancellationToken token)
    {
        return Task.Run(async delegate
        {
            token.ThrowIfCancellationRequested();
            byte[] format, samples; ReadWav(wav, out format, out samples);
            IntPtr device = IntPtr.Zero, f = IntPtr.Zero, data = IntPtr.Zero, header = IntPtr.Zero;
            bool prepared = false; uint size = (uint)Marshal.SizeOf(typeof(Header));
            try
            {
                f = Marshal.AllocHGlobal(format.Length); Marshal.Copy(format, 0, f, format.Length);
                Check(waveOutOpen(out device, UInt32.MaxValue, f, IntPtr.Zero, IntPtr.Zero, 0));
                data = Marshal.AllocHGlobal(samples.Length); Marshal.Copy(samples, 0, data, samples.Length);
                header = Marshal.AllocHGlobal((int)size); Marshal.StructureToPtr(new Header { Data = data, Length = (uint)samples.Length }, header, false);
                Check(waveOutPrepareHeader(device, header, size)); prepared = true;
                Check(waveOutWrite(device, header, size));
                while ((((Header)Marshal.PtrToStructure(header, typeof(Header))).Flags & 1) == 0) await Task.Delay(25, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
            }
            finally
            {
                if (device != IntPtr.Zero) waveOutReset(device);
                if (prepared) waveOutUnprepareHeader(device, header, size);
                if (device != IntPtr.Zero) waveOutClose(device);
                if (header != IntPtr.Zero) Marshal.FreeHGlobal(header);
                if (data != IntPtr.Zero) Marshal.FreeHGlobal(data);
                if (f != IntPtr.Zero) Marshal.FreeHGlobal(f);
            }
        }, token);
    }
}
