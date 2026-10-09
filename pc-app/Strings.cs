using System.Globalization;

namespace pc_app;

/// <summary>
/// 界面文案（中/英），跟随系统语言自动切换 —— 不提供手动选项。
/// <para>
/// 判定方式：读 Windows 的**首选 UI 语言**（<see cref="CultureInfo.CurrentUICulture"/>，
/// 非打包应用下它就等于系统显示语言）。以 <c>zh</c> 开头用中文，其余一律英文。
/// 与手机端一致 —— 手机端读 <c>navigator.language</c> 做同样的判断，
/// 所以两端会同时是中文或同时是英文，不会一个中文一个英文。
/// </para>
/// <para>
/// 注意：<strong>只翻译界面上看得见的东西</strong>。写进 <c>app.log</c> 的调试日志保持中文不动 ——
/// 它面向开发者排障，翻译它只会让日志检索变难，没有收益。
/// </para>
/// </summary>
public static class Strings
{
    /// <summary>true 表示中文界面。</summary>
    public static bool IsChinese { get; } = DetectChinese();

    private static bool DetectChinese()
    {
        // 验证用：LANSOUND_LANG_OVERRIDE=zh|en 可强制语言（不对外暴露，仅排障/验证用）。
        var forced = Environment.GetEnvironmentVariable("LANSOUND_LANG_OVERRIDE");
        if (!string.IsNullOrWhiteSpace(forced))
        {
            var zh = forced.Trim().StartsWith("zh", StringComparison.OrdinalIgnoreCase);
            Debug($"LANSOUND_LANG_OVERRIDE={forced} → {(zh ? "zh" : "en")}");
            return zh;
        }

        string source;
        try
        {
            // 读 Windows 的首选 UI 语言（例如 "zh-Hans-CN" / "en-US"）。
            // 非打包应用下 CultureInfo.CurrentUICulture 就等于系统显示语言，不必再查
            // Windows.Globalization.ApplicationLanguages —— 那要求 Windows App SDK 已初始化，
            // 早期调用会抛异常，反而把判断变成"总是中文"。
            source = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        }
        catch
        {
            source = "zh";   // 拿不到就按中文（本机开发环境是中文，保持原有行为）
        }

        var isZh = source.Equals("zh", StringComparison.OrdinalIgnoreCase);
        Debug($"CurrentUICulture={CultureInfo.CurrentUICulture.Name} → ui={source} → {(isZh ? "zh" : "en")}");
        return isZh;
    }

    /// <summary>仅在显式设了 LANSOUND_LANG_DEBUG=1 时输出，用于验证语言适配；平时保持安静。</summary>
    private static void Debug(string message)
    {
        if (Environment.GetEnvironmentVariable("LANSOUND_LANG_DEBUG") == "1")
            Console.WriteLine("[lang] " + message);
    }

    private static string T(string zh, string en) => IsChinese ? zh : en;

    // ---- 窗口标题 ----
    public static string WindowTitle => T("澜声 LanSound - 电脑声音传到手机",
                                          "LanSound - Play PC audio on your phone");

    // ---- 状态行 ----
    public static string StatusStarting => T("正在启动信令服务…", "Starting service…");
    public static string StatusWaitScan => T("请用手机扫码连接（同一局域网）", "Scan the QR code with your phone (same Wi-Fi)");
    public static string StatusConnected => T("手机已连接", "Phone connected");
    public static string StatusDisconnected => T("手机已断开，等待自动重连", "Phone disconnected, waiting to reconnect");
    public static string StatusPortChanged => T("端口已切换，请让手机重新扫码", "Port changed — scan the QR code again");
    public static string StatusSwitchingPort => T("正在切换端口…", "Changing port…");

    // ---- 卡片标题 ----
    public static string CardQr => T("扫码连接", "Scan to connect");
    public static string CardSettings => T("设置", "Settings");
    public static string CardLog => T("运行日志", "Log");

    // ---- 二维码下方提示 ----
    public static string CertHint => T("证书警告页点“继续访问”即可", "On the certificate warning, tap \"Continue\"");

    // ---- 设置 ----
    public static string LabelQuality => T("音质", "Quality");
    public static string ModeHighQuality => T("高音质", "High quality");
    public static string ModeLowLatency => T("低延迟", "Low latency");
    public static string HintHighQuality => T("高音质：抖动缓冲 200ms，优先不断音、不丢采样",
                                             "High quality: 200 ms jitter buffer — prioritises uninterrupted, lossless playback");
    public static string HintLowLatency => T("低延迟：抖动缓冲 60ms，响应更快，但网络一抖更易断续",
                                             "Low latency: 60 ms jitter buffer — snappier, but dropouts are more likely on a flaky network");
    public static string TipHighQuality => T("抖动缓冲 200ms，优先不断音、不丢采样",
                                             "200 ms jitter buffer; prioritises no dropouts and no lost samples");
    public static string TipLowLatency => T("抖动缓冲 60ms，响应更快，但网络一抖更易断续",
                                            "60 ms jitter buffer; snappier, but more prone to dropouts");

    public static string AutoStartLabel => T("开机自动启动", "Start with Windows");
    public static string TipAutoStart => T("登录 Windows 后自动运行，手机随时能连。不需要管理员权限。",
                                           "Runs after you sign in, so your phone can always connect. No admin rights needed.");
    public static string AutoStartOn(string path) => T($"登录 Windows 后自动运行：{path}",
                                                       $"Runs after you sign in: {path}");
    public static string AutoStartOff => T("关闭时需手动启动。开启后登录 Windows 即自动运行，手机随时能连。",
                                           "Off: start it manually. On: it runs after you sign in, so your phone can always connect.");

    // ---- 访问地址 ----
    public static string TipIpCombo => T("选择手机可达的本机网卡 IP（多网卡时请手动切换）",
                                         "Pick the PC's IP that your phone can reach (switch manually on multi-NIC machines)");
    public static string TipPort => T("服务端口（1024-65535），改完自动生效。改端口后手机要重新扫码。",
                                      "Service port (1024-65535); applies immediately. Rescan the QR code after changing it.");

    // ---- 提示条 ----
    public static string InfoBarTitle => T("请允许防火墙放行；若手机网页加载过久，请重新开关 WiFi",
                                           "Allow this app through the firewall. If the page loads slowly, toggle Wi-Fi off and on");

    // ---- 端口切换确认对话框 ----
    public static string DialogPortTitle => T("切换服务端口", "Change service port");
    public static string DialogPortBody(int from, int to) =>
        T($"将把服务端口从 {from} 改为 {to}。\n\n内置服务会重启，手机上的连接会断开，需要重新扫码。\n确定继续吗？",
          $"The service port will change from {from} to {to}.\n\nThe built-in service restarts, the phone's connection drops, and you'll need to scan the QR code again.\nContinue?");
    public static string DialogConfirm => T("确定切换", "Change port");
    public static string DialogCancel => T("取消", "Cancel");

    // ---- 界面日志（MainWindow 自己产生的那部分）----
    public static string LogPortChanged(int port) => T($"服务端口已切换为 {port}，请让手机重新扫码",
                                                      $"Service port changed to {port} — scan the QR code again");
    public static string LogAudioMode(bool lowLatency) => lowLatency
        ? T("已切换到低延迟模式（抖动缓冲更小）", "Switched to low-latency mode (smaller jitter buffer)")
        : T("已切换到高音质模式（优先不断音）", "Switched to high-quality mode (prioritises no dropouts)");
    public static string LogAutoStartFailed => T("开机自启设置失败，可能被系统策略限制",
                                                 "Could not change the start-with-Windows setting (possibly blocked by policy)");
    public static string LogAutoStartOn => T("已开启开机自启（登录后自动运行）",
                                             "Start with Windows enabled (runs after sign-in)");
    public static string LogAutoStartOff => T("已关闭开机自启", "Start with Windows disabled");

    // ---- 服务端日志（会显示在界面的运行日志里）----
    public static string LogServerStarted(string scheme, int port, int certPort) =>
        T($"信令服务已启动：{scheme}://0.0.0.0:{port}（根证书下载：http://0.0.0.0:{certPort}/ca.crt）",
          $"Service started: {scheme}://0.0.0.0:{port} (root certificate: http://0.0.0.0:{certPort}/ca.crt)");
    public static string LogStopError(string message) => T($"停止服务时出错（继续重启）：{message}",
                                                           $"Error while stopping the service (continuing with restart): {message}");
    public static string LogPhoneWebMissing => T("警告：未找到 phone-web 目录，手机页面将无法加载",
                                                 "Warning: phone-web folder not found; the phone page will not load");
    public static string LogReplacedOldSession => T("新手机接入，断开上一会话", "New phone connected; closing the previous session");
    public static string LogPhoneConnected => T("手机已连接", "Phone connected");
    public static string LogPhoneDisconnected => T("手机已断开", "Phone disconnected");
    public static string LogSessionError(string message) => T($"会话异常：{message}", $"Session error: {message}");
    public static string LogBackpressureOn => T("下行数据积压，已开始丢弃音频帧（手机侧网络变慢）",
                                                "Downlink backlogged; dropping audio frames (the phone's network is slow)");
    public static string LogBackpressureOff => T("下行数据积压已恢复", "Downlink backlog cleared");
    public static string LogSendFailed(string message) => T($"下行 PCM 发送失败：{message}", $"Failed to send downlink PCM: {message}");
    public static string LogIdleTimeout(int seconds) => T($"连接空闲超过 {seconds} 秒，自动断开",
                                                          $"Connection idle for over {seconds}s; closing it");

    // ---- 音频采集（会显示在界面日志里）----
    public static string LogCaptureStarted(string device, string format) =>
        T($"已开始采集系统声音：{device}（{format} → 48kHz 立体声下行）",
          $"Capturing system audio: {device} ({format} → 48 kHz stereo downlink)");
    public static string LogCaptureReattached(string device, string format) =>
        T($"重新挂载采集：{device}（{format}）", $"Recapture attached: {device} ({format})");
    public static string LogDeviceChanged(string reason) =>
        T($"检测到音频设备变化（{reason}），重新挂载采集", $"Audio device changed ({reason}); reattaching capture");
    public static string ReasonDeviceSwitched => T("系统默认输出设备改变", "default output device changed");
    public static string ReasonDeviceInvalid => T("当前采集设备失效", "current capture device became invalid");
    public static string ReasonDeviceRemoved => T("当前采集设备被移除", "current capture device was removed");
    public static string ReasonCaptureStopped => T("采集已停止", "capture stopped");
    public static string ReasonStartup => T("启动采集", "starting capture");
    public static string ReasonDeviceChangedByPoll => T("默认设备已变化", "default device changed");
    public static string LogCaptureSilent(int seconds) => T($"采集已静默 {seconds} 秒", $"capture silent for {seconds}s");
    public static string LogCaptureNotRunning => T("采集未运行，尝试恢复", "capture not running; trying to recover");
    public static string LogAttachFailed(string message) => T($"接入默认播放设备失败：{message}",
                                                              $"Could not attach to the default playback device: {message}");
    public static string LogFormatUnsupported(int rate, string encoding) =>
        T($"扬声器采集格式不支持（{rate}Hz/{encoding}），无法回传",
          $"Unsupported capture format ({rate}Hz/{encoding}); cannot stream");
    public static string LogCaptureInterrupted(string message) => T($"系统声音采集中断：{message}",
                                                                    $"System audio capture interrupted: {message}");
    public static string LogConvertFailed(string message) => T($"音频格式转换失败：{message}",
                                                               $"Audio format conversion failed: {message}");
    public static string LogReattachError(string message) => T($"重挂采集异常：{message}", $"Recapture error: {message}");
    public static string LogModeSwitched(string mode) => T($"音频档位已切换为 {mode}", $"Audio mode switched to {mode}");
    public static string ModeDescLowLatency => T("低延迟（手机端抖动缓冲更小）", "low latency (smaller jitter buffer)");
    public static string ModeDescHighQuality => T("高音质（缓冲给足，优先不断音）", "high quality (bigger buffer, prioritises no dropouts)");
    public static string LogResamplingNote(int rate, int channels) =>
        T($"{rate}Hz {channels}ch（重采样为 48000Hz）",
          $"{rate}Hz {channels}ch (resampled to 48000Hz)");

    // ---- 证书 ----
    public static string LogCertIssued(string san) => T($"签发新的服务器证书（SAN: {san}）",
                                                        $"Issued a new server certificate (SAN: {san})");
    public static string LogCaCreated => T("首次启动：生成本地根 CA（LanSound Local Root CA）",
                                           "First run: created the local root CA (LanSound Local Root CA)");
}
