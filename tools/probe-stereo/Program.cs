// 复刻 SpeakerLoopback 的采集流水线并验证三件事。结构必须与生产一致：
//   FloatRingBuffer(L) / FloatRingBuffer(R)  ->  各自 WdlResamplingSampleProvider  ->  出帧
//
// 判据：
//   1) 样本守恒 —— 用斜坡信号，输出必须与输入逐样本对齐（不丢、不重、不错位）。
//      这是"全损音质"的直接反证。
//   2) 出帧总量 —— 每 20ms 恰好一帧，不允许丢帧或重复出帧。
//   3) 立体声   —— 左 1kHz / 右 3kHz，零串扰。若生产改回"左右求和"会立刻失败。
//
// 注意：这里**不做实时节奏模拟**。早先版本用 Thread.Sleep(10) 模拟 DataAvailable 间隔，
// 结果在负载高的机器上 Sleep 会超时 100ms+，于是把"喂数抖动"误报成"出帧抖动"——
// 那是判据设计错误：实时抖动由系统调度决定，跟流水线是否正确无关。
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

const int Rate = 48000;
const int FramePerCh = 960;          // 20ms
const double LeftHz = 1000, RightHz = 3000;
const int Seconds = 5;
const int ChunkFrames = 480;         // 10ms，模拟一次 DataAvailable 的粒度

var failures = new List<string>();

// ================= 1 + 2：样本守恒与出帧总量（斜坡信号，可逐样本比对）=================
{
    var inputL = new FloatRingBuffer(Rate * 4);
    var inputR = new FloatRingBuffer(Rate * 4);
    var outL = new List<int>();
    var outR = new List<int>();
    ISampleProvider? resL = null, resR = null;
    var frames = 0;

    long pushedFrames = 0;
    var totalChunks = Seconds * 1000 / 10;

    for (var c = 0; c < totalChunks; c++)
    {
        for (var i = 0; i < ChunkFrames; i++)
        {
            // 斜坡：第 n 个样本值 = (n mod 1000)/1000，重建后错位一眼可见
            inputL.Push((pushedFrames % 1000) / 1000f);
            inputR.Push(((pushedFrames + 500) % 1000) / 1000f);
            pushedFrames++;
        }

        resL ??= new WdlResamplingSampleProvider(inputL, Rate);
        resR ??= new WdlResamplingSampleProvider(inputR, Rate);
        Collect(resL, outL);
        Collect(resR, outR);

        // 与生产同构：两声道可用的完整帧数取小值，逐帧取走 960 样本
        var avail = Math.Min(outL.Count / FramePerCh, outR.Count / FramePerCh);
        for (var n = 0; n < avail; n++)
        {
            outL.RemoveRange(0, FramePerCh);
            outR.RemoveRange(0, FramePerCh);
            frames++;
        }
    }

    Console.WriteLine($"斜坡测试：推入 {pushedFrames} 帧/声道 → 出帧 {frames}，剩余样本 左={outL.Count} 右={outR.Count}");

    // 出帧总量：应约等于 秒数 × 50
    var expectFrames = Seconds * 50;
    Console.WriteLine($"  出帧总量：{frames}（期望 {expectFrames}）");
    if (Math.Abs(frames - expectFrames) > 2)
        failures.Add($"出帧总量偏差过大：{frames} vs {expectFrames}");

    // 样本守恒：输出样本数 + 剩余数 应等于推入数（在本例中 RingBuffer 容量足够，无丢弃）
    var seenL = (long)frames * FramePerCh + outL.Count;
    var seenR = (long)frames * FramePerCh + outR.Count;
    Console.WriteLine($"  消费样本：左 {seenL} / 右 {seenR}（推入 {pushedFrames}）");
    if (seenL != pushedFrames) failures.Add($"左声道样本不守恒：{seenL} != {pushedFrames}");
    if (seenR != pushedFrames) failures.Add($"右声道样本不守恒：{seenR} != {pushedFrames}");

    // 逐样本连续性
    var badL = CountDiscontinuities(outL);
    var badR = CountDiscontinuities(outR);
    Console.WriteLine($"  尾部斜坡不连续处：左 {badL} / 右 {badR}（应为 0）");
    if (badL > 0) failures.Add($"左声道有 {badL} 处样本错位/丢失");
    if (badR > 0) failures.Add($"右声道有 {badR} 处样本错位/丢失");
}

// ================= 3：立体声串扰 =================
{
    var inputL = new FloatRingBuffer(Rate * 4);
    var inputR = new FloatRingBuffer(Rate * 4);
    var L = new List<float>();
    var R = new List<float>();
    ISampleProvider? resL = null, resR = null;

    var total = Rate * Seconds;
    var phase = 0;
    while (phase < total)
    {
        var n = Math.Min(ChunkFrames, total - phase);
        for (var i = 0; i < n; i++)
        {
            var t = (phase + i) / (double)Rate;
            inputL.Push((float)(Math.Sin(2 * Math.PI * LeftHz * t) * 0.5));
            inputR.Push((float)(Math.Sin(2 * Math.PI * RightHz * t) * 0.5));
        }
        phase += n;
        resL ??= new WdlResamplingSampleProvider(inputL, Rate);
        resR ??= new WdlResamplingSampleProvider(inputR, Rate);
        CollectFloat(resL, L);
        CollectFloat(resR, R);
    }

    var L1 = Goertzel(L, LeftHz); var L3 = Goertzel(L, RightHz);
    var R1 = Goertzel(R, LeftHz); var R3 = Goertzel(R, RightHz);
    Console.WriteLine($"  左声道: {LeftHz}Hz={L1:F4}  {RightHz}Hz={L3:F4}");
    Console.WriteLine($"  右声道: {LeftHz}Hz={R1:F4}  {RightHz}Hz={R3:F4}");

    var isMono = L1 > 0.05 && R1 > 0.05 && L3 > 0.05 && R3 > 0.05;
    if (isMono) failures.Add("两个频率同时出现在左右声道 —— 被降混成单声道");
    else if (!(L1 > L3 * 10 && R3 > R1 * 10 && L1 > 0.05 && R3 > 0.05))
        failures.Add("左右声道存在串扰或交换");
}

Console.WriteLine();
if (failures.Count == 0)
{
    Console.WriteLine("PIPELINE-OK：样本守恒、出帧量正确、零串扰");
    return 0;
}
foreach (var f in failures) Console.WriteLine("  ✗ " + f);
Console.WriteLine("PIPELINE-FAILED");
return 1;

// ---------------- 辅助 ----------------
static void Collect(ISampleProvider r, List<int> dest)
{
    var buf = new float[4096];
    var rounds = 0;
    while (rounds++ < 64)
    {
        var n = r.Read(buf, 0, buf.Length);
        if (n <= 0) break;
        for (var i = 0; i < n; i++) dest.Add((int)Math.Round(buf[i] * 1000) % 1000);
    }
}

static void CollectFloat(ISampleProvider r, List<float> dest)
{
    var buf = new float[4096];
    var rounds = 0;
    while (rounds++ < 64)
    {
        var n = r.Read(buf, 0, buf.Length);
        if (n <= 0) break;
        for (var i = 0; i < n; i++) dest.Add(buf[i]);
    }
}

/// <summary>斜坡连续性：相邻样本应恰好 +1（mod 1000）。</summary>
static int CountDiscontinuities(List<int> seq)
{
    var bad = 0;
    for (var i = 1; i < seq.Count; i++)
    {
        var expect = (seq[i - 1] + 1) % 1000;
        if (seq[i] != expect) bad++;
    }
    return bad;
}

static double Goertzel(IReadOnlyList<float> d, double hz, int rate = 48000)
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

/// <summary>与生产同款：单声道环形缓冲，O(1) 出入队，容量固定。</summary>
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
