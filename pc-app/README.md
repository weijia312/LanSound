# 澜声 LanSound — PC 端（WinUI 3）

把**电脑声音传到手机**的局域网工具。PC 与手机连同一局域网，手机扫码打开网页即可收听。
本目录是 Windows 桌面端，已从 WPF 迁移到 **WinUI 3 / Windows App SDK**；手机端页面在 [`../phone-web`](../phone-web)。

> 本项目曾支持"手机当电脑麦克风"（收录）功能，该链路及配套的虚拟声卡注入已**整体移除**，
> 现在是纯收音工具。信令里的 `offer`/`answer`/`ice-candidate` 也已废弃。

## 运行要求

| 项目 | 要求 |
|---|---|
| 操作系统 | Windows 10 19041 及以上（Windows 11 推荐） |
| 运行时 | .NET 8 Desktop Runtime + **Windows App Runtime 2.5** |
| 构建 | .NET 8 SDK（**不需要** Visual Studio，也不需要单独安装 Windows SDK） |

> Windows App Runtime 若缺失，装 `WindowsAppRuntimeInstall-x64.exe`，
> 或改用自包含发布（见下文），后者不依赖机器上预装的运行时。

## 构建与运行

```powershell
# 调试构建
dotnet build .\pc-app.csproj -c Debug

# 运行（输出目录）
.\bin\Debug\net8.0-windows10.0.19041.0\LanSound.exe
```

应用是**非打包（unpackaged）**形式：直接双击 exe 即可，无需 MSIX 安装、无需开发者模式。
`phone-web` 目录由程序从 exe 所在目录**向上逐级查找**，所以在仓库里从 `bin` 下直接运行也能托管手机页面。

### 打发布包

```powershell
# 在仓库 tools/ 下执行；产出 dist/LanSound-win-x64
powershell -File tools\pack.ps1
powershell -File tools\pack.ps1 -Rid win-arm64     # ARM64
```

> ⚠️ **不要用 `dotnet publish` 发布这个工程。**
>
> 实测：`dotnet publish` **不会**产出 XAML 的编译资源，缺这三个文件 ——
> `App.xbf`、`MainWindow.xbf`（`LoadComponent` 要加载它们）和 `LanSound.pri`（资源索引）。
> 缺了就是**双击即崩**（`XamlParseException`，退出码 `0xC0000409`），
> 而崩溃点在 `InitializeComponent()`，日志里只留一行看不出所以然的异常。
> 它还会多塞 DirectML / onnxruntime 等本工程用不到的 AI 库。
>
> `pack.ps1` 以 **RID 专用 `build -c Release -r <rid>`** 的输出为准（实测资源齐全、可运行），
> 并在打包前**校验那三个文件存在**，缺了直接报错，而不是产出一个坏包。

发布包内容（62 个文件，约 38.6 MB）：

| 项 | 说明 |
|---|---|
| `LanSound.exe` | 主程序 |
| `App.xbf` / `MainWindow.xbf` / `LanSound.pri` | **必须有**，否则启动崩溃 |
| `Assets/LanSound.ico` | 窗口图标（运行时由 `AppWindow.SetIcon` 加载） |
| `phone-web/` | 手机页面，**必须随包分发**，否则手机打开是 404 |

### 用户机器需要什么

| 依赖 | 是否必需 |
|---|---|
| Windows 10 1809+（内部版本 17763+）或 Windows 11 | 必需 |
| **.NET 8 桌面运行时** | 必需（当前是框架依赖发布） |
| Windows App Runtime | **不需要**单独装 —— `Microsoft.WindowsAppRuntime.Bootstrap.dll` 已随包 |

想让用户什么都不装，可改成自包含：给 `pack.ps1` 里的 `dotnet build` 加
`-p:WindowsAppSDKSelfContained=true`，体积约 +100 MB。

## 使用

1. 启动澜声 LanSound，PC 与手机连同一局域网。
2. 手机扫码打开页面；首次会有证书警告，点「继续访问」。
   （也可先访问 `http://<IP>:7444/ca.crt` 安装根证书，iOS 还需在
   设置 → 通用 → 关于本机 → 证书信任设置 中开启完全信任。）
3. 手机上点一下按钮开始收听。**电脑上播放什么，手机就能听到什么。**
   - 页面上的大电源钮 = 收听开关
   - 旋钮 = 音量
   - 底部「设置」里可改**端口**与**音频档位**（低延迟 / 高音质）
   - 锁屏可用，媒体键也能控制收听开关

### 窗口底色 = 连接状态

| 状态 | 底色 | 主题 | 文字 |
|---|---|---|---|
| 手机未连接 | `#F3F3F3`（WinUI 默认窗口底色） | Light | 深色 |
| 手机已连接 | `#0F5C0C`（WinUI 调色板深绿） | **Dark** | 浅色 |

标题栏同步变色，远处瞟一眼就知道手机连上没有。

**两种状态必须连主题一起切**，只换底色会直接不可读 —— 这是实测踩出来的：

| 组合 | 主文字 | 日志/次要文字 | 结论 |
|---|---|---|---|
| `#13A10E` 亮绿 + 深字 | 5.08:1 ✅ | **1.77:1 ❌** | 最初就是这个，日志几乎看不清 |
| `#13A10E` 亮绿 + 浅字 | — | — | 更糟：亮绿上白字只有 3.42:1 |
| **`#0F5C0C` 深绿 + 浅字** | **8.21:1 ✅** | **4.91:1 ✅** | 采用 |

> 教训：**亮绿是"中间亮度"，深字压不住、浅字也压不住** —— 两条路都不达标。
> 必须先决定用深底浅字还是浅底深字，再去挑具体色值。
> 顺带一提：把亮绿"调深一点"并不能救深字（`#0F7A0B` 时日志掉到 1.10:1），方向就错了。

### 卡片叠加层随主题翻转

卡片要"融入背景但保留区域感"，而两种底色的叠加方向正好相反：

| 主题 | 底色 | 叠加 | 效果 |
|---|---|---|---|
| Light | `#F3F3F3` 浅灰 | `#0D000000`（5% 黑） | 比底色深 12 RGB |
| Dark | `#0F5C0C` 深绿 | `#1AFFFFFF`（10% 白） | 比底色浅 24 RGB |

所以用 `App.xaml` 里的 `ResourceDictionary.ThemeDictionaries` 分主题给，
`RequestedTheme` 一变叠加层自动翻转，不必手工管每个控件。

> 两个编译期坑：
> 1. `ThemeDictionaries` **必须放在 `Application.Resources` 的 `ResourceDictionary` 里**。
>    写在页面的 `<Grid.Resources>` 下会报 `WMC9997 / WMC9999`
>    （页面的 `Resources` 属性不是完整的 `ResourceDictionary`）。
> 2. 不能在 `<Grid.Resources>` 里再包一层 `<ResourceDictionary>` ——
>    同样报 `WMC9999: This Member 'Resources' has more than one item`。

二维码白底保留 `White`：唯一一处刻意不融合的地方，二维码黑块需要足够静区才能被扫到。

> 想换底色时，用 `tools/check-contrast.py` 先核算对比度与卡片可见性。

### 改名说明：澜声 LanSound

显示名与程序集名已从 LanMic 改为**澜声 LanSound**（exe 为 `LanSound.exe`）。
以下三处**刻意保持 LanMic**，改动它们会造成实际损失：

| 保留项 | 原因 |
|---|---|
| 数据目录 `%AppData%\LanMic` | 里面有 `settings.json` 与根 CA。改路径会丢配置，且**手机必须重新导入证书** |
| 证书 CN `LanMic Local Root CA` / `LanMic PC` | 换 CN 等于换一张 CA，已信任旧 CA 的手机会全部掉线 |
| 环境变量 `LANMIC_DATA_DIR` / `LANMIC_NO_HTTPS` | 仅在自测与排障脚本里使用，不属于产品界面 |

如果确实想连这些一起换（会让所有手机需要重新信任证书），改完记得清掉
`%AppData%\LanMic` 并让手机重新扫码 + 重新导入根证书。
### 音频档位：低延迟 / 高音质

两档的**码流完全一样**（48kHz 立体声无损 PCM，20ms 一帧），差别只在"延迟 vs 抗抖动"的取舍：

| | 低延迟 | 高音质 |
|---|---|---|
| 手机端抖动缓冲 | 60ms | 200ms |
| 重排上限（时钟漂移修正） | 250ms | 750ms |
| PC 端背压队列 | 8 帧（≈160ms） | 25 帧（≈500ms） |
| 适用 | 玩游戏、看视频，要跟手 | 听音乐，要不断音不丢采样 |

界面上是**两个并列的单选项**：`○ 高音质　○ 低延迟`，圆点挨着哪个词就是哪个模式，默认高音质。
用单选项而不是 ToggleSwitch，是因为 ToggleSwitch 的标签等于"当前状态"、且只有一侧，
在双档位场景下容易让人误读当前到底选了哪个。切换立即作用于进行中的会话：PC 端改背压阈值，并通过 `audio-mode` 信令让手机端换抖动缓冲档位（手机端会重置排播时间让新档位立刻生效）。结果记在 `settings.json`。

**延迟收益主要来自手机端抖动缓冲**（200ms → 60ms，省约 140ms）。PC 侧采集缓冲原本也想按档位压缩，但
`NAudio` 的 `WasapiLoopbackCapture` 没有暴露 `AudioClient`，公开 API 设不了，用反射改私有字段太脆弱，故未做。

> 抖动缓冲不是越小越好：它要吃掉的正是网络抖动与音频时钟漂移。低延迟档在 WiFi 不稳时更容易出现断续，
> 这是取舍而非缺陷；网络环境差就切回高音质。
### 中英适配：跟随系统语言，无手动选项

PC 端与手机端**各自读自己的环境**，都不提供语言选项：

| 端 | 判定来源 | 中文 | 英文 |
|---|---|---|---|
| PC（WinUI） | `CultureInfo.CurrentUICulture`（= Windows 首选 UI 语言） | 以 `zh` 开头 | 其余一律 |
| 手机（网页） | `navigator.language` / `navigator.languages` | 以 `zh` 开头 | 其余一律 |

两端判定规则一致，所以不会出现"电脑中文、手机英文"。手机端还监听 `languagechange`，
用户在浏览器里改语言不用刷新。

**文案只翻译界面上看得见的**。写进 `app.log` 的调试日志保持中文 ——
它面向排障，翻译只会让日志检索变难。PC 端文案集中在 `Strings.cs`，
XAML 里**不写任何文案**（语言运行时才确定，写死就没法换），启动后由 `ApplyLanguage()` 覆盖。
ToolTip 和无障碍名（`AutomationProperties.Name`）同样走 `Strings` —— 读屏软件也要跟着语言走。

> 踩过的坑：一开始用 `Windows.Globalization.ApplicationLanguages.Languages` 做二级判断，
> 但那要求 Windows App SDK 已初始化，早期调用会抛异常，
> 被 `catch` 吞掉后**回退成"总是中文"** —— 表现为英文机器上界面仍是中文。
> 现在只读 `CultureInfo.CurrentUICulture`，不需要任何初始化。

验证用（不对外暴露）：设 `LANSOUND_LANG_OVERRIDE=zh|en` 强制语言，
`LANSOUND_LANG_DEBUG=1` 打印判定过程。
### 开机自启

设置卡里的「开机自动启动」复选框，勾选后写当前用户的注册表 Run 项：

```
HKCU\Software\Microsoft\Windows\CurrentVersion\Run
  LanSound = "<exe 绝对路径>"
```

几个设计取舍：

- **用 `HKCU` 不用 `HKLM`**：不需要管理员权限，也不影响其他用户。
  这个程序是"随用随开"的个人工具，没有理由要提权。
- **只在用户登录时触发**，不是开机就起 —— 对"回到电脑前手机就能连上"这个用途正合适。
- **路径会自愈**：`IsEnabled()` 不只判断键存在，还会比对路径；
  用户把程序挪到别的目录后，旧路径会在下次启动时自动更新，
  否则会留下一个"指向不存在的 exe"的死项，表现为"开机起不来但设置显示已开启"。
- **写失败不谎报**：写注册表失败（组策略限制等）时勾选状态会回滚并记日志，
  不会出现"界面显示已开启、实际没生效"。
- 路径带空格时必须加引号，否则 Run 项会被拆成"命令 + 参数"两截。

实测（用真实 `AutoStart` 代码跑一遍开→查→关）：注册表写入正确、`IsEnabled` 状态一致、
关闭后无残留。
### 自定义端口

界面上「应用」按钮左边的输入框可改**服务端口**（默认 7443）：

- 取值范围 1024 - 65535；低于 1024 需要管理员权限，程序会拦下并提示
- 改完会**重启内置服务**，手机连接断开、需要重新扫码，所以会先弹确认框
- 选择会记到 `%AppData%\LanMic\settings.json`，下次启动沿用
- 切换失败（端口被占用）会自动回滚到原端口，不会把服务搞没
- `7444` 固定不变：证书按 IP 签发，换端口无需重签，手机也只需认一个根证书地址

端口：`7443` HTTPS/WSS（页面 + 信令 + 下行 PCM）、`7444` HTTP（仅根证书下载）。

## 代码结构

| 路径 | 说明 |
|---|---|
| `App.xaml(.cs)` | 入口：单实例保护、启动信令服务、崩溃日志 |
| `MainWindow.xaml(.cs)` | WinUI 3 界面：二维码、网卡 IP、自定义端口、连接状态配色、运行日志 |
| `Native.cs` | 启动早期的原生提示框（WinUI 没有 WPF 的 MessageBox） |
| `Signaling/SignalServer.cs` | 内嵌 Kestrel：HTTPS/WSS 信令 + 托管手机页面 |
| `Signaling/AudioStreamSession.cs` | 一次会话的声音下行：采集 → 20ms PCM 帧 → WebSocket 二进制帧 |
| `Audio/SpeakerLoopback.cs` | WASAPI 环回采集默认输出设备的声音 |
| `Pairing/CertManager.cs` | 本地根 CA 与服务器证书签发 |
| `Pairing/QrPairing.cs` | 网卡 IP 枚举、二维码 PNG 生成 |
| `Assets/LanSound.ico` | 应用与窗口图标（由 `../tools/make-icons.py` 从主图生成） |

信令消息类型与音频帧格式见 [`../docs/PROTOCOL.md`](../docs/PROTOCOL.md)。

## 自测工具（`../tools/`）

改动音频链路后**至少跑一遍**「判据」那一组 —— 这几个是能真正证伪的，
不像"能出声"那样收 3 帧和收 1500 帧都算通过。

### 判据类（有明确 PASS/FAIL）

| 工具 | 用途 | 怎么跑 |
|---|---|---|
| `probe-stereo` | 三项：样本守恒（斜坡逐样本比对）/ 出帧总量 / 立体声零串扰 | `dotnet run --project tools/probe-stereo -c Release` |
| `probe-pitch` | 非 48kHz 设备是否被正确重采样（音调是否偏移） | `dotnet run --project tools/probe-pitch -c Release` |

### 端到端联调类（需要应用正在运行）

| 工具 | 用途 |
|---|---|
| `play-tone.ps1` | 通过系统默认播放设备放测试音。**不要用 `[console]::Beep`** —— 那走主板蜂鸣器，环回采集抓不到。`-LeftHz/-RightHz` 可给左右声道不同频率 |
| `frame-timing.js` | 帧到达间隔分布，用来判断传输层是否引入额外延迟（如 Nagle 攒包） |
| `watch-frames.js` | 每 2 秒报一次帧率，观察帧流是否中断（会提示切换默认设备） |
| `switch-test.js` | 一边放音一边程序化切换默认设备，测量接收电平，判定"没声"是设备问题还是重挂失败 |
| `mock-phone.js` | 模拟手机端完整走一遍握手 + 收音开关，做整体回归 |

> 上面几个 node 脚本**默认连 `wss`（真实路径）**，设 `PLAIN=1` 才走明文（配合 `LANMIC_NO_HTTPS=1`）。
> **关于判据设计的一条教训**：早期版本的 `probe-stereo` 用 `Thread.Sleep(10)` 模拟
> `DataAvailable` 的实时节奏，并断言"出帧间隔不得超过 45ms"。这在负载高的机器上必然误报 ——
> Windows 定时器精度约 15ms，`Sleep(10)` 超时到 100ms+ 很常见，于是把**喂数抖动**
> 当成了**出帧抖动**。同样地，`frame-timing.js` 一度把"成簇到达"（中位 0ms、p90 60ms）
> 判为抖动大，而帧本来就是发送线程一次调度发多帧。
>
> 现在两个工具的判据都是**与实时调度无关**的量：样本守恒、出帧总量、平均间隔 ≈ 帧时长。
> 写音频判据时要记住：**别把系统调度噪声算到被测代码头上**。


### 诊断类（排查具体故障时用）

| 工具 | 用途 |
|---|---|
| `probe-loopback` | 独立跑一次 WASAPI 环回采集，区分"采集没起来"和"起来了但没数据" |
| `probe-formats` | 枚举所有播放设备，打印 `MixFormat` 与 `capture.WaveFormat` 的真实取值 |
| `probe-switch` | `list` 列出播放设备；`set <序号>` 程序化切换默认设备（走 `IPolicyConfig` COM） |
| `probe-resample` | 重采样质量：时长守恒 / 音调保持 / 幅度无损 |

### 辅助脚本

| 工具 | 用途 |
|---|---|
| `check-contrast.py` | 核算配色对比度（换底色前先跑） |
| `capture-window.py` | 按进程名截窗口（用 `PrintWindow`，绕开 WinUI 合成器抓黑图的坑） |
| `pack.ps1` | **打发布包**（见「构建与运行」；不要用 `dotnet publish`） |
| `make-icons.py` | 从 `tools/icon-master.webp` 生成全部图标（应用 ico / favicon / touch icon / 封面） |
| `measure-phone.py` | 用 CDP 在真实视口下量手机端 DOM 尺寸并截图 —— **判断手机布局问题必须用它**，`--window-size` 截图会误导 |

## 已修的坑（改动原因记录）

### 1. TLS 首次签发必然握手失败

新签发的服务器证书用 `CopyWithPrivateKey` 得到的是**进程内临时密钥**，SChannel 无法用于 TLS ——
表现为服务正常监听、端口正常，但 https 一访问就在握手阶段断开
（`curl: schannel: failed to receive handshake`）。Kestrel 不会报错，只有真机访问才暴露；
换网络触发证书重签时也会复现。

修法：证书落盘后以 `PersistKeySet` 重新载入（`CertManager.ServerKeyFlags`）。
根 CA 只在内存中签名，改用 `EphemeralKeySet`，避免重复持久化导致 `Export` 抛 `CryptographicException`。

### 2. 下行音质的核心取舍：不走 WebRTC

iOS 的通话渲染通道会把播放**混成单声道**。因此下行改为
WASAPI 环回采集 → 48kHz 立体声裸 PCM → **WebSocket 二进制帧** → 手机 Web Audio 播放。
代价是要自己做抖动缓冲与重采样，收益是真立体声 + 可后台/锁屏播放 + 无需 SDP 协商。

### 3. 切换系统音频设备后手机端没声

两个独立的坑，都会表现成"突然没声、且不报任何错"：

**（a）格式校验查错了对象（更要命）**

原代码用 `device.AudioClient.MixFormat` 判断格式，并与 `IeeeFloat` 比较。但设备端的混音格式
常报 `Extensible`（例如蓝牙耳机的 32bit Extensible），永远不等于 `IeeeFloat`，
于是 `TryStart` 直接返回 false —— **静默不采集，连日志都没有**，手机端从头到尾没声音。

正确做法是校验 `capture.WaveFormat`：捕获对象把格式交给 WASAPI 解包后报的才是
`IeeeFloat`，也就是 `DataAvailable` 里真正的数据格式。实测同一台设备：

```
device.AudioClient.MixFormat : 48000Hz 2ch Extensible 32bit   ← 校验这个是错的
capture.WaveFormat           : 48000Hz 2ch IeeeFloat          ← 应该校验这个
```

**（b）采集实例不会跟着默认设备走**

`WasapiLoopbackCapture` 在创建时就**绑定当时那台默认设备**。用户插耳机、连蓝牙后，
旧实例继续盯着那台已经不出声的设备，于是手机端静默。

修法：`SpeakerLoopback` 注册 `IMMNotificationClient`，在默认输出设备变化、
当前设备被移除/失效时自动重挂采集；另加两道兜底 ——

- 采集线程异常退出（`RecordingStopped` 带异常，典型是设备被拔）也触发重挂
- 每 10 秒巡检：默认设备 ID 变了、或连续 8 秒没有采集数据，就重挂一次

重挂有 1.5 秒节流（设备切换瞬间系统会连发多条通知），并清空组帧缓冲避免新旧音频拼成一帧。

> 排查这类"静默失效"时，`tools/probe-loopback` 很有用：它独立启动一次 WASAPI 环回采集，
> 直接打印默认设备、`MixFormat`、`capture.WaveFormat` 和实际回调字节数，
> 能一眼区分"采集没起来"和"起来了但没数据"。
### 4. 采集硬性要求 48kHz：44.1kHz 的设备直接没声

原代码校验采集格式时要求**采样率必须是 48kHz**，不满足就 `return false` 放弃采集。
而**采集格式跟随设备配置** —— 用户把播放设备设成 44.1kHz，采集就是 44.1kHz。
结果是那类机器永远没声音，且和 `MixFormat` 那个坑一样属于"静默失败"。

修法：不再限制采样率，对设备原生格式做重采样兜底 ——
`DataAvailable` 的 float 样本按实际声道数适配成左右两路（单声道复制、>2 声道取前两路），
**左右各自**经 `WdlResamplingSampleProvider` 转成 48kHz 后组帧（详见第 6 条）。

> **还有一个更隐蔽的坑**：`WdlResamplingSampleProvider` 是在**构造时**读
> `source.WaveFormat.SampleRate` 来决定是否需要重采样的，而输入环形缓冲最初把它写死成 48000。
> 于是 44.1kHz 的设备被当成 48kHz **直通、根本不重采样**，音调整体偏高约 8.8%。
> 修法是让 `FloatRingBuffer.WaveFormat` 可写，在 `InitPipeline()` 里用设备真实采样率覆盖。
> 验证判据见 `tools/probe-pitch`（同一份 44.1kHz 数据，只改声明值即可复现 0% 与 +8.8% 的差别）。

### 5. 重采样器的数据源必须长期可读（这一条最隐蔽）

第一版重采样实现里，每次回调都 `new` 一个包着**一次性数组**的 provider：

```csharp
_resampler ??= new WdlResamplingSampleProvider(new MonoFromCapture(input, ch), SampleRate);
```

`??=` 只在第一次赋值，于是重采样器**永远绑在第一次那份已经耗尽的数组上**，
之后每次读取都返回 0。表现是：出 3 帧后彻底没声音，**没有任何异常、进程也不崩**，
采集回调仍在正常触发 —— 靠日志完全看不出问题，最后是用直写文件的方式
打印出帧累计数才发现"framing 卡在 3 不再增长"。

修法：改成一个**持续存在**的 `FloatRingBuffer`，生产侧 `Push`、消费侧 `Read`，
重采样器始终从同一个活缓冲取数据。

> 顺带记两个教训：
> 1. 给 `ISampleProvider` 写实现时，`Read` **必须推进读取位置**并最终返回 0。
>    既不推进位置（永不结束）也不维护位置（只出一次），两种都会坏事。
> 2. 这类"静默失效"光看日志没用，要直接打点关键计数器（出帧累计数）才能定位。
### 6. 重采样绝不能降混成单声道（一次真实的音质事故）

为了让重采样器实现简单，加 44.1kHz 兜底时把 N 声道**求和成单声道**、
重采样完再复制成左右两路。结果立体声像被彻底抹平。

用户反馈是"和手机原生播放音质差距好大，明显不是 EQ" —— 判断很准：
这不是频响问题，是**空间信息整个丢了**。而它不会报错、不会断音，
所有既有自测（帧大小、帧数、阶段切换）全部通过，只有耳朵能发现。

**修法**：左右各用一个 `FloatRingBuffer`（`_inputL`/`_inputR`）作为重采样器的数据源，
各自接一个 `WdlResamplingSampleProvider`，**逐声道独立重采样**，左右互不影响；
声道数适配（单声道复制、>2 声道取前两路）放在写入环形缓冲之前做，
**任何情况下都不做左右求和**。

验证用 `tools/probe-stereo`：喂 L=1kHz / R=3kHz，输出

```
左声道: 1000Hz=0.2500   3000Hz=0.0000
右声道: 1000Hz=0.0000   3000Hz=0.2500
```

零串扰。若改回求和成单声道，这个测试会立刻报 STEREO-FAILED。

> 教训：**"能出声"和"音质正确"是两件事**。自动化自测只覆盖了前者，
> 立体声/相位/动态这类指标必须有专门的判据，否则静默退化无人察觉。
### 7. 组帧缓冲不能用 List + RemoveRange（会造成断续）

修立体声时我把出帧前的中转缓冲写成了 `List<float>` + `RemoveRange(0, 1920)`。
两个问题叠在一起：

1. **`RemoveRange(0,n)` 是 O(n) 搬移** —— 每出一帧就把剩余元素整体前移一次。
2. **丢掉了容量上限** —— 旧实现有"缓冲满就 return"的封顶，我改成了 `continue`，
   于是列表可以无限增长，搬移代价随之膨胀，而且积压会一直累积成延迟。

结果就是采集线程被拖慢 → 声音**断续**且音质**全损**。

**修法**：改用固定容量的 `FloatRingBuffer`，出入队都是 O(1)，内存有界，
并且左右各一个（`_inputL/_inputR` 供重采样，`_readyL/_readyR` 存结果），
两个声道的可用帧数取小值后出帧。

三项判据（`tools/probe-stereo` + 真实应用实测）：

| 检查 | 结果 |
|---|---|
| 出帧速率 | 50.0 帧/秒（40 秒连续实测，每 2 秒窗口恰好 100 帧，零空档） |
| 左右声道 | 1kHz 只在左、3kHz 只在右，零串扰 |
| 样本对齐 | 推入 192000 → 输出 192000，不连续处 0（用斜坡信号验证） |

> 教训：**音频流水线里任何 O(n) 的搬移都可能是致命的**。
> 20ms 一帧、每秒 50 帧，只要单帧处理时间超过 20ms 就会开始掉数据。
> 环形缓冲是这类场景的正确数据结构，不是"优化"，是基本要求。
### 8. 界面读不到配置：Load 的时机错了

`OnLaunched` 里原本是「先 `new MainWindow()` → 之后才 `AppConfig.Load()`」。
而 `MainWindow` 的构造函数要读 `AppConfig` 来初始化端口框与档位开关 ——
于是界面永远显示**未加载的默认值**。

之所以长期没被发现：端口默认值恰好和配置文件里的值一样（都是 7443），
看起来一切正常；直到加了音频档位开关，两种档位渲染结果完全相同，才暴露出来。

修法：把 `AppConfig.Load()` 提到创建窗口之前，并在调用处写明这个顺序依赖。

### 9. 手改配置文件会静默失效（BOM）

`System.Text.Json` **不接受 BOM**，而记事本、以及 Windows PowerShell 的
`Set-Content -Encoding UTF8` 都会写出带 BOM 的 UTF-8。
用户一旦手改 `settings.json`，解析就抛异常、`Load()` 回落到默认值 —— 表现为"改了没反应"，
且只在 `app.log` 里留一行不易察觉的记录。

修法：读取时先剥掉 BOM 与首尾空白（`StripBom`）；写入时显式用不带 BOM 的 UTF-8。
### 10. 手机端"关麦"不能 disable 轨（已随收录功能移除）

历史问题，记录备查：iOS 上 `track.enabled = false` 会挂起整个音频会话、连带下行也断。
当时的解法是保留轨道、由 PC 端丢弃上行包。现无上行，此问题不再存在。

### 11. 关于虚拟声卡与内核驱动的结论（已移除相关代码）

曾尝试让程序自带虚拟声卡（把手机麦克风的声音注入虚拟声卡供会议软件采集），
实测撞到两个硬限制：

1. **开着 Secure Boot 的机器无法加载**非微软签名的内核驱动。
   仓库曾内置 MikeTheTech 的驱动（SignPath Foundation 签名），Windows 报 `CM_PROB_UNSIGNED_DRIVER`。
   官方文档对 Code 52 的说明只有一句：获取并安装经过数字签名的驱动。
2. **不能改它的显示名**。目录文件 `.cat` 记录了 INF 的哈希，改 INF 里的字符串会导致
   `pnputil` 报"文件的哈希值不在指定的目录文件中"。实测 A/B 对照：原版 INF 安装成功，改名 INF 被拒。

结论：内核模式驱动必须走 EV 证书 + 微软证明签名（或 WHQL），没有免费的技术绕法。
[VB-CABLE](https://vb-audio.com/Cable/) 的目录文件由 **Microsoft Windows Hardware Compatibility
Publisher** 签名（WHQL），是免费且可用的替代方案。

既然收录功能已整体移除，`drivers/` 目录与相关的选择、注入、自检代码也一并删除。

### 12. 陈旧设置会压制自动识别（已随虚拟声卡选择移除）

历史问题，记录备查：早期版本把"自动识别"的结果也当成"用户选择"持久化到 `settings.json`，
导致后来装了正规虚拟声卡也不会自动切过去，仍指向一个单向的伪设备 ——
表现为"手机麦克风一直没声音"，且不报任何错。这类静默用错设备的 bug 很难靠肉眼发现。

### 13. 切音频设备后整机无声 + 手机永远"正在连接电脑"（一次 COM 死锁连锁）

症状极具迷惑性：切换系统输出设备后没声、切回去也没声；手机端返回首页再进，
永远停在"正在连接电脑"，PC 日志只有"手机已连接"、没有"握手成功"，也没有"断开"。

`app.log` 里的指纹是：`检测到音频设备变化…重新挂载采集` 之后**没有任何接入成功/失败的后续**，
然后是每 10 秒一条"新手机接入"。

根因链：

1. 设备变化通知跑在 **COM RPC 线程**、`RecordingStopped` 跑在**采集线程**上，
   而 `ReattachCore` 在这两个线程上同步调 `StopRecording()` —— 它要等新采集线程退出。
   设备拔除瞬间采集线程正卡在 audiosrv（甚至 RecordingStopped 路径是"自己等自己"），**死锁**。
2. audiosrv 被拖住后，新手机会话的 `new AudioStreamSession()` → `TryStart` 里的
   音频 COM 调用全部阻塞：服务器接受了 WebSocket，却永远走不到收 hello ——
   手机就一直"正在连接电脑"。
3. 通知线程堵死后，切回原设备的通知也进不来，所以"切回去依然没声"。

修法（`SpeakerLoopback` 的线程模型重写）：

- 通知回调 / `RecordingStopped` 只**投递重挂请求**到 `BlockingCollection`，
  由**专用工作线程**串行执行全部 attach/detach（突发通知合并成一次）；
- `DetachCurrent` 改为**遗弃式**：字段解引后把 `StopRecording`/`Dispose` 交给一次性线程慢慢收，
  绝不在关键路径上同步等 audiosrv；
- `TryStart` 不再同步接入设备，会话构造函数里**零音频 COM 调用** ——
  audiosrv 卡死时信令照常握手，音频等 audiosrv 恢复后由工作线程挂上；
- 巡检拿不到锁就跳过本轮（`Monitor.TryEnter`），定时器线程不陪工作线程一起堵。

> 教训：**音频栈的回调线程上只能做"投递"，任何同步等待都可能变成全局死锁**。
> 尤其是 `StopRecording` 这种看似无害的清理调用，在设备切换窗口期就是定时炸弹。

### 14. 手机端设备名显示不全（量错宽度导致自适应从未生效）

OLED 上的"已连接 <设备名>"在部分机器上显示不全。原本写了 `fitTrackText()`
按容器宽度自动缩小字号，但它的超出检测用的是：

```js
var needed = el.scrollWidth;   // ← 恒等于 clientWidth
```

`.oled-track` 设了 `width: 100%`，于是 `scrollWidth` **永远等于** `clientWidth`，
"是否超出"永远判为"没超出"，**字号从来没有被缩小过**。
实测 30 个字符的设备名明明溢出了，检测仍一路返回放得下。

修法：临时把宽度约束换成 `max-content` 让盒子按内容撑开，量到真实文字宽度后立刻还原
（见 `measureTextWidth()`）。修正后实测：

| 设备名 | 基准文字宽 | 缩放后 | 可用 | 字号 |
|---|---|---|---|---|
| 24 字符 | 346 | 330 | 330 | 11 → 10.5 |
| 46 字符 | 416 | 330 | 330 | 11 → 8.7 |
| 48 字符 | 432 | 330 | 330 | 11 → 8.4 |

> 教训：**`scrollWidth` 在有 `width:100%` 的元素上不能用来判断内容是否溢出。**

### 15. 音量键错位、顶出机箱（容器尺寸与按钮不自洽）

用户的反馈是"音量键会错位"。根因有两层：

1. `.capsule-slot` 写死 `width: 33px / height: 64px`，而 `.capsule-btn` 是 44px ——
   **子元素比容器还宽**；高度也只留 20px 间隙，两颗按钮几乎贴在一起。
2. 整个按钮组 `rotate(-38deg)`，而机箱右下角是**圆角、向内收** ——
   旋转后最下面那颗按钮的角会顶到圆角外侧，只按"本体尺寸"留边距不够。

修法：改成**尺寸自洽**的方案 —— 槽的宽高由 `--cap-btn` 变量算出来，
间距用 flex `gap` 控制，不再写死宽高；右边距按旋转后的外框算。

```css
.capsule-slot {
  --cap-btn: 44px;                              /* 各媒体查询只改这一个值 */
  width: calc(var(--cap-btn) + 8px);
  height: auto;
  border-radius: calc((var(--cap-btn) + 8px) / 2);
  gap: 8px;
  right: clamp(28px, 6vw, 36px);                /* 留够旋转外框 + 圆角 */
}
```

实测各宽度下按钮超出机箱均为 **0.0px**（320 / 337 / 360 / 390 / 412）。

> 两个坑记一下：
> 1. **容器尺寸要由内容决定，别写死成比子元素还小** —— 再加旋转，偏移会被放大。
> 2. 这处被 `@media (max-width:480px)`、`@media (max-height:620px)`、
>    两者组合共三条规则按源码顺序覆盖，**只改基础规则不生效**；
>    改成单一 `--cap-btn` 变量后，各档位只覆盖一个值，不再互相打架。