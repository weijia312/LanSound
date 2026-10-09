// 只读探针：枚举所有播放设备，打印设备端 MixFormat 与 WasapiLoopbackCapture 实际报的格式，
// 用来判断"44.1kHz 设备会被拒绝"这种情况到底有多现实。
// 不做任何修改，只读。
using NAudio.CoreAudioApi;
using NAudio.Wave;

Console.WriteLine("=== 播放设备格式普查 ===");
Console.WriteLine();

using var en = new MMDeviceEnumerator();
int total = 0, wouldReject = 0;

foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
{
    total++;
    string mixDesc, capDesc, verdict;
    try
    {
        var mix = d.AudioClient.MixFormat;
        mixDesc = $"{mix.SampleRate}Hz/{mix.Channels}ch/{mix.Encoding}/{mix.BitsPerSample}bit";
    }
    catch (Exception ex) { mixDesc = $"读取失败({ex.GetType().Name})"; }

    WasapiLoopbackCapture? cap = null;
    try
    {
        cap = new WasapiLoopbackCapture(d);
        var f = cap.WaveFormat;
        capDesc = $"{f.SampleRate}Hz/{f.Channels}ch/{f.Encoding}/{f.BitsPerSample}bit";

        // 复现 SpeakerLoopback 当前的判定
        bool rejected = f.SampleRate != 48000 || f.Encoding != WaveFormatEncoding.IeeeFloat;
        if (rejected) wouldReject++;
        verdict = rejected ? "会被拒绝（当前代码）" : "可用";
    }
    catch (Exception ex)
    {
        capDesc = $"构造失败({ex.GetType().Name})";
        verdict = "构造就失败";
        wouldReject++;
    }
    finally { cap?.Dispose(); d.Dispose(); }

    Console.WriteLine($"设备 : {d.FriendlyName}");
    Console.WriteLine($"  设备 MixFormat : {mixDesc}");
    Console.WriteLine($"  采集实际格式   : {capDesc}");
    Console.WriteLine($"  当前代码判定   : {verdict}");
    Console.WriteLine();
}

Console.WriteLine($"共 {total} 个设备，其中 {wouldReject} 个会被当前代码拒绝。");
