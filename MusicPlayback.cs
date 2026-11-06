using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

// Meter values only: no loopback recording, microphone access or audio buffers.
internal sealed class MusicActivityGate
{
    double sounding, quiet;
    internal bool Playing { get; private set; }
    internal void Update(float peak, double dt)
    {
        dt = Math.Min(.25, Math.Max(0, dt));
        if (!float.IsNaN(peak) && peak >= (Playing ? .0008f : .0015f))
        {
            sounding += dt; quiet = 0;
            if (sounding >= .65) Playing = true;
        }
        else
        {
            quiet += dt;
            if (!Playing) sounding = Math.Max(0, sounding - dt * 2);
            if (quiet >= 2.2) { Playing = false; sounding = 0; }
        }
    }
}

internal sealed class SystemMusicMonitor : IDisposable
{
    readonly ManualResetEvent stop = new ManualResetEvent(false);
    readonly Thread worker;
    internal volatile bool Playing, Available;
    internal volatile float Peak;
    internal volatile int LastError;
    internal SystemMusicMonitor()
    {
        worker = new Thread(Observe) { IsBackground = true, Name = "Desktop pet output meter" };
        worker.SetApartmentState(ApartmentState.MTA); worker.Start();
    }
    void Observe()
    {
        var meters = new List<OutputMeter>();
        var gate = new MusicActivityGate();
        var clock = Stopwatch.StartNew();
        double previous = 0, refresh = 0;
        try
        {
            while (!stop.WaitOne(100))
            {
                double now = clock.Elapsed.TotalSeconds;
                if (now >= refresh)
                {
                    foreach (var meter in meters) meter.Dispose(); meters.Clear();
                    try { OutputMeter.Find(meters); LastError = 0; }
                    catch (COMException error) { LastError = error.ErrorCode; }
                    catch (InvalidCastException error) { LastError = error.HResult; }
                    refresh = now + 2;
                }
                float peak = 0;
                foreach (var meter in meters)
                {
                    try { peak = Math.Max(peak, meter.Read()); }
                    catch (COMException error) { LastError = error.ErrorCode; refresh = 0; }
                }
                Available = meters.Count > 0; Peak = peak;
                gate.Update(peak, now - previous); previous = now; Playing = gate.Playing;
            }
        }
        finally { foreach (var meter in meters) meter.Dispose(); Playing = Available = false; Peak = 0; }
    }
    public void Dispose()
    {
        stop.Set(); worker.Join(); stop.Dispose();
    }

    sealed class OutputMeter : IDisposable
    {
        IAudioMeterInformation meter;
        IAudioEndpointVolume volume;
        static void Release(object value) { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
        internal float Read()
        {
            bool muted; float level, peak;
            Marshal.ThrowExceptionForHR(volume.GetMute(out muted));
            Marshal.ThrowExceptionForHR(volume.GetMasterVolumeLevelScalar(out level));
            if (muted || level <= 0) return 0;
            Marshal.ThrowExceptionForHR(meter.GetPeakValue(out peak)); return peak;
        }
        internal static void Find(List<OutputMeter> result)
        {
            IMMDeviceEnumerator enumerator = null; IMMDeviceCollection devices = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(0, 1, out devices));
                uint count; Marshal.ThrowExceptionForHR(devices.GetCount(out count));
                COMException lastError = null;
                for (uint i = 0; i < count; i++)
                {
                    IMMDevice device = null; object meter = null, volume = null;
                    try
                    {
                        Marshal.ThrowExceptionForHR(devices.Item(i, out device));
                        Guid meterId = typeof(IAudioMeterInformation).GUID, volumeId = typeof(IAudioEndpointVolume).GUID;
                        Marshal.ThrowExceptionForHR(device.Activate(ref meterId, 23, IntPtr.Zero, out meter));
                        Marshal.ThrowExceptionForHR(device.Activate(ref volumeId, 23, IntPtr.Zero, out volume));
                        result.Add(new OutputMeter { meter = (IAudioMeterInformation)meter, volume = (IAudioEndpointVolume)volume });
                        meter = volume = null;
                    }
                    catch (COMException error) { lastError = error; } // A disconnected endpoint must not hide other outputs.
                    finally { Release(meter); Release(volume); Release(device); }
                }
                if(result.Count==0 && lastError!=null)throw lastError;
            }
            finally { Release(devices); Release(enumerator); }
        }
        public void Dispose() { Release(meter); Release(volume); meter = null; volume = null; }
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint state, out IMMDeviceCollection devices);
    }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid id, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
    }
    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioMeterInformation
    {
        [PreserveSig] int GetPeakValue(out float peak);
    }
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr callback);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr callback);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float value, IntPtr context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float value, IntPtr context);
        [PreserveSig] int GetMasterVolumeLevel(out float value);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float value);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float value, IntPtr context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float value, IntPtr context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float value);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float value);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, IntPtr context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
