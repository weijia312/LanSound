using pc_app.Audio;

namespace pc_app.Signaling;

/// <summary>
/// 一次手机会话的声音下行：
/// WASAPI 环回采集系统声音 → 48kHz s16le 立体声 20ms 帧（3840 字节，无包头）
/// → 经 WebSocket 二进制帧直发手机，手机用 Web Audio 排队播放。
/// <para>
/// 刻意不走 WebRTC：iOS 的通话渲染通道会把播放混成单声道，而且需要 SDP 协商；
/// 直发裸 PCM 既保住立体声，也省掉整个信令协商流程。
/// </para>
/// </summary>
public sealed class AudioStreamSession : IDisposable
{
    private readonly SpeakerLoopback _loopback;

    /// <summary>扬声器下行开关（手机端 mode-change 控制，不涉及任何重新协商）。</summary>
    public volatile bool DownlinkEnabled = true;

    /// <summary>日志事件（工作线程触发）。</summary>
    public event Action<string>? Log;

    /// <summary>需要经 WebSocket 二进制帧发给手机的下行 PCM。</summary>
    public event Func<byte[], Task>? SendBinary;

    /// <summary>需要发给手机的 JSON 信令（当前只用于下发音频档位）。</summary>
    public event Func<object, Task>? SendMessage;

    public AudioStreamSession()
    {
        // 采集端会监听系统默认播放设备变化并自动重挂，日志经 OnLog 透出
        _loopback = new SpeakerLoopback(OnLog);
        _loopback.FrameReady += OnLoopbackFrame;
        // 会话建立即开始采集系统声音（异步接入，失败由巡检重试）
        _loopback.Start();

        // 档位变化只影响手机端抖动缓冲与 PC 端背压阈值，这里仅记录（下发由 SignalServer 负责）
        AppConfig.AudioModeChanged += OnAudioModeChanged;
    }

    /// <summary>
    /// 把当前音频档位告诉手机端 —— 手机据此选择抖动缓冲策略。
    /// 档位只影响"延迟 vs 抗抖动"的取舍：码流始终是 48kHz 立体声无损 PCM。
    /// </summary>
    public Task SendCurrentModeAsync() => SendMessage?.Invoke(new
    {
        type = "audio-mode",
        payload = new { mode = AppConfig.AudioMode.ToString() }
    }) ?? Task.CompletedTask;

    /// <summary>
    /// 档位变化时本会话要做的处理。
    /// <para>
    /// 只记日志 —— 下发由 <c>App</c> → <c>SignalServer.NotifyAudioMode</c> 这一条路负责，
    /// 这里再发一次会重复下发同一条信令。
    /// </para>
    /// <para>
    /// 也刻意<strong>不</strong>重挂采集：档位只影响手机端的抖动缓冲与 PC 端的背压阈值，
    /// 与 WASAPI 采集参数无关（采集缓冲无法通过公开 API 调整，见 SpeakerLoopback 的说明），
    /// 重挂只会平白打断一次音频。
    /// </para>
    /// </summary>
    private void OnAudioModeChanged(AudioMode mode)
        => OnLog($"音频档位已切换为 {Describe(mode)}");

    private static string Describe(AudioMode mode) => mode == AudioMode.LowLatency
        ? "低延迟（手机端抖动缓冲更小）"
        : "高音质（缓冲给足，优先不断音）";

    /// <summary>环回凑满一帧（20ms 立体声）→ 转小端字节 → 直发手机。</summary>
    /// <remarks>
    /// 采集端复用同一个出帧缓冲（避免每 20ms 产生垃圾），因此必须在返回前
    /// 把数据拷进独立数组再异步发送，否则快速推送时会读到被覆盖的内容。
    /// </remarks>
    private void OnLoopbackFrame(short[] frame)
    {
        if (!DownlinkEnabled) return;
        var handler = SendBinary;
        if (handler == null) return;

        var bytes = new byte[frame.Length * 2];
        Buffer.BlockCopy(frame, 0, bytes, 0, bytes.Length);
        _ = SendSafeAsync(handler, bytes);
    }

    private async Task SendSafeAsync(Func<byte[], Task> handler, byte[] bytes)
    {
        try { await handler(bytes); }
        catch (Exception ex) { OnLog($"下行 PCM 发送失败：{ex.Message}"); }
    }

    public void Dispose()
    {
        AppConfig.AudioModeChanged -= OnAudioModeChanged;
        _loopback.FrameReady -= OnLoopbackFrame;
        _loopback.Dispose();
    }

    private void OnLog(string msg) => Log?.Invoke(msg);
}
