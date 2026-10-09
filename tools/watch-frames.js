/**
 * 诊断"切换系统音频设备后手机端没声"：
 *   持续监听下行 PCM 帧，每 2 秒报一次帧数。
 *   在运行期间手动切换系统默认播放设备，观察帧率是否掉到 0。
 *
 * 用法: node tools/watch-frames.js [host] [seconds]
 */
const host = process.argv[2] || '127.0.0.1';
const seconds = Number(process.argv[3] || 40);
// 服务默认是 HTTPS/WSS；只有在 LANMIC_NO_HTTPS=1 时才走明文。
// 设 PLAIN=1 可强制明文。自签证书需要放开校验（下面的 warning 可忽略）。
const PLAIN = process.env.PLAIN === '1';
if (!PLAIN) process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0';
const URL = `${PLAIN ? 'ws' : 'wss'}://${host}:7443/signal`;

const ws = new WebSocket(URL);
ws.binaryType = 'arraybuffer';

let total = 0;
let perWindow = 0;
let firstSeen = 0;
let lastSeen = 0;

ws.addEventListener('open', () => {
  console.log('[连接] ' + URL);
  ws.send(JSON.stringify({ type: 'hello', payload: { deviceName: 'FrameWatcher' } }));
  setInterval(() => ws.send(JSON.stringify({ type: 'ping', payload: {} })), 10000);
});

ws.addEventListener('message', (ev) => {
  if (typeof ev.data === 'string') {
    const m = JSON.parse(ev.data);
    if (m.type === 'hello') {
      console.log('[握手] 已连接 ' + (m.payload.deviceName || ''));
      // 确保处于收音状态
      ws.send(JSON.stringify({ type: 'mode-change', payload: { speaker: true } }));
      startReporting();
    }
    return;
  }
  const n = ev.data.byteLength;
  total++; perWindow++;
  if (!firstSeen) firstSeen = Date.now();
  lastSeen = Date.now();
  if (n !== 3840) console.log('  !! 帧大小异常 ' + n);
});

ws.addEventListener('error', (e) => console.log('[错误] ' + (e.message || e.error?.message || e)));

let elapsed = 0;
function startReporting() {
  console.log('[提示] 现在切换系统默认播放设备，观察下方帧率');
  console.log('');
  const t = setInterval(() => {
    elapsed += 2;
    const fps = (perWindow / 2).toFixed(1);
    const flag = perWindow === 0 ? '   <== 无声音！' : '';
    console.log(`  t=${String(elapsed).padStart(3)}s  本窗口 ${String(perWindow).padStart(4)} 帧  ${fps.padStart(5)} fps  累计 ${total}${flag}`);
    perWindow = 0;
    if (elapsed >= seconds) {
      clearInterval(t);
      console.log('');
      console.log(total > 0 ? 'FRAMES-OK：期间收到过音频' : 'FRAMES-NONE：全程没有音频');
      process.exit(0);
    }
  }, 2000);
}
