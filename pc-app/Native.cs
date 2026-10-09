using System.Runtime.InteropServices;

namespace pc_app;

/// <summary>
/// WinUI 3 没有 WPF 的 MessageBox。启动早期（窗口尚未建立、拿不到 XamlRoot，
/// 无法使用 ContentDialog）的致命提示改用系统原生对话框，保证一定能弹出来。
/// </summary>
internal static class Native
{
    private const uint MB_OK = 0x00000000;
    private const uint MB_ICONINFORMATION = 0x00000040;
    private const uint MB_ICONERROR = 0x00000010;
    private const uint MB_SETFOREGROUND = 0x00010000;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    /// <summary>信息提示（等价 WPF MessageBox.Information）。</summary>
    public static void MessageBox(string text, string caption)
        => MessageBoxW(IntPtr.Zero, text, caption, MB_OK | MB_ICONINFORMATION | MB_SETFOREGROUND);

    /// <summary>错误提示（等价 WPF MessageBox.Error）。</summary>
    public static void ErrorBox(string text, string caption)
        => MessageBoxW(IntPtr.Zero, text, caption, MB_OK | MB_ICONERROR | MB_SETFOREGROUND);
}
