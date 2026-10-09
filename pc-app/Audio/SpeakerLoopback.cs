using System.Collections.Concurrent;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace pc_app.Audio;

/// <summary>
/// 扬声器 Loopback 采集：采集系统默认输出设备正在播放的声音，
/// 统一转成 48kHz 16bit **立体声** 20ms 帧（960 采样/声道）交给 AudioStreamSession 直发手机。
/// <para>
/// 必须知道的几个坑（都实测踩过，且都表现为"静默没声音"或"音质变差"）：
/// </para>
/// <list type="number">
/// <item>
/// <see cref="WasapiLoopbackCapture"/> 创建时就<strong>绑定当时那台默认设备</strong>。
/// 用户切换默认播放设备后，旧实例会继续盯着已经不出声的那台，所以这里监听设备变化并自动重挂。
/// </item>
/// <item>
/// <strong>采集格式跟随设备配置</strong>：用户把播放设备设成 44.1kHz 时采集就是 44.1kHz。
/// 早期版本要求必须 48kHz、否则直接放弃采集，结果那类机器永远没声音且查不出原因。
/// 现在接受设备原生格式，由内部重采样统一到 48kHz。
/// </item>
/// <item>
/// <strong>重采样必须逐声道进行，绝不能降混成单声道</strong>。
/// 曾经为了让重采样器简单，把 N 声道求和成单声道再复制成左右 —— 结果立体声像被彻底抹平，
/// 听感上"和手机原生播放差距很大"，而这属于实打实的音质损失，不是 EQ 能解释的。
/// </item>
/// </list>
/// </summary>
public sealed class SpeakerLoopback : IDisposable
{
    public const int SampleRate = 48000;
    public const int Channels = 2;
    /// <summary>20ms @48kHz，每声道采样数（960）。</summary>
    public const int FrameSamplesPerChannel = 960;
    public const int FrameSamples = FrameSamplesPerChannel * Channels;

    private readonly object _gate = new();
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly DeviceChangeWatcher _watcher;
    private readonly Action<string> _log;

    private WasapiLoopbackCapture? _capture;
    private MMDevice? _device;
    private string? _deviceId;

    // ---- 采集 → 48kHz 立体声的转换流水线 ----
    /// <summary>采集实例实际报出的格式（决定按几个声道解析 DataAvailable）。</summary>
    private WaveFormat? _captureFormat;

    /// <summary>本轮回调累积的设备原始样本（交错、未降混）。</summary>
    private readonly List<float> _captureSamples = new();

    /// <summary>左右各一个重采样器：逐声道独立处理，立体声像不被破坏。</summary>
    private ISampleProvider? _resamplerL;
    private ISampleProvider? _resamplerR;

    /// <summary>重采样器的数据源：左右各一个持续存在的环形缓冲（容量约 1 秒）。</summary>
    private readonly FloatRingBuffer _inputL = new(SampleRate);
    private readonly FloatRingBuffer _inputR = new(SampleRate);

    /// <summary>重采样后、等待组帧的样本：左右各一个固定容量环形缓冲（约 0.5 秒）。</summary>
    private readonly FloatRingBuffer _readyL = new(SampleRate / 2);
    private readonly FloatRingBuffer _readyR = new(SampleRate / 2);

    private bool _pipelineReady;
    private int _expectedChannels = 2;

    /// <summary>最后一次收到采集数据的时间，用于识别"设备还在但已经不出声"。</summary>
    private long _lastDataTicks;

    /// <summary>
    /// "应当在采集"的用户级意图。与 <see cref="_capture"/> 区分开：
    /// 设备切换瞬间新设备往往未就绪（蓝牙尤甚），接入会失败、<see cref="_capture"/> 为 null，
    /// 但只要本标志还在，巡检就要持续重试 —— 否则一次失败就是永久无声。
    /// </summary>
    private volatile bool _started;

    /// <summary>是否已经成功挂上过设备（决定首次接入与后续重挂的日志措辞）。</summary>
    private bool _everAttached;

    /// <summary>
    /// 重挂请求队列 + 专用工作线程。所有 attach/detach 只允许跑在工作线程上：
    /// 设备通知跑在 COM RPC 线程、<see cref="OnRecordingStopped"/> 跑在采集线程，
    /// 在那两个线程上同步调用 StopRecording 会形成死锁 ——
    /// 实测设备切换后 StopRecording 等采集线程退出、采集线程卡在 audiosrv，
    /// 连锁反应是 audiosrv 拖死、新会话的音频 COM 调用全部阻塞，
    /// 手机连 hello 都收不到应答（一直"正在连接电脑"）。
    /// </summary>
    private readonly BlockingCollection<string> _reattachQueue = new();
    private readonly Thread _worker;

    private Timer? _healthTimer;

    /// <summary>重挂节流：设备切换瞬间系统会连发多条通知，避免连环重试。</summary>
    private long _lastRestartTicks;

    /// <summary>复用的出帧缓冲：交给订阅者即用即弃，每次新建会持续产生垃圾。</summary>
    private readonly short[] _frame = new short[FrameSamples];

    /// <summary>凑满一帧（FrameSamples 个 short，交错立体声）时触发；在 NAudio 捕获线程上调用。</summary>
    /// <remarks>回调中必须立即消费完传入数组，不要长期持有。</remarks>
    public event Action<short[]>? FrameReady;

    public SpeakerLoopback(Action<string> log)
    {
        _log = log;
        _watcher = new DeviceChangeWatcher(this);
        // 注册默认设备变化通知（回调只投递重挂请求，绝不在通知线程上碰音频 COM 调用）
        _enumerator.RegisterEndpointNotificationCallback(_watcher);
        _worker = new Thread(WorkLoop) { IsBackground = true, Name = "SpeakerLoopbackWorker" };
        _worker.Start();
    }

    /// <summary>
    /// 开始采集系统默认输出设备。实际接入在工作线程上异步完成，结果以日志为准 ——
    /// audiosrv 在设备切换瞬间可能长时间不响应 COM 调用，同步接入会把调用方
    /// （会话线程）一起拖死，手机端表现为永远"正在连接电脑"。
    /// <para>
    /// 因此这里<strong>不返回成功与否</strong>：只登记意图并投递一次挂载请求，
    /// 失败由 10 秒巡检周期性重试，直到成功或 Dispose。
    /// </para>
    /// </summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_started) return;
            _started = true;
        }

        RequestReattach("启动采集");
        // 巡检无条件启动：接入失败 / 静默失效（蓝牙切模式等）都靠它周期性重试恢复
        _healthTimer = new Timer(_ => CheckHealth(), null,
            TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
    }

    /// <summary>投递一次重挂请求（幂等；突发通知由工作线程合并处理）。</summary>
    private void RequestReattach(string reason)
    {
        try { _reattachQueue.Add(reason); }
        catch (Exception) { /* 已 Dispose */ }
    }

    /// <summary>专用工作线程：串行执行全部 attach/detach，是唯一允许阻塞在音频 COM 调用上的线程。</summary>
    private void WorkLoop()
    {
        try
        {
            foreach (var reason in _reattachQueue.GetConsumingEnumerable())
            {
                // 合并突发：设备切换瞬间系统会连发多条通知，排空后只重挂一次
                while (_reattachQueue.TryTake(out _)) { }
                try { Reattach(reason); }
                catch (Exception ex) { _log($"重挂采集异常：{ex.Message}"); }
            }
        }
        catch (ObjectDisposedException)
        {
            // 会话关闭时队列可能在工作线程等待期间被释放；这是正常退出路径。
        }
    }

    /// <summary>挂到当前默认输出设备上；成功返回 true。调用方需自行加锁。</summary>
    private bool AttachToDefaultDevice(bool announce)
    {
        MMDevice? device = null;
        WasapiLoopbackCapture? capture = null;
        try
        {
            device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            capture = new WasapiLoopbackCapture(device);

            // 校验必须看 capture.WaveFormat，而不是 device.AudioClient.MixFormat：
            // 设备端常报 Extensible（例如蓝牙耳机的 32bit Extensible），与 IeeeFloat 相等比较必然为假。
            // 早期版本就是错在检查 MixFormat，导致在这台机器上静默不采集、手机端永远没声。
            // 采样率不再限制 —— 由重采样流水线兜底。
            var fmt = capture.WaveFormat;
            if (fmt.Encoding != WaveFormatEncoding.IeeeFloat)
            {
                _log($"扬声器采集格式不支持（{fmt.SampleRate}Hz/{fmt.Encoding}），无法回传");
                capture.Dispose();
                device.Dispose();
                return false;
            }
            _captureFormat = fmt;

            capture.DataAvailable += OnData;
            capture.RecordingStopped += OnRecordingStopped;
            capture.StartRecording();

            _capture = capture;
            _device = device;
            _deviceId = device.ID;
            Volatile.Write(ref _lastDataTicks, Environment.TickCount64);

            var how = fmt.SampleRate == SampleRate
                ? $"{fmt.SampleRate}Hz {fmt.Channels}ch"
                : $"{fmt.SampleRate}Hz {fmt.Channels}ch（重采样为 {SampleRate}Hz）";
            _log(announce
                ? $"已开始采集系统声音：{device.FriendlyName}（{how} → 48kHz 立体声下行）"
                : $"重新挂载采集：{device.FriendlyName}（{how}）");
            return true;
        }
        catch (Exception ex)
        {
            _log($"接入默认播放设备失败：{ex.Message}");
            try { capture?.Dispose(); } catch { }
            try { device?.Dispose(); } catch { }
            return false;
        }
    }

    /// <summary>
    /// 释放当前采集实例（遗弃式，<strong>不同步等 StopRecording</strong>）。
    /// StopRecording 要等采集线程退出，而设备拔除瞬间采集线程可能正卡在 audiosrv，
    /// 同步等待会把本线程一起拖死（实测这就是"切设备后整机无声 + 新会话阻塞"的起点）。
    /// 旧实例交给一次性线程慢慢收；字段解引后工作线程可立即挂新设备。
    /// </summary>
    private void DetachCurrent()
    {
        var capture = Interlocked.Exchange(ref _capture, null);
        var device = Interlocked.Exchange(ref _device, null);
        _deviceId = null;
        if (capture != null)
        {
            capture.DataAvailable -= OnData;
            capture.RecordingStopped -= OnRecordingStopped;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { capture.StopRecording(); } catch { }
                try { capture.Dispose(); } catch { }
                try { device?.Dispose(); } catch { }
            });
        }
        else if (device != null)
        {
            try { device.Dispose(); } catch { }
        }
    }

    /// <summary>换设备重挂（仅工作线程调用，自行加锁）。</summary>
    private void Reattach(string reason)
    {
        lock (_gate) ReattachCore(reason);
    }

    /// <summary>重挂实现。<strong>调用方必须已持有 <see cref="_gate"/></strong>。</summary>
    private void ReattachCore(string reason)
    {
        // 节流：设备切换瞬间系统会连发多条通知
        var now = Environment.TickCount64;
        if (now - Volatile.Read(ref _lastRestartTicks) < 1500) return;
        Volatile.Write(ref _lastRestartTicks, now);

        // 以"意图"为准而不是 _capture：接入失败会让 _capture 为 null，
        // 但只要还在运行态就必须允许继续重挂，否则一次失败 = 永久无声。
        if (!_started) return;   // 已 Dispose

        _log($"检测到音频设备变化（{reason}），重新挂载采集");
        DetachCurrent();
        ResetPipeline();
        if (AttachToDefaultDevice(announce: !_everAttached)) _everAttached = true;

        // 接入期间可能已被 Dispose（会话结束）：立即撤掉，避免留下无主采集实例
        if (!_started) DetachCurrent();
    }

    /// <summary>巡检：默认设备换了，或长时间没有采集数据，就排队重挂一次。</summary>
    private void CheckHealth()
    {
        if (!_started) return;

        string? currentId;
        try
        {
            using var probe = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            currentId = probe.ID;
        }
        catch
        {
            return; // 枚举失败时不动作，等下一次
        }

        // 拿不到锁就跳过本轮：工作线程正在重挂（可能卡在 audiosrv），
        // 巡检绝不能陪它一起阻塞 —— 定时器线程池经不起每 10 秒堵一个线程
        if (!Monitor.TryEnter(_gate)) return;
        try
        {
            if (!_started) return;

            // 采集处于空窗期（此前接入失败，例如设备未就绪/被拔）：排队重挂重试
            if (_capture == null)
            {
                RequestReattach("采集未运行，尝试恢复");
                return;
            }

            if (currentId != _deviceId)
            {
                RequestReattach("默认设备已变化");
                return;
            }

            // 设备没变但 8 秒没有数据 —— 说明采集静默失效（设备被拔、驱动异常等）
            var idle = Environment.TickCount64 - Volatile.Read(ref _lastDataTicks);
            if (idle > 8000)
                RequestReattach($"采集已静默 {idle / 1000} 秒");
        }
        finally { Monitor.Exit(_gate); }
    }

    /// <summary>
    /// 采集线程异常退出（最典型的是设备被拔出）。本回调就跑在采集线程上，
    /// 只投递重挂请求 —— 在这里同步 StopRecording 等于"等自己退出"，必然死锁。
    /// </summary>
    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
            _log($"系统声音采集中断：{e.Exception.Message}");
        RequestReattach("采集已停止");
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        Volatile.Write(ref _lastDataTicks, Environment.TickCount64);
        if (e.BytesRecorded <= 0) return;

        try
        {
            if (!_pipelineReady) InitPipeline();

            for (var offset = 0; offset < e.BytesRecorded; offset += 4)
                _captureSamples.Add(BitConverter.ToSingle(e.Buffer, offset));

            PumpResampler();
        }
        catch (Exception ex)
        {
            // 只记不抛：一次转换出错不应让采集线程永久停摆
            // （NAudio 会把异常转成 RecordingStopped，进而触发重挂，形成"一次抖动就断流"的连锁）
            _captureSamples.Clear();
            _log($"音频格式转换失败：{ex.Message}");
        }
    }

    /// <summary>按采集的实际格式建立转换流水线（只做一次；重挂设备时会重置）。</summary>
    private void InitPipeline()
    {
        var fmt = _captureFormat ?? throw new InvalidOperationException("采集格式未知");
        _expectedChannels = fmt.Channels;
        // 输入采样率用设备实际值：重采样器构造时读 source.WaveFormat.SampleRate，
        // 若沿用默认 48k 声明，44.1kHz 设备会被直通（音调偏高约 8.8%）
        var inputFormat = WaveFormat.CreateIeeeFloatWaveFormat(fmt.SampleRate, 1);
        _inputL.WaveFormat = inputFormat;
        _inputR.WaveFormat = inputFormat;
        _pipelineReady = true;
    }

    /// <summary>
    /// 把已累积的采集样本灌进重采样器，取出 48kHz 立体声样本写入组帧缓冲。
    /// <para>
    /// 左右<strong>各走一条独立的重采样链</strong>（<see cref="_resamplerL"/> / <see cref="_resamplerR"/>），
    /// 立体声像完整保留。这里只做声道数适配：单声道时同一值复制到两路，>2 声道取前两路。
    /// </para>
    /// <para>
    /// <strong>绝不做左右求和</strong>：曾为了简化实现把 N 声道均值降混成单声道再复制成左右，
    /// 结果立体声像被整个抹平，而帧大小、帧数、阶段切换等自测全部照常通过 —— 只有耳朵能发现。
    /// 详见 README 第 6 条。
    /// </para>
    /// </summary>
    private void PumpResampler()
    {
        if (_captureSamples.Count == 0) return;

        // 1) 把原始样本规整成**左右两路**，写进持续存在的输入环形缓冲。
        //    只做声道数适配（单声道复制、>2 声道取前两路），**绝不做左右求和** ——
        //    那会抹平立体声像（曾经的实测教训）。
        //    重采样器只创建一次，数据源必须是长期可读的缓冲：早先版本每次 new 一个
        //    包着临时数组的 provider，重采样器之后永远读到已耗尽的旧数组，
        //    表现为只出几帧就彻底没声（且无任何异常）。
        var ch = _expectedChannels;
        var frames = _captureSamples.Count / ch;
        for (var f = 0; f < frames; f++)
        {
            var b = f * ch;
            if (ch == 1)
            {
                var v = _captureSamples[b];
                _inputL.Push(v);
                _inputR.Push(v);
            }
            else
            {
                _inputL.Push(_captureSamples[b]);
                _inputR.Push(_captureSamples[b + 1]);
            }
        }
        _captureSamples.Clear();

        // 2) 逐声道重采样到 48kHz，结果也按左右分开存。
        //    两路各自独立，立体声像得以保留。
        _resamplerL ??= new WdlResamplingSampleProvider(_inputL, SampleRate);
        _resamplerR ??= new WdlResamplingSampleProvider(_inputR, SampleRate);

        DrainInto(_resamplerL, _readyL);
        DrainInto(_resamplerR, _readyR);

        // 3) 出帧。两个声道可用的完整帧数取小值；用固定容量环形缓冲，
        //    出队是 O(1) 且内存有界（早先用 List + RemoveRange(0,n)，
        //    每帧都要搬移整个列表，积压时会阻塞采集线程 → 声音断断续续）。
        var avail = Math.Min(_readyL.Count / FrameSamplesPerChannel,
                             _readyR.Count / FrameSamplesPerChannel);
        for (var n = 0; n < avail; n++)
        {
            for (var i = 0; i < FrameSamplesPerChannel; i++)
            {
                _frame[i * 2] = FloatToShort(_readyL.Pop());
                _frame[i * 2 + 1] = FloatToShort(_readyR.Pop());
            }
            FrameReady?.Invoke(_frame);
        }
    }

    /// <summary>把重采样器当前能给出的样本全部取出，写进对应声道的环形缓冲。</summary>
    private static void DrainInto(ISampleProvider resampler, FloatRingBuffer dest)
    {
        var buf = new float[4096];
        // 防御性上限：正常情况下重采样器取尽输入就会返回 0 结束循环。
        // 万一上游行为异常（曾因漏写读取位置而永久返回同一段数据），
        // 这里兜住，避免把采集线程挂死 —— 那种故障表现为"突然没声且无异常"，极难排查。
        var rounds = 0;
        while (rounds++ < 64)
        {
            var read = resampler.Read(buf, 0, buf.Length);
            if (read <= 0) break;
            for (var i = 0; i < read; i++) dest.Push(buf[i]);
        }
    }

    /// <summary>
    /// 持续存在的**单声道**环形缓冲：既可当重采样器的数据源（实现 <see cref="ISampleProvider"/>），
    /// 也可当重采样结果的存放处（<see cref="Push"/>/<see cref="Pop"/>）。
    /// <para>
    /// 为什么必须是这样长期可读的缓冲：不能让重采样器直接包一个一次性数组 ——
    /// 那样它只在第一次拿到数据，之后永远读到耗尽的旧数据，
    /// 表现为"只出几帧就彻底没声，且没有任何异常"。
    /// </para>
    /// <para>
    /// 为什么不用 <c>List</c> + <c>RemoveRange(0,n)</c>：那是 O(n) 搬移，
    /// 每出一帧就要挪动整个列表，积压时会阻塞采集线程 → 声音断断续续。
    /// 这里出队是 O(1)，且容量固定、内存有界。
    /// </para>
    /// </summary>
    private sealed class FloatRingBuffer : ISampleProvider
    {
        private readonly float[] _buf;
        private int _readPos;
        private int _writePos;

        public FloatRingBuffer(int capacity) => _buf = new float[capacity];

        /// <summary>
        /// 对重采样器声明为单声道：左右各一个实例，从而逐声道独立重采样。
        /// 采样率必须跟随实际采集格式（InitPipeline 中设置）：
        /// WdlResamplingSampleProvider 构造时从本属性读输入采样率，写死 48kHz 会让
        /// 44.1kHz 设备被当成 48kHz 直通（不重采样），手机端听到音调偏高约 8.8%。
        /// </summary>
        public WaveFormat WaveFormat { get; set; } = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 1);

        public int Count => (_writePos - _readPos + _buf.Length) % _buf.Length;

        private int Free => _buf.Length - Count - 1;

        public void Push(float sample)
        {
            if (Free <= 0) return;   // 满了就丢，避免覆盖未读数据
            _buf[_writePos] = sample;
            _writePos = (_writePos + 1) % _buf.Length;
        }

        public float Pop()
        {
            if (Count == 0) return 0f;
            var v = _buf[_readPos];
            _readPos = (_readPos + 1) % _buf.Length;
            return v;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            // 关键：必须逐次推进读取位置。重采样器会反复调用直到返回 0 表示取尽；
            // 若每次都返回同一段数据，那个循环永不结束，采集线程会被直接挂死。
            var n = Math.Min(count, Count);
            for (var i = 0; i < n; i++)
            {
                buffer[offset + i] = _buf[_readPos];
                _readPos = (_readPos + 1) % _buf.Length;
            }
            return n;
        }

        public void Clear()
        {
            _readPos = 0;
            _writePos = 0;
        }
    }

    /// <summary>换设备后流水线要重建：新设备的采样率/声道数可能与旧的不同。</summary>
    private void ResetPipeline()
    {
        _captureFormat = null;
        _captureSamples.Clear();
        // 重建数据源与重采样器：新设备的采样率可能与旧的不同
        _inputL.Clear();
        _inputR.Clear();
        _readyL.Clear();
        _readyR.Clear();
        _resamplerL = null;
        _resamplerR = null;
        _pipelineReady = false;
    }

    private static short FloatToShort(float f)
        => (short)Math.Clamp(f * 32767f, short.MinValue, short.MaxValue);

    public void Dispose()
    {
        _started = false;
        try { _healthTimer?.Dispose(); } catch { }
        _healthTimer = null;
        try { _enumerator.UnregisterEndpointNotificationCallback(_watcher); } catch { }
        try { _reattachQueue.CompleteAdding(); } catch { }

        // 遗弃式清理（见 DetachCurrent）：会话清理路径上绝不同步等 audiosrv，
        // 否则工作线程卡住时会话清理线程会被一起拖死
        DetachCurrent();
        try { _enumerator.Dispose(); } catch { }
        try { _reattachQueue.Dispose(); } catch { }
    }

    /// <summary>
    /// 接收系统音频端点变化通知。回调在 NAudio 的专用线程上触发，
    /// 这里只把动作交给带节流与加锁的 <see cref="Reattach"/>。
    /// </summary>
    private sealed class DeviceChangeWatcher : IMMNotificationClient
    {
        private readonly SpeakerLoopback _owner;

        public DeviceChangeWatcher(SpeakerLoopback owner) => _owner = owner;

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            // 只关心"多媒体"角色的输出设备变化（系统默认声音走的就是它）
            if (flow == DataFlow.Render && role == Role.Multimedia)
                _owner.RequestReattach("系统默认输出设备改变");
        }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState)
        {
            // 只在"当前正在采的设备"被拔掉/禁用时动作
            if (_owner._deviceId == deviceId && newState != DeviceState.Active)
                _owner.RequestReattach("当前采集设备失效");
        }

        public void OnDeviceAdded(string pwstrDeviceId) { }

        public void OnDeviceRemoved(string deviceId)
        {
            if (_owner._deviceId == deviceId)
                _owner.RequestReattach("当前采集设备被移除");
        }

        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }
}
