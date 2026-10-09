"""计算 #EEACB2 底色与 WinUI 默认文字/卡片色的 WCAG 对比度，判断是否需要换文字色。"""


def srgb_to_lin(c):
    c /= 255.0
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def rel_lum(hex_color):
    h = hex_color.lstrip('#')
    r, g, b = (int(h[i:i + 2], 16) for i in (0, 2, 4))
    return 0.2126 * srgb_to_lin(r) + 0.7152 * srgb_to_lin(g) + 0.0722 * srgb_to_lin(b)


def contrast(a, b):
    la, lb = rel_lum(a), rel_lum(b)
    hi, lo = max(la, lb), min(la, lb)
    return (hi + 0.05) / (lo + 0.05)


BG = '#EEACB2'   # 新的未连接底色

# WinUI 默认（浅色主题）与深色主题的常用文字色，这里取典型值
candidates = {
    '浅色主题 主文字 #E4000000 近似 #1A1A1A': '#1A1A1A',
    '浅色主题 次文字 #9E000000 近似 #5D5D5D': '#5D5D5D',
    '深色主题 主文字 #FFFFFF': '#FFFFFF',
    '深色主题 次文字 #C5FFFFFF 近似 #B0B0B0': '#B0B0B0',
    '纯白 #FFFFFF': '#FFFFFF',
    '深棕 #3B2A2E': '#3B2A2E',
    '近黑 #201014': '#201014',
}

print(f'底色 = {BG}   相对亮度 = {rel_lum(BG):.4f}\n')
print(f'{"文字色":<44} {"对比度":>7}  判定（正文需 >=4.5，大字需 >=3.0）')
print('-' * 82)
for name, color in candidates.items():
    cr = contrast(BG, color)
    if cr >= 7:
        verdict = '优秀'
    elif cr >= 4.5:
        verdict = '合格（正文可用）'
    elif cr >= 3:
        verdict = '仅大字可用'
    else:
        verdict = '不合格'
    print(f'{name:<44} {cr:>6.2f}:1  {verdict}')

print()
# 已连接状态的绿色底做对照
GREEN = '#13A10E'
print(f'对照：已连接底色 {GREEN}')
for name, color in [('白字', '#FFFFFF'), ('近黑字', '#201014')]:
    print(f'  {name:<8} {contrast(GREEN, color):>5.2f}:1')
