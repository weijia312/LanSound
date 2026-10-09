/**
 * 一边持续测量下行音频电平，一边程序化切换系统默认播放设备，
 * 用来判定"切换后没声"到底是：
 *   (a) 该设备本身就是单向的（回环拿不到声音）—— 换任何设备都一样
 *   (b) 重挂采集失败（代码 bug）—— 切到正常设备后仍然无声
 *
 * 用法: node tools/switch-test.js [host] [secondsPerPhase]
 * 需要同目录的 probe-switch.exe 已构建（tools/probe-switch）。
 */
import { spawn } from 'node:child_process';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const host = process.argv[2] || '127.0.0.1';
const phaseSec = Number(process.argv[3] || 6);
const here = path.dirname(fileURLToPath(import.meta.url));

const switcher = path.join(here, 'probe-switch', 'bin', 'Release', 'net8.0-windows', 'probe-switch.exe');
const toner = path.join(here, 'play-tone.ps1');

process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0';

function run(cmd, args, opts = {}) {
  return new Promise((resolve) => {
    const p = spawn(cmd, args, { ...opts, windowsHide: true });
    let out = '';
    p.stdout?.on('data', (d) => { out += d; });
    p.stderr?.on('data', (d) => { out += d; });
    p.on('close', (code) => resolve({ code, out }));
  });
}

async function listDevices() {
  const { out } = await run(switcher, ['list']);
  const devs = [];
  for (const line of out.split(/\r?\n/)) {
    const m = line.match(/\[(\d+)\]\s*(.+?)(\s+<==.*)?$/);
    if (m) devs.push({ idx: Number(m[1]), name: m[2].trim(), isDefault: !!m[3] });
  }
  return devs;
}

// ---- 连接并持续统计每一段的 RMS / 帧数 ----
const URL = `wss://${host}:7443/signal`;
const ws = new WebSocket(URL);
ws.binaryType = 'arraybuffer';

let segFrames = 0;
let segSumSq = 0;
let segSamples = 0;

ws.addEventListener('open', () => {
  ws.send(JSON.stringify({ type: 'hello', payload: { deviceName: 'SwitchTest' } }));
});
ws.addEventListener('message', (ev) => {
  if (typeof ev.data === 'string') {
    const m = JSON.parse(ev.data);
    if (m.type === 'hello') ws.send(JSON.stringify({ type: 'mode-change', payload: { speaker: true } }));
    return;
  }
  if (ev.data.byteLength !== 3840) return;
  const dv = new DataView(ev.data);
  segFrames++;
  for (let i = 0; i < 960; i++) {
    const l = dv.getInt16(i * 4, true), r = dv.getInt16(i * 4 + 2, true);
    segSumSq += l * l + r * r;
    segSamples += 2;
  }
});
ws.addEventListener('error', (e) => { console.log('  连接错误:', e.message || String(e)); process.exit(1); });

function takeSegment() {
  const rms = segSamples > 0 ? Math.sqrt(segSumSq / segSamples) : 0;
  const res = { frames: segFrames, rms };
  segFrames = 0; segSumSq = 0; segSamples = 0;
  return res;
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

await sleep(1200);
const devices = await listDevices();
console.log('  可用设备:');
for (const d of devices) console.log(`    [${d.idx}] ${d.name}${d.isDefault ? '  <== 当前默认' : ''}`);
console.log('');

// 持续放音（时长覆盖所有阶段）
const totalSec = phaseSec * devices.length + 8;
spawn('powershell', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', toner,
  '-Seconds', String(totalSec)], { windowsHide: true });
await sleep(1500);

console.log(`  每段 ${phaseSec} 秒，逐一切换设备并测量接收电平（RMS）`);
console.log('  RMS ≈ 0 说明该设备上采集不到声音');
console.log('');

const results = [];
for (const d of devices) {
  await run(switcher, ['set', String(d.idx)]);
  await sleep(1500);            // 等重挂完成
  takeSegment();                // 丢掉切换瞬间的过渡数据
  await sleep(phaseSec * 1000);
  const seg = takeSegment();
  results.push({ ...d, ...seg });
  const verdict = seg.rms > 50 ? '有声' : (seg.frames === 0 ? '无数据' : '无声');
  console.log(`  [${d.idx}] ${d.name.padEnd(30)} 帧=${String(seg.frames).padStart(4)}  RMS=${seg.rms.toFixed(1).padStart(7)}   ${verdict}`);
}

console.log('');
const audible = results.filter((r) => r.rms > 50);
const silent = results.filter((r) => r.rms <= 50);

if (audible.length > 0) {
  console.log('  结论：切到正常设备后能恢复出声 → 重挂机制本身工作正常。');
  if (silent.length > 0) {
    console.log('        以下设备回环拿不到声音（设备本身单向，不是代码问题）：');
    for (const s of silent) console.log(`          - ${s.name}`);
  }
  console.log('  SWITCH-OK');
  process.exit(0);
} else {
  console.log('  结论：所有设备都测不到声音 → 重挂可能失败，或放音没走当前默认设备。');
  console.log('  SWITCH-FAILED');
  process.exit(1);
}
