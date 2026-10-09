# 澜声 LanSound 信令协议

WebSocket 信令通道，PC 端为 Server，手机端为 Client。

**当前形态：纯收音** —— 只把电脑声音传到手机播放。手机麦克风（收录）那条链路已移除。

## 连接

| 阶段 | 地址 |
|---|---|
| 任务 1~3（历史） | `wss://<PC局域网IP>:7443/signal?token=<一次性配对Token>` |
| 当前 | `wss://<PC局域网IP>:7443/signal`（免配对码，局域网直连） |

手机端页面由 PC 端 Kestrel 托管，扫码即打开 `https://<host>:7443/`，页面自行拼接信令地址；信令断开由手机端每 3 秒自动重连。

证书：PC 首次启动生成本地根 CA（存于 `%AppData%/LanMic/certs`），按当前全部网卡 IP 签发服务器证书（SAN 含各网卡 IPv4 + localhost），IP 集合变化自动重签。手机浏览器在证书警告页点"继续访问"即可（iOS Safari/Android Chrome 均可）；也可通过 `http://<PC局域网IP>:7444/ca.crt` 下载安装根证书（iOS 还需在 设置→通用→关于本机→证书信任设置 中开启完全信任）。

## 通用规则

- 信令消息统一为 JSON 信封：`{ "type": "string", "payload": { } }`
- **下行音频不走 JSON**：PC 直接发 WebSocket **二进制帧**，手机端按二进制处理（见下）
- 连接空闲 30 秒无消息自动断开（PC 端主动 close）；手机端每 10 秒发送 `ping` 心跳保活
- 一台 PC 同一时间只服务一台手机：新连接接入时，PC 主动关闭旧连接（旧连接多为切网残留的半开会话）

## 消息类型

| type | 方向 | payload | 说明 |
|---|---|---|---|
| `hello` | 手机→PC | `{ "deviceName": "iPhone 15" }` | 建连后第一条消息 |
| `hello` | PC→手机 | `{ "deviceName": "DESKTOP-ABC" }` | 握手应答（PC 机器名） |
| `mode-change` | 手机→PC | `{ "speaker": true }` | 收听开关：PC 是否继续发送下行音频 |
| `error` | PC→手机 | `{ "code": "...", "message": "..." }` | 错误反馈 |
| `ping` | 手机→PC | `{}` | 心跳保活（每 10 秒） |
| `pong` | PC→手机 | `{}` | 心跳应答。手机端据此做失活检测：切网/锁屏时 TCP 半开不会触发 onclose，超过阈值无入站流量即主动重连 |
| `bye` | 双向 | `{}` | 主动断开 |

> 历史遗留：`offer` / `answer` / `ice-candidate` 曾用于手机麦克风的 WebRTC 上行，
> 已随收录功能一并移除。PC 端收到这些类型会记录"未知消息类型"并忽略。

## 音频通道：下行（电脑扬声器 → 手机）

- PC 用 WASAPI 环回采集**默认输出设备**正在播放的声音。
- 格式：**48kHz / s16le / 立体声交错 / 每帧 20ms（3840 字节，无包头）**，经 WebSocket 二进制帧直发。
- 手机端用 Web Audio（AudioContext）重采样排队播放，抖动缓冲 **150ms**，积压超 0.5s 时丢弃重排。
- 播放出口是 `<audio>` 元素（`MediaStreamDestination`），配合 Media Session，
  锁屏/切后台后按"媒体播放"继续出声；不支持时退回 Web Audio 直出。

### 为什么不走 WebRTC

iOS 的通话渲染通道会把播放**混成单声道**，而直发裸 PCM + Web Audio 能保住真立体声，
同时省掉整个 SDP 协商与 ICE 流程。这是本项目最关键的一个取舍。

### 采集格式要求

`SpeakerLoopback` 要求默认输出设备的混音格式是 **48kHz + 32bit 浮点**
（Windows 共享模式的常见配置）。不满足时会在日志里说明原因并停止采集。
