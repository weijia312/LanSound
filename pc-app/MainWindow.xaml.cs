using System.Collections.ObjectModel;
using System.IO;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using pc_app.Pairing;
using Windows.Storage.Streams;
using Windows.UI;

namespace pc_app;

/// <summary>
/// 主窗口（WinUI 3）。负责展示配对二维码、切换网卡 IP、显示信令服务日志。
/// 音频与信令逻辑在 Signaling/ 下，与 UI 框架无关。
/// </summary>
public sealed partial class MainWindow : Window
{
    /// <summary>日志上限：长时间运行避免列表无限增长吃内存。</summary>
    private const int MaxLogItems = 500;

    /// <summary>二维码显示边长（与 XAML 中 Image 的尺寸一致）。</summary>
    private const int QrPixelSize = 168;

    /// <summary>
    /// 窗口尺寸常量，单位是 <strong>有效像素</strong>（XAML 的布局单位）。
    /// 下发前会按窗口 DPI 换算成物理像素 —— MoveAndResize 吃的是物理像素，
    /// 直接把有效像素填进去，在 125% 缩放的机器上窗口会小 20%，内容就装不下了。
    /// <para>
    /// 520x880 是实测标定出来的：内容（状态提示 + 二维码 + IP/URL + 设置卡 + 提示条 + 日志）
    /// 到这个尺寸刚好放满、无裁切也无多余空白。
    /// 设置卡改成逐项<strong>向下排列</strong>后变高了（端口 / 音质 / 开机自启各占一行），
    /// 高度因此从 810 提到 880；再降会把日志卡片挤出窗口。
    /// </para>
    /// </summary>
    private const int WindowWidthDip = 520;
    private const int WindowHeightDip = 880;

    /// <summary>目标宽高比（宽 ÷ 高）。装不下时按比例收拢，而不是只裁高度。</summary>
    private const double WindowAspect = 0.60;

    /// <summary>内容允许的最小有效宽度，再窄会横向裁切设置卡与提示文字。</summary>
    private const int MinWidthDip = 480;


    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);

    /// <summary>
    /// 未连接时的窗口底色：WinUI 默认窗口底色（Light 主题的 SolidBackgroundFillColorBase = #F3F3F3），
    /// 不再用橙色 —— 中性底不会和"已连接的绿"抢视线，也更符合系统原生观感。
    /// 浅底必须配深色字，见 XAML 的 RequestedTheme=Light。
    /// </summary>
    private static readonly SolidColorBrush DisconnectedBrush = new(Color.FromArgb(255, 0xF3, 0xF3, 0xF3));

    /// <summary>手机连上后的窗口底色（绿）。</summary>
    private static readonly SolidColorBrush ConnectedBrush = new(Color.FromArgb(255, 0x0F, 0x5C, 0x0C));

    /// <summary>未连接底色的原始分量，用于同步标题栏（Windows.UI.Color 与 WinUI Color 是两个类型）。</summary>
    private const byte DisconnectedR = 0xF3, DisconnectedG = 0xF3, DisconnectedB = 0xF3;
    /// <summary>已连接底色的原始分量（同上）。</summary>
    private const byte ConnectedR = 0x0F, ConnectedG = 0x5C, ConnectedB = 0x0C;

    private readonly ObservableCollection<string> _log = new();

    /// <summary>程序化改动端口框时置位，避免触发"用户输入"逻辑。</summary>
    private bool _suppressPortEvents;

    private bool _connected;

    public MainWindow()
    {
        InitializeComponent();
        ApplyLanguage();

        ConfigureWindow();
        LogList.ItemsSource = _log;
        HookServices();
        InitPairing();

        InitSettings();
        ApplyConnectionColors();
    }

    /// <summary>
    /// 把界面文案设成当前系统语言（见 <see cref="Strings"/>）。
    /// <para>
    /// 文案全部写在代码里而不是 XAML：语言在运行时才确定，XAML 里写死就没法换。
    /// XAML 只留启动瞬间的占位文本，这里立刻覆盖掉。
    /// </para>
    /// </summary>
    private void ApplyLanguage()
    {
        Title = Strings.WindowTitle;
        SetStatus(Strings.StatusStarting);

        QrHeader.Text = Strings.CardQr;
        CertHintText.Text = Strings.CertHint;
        SettingsHeader.Text = Strings.CardSettings;
        QualityLabel.Text = Strings.LabelQuality;
        LogHeader.Text = Strings.CardLog;
        HintBar.Title = Strings.InfoBarTitle;

        ModeHighQuality.Content = Strings.ModeHighQuality;
        ModeLowLatency.Content = Strings.ModeLowLatency;
        AutoStartCheck.Content = Strings.AutoStartLabel;

        // ToolTip 与无障碍名也要跟着语言走（它们同样会被读屏软件念出来）
        ToolTipService.SetToolTip(IpCombo, Strings.TipIpCombo);
        AutomationProperties.SetName(IpCombo, Strings.TipIpCombo);
        ToolTipService.SetToolTip(PortText, Strings.TipPort);
        AutomationProperties.SetName(PortText, Strings.TipPort);
        ToolTipService.SetToolTip(ModeHighQuality, Strings.TipHighQuality);
        AutomationProperties.SetName(ModeHighQuality, Strings.TipHighQuality);
        ToolTipService.SetToolTip(ModeLowLatency, Strings.TipLowLatency);
        AutomationProperties.SetName(ModeLowLatency, Strings.TipLowLatency);
        ToolTipService.SetToolTip(AutoStartCheck, Strings.TipAutoStart);
        AutomationProperties.SetName(AutoStartCheck, Strings.AutoStartLabel);
        AutomationProperties.SetName(QrImage, Strings.CardQr);
    }

    /// <summary>整窗底色随连接状态变化：未连接 WinUI 默认浅色、已连接深绿。</summary>
    private void ApplyConnectionColors()
    {
        var brush = _connected ? ConnectedBrush : DisconnectedBrush;
        RootGrid.Background = brush;

        // 底色换成深绿后必须把主题一起切到 Dark，否则文字仍是深色 ——
        // 实测深色次要文字在亮绿 #13A10E 上只有 1.77:1，几乎读不出来。
        // 切到 Dark 后控件文字自动变浅（白字在 #0F5C0C 上 8.21:1），
        // 卡片叠加也会走 ThemeDictionaries 的 Dark 分支（改成叠白）。
        RootGrid.RequestedTheme = _connected ? ElementTheme.Dark : ElementTheme.Light;

        // 标题栏跟着一起变色，避免上下一截颜色不一致（不支持的系统上忽略）
        try
        {
            AppWindow.TitleBar.BackgroundColor = _connected
                ? Windows.UI.Color.FromArgb(255, ConnectedR, ConnectedG, ConnectedB)
                : Windows.UI.Color.FromArgb(255, DisconnectedR, DisconnectedG, DisconnectedB);
        }
        catch { }
    }

    /// <summary>尺寸、居中、图标、背景。</summary>
    private void ConfigureWindow()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));

        var area = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Nearest).WorkArea;

        // 布局常量是有效像素，MoveAndResize 要物理像素，这里换算。
        // DPI 拿不到就按 96 处理（等于不缩放），至少不会比目标更小。
        var dpi = GetDpiForWindow(hwnd);
        var scale = dpi == 0 ? 1.0 : dpi / 96.0;

        var (wantW, wantH) = (WindowWidthDip, WindowHeightDip);

        // 工作区能容纳的最大有效尺寸（留 24 有效像素边距）
        const int margin = 24;
        var maxWDip = Math.Max(MinWidthDip, (int)(area.Width / scale) - margin * 2);
        var maxHDip = Math.Max(MinWidthDip, (int)(area.Height / scale) - margin * 2);

        var heightDip = Math.Min(wantH, maxHDip);
        var widthDip = Math.Min(wantW, maxWDip);

        // 保持目标宽高比；但不低于内容最小宽度（再窄会横向裁切）
        widthDip = Math.Max(MinWidthDip, Math.Min(widthDip, (int)(heightDip * WindowAspect)));
        widthDip = Math.Min(widthDip, maxWDip);

        var widthPx = (int)Math.Round(widthDip * scale);
        var heightPx = (int)Math.Round(heightDip * scale);

        appWindow.MoveAndResize(new Windows.Graphics.RectInt32
        {
            X = area.X + Math.Max(0, (area.Width - widthPx) / 2),
            Y = area.Y + Math.Max(0, (area.Height - heightPx) / 2),
            Width = widthPx,
            Height = heightPx
        });
        App.AppLog($"窗口尺寸：{widthDip}x{heightDip} 有效像素（DPI {dpi}，缩放 {scale:0.##}）"
                   + $" → {widthPx}x{heightPx} 物理像素；工作区 {area.Width}x{area.Height}");

        // 窗口/任务栏图标（exe 图标由 csproj 的 ApplicationIcon 负责）
        var icoPath = Path.Combine(AppContext.BaseDirectory, "Assets", "LanSound.ico");
        if (File.Exists(icoPath))
        {
            try { appWindow.SetIcon(icoPath); }
            catch (Exception ex) { App.AppLog($"设置窗口图标失败：{ex.Message}"); }
        }

        // 注意：不使用 Mica —— 窗口底色要按连接状态显示纯色（浅灰/深绿），
        // 而 SystemBackdrop 会让 XAML 背景透出桌面，纯色便显示不出来。

        Closed += (_, _) => App.Instance.ShutdownServices();
    }

    /// <summary>接上信令服务日志（含启动早期已产生的缓冲日志），并恢复连接状态显示。</summary>
    private void HookServices()
    {
        var app = App.Instance;

        // 服务在窗口创建前已输出的日志，先灌进来
        foreach (var line in app.StartupLog)
            AppendLog(line);
        app.StartupLog.Clear();

        app.Signal.Log += OnSignalLog;
    }

    /// <summary>列出所有网卡 IP 供选择，默认选中自动探测结果。</summary>
    private void InitPairing()
    {
        IpCombo.ItemsSource = QrPairing.GetLanIps();
        IpCombo.SelectedIndex = 0; // 触发 SelectionChanged → UpdatePairingDisplay
        if (IpCombo.SelectedIndex < 0)
            UpdatePairingDisplay(); // 没有任何网卡 IP 时兜底
    }

    /// <summary>按当前选中的网卡 IP 刷新 URL 与二维码（免配对码，扫码即连）。</summary>
    private async void UpdatePairingDisplay()
    {
        var ip = IpCombo.SelectedItem as string ?? QrPairing.GetLanIp();
        var url = $"https://{ip}:{App.Instance.Port}/";

        UrlText.Text = url;
        if (!_connected)
            SetStatus(Strings.StatusWaitScan);

        // 每个模块 14px，缩放到 184 显示仍清晰
        await SetQrAsync(QrPairing.GeneratePng(url, pixelsPerModule: 14));
    }

    /// <summary>把二维码 PNG 字节解码进 Image 控件（WinUI 需用 IRandomAccessStream）。</summary>
    private async Task SetQrAsync(byte[] png)
    {
        try
        {
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(png);
                await writer.StoreAsync();
                await writer.FlushAsync();
                writer.DetachStream();
            }
            stream.Seek(0);

            var bitmap = new BitmapImage { DecodePixelWidth = QrPixelSize, DecodePixelHeight = QrPixelSize };
            await bitmap.SetSourceAsync(stream);

            QrImage.Source = bitmap;
        }
        catch (Exception ex)
        {
            App.AppLog($"二维码渲染失败：{ex.Message}");
        }
    }

    private void IpCombo_SelectionChanged(object sender, Microsoft.UI.Xaml.Controls.SelectionChangedEventArgs e)
        => UpdatePairingDisplay();

    /// <summary>信令服务在工作线程触发，需切回 UI 线程更新界面。</summary>
    private void OnSignalLog(string msg)
        => App.Instance.UIQueue.TryEnqueue(() =>
        {
            AppendLog(msg);
            if (msg.Contains(Strings.LogPhoneConnected))
            {
                _connected = true;
                SetStatus(Strings.StatusConnected);
            }
            else if (msg.Contains(Strings.LogPhoneDisconnected))
            {
                _connected = false;
                SetStatus(Strings.StatusDisconnected);
            }
            ApplyConnectionColors();
        });

    private void SetStatus(string text) => StatusText.Text = text;

    // ================= 设置：端口 / 音频档位 / 开机自启 =================

    /// <summary>初始化设置区：端口框、音频档位选项、开机自启、说明文字。</summary>
    private void InitSettings()
    {
        _suppressPortEvents = true;
        try
        {
            PortText.Text = App.Instance.Port.ToString();
            // 默认选中高音质；两个 RadioButton 同组，选中一个会自然取消另一个
            ModeHighQuality.IsChecked = App.Instance.AudioMode == AudioMode.MaxQuality;
            ModeLowLatency.IsChecked = App.Instance.AudioMode == AudioMode.LowLatency;
            // 自启状态以注册表为准（用户可能在别处改过）
            AutoStartCheck.IsChecked = AutoStart.IsEnabled();
        }
        finally
        {
            _suppressPortEvents = false;
        }

        PortText.TextChanged += PortText_TextChanged;
        UpdateAudioModeHint();
        UpdateAutoStartHint();
    }

    /// <summary>开机自启勾选变化：写注册表。失败时把勾选状态回滚，不谎报。</summary>
    private void AutoStart_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressPortEvents) return;

        var want = AutoStartCheck.IsChecked == true;
        if (!AutoStart.SetEnabled(want))
        {
            // 写不进去（组策略限制等）就把界面改回去，避免显示"已开启"但实际没生效
            _suppressPortEvents = true;
            try { AutoStartCheck.IsChecked = !want; }
            finally { _suppressPortEvents = false; }

            AppendLog(Strings.LogAutoStartFailed);
            UpdateAutoStartHint();
            return;
        }

        AppendLog(want ? Strings.LogAutoStartOn : Strings.LogAutoStartOff);
        UpdateAutoStartHint();
    }

    /// <summary>自启说明：把真实写入的路径显示出来，便于排查"为什么没起来"。</summary>
    private void UpdateAutoStartHint()
    {
        AutoStartHint.Text = AutoStartCheck.IsChecked == true
            ? Strings.AutoStartOn(AutoStart.LaunchCommand)
            : Strings.AutoStartOff;
    }

    /// <summary>端口框直接生效（不需要按钮）：内容合法且与当前不同时切换。</summary>
    private async void PortText_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressPortEvents) return;

        var text = PortText.Text.Trim();
        if (!int.TryParse(text, out var port) || port < AppConfig.MinPort || port > AppConfig.MaxPort)
        {
            // 输入到一半（例如清空重打）不打扰用户，等它成为合法值再说
            return;
        }
        if (port == App.Instance.Port) return;

        var confirmed = await ShowDialogAsync(Strings.DialogPortTitle,
            Strings.DialogPortBody(App.Instance.Port, port),
            Strings.DialogConfirm, Strings.DialogCancel);
        if (!confirmed)
        {
            _suppressPortEvents = true;
            PortText.Text = App.Instance.Port.ToString();
            _suppressPortEvents = false;
            return;
        }

        SetStatus(Strings.StatusSwitchingPort);
        if (!await App.Instance.ChangePortAsync(port))
        {
            // 失败原因已由 App 弹窗说明，这里回显旧端口
            _suppressPortEvents = true;
            PortText.Text = App.Instance.Port.ToString();
            _suppressPortEvents = false;
            SetStatus(_connected ? Strings.StatusConnected : Strings.StatusWaitScan);
            return;
        }

        UpdatePairingDisplay();
        AppendLog(Strings.LogPortChanged(port));
        SetStatus(Strings.StatusPortChanged);
    }

    /// <summary>切换音频档位：立即作用于进行中的会话，并落盘记住。</summary>
    /// <remarks>
    /// 两个 RadioButton 共用这个处理器，靠 <c>GroupName</c> 互斥。
    /// 初始化时程序化设置 IsChecked 也会触发本事件，故用 _suppressPortEvents 挡住。
    /// </remarks>
    private void AudioMode_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppressPortEvents) return;
        var mode = ReferenceEquals(sender, ModeLowLatency)
            ? AudioMode.LowLatency
            : AudioMode.MaxQuality;
        if (mode == App.Instance.AudioMode) return;

        AppConfig.SetAudioMode(mode);
        UpdateAudioModeHint();
        AppendLog(Strings.LogAudioMode(mode == AudioMode.LowLatency));
    }

    /// <summary>档位说明 —— 让用户知道这一档换来的是什么。</summary>
    private void UpdateAudioModeHint()
    {
        AudioModeHint.Text = App.Instance.AudioMode == AudioMode.LowLatency
            ? Strings.HintLowLatency
            : Strings.HintHighQuality;
    }

    /// <summary>显示一个模态对话框，返回用户是否点了主按钮。首参为空则只显示确定。</summary>
    private async Task<bool> ShowDialogAsync(string title, string message,
        string primary = "OK", string? close = null)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            Title = title,
            Content = message,
            PrimaryButtonText = primary,
            CloseButtonText = close ?? string.Empty,
            DefaultButton = close == null ? ContentDialogButton.Primary : ContentDialogButton.Close
        };
        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary;
    }

    private void AppendLog(string msg)
    {
        _log.Add(msg);
        if (_log.Count > MaxLogItems)
            _log.RemoveAt(0);

        // 滚到最新一条（ItemsSource 在构造函数里绑定，此处已可用）
        LogList.ScrollIntoView(msg);
    }
}
