// 验证 SpeakerLoopback 用的那条重采样路径：44.1kHz 立体声 → 48kHz 立体声
// 用与生产代码相同的 NAudio 类型（WdlResamplingSampleProvider）与相同的降混方式。
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

const int TargetRate = 48000;
const int SourceRate = 44100;
const double ToneHz = 1000;
const double Seconds = 1.0;

// 1) 造 1 秒 44.1kHz 立体声 float 数据（左右相同，模拟系统输出）
var frames = (int)(SourceRate * Seconds);
var interleaved = new float[frames * 2];
for (var i = 0; i < frames; i++)
{
    var v = (float)(Math.Sin(2 * Math.PI * ToneHz * i / SourceRate) * 0.5);
    interleaved[i * 2] = v;
    interleaved[i * 2 + 1] = v;
}
Console.WriteLine($"输入: {SourceRate}Hz 立体声 {frames} 帧 ({Seconds}s), {ToneHz}Hz 正弦");

// 2) 与生产代码一致的降混（N 声道 → 单声道均值）
static float[] Downmix(float[] src, int channels)
{
    var n = src.Length / channels;
    var mono = new float[n];
    for (var f = 0; f < n; f++)
    {
        var sum = 0f;
        for (var c = 0; c < channels; c++) sum += src[f * channels + c];
        mono[f] = sum / channels;
    }
    return mono;
}

var mono = Downmix(interleaved, 2);

// 3) 重采样到 48kHz
var resampler = new WdlResamplingSampleProvider(
    new ArraySampleProvider(mono, WaveFormat.CreateIeeeFloatWaveFormat(SourceRate, 1)),
    TargetRate);

var output = new List<float>();
var buf = new float[TargetRate];
int read;
while ((read = resampler.Read(buf, 0, buf.Length)) > 0)
    output.AddRange(buf.Take(read));

Console.WriteLine($"输出: {TargetRate}Hz 单声道 {output.Count} 帧，期望约 {TargetRate * Seconds:F0} 帧");

// 4) 校验：时长是否守恒
var expected = TargetRate * Seconds;
var tolerance = expected * 0.02;   // 2% 容差（重采样器有少量延迟）
var durationOk = Math.Abs(output.Count - expected) <= tolerance;
Console.WriteLine($"  时长守恒: {(durationOk ? "通过" : "失败")}"
                  + $"（偏差 {output.Count - expected:F0} 帧，容差 ±{tolerance:F0}）");

// 5) 校验：音调是否还在 1kHz（用 Goertzel 单点检测）
static double Goertzel(IReadOnlyList<float> data, double freq, int rate)
{
    var n = Math.Min(data.Count, rate);          // 取前 1 秒
    var k = (int)(0.5 + n * freq / rate);
    var w = 2 * Math.PI * k / n;
    var coeff = 2 * Math.Cos(w);
    double s1 = 0, s2 = 0;
    for (var i = 0; i < n; i++)
    {
        var s0 = data[i] + coeff * s1 - s2;
        s2 = s1; s1 = s0;
    }
    return Math.Sqrt(s1 * s1 + s2 * s2 - coeff * s1 * s2) / n;
}

var atTone = Goertzel(output, ToneHz, TargetRate);
var atOther = Goertzel(output, 3000, TargetRate);
Console.WriteLine($"  1kHz 能量 {atTone:F4} / 3kHz 能量 {atOther:F4}"
                  + $"  -> {(atTone > atOther * 10 ? "音调保持正确" : "音调异常")}");

// 6) 校验：峰值幅度是否合理（重采样不应显著改变幅度）
var peak = output.Count > 0 ? output.Max(Math.Abs) : 0;
Console.WriteLine($"  峰值幅度 {peak:F3}（输入 0.5）-> {(Math.Abs(peak - 0.5) < 0.05 ? "幅度保持" : "幅度异常")}");

// 7) 关键：复现生产里的"小块重复喂入"方式，检查 Resampler 是否会返回 0 而不推进
Console.WriteLine();
Console.WriteLine("=== 小块喂入测试（复现生产调用方式）===");
var chunkResampler = new WdlResamplingSampleProvider(
    new ArraySampleProvider(mono, WaveFormat.CreateIeeeFloatWaveFormat(SourceRate, 1)), TargetRate);
var consumed = 0;
var outTotal = 0;
var zeroStreak = 0;
var maxZeroStreak = 0;
var chunkBuf = new float[TargetRate];
for (var step = 0; step < 20; step++)
{
    // 每次只喂一小段（模拟一次 DataAvailable 的量）
    var take = Math.Min(1200, mono.Length - consumed);
    if (take <= 0) break;
    var piece = new ArraySampleProvider(mono.Skip(consumed).Take(take).ToArray(),
        WaveFormat.CreateIeeeFloatWaveFormat(SourceRate, 1));
    consumed += take;

    // 注意：生产代码是"每次新建 provider"，这里也照做，以复现真实行为
    var r = new WdlResamplingSampleProvider(piece, TargetRate);
    var got = 0;
    var guard = 0;
    while (true)
    {
        var n = r.Read(chunkBuf, 0, chunkBuf.Length);
        if (n <= 0) { zeroStreak++; maxZeroStreak = Math.Max(maxZeroStreak, zeroStreak); break; }
        zeroStreak = 0;
        got += n; outTotal += n;
        if (++guard > 100) { Console.WriteLine("  !! 循环未退出（guard 触发）"); break; }
    }
    if (step < 5) Console.WriteLine($"  step {step}: 喂入 {take} 样本 -> 产出 {got}");
}
Console.WriteLine($"  20 步共产出 {outTotal} 样本（输入共 {Math.Min(1200 * 20, mono.Length)}）");
Console.WriteLine($"  最长连续零返回: {maxZeroStreak}");
var ok = durationOk && atTone > atOther * 10 && Math.Abs(peak - 0.5) < 0.05;
Console.WriteLine();
Console.WriteLine(ok ? "RESAMPLE-OK：44.1kHz → 48kHz 转换正确" : "RESAMPLE-FAILED");
return ok ? 0 : 1;

/// <summary>把一个 float 数组包成 ISampleProvider（只为测试）。</summary>
sealed class ArraySampleProvider : ISampleProvider
{
    private readonly float[] _data;
    private int _pos;

    public ArraySampleProvider(float[] data, WaveFormat format)
    {
        _data = data;
        WaveFormat = format;
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        var n = Math.Min(count, _data.Length - _pos);
        Array.Copy(_data, _pos, buffer, offset, n);
        _pos += n;
        return n;
    }
}
