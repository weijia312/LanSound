using System.Collections.ObjectModel;
using System.IO;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using pc_app.Signaling;

namespace pc_app;

/// <summary>
/// WinUI 3 应用入口。职责与原 WPF 版一致：
/// 单实例保护、启动内嵌信令服务、记录运行/崩溃日志。
/// </summary>
public partial class App : Application
{
    /// <summary>全局信令服务实例。</summary>
    public SignalServer Signal { get; } = new();

    /// <summary>当前应用实例（WinUI 里 Application.Current 返回基类，需强转）。</summary>
    public static App Instance { get; private set; } = null!;

    /// <summary>UI 线程调度器：信令服务在工作线程触发日志，需切回 UI 线程。</summary>
    public DispatcherQueue UIQueue { get; private set; } = null!;

    /// <summary>
    /// 启动日志缓冲。信令服务在窗口创建前就会输出日志（证书签发、Kestrel 启动），
    /// 先攒在这里，窗口构造时再灌进列表，避免早期日志丢失。
    /// </summary>
    public ObservableCollection<string> StartupLog { get; } = new();

    /// <summary>当前服务端口（可由用户在界面上自定义）。</summary>
    /// <summary>当前服务端口（由 AppConfig 持久化）。</summary>
    public int Port { get; private set; } = AppConfig.Port;

    /// <summary>当前音频档位（由 AppConfig 持久化）。</summary>
    public AudioMode AudioMode => AppConfig.AudioMode;

    /// <summary>可取消的启动令牌：切换端口需要停掉旧实例但保持应用运行，故按次新建。</summary>
    private CancellationTokenSource _serverCts = new();

    private Window? _window;
    private readonly CancellationTokenSource _stop = new();
    private Mutex? _singleInstance;
    private bool _ownsMutex;

    private static readonly string AppLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LanMic", "app.log");

    // 用户设置（端口、音频档位）统一放在 AppConfig 里，避免两处状态不同步。

    /// <summary>启动/崩溃日志：排查"窗口没起来、端口没监听"类静默退出。</summary>
    public static void AppLog(string msg)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AppLogPath)!);
            File.AppendAllText(AppLogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}{Environment.NewLine}");
        }
        catch { }
    }

    public App()
    {
        InitializeComponent();
        Instance = this;
        UIQueue = DispatcherQueue.GetForCurrentThread();

        AppLog("=== 应用启动 (WinUI 3 · Windows App SDK 2.5.1) ===");

        // WinUI 3 没有 WPF 的 DispatcherUnhandledException，用 UnhandledException；
        // 它只覆盖 XAML 线程，且默认会终止进程，所以 Handled 保持 false，仅先落盘。
        UnhandledException += (_, args) =>
        {
            AppLog($"!!! UI 线程未处理异常: {args.Exception}");
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            AppLog($"!!! 未处理异常: {args.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLog($"!!! 未观察的任务异常: {args.Exception}");
            args.SetObserved();
        };

        RunSingleInstanceGuard();
    }

    /// <summary>
    /// 单实例保护：第二个实例抢不到端口会变成"窗口开着但服务没起"的僵尸，直接拒绝启动。
    /// 句柄可能因父进程句柄继承而残留（createdNew=False 但无人持有），
    /// 所以再 WaitOne(0) 试取所有权——能取到说明旧实例已死，继续启动。
    /// </summary>
    private void RunSingleInstanceGuard()
    {
        _singleInstance = new Mutex(true, "LanSound.pc-app.single-instance", out var createdNew);
        var acquired = createdNew;
        if (!acquired)
        {
            try { acquired = _singleInstance.WaitOne(0); }
            catch (AbandonedMutexException) { acquired = true; } // 旧持有者被强杀，视为可回收
        }
        _ownsMutex = acquired;
        AppLog($"单实例检查: createdNew={createdNew} acquired={acquired}");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        if (!_ownsMutex)
        {
            // 此时还没有任何窗口，用系统提示框告知后退出
            Native.MessageBox(
                "澜声 LanSound 已经在运行中（可能在任务栏或另一个窗口）。\n请先关闭已运行的实例再启动。",
                "澜声 LanSound");
            Exit();
            return;
        }

        // 用户设置（端口、音频档位）必须在**创建窗口之前**读入：
        // MainWindow 的构造函数会读 AppConfig 来初始化端口框与档位开关，
        // 若放在这之后，界面永远显示的是未加载的默认值（端口看着正常只是因为
        // 默认值恰好和文件里的值一样，属于巧合）。
        AppConfig.Load();
        Port = AppConfig.Port;

        // 运行日志同步落盘，便于远程排障（UI 日志由 MainWindow 订阅显示）
        Signal.Log += msg =>
        {
            AppLog(msg);
            // 窗口还没建好时先缓冲；窗口建好后 MainWindow 自行订阅，这里不再重复入列
            if (_window == null && StartupLog.Count < 200)
                UIQueue.TryEnqueue(() => StartupLog.Add(msg));
        };

        _window = new MainWindow();
        _window.Activate();

        // 档位变化要下发到手机端（改变它的抖动缓冲策略）
        AppConfig.AudioModeChanged += mode => Signal.NotifyAudioMode(mode);

        // Kestrel 在后台线程运行，不阻塞 UI；启动失败（如端口被占）显式弹窗
        Task.Run(() => StartServerAsync(Port));
    }

    /// <summary>启动内置服务；失败时弹窗提示（端口被占用是最常见原因）。</summary>
    private async Task StartServerAsync(int port)
    {
        var cts = new CancellationTokenSource();
        _serverCts = cts;
        try
        {
            await Signal.StartAsync(port, cts.Token);
        }
        catch (Exception ex)
        {
            AppLog($"!!! 信令服务启动失败: {ex}");
            UIQueue.TryEnqueue(() => Native.ErrorBox(
                $"信令服务启动失败：{ex.Message}\n"
                + $"请检查 {port} / {SignalServer.CertDownloadPort} 端口是否被其它程序占用。",
                "澜声 LanSound"));
        }
    }

    /// <summary>
    /// 切换服务端口：停掉旧实例再用新端口启动，并落盘记住。
    /// 返回是否成功（失败时已弹窗说明原因）。
    /// </summary>
    public async Task<bool> ChangePortAsync(int port)
    {
        AppLog($"切换服务端口：{Port} → {port}");
        var cts = new CancellationTokenSource();
        try
        {
            await Signal.RestartAsync(port, cts.Token);
        }
        catch (Exception ex)
        {
            AppLog($"!!! 切换端口失败: {ex}");
            UIQueue.TryEnqueue(() => Native.ErrorBox(
                $"切换到端口 {port} 失败：{ex.Message}\n"
                + $"该端口可能已被占用，请换一个（例如 {port + 1}）。",
                "澜声 LanSound"));
            // 回滚：仍用旧端口把服务拉起来，避免彻底没服务
            try
            {
                var rollback = new CancellationTokenSource();
                _ = Task.Run(() => Signal.StartAsync(Port, rollback.Token));
            }
            catch { }
            return false;
        }

        _serverCts.Cancel();   // 旧令牌作废
        _serverCts = cts;
        Port = port;
        AppConfig.Port = port;
        AppConfig.Save();
        return true;
    }

    /// <summary>由 MainWindow 关闭时回调，停止后台服务并释放单实例锁。</summary>
    public void ShutdownServices()
    {
        AppLog("=== 应用退出 ===");
        try { _serverCts.Cancel(); } catch { }
        // 优雅停服（3 秒兜底）：让进行中的手机会话收到正常关闭帧
        try { Signal.StopAsync().Wait(TimeSpan.FromSeconds(3)); } catch { }
        try { _stop.Cancel(); } catch { }
        if (_ownsMutex)
        {
            try { _singleInstance?.ReleaseMutex(); } catch { }
        }
        _singleInstance?.Dispose();
        _singleInstance = null;
    }
}
