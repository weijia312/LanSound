// 独立探针：验证 WASAPI 环回采集在当前进程里能否正常启动并出数据。
// 用法: dotnet run --project probe-loopback   （或编译后直接运行）
using NAudio.CoreAudioApi;
using NAudio.Wave;

var enumerator = new MMDeviceEnumerator();
var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
Console.WriteLine($"默认播放设备 : {device.FriendlyName}");
Console.WriteLine($"设备 ID      : {device.ID}");

var fmt = device.AudioClient.MixFormat;
Console.WriteLine($"混音格式     : {fmt.SampleRate}Hz {fmt.Channels}ch {fmt.Encoding} {fmt.BitsPerSample}bit");
Console.WriteLine($"格式校验     : SampleRate==48000 -> {fmt.SampleRate == 48000}, " +
                  $"IeeeFloat -> {fmt.Encoding == WaveFormatEncoding.IeeeFloat}");

var capture = new WasapiLoopbackCapture(device);
Console.WriteLine($"capture.WaveFormat : {capture.WaveFormat.SampleRate}Hz " +
                  $"{capture.WaveFormat.Channels}ch {capture.WaveFormat.Encoding}");

int callbacks = 0;
long bytes = 0;
capture.DataAvailable += (_, e) =>
{
    Interlocked.Increment(ref callbacks);
    Interlocked.Add(ref bytes, e.BytesRecorded);
};
capture.RecordingStopped += (_, e) =>
{
    Console.WriteLine($"RecordingStopped: {e.Exception?.Message ?? "(正常停止)"}");
};

try
{
    capture.StartRecording();
    Console.WriteLine("StartRecording() 已调用，等待 5 秒采集…");
}
catch (Exception ex)
{
    Console.WriteLine($"!! StartRecording 抛异常: {ex.GetType().Name}: {ex.Message}");
    return 1;
}

for (int i = 1; i <= 5; i++)
{
    Thread.Sleep(1000);
    Console.WriteLine($"  t={i}s  回调 {callbacks} 次  累计 {bytes} 字节");
}

capture.StopRecording();
capture.Dispose();
Console.WriteLine(callbacks > 0 ? "PROBE-OK：采集有数据" : "PROBE-SILENT：采集回调一次都没触发");
return 0;
