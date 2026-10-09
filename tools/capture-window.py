"""按进程名截取窗口 —— 用 PrintWindow(PW_RENDERFULLCONTENT)。

为什么不用 PowerShell：SetForegroundWindow 常被系统拒绝，
CopyFromScreen 就会抓到前台别的窗口。这里直接让目标窗口把自己画进位图，不依赖前台状态。

用法：
    python tools/capture-window.py LanSound out.png [--title-substr 澜声]
"""
import ctypes
import ctypes.wintypes as wt
import sys

user32 = ctypes.windll.user32
gdi32 = ctypes.windll.gdi32

PW_RENDERFULLCONTENT = 0x00000002
SRCCOPY = 0x00CC0020
DIB_RGB_COLORS = 0


class BITMAPINFOHEADER(ctypes.Structure):
    _fields_ = [
        ("biSize", wt.DWORD), ("biWidth", wt.LONG), ("biHeight", wt.LONG),
        ("biPlanes", wt.WORD), ("biBitCount", wt.WORD), ("biCompression", wt.DWORD),
        ("biSizeImage", wt.DWORD), ("biXPelsPerMeter", wt.LONG),
        ("biYPelsPerMeter", wt.LONG), ("biClrUsed", wt.DWORD), ("biClrImportant", wt.DWORD),
    ]


class BITMAPINFO(ctypes.Structure):
    _fields_ = [("bmiHeader", BITMAPINFOHEADER), ("bmiColors", wt.DWORD * 3)]


def find_window(proc_name, title_substr=None):
    """按进程名（可再加标题子串）找工作区窗口，返回 (hwnd, pid, title)。"""
    import subprocess
    hits = []
    cb = ctypes.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)

    def visit(hwnd, _):
        if not user32.IsWindowVisible(hwnd):
            return True
        n = user32.GetWindowTextLengthW(hwnd)
        if n == 0:
            return True
        buf = ctypes.create_unicode_buffer(n + 1)
        user32.GetWindowTextW(hwnd, buf, n + 1)
        title = buf.value
        pid = wt.DWORD()
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
        if title_substr and title_substr not in title:
            return True
        r = wt.RECT()
        user32.GetWindowRect(hwnd, ctypes.byref(r))
        area = max(0, r.right - r.left) * max(0, r.bottom - r.top)
        hits.append((area, hwnd, pid.value, title))
        return True

    user32.EnumWindows(cb(visit), 0)

    # 同名进程可能有多个窗口（含 0x0 的隐藏辅助窗口），取面积最大的那个，
    # 否则容易抓到极小窗口、截出空白图。
    for area, hwnd, pid, title in sorted(hits, key=lambda x: -x[0]):
        try:
            out = subprocess.run(
                ["tasklist", "/FI", f"PID eq {pid}", "/FO", "CSV", "/NH"],
                capture_output=True, text=True, timeout=10).stdout
        except Exception:
            continue
        if proc_name.lower() in out.lower():
            return hwnd, pid, title
    return None


def capture(hwnd, out_path):
    rect = wt.RECT()
    # DwmGetWindowAttribute(9=EXTENDED_FRAME_BOUNDS) 能拿到不含投影的真实边界
    try:
        dwm = ctypes.windll.dwmapi
        if dwm.DwmGetWindowAttribute(hwnd, 9, ctypes.byref(rect), ctypes.sizeof(rect)) != 0:
            raise OSError
    except Exception:
        user32.GetWindowRect(hwnd, ctypes.byref(rect))

    w = rect.right - rect.left
    h = rect.bottom - rect.top
    if w <= 0 or h <= 0:
        raise SystemExit(f"bad window size {w}x{h}")

    hdc = user32.GetWindowDC(hwnd)
    memdc = gdi32.CreateCompatibleDC(hdc)
    bmp = gdi32.CreateCompatibleBitmap(hdc, w, h)
    gdi32.SelectObject(memdc, bmp)

    ok = user32.PrintWindow(hwnd, memdc, PW_RENDERFULLCONTENT)
    if not ok:
        user32.PrintWindow(hwnd, memdc, 0)

    bmi = BITMAPINFO()
    bmi.bmiHeader.biSize = ctypes.sizeof(BITMAPINFOHEADER)
    bmi.bmiHeader.biWidth = w
    bmi.bmiHeader.biHeight = -h        # 负数 = 自上而下
    bmi.bmiHeader.biPlanes = 1
    bmi.bmiHeader.biBitCount = 32
    bmi.bmiHeader.biCompression = 0     # BI_RGB

    buf = ctypes.create_string_buffer(w * h * 4)
    got = gdi32.GetDIBits(memdc, bmp, 0, h, buf, ctypes.byref(bmi), DIB_RGB_COLORS)

    gdi32.DeleteObject(bmp)
    gdi32.DeleteDC(memdc)
    user32.ReleaseDC(hwnd, hdc)

    if got == 0:
        raise SystemExit("GetDIBits failed")

    from PIL import Image
    img = Image.frombuffer("RGBA", (w, h), buf, "raw", "BGRA", 0, 1)
    img.convert("RGB").save(out_path)
    return w, h, ok


def main():
    if len(sys.argv) < 3:
        raise SystemExit(__doc__)
    proc_name, out_path = sys.argv[1], sys.argv[2]
    title_substr = None
    if "--title-substr" in sys.argv:
        title_substr = sys.argv[sys.argv.index("--title-substr") + 1]

    found = find_window(proc_name, title_substr)
    if not found:
        raise SystemExit(f"no window for process '{proc_name}'")
    hwnd, pid, title = found
    w, h, ok = capture(hwnd, out_path)
    print(f"pid   : {pid}")
    print(f"hwnd  : {hwnd}")
    print(f"size  : {w}x{h}   PrintWindow={bool(ok)}")
    print(f"saved : {out_path}")


if __name__ == "__main__":
    main()
