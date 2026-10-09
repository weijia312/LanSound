using Microsoft.Win32;

namespace pc_app;

/// <summary>
/// 开机自启：写当前用户注册表 Run 项。
/// <para>
/// 用 <c>HKCU</c> 而不是 <c>HKLM</c>：不需要管理员权限，也不会影响其他用户 ——
/// 这个程序本来就是"随用随开"的个人工具，没有理由要提权。
/// </para>
/// <para>
/// 注意：Run 项只在**用户登录**时触发，不是开机就起。对"回到电脑前手机就能连上"
/// 这个用途来说正合适，也符合用户对"开机自启"的日常预期。
/// </para>
/// </summary>
public static class AutoStart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LanSound";

    /// <summary>取当前程序路径。非打包应用就是 exe 自身的绝对路径。</summary>
    private static string ExePath
    {
        get
        {
            var path = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(path)) return path;
            // 兜底：ProcessPath 在极少数宿主下可能为空
            return Path.Combine(AppContext.BaseDirectory, "LanSound.exe");
        }
    }

    /// <summary>供界面提示用：当前会被写入 Run 项的路径。</summary>
    public static string LaunchCommand => Quote(ExePath);

    /// <summary>路径里可能带空格，必须加引号，否则 Run 项会被拆成命令 + 参数。</summary>
    private static string Quote(string path) => "\"" + path + "\"";

    /// <summary>是否已开启自启，并且指向的是**当前这个 exe**。</summary>
    /// <remarks>
    /// 只判断键存在是不够的：用户可能换了目录（重新解压到别处），
    /// 旧路径还留在注册表里，那样"重新开机起不来"。这里顺带做一次自愈。
    /// </remarks>
    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var current = key?.GetValue(ValueName) as string;
            if (string.IsNullOrWhiteSpace(current)) return false;

            // 路径变了就顺手更新，避免留下指向旧位置的死项
            if (!string.Equals(current, LaunchCommand, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    using var writable = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                    writable?.SetValue(ValueName, LaunchCommand);
                    App.AppLog($"开机自启路径已自动更新：{current} → {LaunchCommand}");
                }
                catch (Exception ex)
                {
                    App.AppLog($"更新开机自启路径失败：{ex.Message}");
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            App.AppLog($"读取开机自启状态失败：{ex.Message}");
            return false;
        }
    }

    /// <summary>开启/关闭自启。返回是否操作成功（失败时不该谎报状态）。</summary>
    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key == null)
            {
                App.AppLog("开机自启设置失败：找不到 Run 注册表项");
                return false;
            }

            if (enabled)
            {
                key.SetValue(ValueName, LaunchCommand, RegistryValueKind.String);
                App.AppLog($"已开启开机自启：{LaunchCommand}");
            }
            else
            {
                // 删不存在的值不会抛异常，直接删即可
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                App.AppLog("已关闭开机自启");
            }
            return true;
        }
        catch (Exception ex)
        {
            App.AppLog($"开机自启设置失败：{ex.Message}");
            return false;
        }
    }
}
