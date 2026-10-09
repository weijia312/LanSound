using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using pc_app.Pairing;

namespace pc_app.Signaling;

/// <summary>
/// 内嵌 Kestrel 信令服务：
///  - 7443 端口：https/wss（自建 CA 签发的服务器证书），托管 phone-web + /signal
///  - 7444 端口：仅 http，用于手机下载根证书 /ca.crt（未信任证书前 https 打不开）
/// 消息协议见 docs/PROTOCOL.md。
/// </summary>
public sealed class SignalServer
{
    /// <summary>默认服务端口；实际端口可由用户自定义（见 <see cref="Port"/>）。</summary>
    public const int DefaultPort = 7443;

    /// <summary>根证书下载端口。固定不变：证书按 IP 签发，换端口无需重签，手机也只需认一个地址。</summary>
    public const int CertDownloadPort = 7444;

    /// <summary>当前实际使用的服务端口（托管页面 + 信令 + 下行 PCM）。</summary>
    public int Port { get; private set; } = DefaultPort;

    private WebApplication? _app;
    private Task? _runTask;

    /// <summary>连接空闲超过该时长无消息自动断开（协议要求）。</summary>
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(30);

    /// <summary>单条信令消息上限。手机只会发很短的小 JSON，超过即视为异常。</summary>
    private const int MaxMessageBytes = 8 * 1024;

    /// <summary>
    /// 下行 PCM 的排队上限（按帧计）。WASAPI 每 20ms 触发一帧，
    /// 一旦手机侧网络变慢，发送会在这里排队；超过上限就丢弃，避免延迟无限增长。
    /// <para>
    /// 低延迟档收得更紧：宁可丢几帧也不让延迟堆起来；音质档放宽，优先不丢采样。
    /// </para>
    /// </summary>
    private static int MaxQueuedFrames => AppConfig.AudioMode == AudioMode.LowLatency ? 8 : 25;

    /// <summary>自测/排障开关：置 1 时 7443 走明文 HTTP，便于脚本直连验证，无需处理自签证书。</summary>
    private static bool PlainHttp => Environment.GetEnvironmentVariable("LANMIC_NO_HTTPS") == "1";

    /// <summary>当前手机会话（同一时间只服务一台手机），供下发音频档位用。</summary>
    private AudioStreamSession? _session;

    /// <summary>当前会话的 WebSocket：新手机接入时用来主动顶掉旧连接。</summary>
    private WebSocket? _currentWs;

    /// <summary>日志事件（在工作线程触发，UI 需自行 Dispatcher 切换）。</summary>
    public event Action<string>? Log;

    /// <summary>按指定端口启动服务。</summary>
    public async Task StartAsync(int port, CancellationToken stopping)
    {
        Port = port;

        // 按当前全部网卡 IP 签发服务器证书（换网卡/切下拉框无需重启）
        var serverCert = CertManager.EnsureServerCertificate(QrPairing.GetLanIps(), OnLog);

        var builder = WebApplication.CreateBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            if (PlainHttp)
                kestrel.ListenAnyIP(Port);
            else
                kestrel.ListenAnyIP(Port, o => o.UseHttps(serverCert));
            kestrel.ListenAnyIP(CertDownloadPort); // http：证书下载 + 免证书入口
        });

        var app = builder.Build();

        MountPhoneWeb(app);

        // 根证书下载（手机浏览器直接打开会触发系统证书安装）
        app.MapGet("/ca.crt", () =>
        {
            if (!File.Exists(CertManager.CaCrtPath)) return Results.NotFound();
            return Results.File(File.ReadAllBytes(CertManager.CaCrtPath),
                "application/x-x509-ca-cert", "lanmic-root-ca.crt");
        });

        app.UseWebSockets(new WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromSeconds(15)
        });
        app.Map("/signal", HandleSignalAsync);

        // StartAsync 等到"监听就绪"即返回（端口被占会在这里抛异常），
        // 绝不能 await 运行任务本身 —— 那会让 ChangePortAsync 永远等不到返回，
        // 端口不落盘、状态栏永远停在"正在切换端口…"。
        _app = app;
        await app.StartAsync(stopping);
        _runTask = app.WaitForShutdownAsync();

        var scheme = PlainHttp ? "http" : "https";
        OnLog(Strings.LogServerStarted(scheme, Port, CertDownloadPort));
    }

    /// <summary>停止服务，供切换端口时重启使用。可重复调用。</summary>
    public async Task StopAsync()
    {
        var app = _app;
        var run = _runTask;
        _app = null;
        _runTask = null;
        if (app == null) return;

        try
        {
            await app.StopAsync(TimeSpan.FromSeconds(5));
            if (run != null) await run.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception ex)
        {
            OnLog(Strings.LogStopError(ex.Message));
        }
        finally
        {
            try { await app.DisposeAsync(); } catch { }
        }
    }

    /// <summary>换端口重启：先停旧实例，再用新端口启动。</summary>
    public async Task RestartAsync(int port, CancellationToken stopping)
    {
        await StopAsync();
        await StartAsync(port, stopping);
    }

    /// <summary>托管手机端页面；找不到目录时只记日志，服务照常启动。</summary>
    private void MountPhoneWeb(WebApplication app)
    {
        var webRoot = FindPhoneWebDir();
        if (webRoot == null)
        {
            OnLog(Strings.LogPhoneWebMissing);
            return;
        }
        var provider = new PhysicalFileProvider(webRoot);
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = provider });
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = provider,
            OnPrepareResponse = ctx =>
            {
                // 手机端页面迭代频繁：每次加载都回源校验，绝不用缓存的旧 JS/CSS
                ctx.Context.Response.Headers.CacheControl = "no-cache";
            }
        });
        OnLog($"手机端页面目录：{webRoot}");
    }

    private async Task HandleSignalAsync(HttpContext ctx)
    {
        if (!ctx.WebSockets.IsWebSocketRequest)
        {
            ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        // 免配对码：局域网内直接连接，手机端断开后可随时自动重连
        using var ws = await ctx.WebSockets.AcceptWebSocketAsync();

        // 一台 PC 同时只服务一台手机：新连接直接顶掉旧连接。
        // 旧连接多是手机切网后残留的半开会话（服务器要等 30 秒空闲超时才发觉），
        // 不主动关掉会白占一个采集实例、还会让界面状态错乱。
        var previous = Interlocked.Exchange(ref _currentWs, ws);
        if (previous != null)
        {
            OnLog(Strings.LogReplacedOldSession);
            await CloseQuietlyAsync(previous, WebSocketCloseStatus.NormalClosure, "replaced");
        }

        OnLog(Strings.LogPhoneConnected);
        try
        {
            await RunSessionAsync(ws, ctx.RequestAborted);
        }
        catch (WebSocketException ex)
        {
            OnLog(Strings.LogSessionError(ex.Message));
        }
        catch (OperationCanceledException)
        {
            // 客户端直接断开，属正常情况
        }
        finally
        {
            // 仅当前连接的退出可以清除当前状态；被新手机替换的旧连接
            // 不能把新连接的状态也清掉或让 UI 误报“手机已断开”。
            if (ReferenceEquals(Interlocked.CompareExchange(ref _currentWs, null, ws), ws))
                OnLog(Strings.LogPhoneDisconnected);
        }
    }

    private async Task RunSessionAsync(WebSocket ws, CancellationToken stopping)
    {
        // 本次手机会话的声音下行
        using var audio = new AudioStreamSession();
        audio.Log += OnLog;
        Interlocked.Exchange(ref _session, audio);

        // WebSocket 同一时刻只允许一个发送操作，而音频帧来自采集线程、信令回应来自本循环
        var sendLock = new SemaphoreSlim(1, 1);
        var queued = 0;
        var dropsLogged = false;

        // 音频档位变化要下发给手机（手机据此调整抖动缓冲策略）
        audio.SendMessage += msg => SendJsonAsync(ws, msg, sendLock, CancellationToken.None);

        audio.SendBinary += async bytes =>
        {
            // 背压：队列已满说明手机侧跟不上，直接丢弃这一帧而不是无限堆积
            if (Volatile.Read(ref queued) >= MaxQueuedFrames)
            {
                if (!dropsLogged)
                {
                    dropsLogged = true;
                    OnLog(Strings.LogBackpressureOn);
                }
                return;
            }

            Interlocked.Increment(ref queued);
            try
            {
                await sendLock.WaitAsync();
                try
                {
                    await ws.SendAsync(bytes, WebSocketMessageType.Binary, true, CancellationToken.None);
                }
                finally { sendLock.Release(); }
            }
            catch (Exception ex)
            {
                OnLog(Strings.LogSendFailed(ex.Message));
            }
            finally
            {
                if (Interlocked.Decrement(ref queued) < MaxQueuedFrames && dropsLogged)
                {
                    dropsLogged = false;
                    OnLog(Strings.LogBackpressureOff);
                }
            }
        };

        try
        {
            await ReceiveLoopAsync(ws, audio, sendLock, stopping);
        }
        finally
        {
            // 结束的旧会话只清除自己的引用，不能覆盖刚接入的新会话。
            Interlocked.CompareExchange(ref _session, null, audio);
        }
    }

    /// <summary>读取手机端发来的信令，直到断开或出错。</summary>
    private async Task ReceiveLoopAsync(WebSocket ws, AudioStreamSession audio, SemaphoreSlim sendLock, CancellationToken stopping)
    {
        var buffer = new byte[MaxMessageBytes];
        var received = new MemoryStream();

        while (ws.State == WebSocketState.Open && !stopping.IsCancellationRequested)
        {
            ValueWebSocketReceiveResult result;
            using (var idleCts = CancellationTokenSource.CreateLinkedTokenSource(stopping))
            {
                idleCts.CancelAfter(IdleTimeout);
                try
                {
                    result = await ws.ReceiveAsync(buffer.AsMemory(), idleCts.Token);
                }
                catch (OperationCanceledException) when (!stopping.IsCancellationRequested)
                {
                    // 下行是 PC 单方推送，手机端每 10 秒发 ping 保活，所以超时即可判定为断开
                    OnLog(Strings.LogIdleTimeout((int)IdleTimeout.TotalSeconds));
                    await CloseQuietlyAsync(ws, WebSocketCloseStatus.NormalClosure, "idle-timeout");
                    return;
                }
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                await CloseQuietlyAsync(ws, WebSocketCloseStatus.NormalClosure, null);
                return;
            }

            // 消息可能被分片，必须累积到 EndOfMessage 才能解析
            received.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage) continue;

            if (received.Length > MaxMessageBytes)
            {
                OnLog($"收到过大的消息（{received.Length} 字节），已断开");
                await CloseQuietlyAsync(ws, WebSocketCloseStatus.MessageTooBig, "message-too-big");
                return;
            }

            var json = Encoding.UTF8.GetString(received.GetBuffer(), 0, (int)received.Length);
            received.SetLength(0);

            if (!await HandleMessageAsync(ws, json, audio, sendLock, stopping))
                return;
        }
    }

    /// <summary>返回 false 表示应结束会话。</summary>
    private async Task<bool> HandleMessageAsync(WebSocket ws, string json, AudioStreamSession audio, SemaphoreSlim sendLock, CancellationToken stopping)
    {
        string? type;
        JsonElement payload;
        try
        {
            using var doc = JsonDocument.Parse(json);
            type = doc.RootElement.GetProperty("type").GetString();
            // Clone 使 payload 脱离 doc 生命周期，doc 释放后仍可使用
            payload = doc.RootElement.TryGetProperty("payload", out var p) ? p.Clone() : default;
        }
        catch (Exception)
        {
            OnLog($"收到无法解析的消息：{json}");
            return true;
        }

        switch (type)
        {
            case "hello":
                var deviceName = payload.ValueKind == JsonValueKind.Object
                    && payload.TryGetProperty("deviceName", out var dn) ? dn.GetString() : "未知设备";
                OnLog($"握手成功：{deviceName}");
                // PC 端回应 hello，携带本机名称
                await SendJsonAsync(ws, new
                {
                    type = "hello",
                    payload = new { deviceName = Environment.MachineName }
                }, sendLock, stopping);
                // 紧接着告知音频档位，手机据此设置抖动缓冲
                await audio.SendCurrentModeAsync();
                return true;

            case "ping": // 手机心跳：刷新空闲计时，并回 pong —— 手机端靠它做失活检测
                await SendJsonAsync(ws, new { type = "pong", payload = new { } }, sendLock, stopping);
                return true;

            case "mode-change":
                // speaker 控制 PC 是否继续发送下行音频
                if (payload.ValueKind == JsonValueKind.Object
                    && payload.TryGetProperty("speaker", out var sp)
                    && sp.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    audio.DownlinkEnabled = sp.GetBoolean();
                    OnLog(sp.GetBoolean() ? "手机已开启电脑声音下行" : "手机已关闭电脑声音下行");
                }
                return true;

            case "bye":
                OnLog("手机主动断开（bye）");
                // 对端可能不应答关闭握手（浏览器直接杀进程等），限时避免阻塞会话清理
                await CloseQuietlyAsync(ws, WebSocketCloseStatus.NormalClosure, "bye");
                return false;

            default:
                OnLog($"收到未知消息类型：{type}");
                return true;
        }
    }

    /// <summary>
    /// 界面上切换音频档位后调用：把新档位下发给当前手机。
    /// 没有会话时是空操作（档位已持久化，下次连接会用新档位）。
    /// </summary>
    public void NotifyAudioMode(AudioMode mode)
    {
        var session = Volatile.Read(ref _session);
        if (session == null) return;
        _ = session.SendCurrentModeAsync();
    }

    /// <summary>加锁发送一条 JSON 信令（与下行二进制帧互斥）。</summary>
    private static async Task SendJsonAsync(WebSocket ws, object message, SemaphoreSlim sendLock, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(message);
        var bytes = Encoding.UTF8.GetBytes(json);
        await sendLock.WaitAsync(ct);
        try
        {
            await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
        }
        finally { sendLock.Release(); }
    }

    /// <summary>关闭连接；对端已消失时不抛异常。</summary>
    private static async Task CloseQuietlyAsync(WebSocket ws, WebSocketCloseStatus status, string? reason)
    {
        try
        {
            await ws.CloseAsync(status, reason, CancellationToken.None)
                .WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch (Exception) { /* 对端不回应关闭握手属正常 */ }
    }

    /// <summary>从程序输出目录向上查找 phone-web 文件夹。</summary>
    private static string? FindPhoneWebDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "phone-web");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    /// <summary>日志出口（工作线程触发）。</summary>
    private void OnLog(string msg)
    {
        if (IsHiddenLog(msg)) return;
        Log?.Invoke($"[{DateTime.Now:HH:mm:ss}] {msg}");
    }

    /// <summary>
    /// 界面运行日志中隐藏的「启动配置类」消息（服务地址、证书路径、页面目录等）。
    /// 这些是给排障看的，正常使用不需要出现在界面上；
    /// 它们仍会原样写入 %AppData%\LanMic\app.log，需要时去那边查。
    /// </summary>
    private static bool IsHiddenLog(string msg) => msg.Contains("信令服务已启动")
        || msg.Contains("根证书下载")
        || msg.Contains("签发新的服务器证书")
        || msg.Contains("手机端页面目录");
}
