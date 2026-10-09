# Play a test tone through the system default playback device, so WASAPI loopback
# capture can pick it up.
#
# Do NOT use [console]::Beep -- that drives the motherboard speaker and bypasses
# the default audio device, so loopback never sees it.
#
# Stereo verification: -LeftHz and -RightHz put different frequencies on each
# channel. If the two channels are later mixed together or swapped anywhere in
# the pipeline, the receiving side can detect it by frequency analysis.
#
# ASCII-only on purpose: this file is parsed by Windows PowerShell, whose default
# file encoding handling mangles non-ASCII literals.
param(
    [int]$Seconds = 20,
    [double]$LeftHz = 660,
    [double]$RightHz = 660,
    [int]$Amplitude = 12000,
    [int]$Rate = 44100
)

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Threading;

public static class TonePlayer
{
    [StructLayout(LayoutKind.Sequential)]
    struct WAVEFORMATEX
    {
        public ushort wFormatTag, nChannels;
        public uint nSamplesPerSec, nAvgBytesPerSec;
        public ushort nBlockAlign, wBitsPerSample, cbSize;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct WAVEHDR
    {
        public IntPtr lpData; public uint dwBufferLength, dwBytesRecorded;
        public IntPtr dwUser; public uint dwFlags, dwLoops; public IntPtr lpNext, reserved;
    }

    [DllImport("winmm.dll")] static extern int waveOutOpen(out IntPtr h, uint id, ref WAVEFORMATEX f, IntPtr cb, IntPtr inst, uint flags);
    [DllImport("winmm.dll")] static extern int waveOutPrepareHeader(IntPtr h, ref WAVEHDR hdr, int size);
    [DllImport("winmm.dll")] static extern int waveOutWrite(IntPtr h, ref WAVEHDR hdr, int size);
    [DllImport("winmm.dll")] static extern int waveOutUnprepareHeader(IntPtr h, ref WAVEHDR hdr, int size);
    [DllImport("winmm.dll")] static extern int waveOutClose(IntPtr h);

    public static string Play(int seconds, double leftHz, double rightHz, int amp, int rate)
    {
        var fmt = new WAVEFORMATEX
        {
            wFormatTag = 1, nChannels = 2, nSamplesPerSec = (uint)rate,
            nAvgBytesPerSec = (uint)(rate * 4), nBlockAlign = 4, wBitsPerSample = 16
        };
        IntPtr h;
        // WAVE_MAPPER = -1 -> system default playback device
        int rc = waveOutOpen(out h, unchecked((uint)-1), ref fmt, IntPtr.Zero, IntPtr.Zero, 0);
        if (rc != 0) return "waveOutOpen failed: " + rc;

        int samples = rate * seconds;
        var data = new byte[samples * 4];
        for (int i = 0; i < samples; i++)
        {
            double env = Math.Min(1.0, Math.Min(i, samples - i) / (rate * 0.05));
            short l = (short)(Math.Sin(2 * Math.PI * leftHz * i / rate) * amp * env);
            short r = (short)(Math.Sin(2 * Math.PI * rightHz * i / rate) * amp * env);
            data[i * 4] = (byte)(l & 0xFF); data[i * 4 + 1] = (byte)(l >> 8);
            data[i * 4 + 2] = (byte)(r & 0xFF); data[i * 4 + 3] = (byte)(r >> 8);
        }

        IntPtr buf = Marshal.AllocHGlobal(data.Length);
        Marshal.Copy(data, 0, buf, data.Length);
        var hdr = new WAVEHDR { lpData = buf, dwBufferLength = (uint)data.Length };
        waveOutPrepareHeader(h, ref hdr, Marshal.SizeOf(typeof(WAVEHDR)));
        waveOutWrite(h, ref hdr, Marshal.SizeOf(typeof(WAVEHDR)));
        Thread.Sleep((seconds + 1) * 1000);
        waveOutUnprepareHeader(h, ref hdr, Marshal.SizeOf(typeof(WAVEHDR)));
        Marshal.FreeHGlobal(buf);
        waveOutClose(h);
        return "played " + seconds + "s: L=" + leftHz + "Hz R=" + rightHz + "Hz on default device";
    }
}
'@ -ErrorAction Stop

Write-Host ([TonePlayer]::Play($Seconds, $LeftHz, $RightHz, $Amplitude, $Rate))
