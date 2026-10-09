using System.Text.Json;

namespace pc_app;

/// <summary>
/// 音频传输档位。两档的差别是**延迟与抗抖动的取舍**，不是"音质好坏"：
/// 采集与下行始终是 48kHz 立体声无损 PCM，不存在有损压缩。
/// </summary>
public enum AudioMode
{
    /// <summary>低延迟：压缩采集缓冲与抖动缓冲，代价是网络一抖更容易听出断续。</summary>
    LowLatency,

    /// <summary>高音质：抖动缓冲给足，优先保证不断音、不丢采样。</summary>
    MaxQuality
}

/// <summary>
/// 用户配置，持久化在 <c>%AppData%/LanMic/settings.json</c>。
/// 音频档位存在这里而不是各自的模块里，避免出现两处状态不同步。
/// </summary>
public static class AppConfig
{
    public const int MinPort = 1024;
    public const int MaxPort = 65535;

    /// <summary>
    /// 配置文件路径。<c>LANMIC_DATA_DIR</c> 可覆盖数据目录，
    /// 供自测在隔离目录里跑、不动用户真实配置。
    /// </summary>
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetEnvironmentVariable("LANMIC_DATA_DIR")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LanMic"),
        "settings.json");

    /// <summary>服务端口。</summary>
    public static int Port { get; set; } = Signaling.SignalServer.DefaultPort;

    /// <summary>音频传输档位；改动立即作用于进行中的会话。</summary>
    public static AudioMode AudioMode { get; set; } = AudioMode.MaxQuality;

    /// <summary>档位变化通知（下发到手机端调整播放策略）。</summary>
    public static event Action<AudioMode>? AudioModeChanged;

    /// <summary>切换档位并通知当前会话。</summary>
    public static void SetAudioMode(AudioMode mode)
    {
        if (AudioMode == mode) return;
        AudioMode = mode;
        Save();
        AudioModeChanged?.Invoke(mode);
    }

    private sealed record Persisted(int Port, AudioMode AudioMode);

    /// <summary>
    /// 读取配置；文件缺失或损坏时回落到默认值。
    /// <para>
    /// 容忍 BOM 与首尾空白：记事本、PowerShell 的 <c>Set-Content -Encoding UTF8</c>
    /// 都会写出带 BOM 的 UTF-8，而 <see cref="JsonSerializer"/> 遇到 BOM 会直接抛异常 ——
    /// 那样用户手改配置文件就会"改了没反应"，且只在日志里留一行不易发现的记录。
    /// </para>
    /// </summary>
    public static void Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            var text = StripBom(File.ReadAllText(SettingsPath));
            if (string.IsNullOrWhiteSpace(text)) return;

            var s = JsonSerializer.Deserialize<Persisted>(text);
            if (s == null) return;
            if (s.Port is >= MinPort and <= MaxPort) Port = s.Port;
            AudioMode = s.AudioMode;
        }
        catch (Exception ex)
        {
            App.AppLog($"读取设置失败（用默认值）：{ex.Message}");
        }
    }

    /// <summary>去掉 UTF-8 BOM 与首尾空白。</summary>
    private static string StripBom(string text)
        => text.TrimStart('\uFEFF').Trim();

    /// <summary>写入配置。失败只记日志，不影响运行。</summary>
    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            // 不带 BOM 写入：其它程序读这份文件时不必再处理 BOM
            var json = JsonSerializer.Serialize(new Persisted(Port, AudioMode));
            File.WriteAllText(SettingsPath, json, new System.Text.UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            App.AppLog($"保存设置失败：{ex.Message}");
        }
    }
}
