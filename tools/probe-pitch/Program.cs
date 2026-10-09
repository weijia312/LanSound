// 决定性实验：复刻 SpeakerLoopback 的流水线，只改 FloatRingBuffer 声明的输入采样率，
// 喂入完全相同的 44.1kHz 数据，比较输出音调。
//
// 目的：验证"WaveFormat 恒声明 48000 会让 44.1kHz 设备音调偏高约 8.8%"这一说法。
//   声明 44100 → 应正确重采样到 48kHz，1kHz 仍是 1kHz
//   声明 48000 → 被当成 48kHz 直通，1kHz 变成 1000 * 48000/44100 ≈ 1088Hz
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

const int TargetRate = 48000;
const int DeviceRate = 44100;
const double ToneHz = 1000;
const double Seconds = 1.0;

// 造 1 秒 44.1kHz 单声道 1kHz 正弦（第 0 声道）
var deviceFrames = (int)(DeviceRate * Seconds);
var source = new float[deviceFrames];
for (var i = 0; i < deviceFrames; i++)
    source[i] = (float)(Math.Sin(2 * Math.PI * ToneHz * i / DeviceRate) * 0.5);

Console.WriteLine($"输入: 设备 {DeviceRate}Hz, {ToneHz}Hz 正弦, {deviceFrames} 样本");
Console.WriteLine("");

foreach (var declaredRate in new[] { DeviceRate, TargetRate })
{
    var ring = new FloatRingBuffer(TargetRate * 4)
    {
        // 这就是被验证的那一行
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(declaredRate, 1)
    };
    var resampler = new WdlResamplingSampleProvider(ring, TargetRate);

    // 模拟 DataAvailable 的分块喂入
    const int chunk = 441;
    for (var start = 0; start < source.Length; start += chunk)
    {
        var n = Math.Min(chunk, source.Length - start);
        for (var i = 0; i < n; i++) ring.Push(source[start + i]);
    }

    var output = new List<float>();
    var buf = new float[4096];
    var rounds = 0;
    while (rounds++ < 4096)
    {
        var read = resampler.Read(buf, 0, buf.Length);
        if (read <= 0) break;
        for (var i = 0; i < read; i++) output.Add(buf[i]);
    }

    var f1 = Goertzel(output, 1000, TargetRate);
    var f2 = Goertzel(output, 1088, TargetRate);
    var peak = PeakHz(output, TargetRate, 900, 1200);

    var expect = declaredRate == DeviceRate ? "应≈1000Hz（正确重采样）" : "若直通则≈1088Hz（音调偏高）";
    Console.WriteLine($"声明输入率 = {declaredRate}Hz   {expect}");
    Console.WriteLine($"  输出样本数 : {output.Count}（期望约 {TargetRate * Seconds:F0}）");
    Console.WriteLine($"  1kHz 能量  : {f1:F4}");
    Console.WriteLine($"  1088Hz 能量: {f2:F4}");
    Console.WriteLine($"  实测主频   : {peak:F1} Hz");
    Console.WriteLine($"  偏差       : {(peak - ToneHz) / ToneHz * 100:+0.0;-0.0;0.0} %");
    Console.WriteLine("");
}

static double Goertzel(IReadOnlyList<float> d, double hz, int rate)
{
    var n = Math.Min(d.Count, rate);
    if (n < 100) return 0;
    var k = (int)Math.Round(0.5 + n * hz / rate);
    var w = 2 * Math.PI * k / n;
    var c = 2 * Math.Cos(w);
    double s1 = 0, s2 = 0;
    for (var i = 0; i < n; i++) { var s0 = d[i] + c * s1 - s2; s2 = s1; s1 = s0; }
    return Math.Sqrt(Math.Max(0, s1 * s1 + s2 * s2 - c * s1 * s2)) / n;
}

// 在给定范围内扫描找能量峰（粗粒度，够用）
static double PeakHz(IReadOnlyList<float> d, int rate, double lo, double hi)
{
    double best = 0, bestHz = 0;
    for (var hz = lo; hz <= hi; hz += 2)
    {
        var e = Goertzel(d, hz, rate);
        if (e > best) { best = e; bestHz = hz; }
    }
    return bestHz;
}

/// <summary>与生产同款：WaveFormat 可写（这正是被验证的机制）。</summary>
sealed class FloatRingBuffer : ISampleProvider
{
    private readonly float[] _buf;
    private int _readPos, _writePos;

    public FloatRingBuffer(int capacity) => _buf = new float[capacity];

    public WaveFormat WaveFormat { get; set; } = WaveFormat.CreateIeeeFloatWaveFormat(48000, 1);

    public int Count => (_writePos - _readPos + _buf.Length) % _buf.Length;
    private int Free => _buf.Length - Count - 1;

    public void Push(float s)
    {
        if (Free <= 0) return;
        _buf[_writePos] = s;
        _writePos = (_writePos + 1) % _buf.Length;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var n = Math.Min(count, Count);
        for (var i = 0; i < n; i++)
        {
            buffer[offset + i] = _buf[_readPos];
            _readPos = (_readPos + 1) % _buf.Length;
        }
        return n;
    }
}
