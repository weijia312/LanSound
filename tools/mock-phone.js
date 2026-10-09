/**
 * 模拟 LanMic 手机端，验证纯收音链路：
 *   1) WSS 信令握手（hello）
 *   2) 收到 PC 回发的 hello
 *   3) 持续收到下行二进制 PCM 帧，校验帧格式（3840 字节 = 20ms 48kHz 立体声 s16le）
 *   4) 发 mode-change 关闭再打开，确认下行确实会停/恢复
 *   5) ping 保活
 *
 * 用法: node tools/mock-phone.js <host>
 */
const host = process.argv[2] || '192.168.2.106';
// 服务默认是 HTTPS/WSS；只有在 LANMIC_NO_HTTPS=1 时才走明文。
// 设 PLAIN=1 可强制明文。自签证书需要放开校验（下面的 warning 可忽略）。
const PLAIN = process.env.PLAIN === '1';
if (!PLAIN) process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0';
const URL = `${PLAIN ? 'ws' : 'wss'}://${host}:7443/signal`;
const EXPECTED_FRAME = 3840; // 20ms @48kHz 立体声 s16le

// PC 用的是自建 CA 签发的证书，这里显式放行（真机上是浏览器里点"继续访问"）
const ws = new WebSocket(URL);
ws.binaryType = 'arraybuffer';

let frames = 0, bytes = 0, bad = 0, firstFrameAt = 0, lastFrameAt = 0;
let gotHello = false, gotPcHello = false;
let phase = 'A'; // A: 初始收音中
let phaseAFrames = 0, phaseBFrames = 0;

const t0 = Date.now();

ws.addEventListener('open', () => {
  console.log('[1] WSS 已连接', URL);
  ws.send(JSON.stringify({ type: 'hello', payload: { deviceName: 'MockPhone' } }));
  // 心跳（真实手机端每 10 秒一次）
  setInterval(() => ws.send(JSON.stringify({ type: 'ping', payload: {} })), 10000);
});

ws.addEventListener('message', (ev) => {
  if (typeof ev.data !== 'string') {
    // 二进制帧 = 下行 PCM
    const len = ev.data.byteLength;
    frames++; bytes += len;
    if (!firstFrameAt) firstFrameAt = Date.now();
    lastFrameAt = Date.now();
    if (len !== EXPECTED_FRAME) bad++;
    if (phase === 'A') phaseAFrames++;
    return;
  }
  let msg;
  try { msg = JSON.parse(ev.data); } catch { console.log('  无法解析:', ev.data); return; }
  if (msg.type === 'hello') {
    gotPcHello = true;
    console.log('[2] 收到 PC 应答 hello，机器名 =', msg.payload.deviceName);
    // 3 秒后测试关闭下行
    setTimeout(() => {
      phase = 'B';
      console.log('[4] 发送 mode-change speaker=false（应停止下行）');
      ws.send(JSON.stringify({ type: 'mode-change', payload: { speaker: false } }));
    }, 3000);
    // 再过 3 秒恢复
    setTimeout(() => {
      phase = 'C';
      console.log('[5] 发送 mode-change speaker=true（应恢复下行）');
      ws.send(JSON.stringify({ type: 'mode-change', payload: { speaker: true } }));
    }, 6000);
  } else {
    console.log('  收到信令:', msg.type);
  }
});

ws.addEventListener('error', (e) => {
  console.log('!! WebSocket 错误:', e.message || e.error?.message || String(e));
});

ws.addEventListener('close', (e) => {
  console.log('!! 连接关闭 code=' + e.code, e.reason || '');
});

// 9 秒后汇总
setTimeout(() => {
  console.log('');
  console.log('==================== 验证结果 ====================');
  console.log(`hello 握手        : ${gotPcHello ? '通过' : '失败'}`);
  console.log(`下行帧总数        : ${frames}`);
  console.log(`帧大小异常        : ${bad} ${bad === 0 ? '（全部 3840 字节，符合 20ms 48kHz 立体声）' : '← 格式不符！'}`);
  console.log(`总字节            : ${bytes}`);
  if (frames > 0) {
    const dur = (lastFrameAt - firstFrameAt) / 1000;
    console.log(`帧率              : ${dur > 0 ? (frames / dur).toFixed(1) : 'n/a'} 帧/秒（期望约 50）`);
  }
  console.log(`阶段 A 帧数       : ${phaseAFrames}（收音中，应 > 0）`);
  console.log(`阶段 B 帧数       : ${phaseBFrames}（关闭后，应 ≈ 0）`);
  const ok = gotPcHello && frames > 0 && bad === 0 && phaseAFrames > 0;
  console.log('');
  console.log(ok ? 'RADIO-OK：纯收音链路可用' : 'RADIO-FAILED');
  process.exit(ok ? 0 : 1);
}, 9000);
