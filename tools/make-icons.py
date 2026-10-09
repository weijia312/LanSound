"""从一张主图生成项目里全部图标资源。

主图：深色圆角方 + 星空屏 + 右下扬声器单元（1024x1024，四角透明）。

用法：
    python tools/make-icons.py [主图路径]

默认主图是 tools/icon-master.webp（**无损 WebP**，保留完整渐变与星点）。
输出（覆盖）：
    pc-app/Assets/LanSound.ico      应用/窗口/任务栏图标（7 档）
    phone-web/favicon.ico           浏览器标签图标（4 档）
    phone-web/apple-touch-icon.png  180x180
    phone-web/album-art.png         512x512（Media Session 封面）

两个必须注意的点（都实际踩过）：

1. **主图绝不能存成调色板模式（P 模式）**。原图是平滑渐变 + 密集星点，
   量化到 256 色后只剩 71 色，背景出现明显色带、星点全部消失。
   本脚本会检查模式，遇到 P 模式直接报错退出，避免悄悄生成劣化图标。
2. **缩小时用多级 box 降采样**。单次 LANCZOS 在 4:1、16:1 这种大比例缩放下
   会丢掉高频细节（星点、扬声器纹理）；分步取平均能保住。

iOS 的 apple-touch-icon **不支持透明**（透明区会被填成黑色），
所以那两份要合成到实底上；其余保留透明圆角。
"""
import os
import sys

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.join(HERE, "..")
PC_ASSETS = os.path.join(ROOT, "pc-app", "Assets")
PHONE = os.path.join(ROOT, "phone-web")

# 默认用仓库内的无损主图，也可命令行指定别的
MASTER = sys.argv[1] if len(sys.argv) > 1 else os.path.join(HERE, "icon-master.webp")

ICO_SIZES = [256, 128, 64, 48, 32, 24, 16]
FAVICON_SIZES = [64, 48, 32, 16]
TOUCH_SIZE = 180
ALBUM_SIZE = 512
# apple-touch-icon / 封面的实底：取圆角外的邻近色，视觉上最自然
FLATTEN_BG = (24, 24, 26)


def load_master(path):
    im = Image.open(path)
    if im.mode in ("P", "PA"):
        raise SystemExit(
            "主图是调色板模式（只用了很少的颜色），会导致渐变色带、星点丢失。\n"
            "请换用无损副本（PNG 或 lossless WebP）：" + path)
    return im.convert("RGBA")


def render(master, size, sharpen=True):
    """缩放到目标尺寸。

    实测对比（1024 → 256，用"相邻像素平均差"粗略衡量细节保留）：
        直接 LANCZOS            2.280
        先逐级 box 降到 256     2.235   ← 反而更差，多余的一步
        直接 LANCZOS + 轻锐化   2.859   ← 采用这个
    1024→256 只有 4:1，一次 LANCZOS 就足够；多加中间步反而多损失一次。
    锐化的半径随尺寸收窄，避免小图上出现光晕。
    """
    if master.size == (size, size):
        return master
    out = master.resize((size, size), Image.LANCZOS)
    if sharpen and size < master.width:
        from PIL import ImageFilter
        radius = 0.6 if size <= 32 else (0.8 if size <= 64 else 1.1)
        out = out.filter(ImageFilter.UnsharpMask(radius=radius, percent=55, threshold=2))
    return out


def save_ico(master, path, sizes):
    frames = [render(master, s) for s in sizes]
    frames[0].save(path, format="ICO", sizes=[(s, s) for s in sizes])
    return os.path.getsize(path)


def save_png(master, path, size, flatten=None):
    im = render(master, size, sharpen=False)
    if flatten is not None:
        bg = Image.new("RGBA", im.size, flatten + (255,))
        bg.alpha_composite(im)
        im = bg.convert("RGB")
    im.save(path, format="PNG", optimize=True)
    return os.path.getsize(path)


def main():
    if not os.path.isfile(MASTER):
        print(__doc__)
        print("找不到主图：" + MASTER)
        return 1

    master = load_master(MASTER)
    cols = master.getcolors(maxcolors=1 << 24)
    print("主图: " + MASTER)
    print("  %s  独立颜色数=%s" % (master.size, len(cols) if cols else ">16M"))

    os.makedirs(PC_ASSETS, exist_ok=True)

    n = save_ico(master, os.path.join(PC_ASSETS, "LanSound.ico"), ICO_SIZES)
    print("  pc-app/Assets/LanSound.ico      %s 字节  %s" % (format(n, ","), ICO_SIZES))

    n = save_ico(master, os.path.join(PHONE, "favicon.ico"), FAVICON_SIZES)
    print("  phone-web/favicon.ico           %s 字节  %s" % (format(n, ","), FAVICON_SIZES))

    n = save_png(master, os.path.join(PHONE, "apple-touch-icon.png"), TOUCH_SIZE,
                 flatten=FLATTEN_BG)
    print("  phone-web/apple-touch-icon.png  %s 字节  %dx%d（实底）"
          % (format(n, ","), TOUCH_SIZE, TOUCH_SIZE))

    n = save_png(master, os.path.join(PHONE, "album-art.png"), ALBUM_SIZE,
                 flatten=FLATTEN_BG)
    print("  phone-web/album-art.png         %s 字节  %dx%d（实底）"
          % (format(n, ","), ALBUM_SIZE, ALBUM_SIZE))

    return 0


if __name__ == "__main__":
    sys.exit(main())
