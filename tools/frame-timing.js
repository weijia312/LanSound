/**
 * 测量下行 PCM 帧的"到达间隔"分布，用来判断传输层是否引入额外延迟。
 *
 * 关键判据：帧应当每 20ms 到达一次。
 *   - 最大间隔接近 100~200ms，或 40/60ms 倍数聚集 → Nagle 在攒包
 *   - 间隔都远小于 20ms → 正常（发送端一次回调可能连发多帧）
 *   - 出现 >100ms 空档 → 真的丢帧/卡顿
 *
 * 用法: node tools/frame-timing.js [host] [seconds] [port]
 */
const host = process.argv[2] || '127.0.0.1';
const seconds = Number(process.argv[3] || 30);
const port = Number(process.argv[4] || 7443);

const scheme = process.env.PLAIN === '1' ? 'ws' : 'wss';
const URL = `${scheme}://${host}:${port}/signal`;

const intervals = [];
let lastAt = 0n;
let total = 0;
let badSize = 0;

// 自签证书：Node 内置 WebSocket 走 undici，用 NODE_TLS_REJECT_UNAUTHORIZED 放开
if (scheme === 'wss') process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0';

console.log(`  连接: ${URL}`);
const ws = new WebSocket(URL);
ws.binaryType = 'arraybuffer';

ws.addEventListener('open', () => {
  console.log('  已连接，测量', seconds, '秒...');
  ws.send(JSON.stringify({ type: 'hello', payload: { deviceName: 'FrameTiming' } }));
});

let started = false;
ws.addEventListener('message', (ev) => {
  if (typeof ev.data === 'string') {
    const m = JSON.parse(ev.data);
    if (m.type === 'hello' && !started) {
      started = true;
      ws.send(JSON.stringify({ type: 'mode-change', payload: { speaker: true } }));
    }
    return;
  }
  const n = ev.data.byteLength;
  if (n !== 3840) { badSize++; return; }
  const now = process.hrtime.bigint();
  total++;
  if (lastAt) intervals.push(Number(now - lastAt) / 1e6);
  lastAt = now;
});

ws.addEventListener('error', (e) => {
  console.log('  连接错误:', e.message || e.error?.message || String(e));
  process.exit(1);
});

setTimeout(() => {
  console.log('');
  console.log(`  收到音频帧: ${total}   非 3840 字节异常帧: ${badSize}`);
  if (total < 10) {
    console.log('  TIMING-NODATA：帧太少，先放点声音再测');
    process.exit(1);
  }

  const s = intervals.slice().sort((a, b) => a - b);
  const pct = (p) => s[Math.min(s.length - 1, Math.floor(s.length * p))];
  const avg = s.reduce((a, b) => a + b, 0) / s.length;
  const max = s[s.length - 1];

  console.log(`  到达间隔: 最小 ${s[0].toFixed(1)}ms  中位 ${pct(0.5).toFixed(1)}ms  平均 ${avg.toFixed(1)}ms`);
  console.log(`            p90 ${pct(0.9).toFixed(1)}ms  p99 ${pct(0.99).toFixed(1)}ms  最大 ${max.toFixed(1)}ms`);

  const gaps = intervals.filter((v) => v > 45).length;
  console.log(`  >45ms 的间隔: ${gaps} 个（${(100 * gaps / intervals.length).toFixed(2)}%）`);
  console.log('');

  // 判据说明：帧是**成簇**到达的 —— 发送线程被调度时一次发好几帧，
  // 所以"中位 0ms + p90 60ms"是正常形态，不能据此判抖动大。
  // 真正有意义的是**平均间隔**：它必须等于帧时长（20ms），
  // 否则说明音频时间轴在丢样或重复，那才是问题。
  const avgOk = Math.abs(avg - 20) <= 2;
  const noStall = max < 200;          // Nagle 攒包的特征值约 200ms

  if (!avgOk) {
    console.log(`  TIMING-FAILED：平均间隔 ${avg.toFixed(1)}ms 明显偏离 20ms —— 音频时间轴在丢样或重复`);
    process.exit(1);
  }
  if (!noStall) {
    console.log(`  TIMING-WARN：最大间隔 ${max.toFixed(0)}ms，接近 Nagle 攒包特征（200ms），建议查 NoDelay`);
    process.exit(0);
  }
  console.log(`  TIMING-OK：平均间隔 ${avg.toFixed(1)}ms ≈ 20ms，音频时间轴无丢失`);
  console.log('             （帧成簇到达属正常：发送线程一次调度发多帧，中位 0ms 不代表异常）');
  process.exit(0);
}, seconds * 1000);
