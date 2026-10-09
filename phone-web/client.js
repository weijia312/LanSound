'use strict';

(function () {
    var STRINGS = {
        zh: {
            startHint: '点上方按钮收听电脑声音', startAria: '开始收听电脑声音', waiting: '等待连接…',
            qualityMode: '高音质', lowLatencyMode: '低延迟', backTitle: '返回首屏', volumeUp: '音量加',
            volumeDown: '音量减', refresh: '刷新连接', play: '开始收听', pause: '暂停收听',
            enableSound: '启用声音', unavailable: '无法使用',
            statusPlay: '播放', statusMute: '静音', statusLink: '连接',
            manualRefresh: '手动刷新', inactiveConnection: '连接已失活（{seconds}s 无入站数据）',
            missingHeartbeat: '心跳缺席，连接疑似半开', idleConnection: '30s 无任何入站数据', foregroundReturn: '前台恢复',
            connectedDevice: '已连接 {name}', liveStream: '实时音频', reconnecting: '连接断开，重连中…',
            connecting: '正在连接电脑…', volumeLabel: '音量 {level}', unsupportedBrowser: '当前浏览器缺少 WebSocket / Web Audio 支持，请换 Safari 或 Chrome 打开。',
            pageError: '页面异常：{message}', asyncError: '音频异步错误：{message}',
            audioStartFailed: '音频播放通道启动失败：{message}', pcmReceived: '收到 PCM：{bytes}B/帧，RMS {rms}，AudioContext {state}',
            bufferAhead: '缓冲超前 {ms}ms，丢帧追回', connected: '已连接：{name}', audioMode: '音频档位：{mode}',
            serverError: '服务端错误：{message}', helloSent: 'hello 已发', connectionClosed: '连接关闭 code={code}，3 秒后重连',
            certHint: '连接被秒拒：请先开 http://{host}:7444 装根证书', connectionError: '连接错误',
            reconnectReason: '{reason}，立即重连', foregroundReconnect: '回到前台，连接已关闭，立即重连',
            wakeLockOn: '屏幕常亮已开启', wakeLockFailed: '屏幕常亮开启失败：{message}',
            listeningOn: '收听已开启', listeningOff: '收听已暂停', soundEnabled: '已手动启用声音', startListening: '开始收听',
            pageTitle: '澜声 LanSound', mediaListeningTitle: 'LanSound 收音中', mediaPausedTitle: 'LanSound 已暂停',
            mediaArtist: '电脑音频', mediaAlbum: '局域网收音'
        },
        en: {
            startHint: 'Tap the button above to listen to your PC', startAria: 'Start listening to PC audio', waiting: 'Waiting for connection…',
            qualityMode: 'High quality', lowLatencyMode: 'Low latency', backTitle: 'Return to start screen', volumeUp: 'Volume up',
            volumeDown: 'Volume down', refresh: 'Reconnect', play: 'Start listening', pause: 'Pause listening',
            enableSound: 'Enable audio', unavailable: 'Unavailable',
            statusPlay: 'PLAY', statusMute: 'MUTE', statusLink: 'LINK',
            manualRefresh: 'Manual refresh', inactiveConnection: 'Connection inactive ({seconds}s without incoming data)',
            missingHeartbeat: 'Heartbeat missing; connection may be half-open', idleConnection: 'No incoming data for 30s', foregroundReturn: 'Returned to foreground',
            connectedDevice: 'Connected: {name}', liveStream: 'LIVE AUDIO', reconnecting: 'Connection lost, reconnecting…',
            connecting: 'Connecting to PC…', volumeLabel: 'VOL {level}', unsupportedBrowser: 'This browser does not support WebSocket or Web Audio. Open this page in Safari or Chrome.',
            pageError: 'Page error: {message}', asyncError: 'Audio error: {message}',
            audioStartFailed: 'Audio output could not start: {message}', pcmReceived: 'PCM received: {bytes}B/frame, RMS {rms}, AudioContext {state}',
            bufferAhead: 'Buffer ahead by {ms}ms; dropping frames to catch up', connected: 'Connected: {name}', audioMode: 'Audio mode: {mode}',
            serverError: 'Server error: {message}', helloSent: 'Hello sent', connectionClosed: 'Connection closed (code {code}); retrying in 3s',
            certHint: 'Connection refused: open http://{host}:7444 first to install the root certificate', connectionError: 'Connection error',
            reconnectReason: '{reason}; reconnecting now', foregroundReconnect: 'Connection closed while away; reconnecting now',
            wakeLockOn: 'Screen will stay on', wakeLockFailed: 'Could not keep the screen on: {message}',
            listeningOn: 'Listening started', listeningOff: 'Listening paused', soundEnabled: 'Audio enabled manually', startListening: 'Listening started',
            pageTitle: 'LanSound', mediaListeningTitle: 'LanSound Listening', mediaPausedTitle: 'LanSound Paused',
            mediaArtist: 'PC Audio', mediaAlbum: 'LAN Audio'
        }
    };
    var logEntries = [];
    var systemLanguage = (navigator.languages && navigator.languages[0]) || navigator.language || '';
    var language = /^zh(?:-|$)/i.test(systemLanguage) ? 'zh' : 'en';
    function tr(key, values) {
        var template = STRINGS[language][key] || STRINGS.zh[key] || key;
        values = Object.assign({}, values || {});
        if (values.modeKey) {
            values.mode = tr(values.modeKey);
            delete values.modeKey;
        }
        Object.keys(values).forEach(function (name) {
            template = template.replace('{' + name + '}', String(values[name]));
        });
        return template;
    }
    function renderLogs() {
        if (!logList) return;
        logList.textContent = '';
        logEntries.forEach(function (entry) {
            var li = document.createElement('li');
            li.textContent = '[' + entry.time + ']  ' + tr(entry.key, entry.values);
            logList.appendChild(li);
        });
    }
    function applyLanguage() {
        document.documentElement.lang = language === 'zh' ? 'zh-CN' : 'en';
        document.title = tr('pageTitle');
        document.querySelectorAll('[data-i18n]').forEach(function (element) {
            element.textContent = tr(element.getAttribute('data-i18n'));
        });
        document.querySelectorAll('[data-i18n-title]').forEach(function (element) {
            element.title = tr(element.getAttribute('data-i18n-title'));
        });
        document.querySelectorAll('[data-i18n-aria]').forEach(function (element) {
            element.setAttribute('aria-label', tr(element.getAttribute('data-i18n-aria')));
        });
        renderLogs();
    }
    applyLanguage();
    window.addEventListener('languagechange', function () {
        var preferredLanguage = (navigator.languages && navigator.languages[0]) || navigator.language || '';
        language = /^zh(?:-|$)/i.test(preferredLanguage) ? 'zh' : 'en';
        applyLanguage();
        if (app && !app.classList.contains('hidden')) {
            refreshUI();
            updateMediaSession();
        }
    });

    // ================= 视口高度修正 =================
    // 部分浏览器不认 100dvh，回退的 100vh 会把地址栏遮挡区算进去，
    // 页面比可视区域高出一截、底栏被挤到屏外。用 JS 精确设定可视高度。
    var appViewport = document.querySelector('.device-viewport');
    var wheelContainer = document.querySelector('.wheel-container');
    var wheelRecess = document.getElementById('wheelRecess');
    var layoutFrame = 0;
    function fitWheel() {
        layoutFrame = 0;
        if (!wheelContainer || !wheelRecess) return;
        var rect = wheelContainer.getBoundingClientRect();
        var style = window.getComputedStyle(wheelContainer);
        var horizontalPadding = parseFloat(style.paddingLeft) + parseFloat(style.paddingRight);
        var verticalPadding = parseFloat(style.paddingTop) + parseFloat(style.paddingBottom);
        var size = Math.max(0, Math.min(350, rect.width - horizontalPadding, rect.height - verticalPadding));
        wheelRecess.style.width = size + 'px';
        wheelRecess.style.height = size + 'px';
    }
    function scheduleLayout() {
        if (layoutFrame) cancelAnimationFrame(layoutFrame);
        layoutFrame = requestAnimationFrame(fitWheel);
    }
    function syncViewportHeight() {
        if (!appViewport) return;
        if (window.matchMedia('(max-width: 480px), (max-height: 700px)').matches) {
            var h = window.visualViewport ? window.visualViewport.height : window.innerHeight;
            appViewport.style.height = h + 'px';
        } else {
            appViewport.style.height = '';
        }
        scheduleLayout();
    }
    syncViewportHeight();
    window.addEventListener('resize', syncViewportHeight);
    window.addEventListener('orientationchange', function () { setTimeout(syncViewportHeight, 300); });
    if (window.visualViewport) window.visualViewport.addEventListener('resize', syncViewportHeight);
    if (window.ResizeObserver && wheelContainer) new ResizeObserver(scheduleLayout).observe(wheelContainer);

    // ================= 基础设施 =================

    function $(id) { return document.getElementById(id); }

    // 启动首屏
    var startScreen = $('startScreen'), startBtn = $('startBtn'), app = $('app');
    // OLED
    var connLed = $('connLed'), oledStatusText = $('oledStatusText'), oledTrack = $('oledTrack');
    var oledTimecode = $('oledTimecode'), wave = $('wave'), wctx = wave.getContext('2d');
    var vuBars = document.querySelectorAll('.vu-bar');
    var modeLabel = $('modeLabel'), volLabel = $('volLabel'), logList = $('logList');
    // 转盘与按键
    var disc = $('wheelDisc'), recess = $('wheelRecess');
    var backBtn = $('backBtn'), volUp = $('volUp'), volDown = $('volDown');
    var refreshBtn = $('refreshBtn'), playBtn = $('playBtn'), stopBtn = $('stopBtn');
    // 兜底 / 音频出口
    var speakerBtn = $('speakerBtn'), bgAudio = $('bgAudio');
    var errorCard = $('errorCard'), errorText = $('errorText');

    var AudioContextCtor = window.AudioContext || window.webkitAudioContext;
    if (!window.WebSocket || !AudioContextCtor) {
        errorCard.classList.remove('hidden');
        errorText.textContent = tr('unsupportedBrowser');
        if (startScreen) startScreen.classList.add('hidden');
        return;
    }

    // ================= 运行状态 =================

    var ws = null;
    var heartbeatTimer = null;
    var livenessTimer = null;
    var wsConnected = false;
    var connecting = false;
    var speakerOn = false;
    var audioCtx = null, gainNode = null, streamDest = null, keepAlive = null;
    var mediaElementOutput = false;
    var nextPlayTime = 0;
    // 启动遮蔽：PCM 刚开始排程时做一次短淡入。
    // 原因见 warmUpAudio()：<audio> 播放 MediaStream 需要一段协商/缓冲时间，
    // 这期间排进去的帧出来是失真的 —— 实测用户听到"每次连接有 1 秒失真"。
    var startupFadePending = false;
    var volume = 0.8;
    var deviceName = '';
    var lastRxAt = Date.now();
    var lastPongAt = Date.now();
    var hadConnected = false;
    var connectAttemptAt = 0;
    var loggedFirstPcmFrame = false;

    // OLED 动效状态
    var scopeL = new Float32Array(168); // 波形快照（左声道抽稀）
    var vuLevel = 0;
    var listenSeconds = 0;
    var angle = 0, angularVelocity = 0;
    var dragging = false, lastPtrAngle = 0, lastPtrTime = 0;

    // 抖动缓冲档位：与 PC 端 AudioMode 对应（audio-mode 信令下发）
    var JITTER_PROFILES = {
        LowLatency: { labelKey: 'lowLatencyMode', targetSec: 0.06, maxAheadSec: 0.12, dropAfterSec: 0.3 },
        MaxQuality: { labelKey: 'qualityMode', targetSec: 0.20, maxAheadSec: 0.50, dropAfterSec: 1.0 }
    };
    var jitter = JITTER_PROFILES.MaxQuality;

    // ================= OLED 日志（最新两条） =================

    function log(key, values) {
        var time = new Date().toLocaleTimeString('zh-CN', { hour12: false });
        logEntries.push({ time: time, key: key, values: values || {} });
        if (logEntries.length > 2) logEntries.shift();
        renderLogs();
        console.log('[LanSound ' + time + '] ' + tr(key, values));
    }

    window.addEventListener('error', function (e) {
        log('pageError', { message: e.message || 'Unknown error' });
    });
    window.addEventListener('unhandledrejection', function (e) {
        var reason = e.reason;
        log('asyncError', { message: reason && reason.message ? reason.message : String(reason || 'Unknown error') });
    });

    // ================= 按键音（微动开关反馈） =================

    function keyClick(freq, dur, vol) {
        try {
            if (!audioCtx || audioCtx.state !== 'running') return;
            var t = audioCtx.currentTime;
            var osc = audioCtx.createOscillator();
            var g = audioCtx.createGain();
            osc.type = 'triangle';
            osc.frequency.setValueAtTime(freq || 900, t);
            osc.frequency.exponentialRampToValueAtTime(100, t + (dur || 0.02));
            g.gain.setValueAtTime(vol || 0.25, t);
            g.gain.exponentialRampToValueAtTime(0.001, t + (dur || 0.02));
            osc.connect(g); g.connect(audioCtx.destination);
            osc.start(); osc.stop(t + (dur || 0.02));
            if ('vibrate' in navigator) navigator.vibrate(8);
        } catch (e) { }
    }

    function heavyClick() {
        keyClick(1100, 0.015, 0.3);
        try {
            if (!audioCtx || audioCtx.state !== 'running') return;
            var t = audioCtx.currentTime;
            var osc = audioCtx.createOscillator();
            var g = audioCtx.createGain();
            osc.type = 'sine';
            osc.frequency.setValueAtTime(160, t);
            osc.frequency.exponentialRampToValueAtTime(35, t + 0.045);
            g.gain.setValueAtTime(0.4, t);
            g.gain.exponentialRampToValueAtTime(0.001, t + 0.045);
            osc.connect(g); g.connect(audioCtx.destination);
            osc.start(); osc.stop(t + 0.045);
            if ('vibrate' in navigator) navigator.vibrate(16);
        } catch (e) { }
    }

    // ================= OLED 状态刷新 =================

    function isPlayingVisual() { return speakerOn && wsConnected; }

    /**
     * 让设备名整行完整显示。
     *
     * 设备名（如 "WEIJIA"、"MacBook-Pro-de-Wei"）长短差很多，早期用
     * text-overflow: ellipsis 解决，但在部分窄屏机上会被硬裁成不可读的半截。
     * 这里改成：二分查找能放进容器且不超高的最大字号，完整显示而不是截断。
     * 中文设备名更宽，所以同时受字号与容器高度两个约束。
     */
    /**
     * 量元素里那行文字的**真实内容宽度**。
     *
     * 不能用 scrollWidth —— 元素设了 width:100% 时，scrollWidth 恒等于 clientWidth，
     * 内容超出也不会变大，"是否放得下"永远判为放得下（这个坑实测踩过：
     * 30 个字符的设备名明明溢出了，检测却一路返回"没超出"，字号从未被缩小）。
     *
     * 做法：临时把宽度约束换成 max-content，让盒子按内容撑开，量完立刻还原。
     * 布局属性连续两行内一改一读会触发同步重排，但这条路径只在设备名变化时走，
     * 不在每帧的热路径上。
     */
    function measureTextWidth(el) {
        var prevWidth = el.style.width;
        var prevMax = el.style.maxWidth;
        var prevTransform = el.style.transform;
        el.style.transform = 'none';
        el.style.width = 'max-content';
        el.style.maxWidth = 'none';
        var w = el.getBoundingClientRect().width;
        el.style.width = prevWidth;
        el.style.maxWidth = prevMax;
        el.style.transform = prevTransform;
        return w;
    }

    /**
     * 让设备名整行完整显示。
     *
     * 设备名（如 "WEIJIA"、"MacBook-Pro-de-Wei"）长短差很多，早期用
     * text-overflow: ellipsis 解决，但在部分窄屏机上会被硬裁成不可读的半截。
     * 这里改成：二分查找能放进容器且不超高的最大字号，完整显示而不是截断。
     * 中文设备名更宽，所以同时受字号与容器高度两个约束。
     */
    function fitTrackText(el) {
        if (!el) return;
        var maxFont = parseFloat(el.dataset.baseFont || '') || parseFloat(getComputedStyle(el).fontSize) || 11;
        if (!el.dataset.baseFont) el.dataset.baseFont = String(maxFont);

        // 复位到基准字号再量，避免上一次缩小的结果累加
        el.style.fontSize = maxFont + 'px';
        el.style.transform = '';

        var avail = el.clientWidth;
        if (!avail) return;                    // 尚未布局
        var needed = measureTextWidth(el);
        if (needed <= avail) return;           // 放得下，保持基准字号

        // 高度也有限：缩得越小越矮，但容器太矮时仍要再压
        var maxH = (el.parentElement ? el.parentElement.clientHeight : 0) || 1e6;
        var byWidth = maxFont * avail / needed;
        var byHeight = maxFont * maxH / Math.max(1, el.scrollHeight);
        var target = Math.max(7, Math.min(byWidth, byHeight));
        el.style.fontSize = target.toFixed(2) + 'px';

        // 兜底：字号已到下限仍差一点时，用 transform 再压（不改变布局盒）
        if (target <= 7.01) {
            var after = measureTextWidth(el);
            if (after > avail) {
                var k = avail / after;
                if (k > 0.55) el.style.transform = 'scaleX(' + k.toFixed(3) + ')';
            }
        }
    }

    function refreshUI() {
        var playing = isPlayingVisual();
        connLed.className = 'status-led' + (wsConnected ? (playing ? ' playing' : '') : ' off');
        oledStatusText.textContent = wsConnected ? (speakerOn ? tr('statusPlay') : tr('statusMute')) : tr('statusLink');
        if (wsConnected) {
            oledTrack.textContent = deviceName ? tr('connectedDevice', { name: deviceName }) : tr('liveStream');
        } else {
            oledTrack.textContent = hadConnected ? tr('reconnecting') : tr('connecting');
        }
        fitTrackText(oledTrack);
        playBtn.classList.toggle('pressed', speakerOn);
        stopBtn.classList.toggle('pressed', !speakerOn);
        playBtn.setAttribute('aria-pressed', String(speakerOn));
        stopBtn.setAttribute('aria-pressed', String(!speakerOn));
        modeLabel.textContent = tr(jitter.labelKey);
        volLabel.textContent = tr('volumeLabel', { level: Math.round(volume * 100) });
    }
    // 屏幕尺寸变化后重算设备名字号（转屏、分屏、地址栏收放都会改可用宽度）
    window.addEventListener('resize', function () { fitTrackText(oledTrack); });
    window.addEventListener('orientationchange', function () {
        setTimeout(function () { fitTrackText(oledTrack); }, 350);
    });
    // 布局本身变化时也重算（比只监听窗口 resize 更可靠：OLED 尺寸可能由容器决定）
    if (window.ResizeObserver && wave) {
        new ResizeObserver(function () { fitTrackText(oledTrack); }).observe(wave);
    }

    function pad(n) { return String(n).padStart(2, '0'); }

    // ================= 音频引擎 =================

    function ensureAudioCtx() {
        if (!audioCtx) {
            // 优先让 AudioContext 与 PC 的 48kHz PCM 同率，避免无谓重采样。
            try { audioCtx = new AudioContextCtor({ sampleRate: 48000 }); }
            catch (e) { audioCtx = new AudioContextCtor(); }
            gainNode = audioCtx.createGain();
            gainNode.gain.value = volume;
            // 优先走 audio 元素以支持锁屏播放；不支持时回退到 Web Audio 直出。
            if (audioCtx.createMediaStreamDestination && bgAudio) {
                streamDest = audioCtx.createMediaStreamDestination();
                gainNode.connect(streamDest);
                bgAudio.srcObject = streamDest.stream;
                mediaElementOutput = true;
            } else {
                gainNode.connect(audioCtx.destination);
            }
            nextPlayTime = 0;
        }
        if (audioCtx.state === 'suspended') audioCtx.resume();
        if (mediaElementOutput && bgAudio && bgAudio.paused) {
            bgAudio.play().catch(function (e) { log('audioStartFailed', { message: e.message }); });
        }
    }

    // 预热音频管线：必须在**第一帧到达之前**调用（在用户点击的手势里调，播放才不会被拦）。
    //
    // 要解决的是一个启动期结构性缺陷，三件事叠在一起：
    //   1. 走的是 MediaStreamDestination → <audio> 这条路（为了锁屏播放）。
    //      AudioContext 只连 MediaStreamDestination 时，因为没有连扬声器，
    //      图的输出不被消费 —— currentTime 不推进、也没有静音"垫着"。
    //   2. <audio> 播 MediaStream 要经过协商 + 缓冲才出声，这是**异步**的；
    //      而 PCM 帧在握手后 ~100ms 就开始到，比它早就开始排程了。
    //   3. 结果是头几十帧落在音频元素还没稳定的窗口里 —— 听感就是开头约 1 秒失真。
    //
    // 修法：用一个恒定 0 的源持续连到扬声器，让 (a) currentTime 立刻开始推进，
    // (b) 元素一开始播放的就是这段静音，等真正的 PCM 接上来时它已经稳定。
    // 它是 0 信号，直接听不见，也不进 MediaStream，锁屏播放不受影响。
    function warmUpAudio() {
        ensureAudioCtx();
        if (!audioCtx) return;
        if (!keepAlive) {
            try {
                keepAlive = audioCtx.createConstantSource();
                keepAlive.offset.value = 0;
                keepAlive.connect(audioCtx.destination);
                keepAlive.start();
            } catch (e) {
                keepAlive = null;   // 不支持就退回原行为，不影响功能
            }
        }
        if (mediaElementOutput && bgAudio) {
            // 显式启动一次：不等到第一帧才 play，避免开头那段排程落在未就绪的窗口里
            if (bgAudio.paused) bgAudio.play().catch(function (e) { log('audioStartFailed', { message: e.message }); });
            bgAudio.muted = false;
        }
    }

    // 下行重新开启（或换了链路）时重置排程：把上一轮遗留的 nextPlayTime 清掉，
    // 并标记需要做一次起播淡入。
    // 不清 nextPlayTime 的话，新会话第一帧会接在上一轮的播放位置上 —— 与音频元素
    // 当前的播放位置错开，听感就是"开头一段是坏的"。
    function resyncPlayback() {
        if (!audioCtx) { startupFadePending = false; return; }
        // 0.08 秒：够把老内容的尾巴打断，又短到人耳只当是"起播"，不会觉得漏了声音
        nextPlayTime = audioCtx.currentTime + 0.08;
        startupFadePending = true;
    }

    function setVolume(v) {
        volume = Math.min(1, Math.max(0, v));
        if (gainNode) gainNode.gain.value = volume;
        volLabel.textContent = tr('volumeLabel', { level: Math.round(volume * 100) });
    }

    function currentDelay() {
        if (!audioCtx) return 0;
        return Math.max(0, nextPlayTime - audioCtx.currentTime);
    }

    // 裸 PCM 帧（48kHz s16le 立体声交错，无包头）：播放 + 波形快照 + VU 电平
    function playPcmFrame(buf) {
        if (!speakerOn) return;
        if (buf.byteLength < 4 || buf.byteLength % 4 !== 0) return;
        ensureAudioCtx();
        var n = buf.byteLength >> 2; // 每声道采样数
        // 服务端发的是 s16le 立体声交错 PCM，不能把字节直接解释成 Float32。
        // 明确按小端读出，并拆成 Web Audio 所需的左右声道浮点数组。
        // WebSocket binaryType='arraybuffer' 返回的是 ArrayBuffer 本身；
        // 部分调用者可能传 TypedArray，所以两种输入都兼容。
        var view = buf instanceof ArrayBuffer
            ? new DataView(buf)
            : new DataView(buf.buffer, buf.byteOffset, buf.byteLength);
        var sourceLeft = new Float32Array(n);
        var sourceRight = new Float32Array(n);
        var step = Math.max(1, Math.floor(n / scopeL.length));
        var sum = 0, j = 0;
        for (var i = 0; i < n; i++) {
            var l = view.getInt16(i * 4, true) / 32768;
            var r = view.getInt16(i * 4 + 2, true) / 32768;
            sourceLeft[i] = l;
            sourceRight[i] = r;
            if (i % step === 0 && j < scopeL.length) {
                scopeL[j++] = l;
                sum += l * l;
            }
        }
        // 左声道抽稀进波形快照 + RMS 电平
        var rms = Math.sqrt(sum / Math.max(1, j));
        if (rms > vuLevel) vuLevel = Math.min(1, rms * 2.2);
        if (!loggedFirstPcmFrame) {
            loggedFirstPcmFrame = true;
            log('pcmReceived', { bytes: buf.byteLength, rms: rms.toFixed(4), state: audioCtx.state });
        }

        // 部分设备无法按请求创建 48kHz AudioContext；此时将 PCM 线性重采样到实际输出率。
        var outRate = audioCtx.sampleRate;
        var outFrames = outRate === 48000 ? n : Math.max(1, Math.round(n * outRate / 48000));
        var audioBuf = audioCtx.createBuffer(2, outFrames, outRate);
        var left = audioBuf.getChannelData(0);
        var right = audioBuf.getChannelData(1);
        if (outRate === 48000) {
            left.set(sourceLeft);
            right.set(sourceRight);
        } else {
            var resampleStep = n > 1 ? (n - 1) / outFrames : 0;
            for (var outIndex = 0; outIndex < outFrames; outIndex++) {
                var pos = outIndex * resampleStep;
                var i0 = Math.floor(pos);
                var frac = pos - i0;
                var i1 = Math.min(n - 1, i0 + 1);
                left[outIndex] = sourceLeft[i0] + (sourceLeft[i1] - sourceLeft[i0]) * frac;
                right[outIndex] = sourceRight[i0] + (sourceRight[i1] - sourceRight[i0]) * frac;
            }
        }

        var src = audioCtx.createBufferSource();
        src.buffer = audioBuf;
        src.connect(gainNode || audioCtx.destination);
        var now = audioCtx.currentTime;
        var delay = currentDelay();
        if (nextPlayTime < now || delay > jitter.maxAheadSec) {
            nextPlayTime = now + jitter.targetSec;
            if (delay > jitter.dropAfterSec) log('bufferAhead', { ms: Math.round(delay * 1000) });
        }
        // 起播遮蔽：只在本次会话的第一帧做一次短淡入。
        // 音频元素从"开始播放"到"真的出声"之间有段过渡，这期间的样本不可信；
        // 与其让它以咔哒/失真出现，不如淡进来 —— 听感上就只是"开始了"。
        if (startupFadePending) {
            startupFadePending = false;
            var fade = 0.12;
            gainNode.gain.setValueAtTime(0, nextPlayTime);
            gainNode.gain.linearRampToValueAtTime(volume, nextPlayTime + fade);
        }
        src.start(nextPlayTime);
        nextPlayTime += outFrames / outRate;
    }

    // ================= 信令（协议见 docs/PROTOCOL.md） =================

    function sendJson(obj) {
        if (ws && ws.readyState === WebSocket.OPEN) ws.send(JSON.stringify(obj));
    }

    function sendMode() { sendJson({ type: 'mode-change', payload: { speaker: speakerOn } }); }

    function stopHeartbeat() { clearInterval(heartbeatTimer); heartbeatTimer = null; }
    function startHeartbeat() {
        stopHeartbeat();
        heartbeatTimer = setInterval(function () { sendJson({ type: 'ping', payload: {} }); }, 10000);
    }

    function handleJson(msg) {
        var p = msg.payload || {};
        if (msg.type === 'hello') {
            // 握手应答：PC 机器名
            deviceName = p.deviceName || '';
            wsConnected = true; connecting = false; hadConnected = true;
            lastRxAt = lastPongAt = Date.now();
            log('connected', { name: deviceName || '?' });
            // 每次(重)连都重置排程：新会话的第一帧不该接在上一轮的播放位置上
            resyncPlayback();
            sendMode();
            updateMediaSession();
            refreshUI();
        } else if (msg.type === 'audio-mode') {
            // 音频档位下发：LowLatency / MaxQuality
            jitter = JITTER_PROFILES[p.mode] || JITTER_PROFILES.MaxQuality;
            log('audioMode', { modeKey: jitter.labelKey });
            refreshUI();
        } else if (msg.type === 'pong') {
            lastPongAt = Date.now();
        } else if (msg.type === 'error') {
            log('serverError', { message: p.message || msg.message || '?' });
        }
    }

    function connect() {
        if (connecting || (ws && (ws.readyState === WebSocket.CONNECTING || ws.readyState === WebSocket.OPEN))) return;
        connecting = true;
        connectAttemptAt = Date.now();
        refreshUI();
        ws = new WebSocket('wss://' + location.host + '/signal');
        ws.binaryType = 'arraybuffer';
        ws.onopen = function () {
            log('helloSent');
            ws.send(JSON.stringify({
                type: 'hello',
                payload: { deviceName: /iPhone|iPad/.test(navigator.userAgent) ? 'iPhone' : 'Android' }
            }));
            startHeartbeat();
            lastRxAt = lastPongAt = Date.now();
        };
        ws.onmessage = function (e) {
            lastRxAt = Date.now();
            if (typeof e.data === 'string') {
                try { handleJson(JSON.parse(e.data)); } catch (err) { }
            } else {
                playPcmFrame(e.data); // 下行即裸 PCM 帧
            }
        };
        ws.onclose = function (e) {
            log('connectionClosed', { code: e.code });
            if (!hadConnected && Date.now() - connectAttemptAt < 3000) {
                log('certHint', { host: location.hostname });
            }
            cleanupConn();
            setTimeout(connect, 3000);
        };
        ws.onerror = function () { log('connectionError'); };
    }

    function cleanupConn() {
        connecting = false; wsConnected = false;
        stopHeartbeat();
        if (audioCtx) { nextPlayTime = 0; }
        refreshUI();
    }

    // 失活判定：丢弃半开连接立即重连
    function dropAndReconnect(reasonKey, values) {
        log('reconnectReason', { reason: tr(reasonKey, values) });
        if (ws) {
            ws.onclose = null; ws.onerror = null; ws.onmessage = null; ws.onopen = null;
            try { ws.close(); } catch (e) { }
            ws = null;
        }
        cleanupConn();
        connect();
    }

    function forceReconnect(reasonKey) { dropAndReconnect(reasonKey || 'manualRefresh'); }

    function startLivenessWatchdog() {
        livenessTimer = setInterval(function () {
            if (!ws) return;
            var idleRx = Date.now() - lastRxAt;
            if (speakerOn && wsConnected && idleRx > 5000) {
                dropAndReconnect('inactiveConnection', { seconds: Math.round(idleRx / 1000) });
            } else if (!speakerOn && wsConnected && Date.now() - lastPongAt > 25000) {
                dropAndReconnect('missingHeartbeat');
            } else if (idleRx > 30000) {
                dropAndReconnect('idleConnection');
            }
        }, 2000);
    }

    document.addEventListener('visibilitychange', function () {
        if (!document.hidden && app && !app.classList.contains('hidden') &&
            (!ws || ws.readyState === WebSocket.CLOSED || ws.readyState === WebSocket.CLOSING)) {
            log('foregroundReconnect');
            forceReconnect('foregroundReturn');
        }
    });

    // 注意：不要在 pagehide 里发 bye —— 手机浏览器切后台/切标签页就会触发 pagehide，
    // 会导致每次后台都断连。真实退出由服务端空闲清理与新会话接管兜底。

    // ================= 屏幕常亮 / 锁屏续播 =================

    var wakeLock = null;
    async function requestWakeLock() {
        try {
            if ('wakeLock' in navigator && !wakeLock) {
                wakeLock = await navigator.wakeLock.request('screen');
                wakeLock.addEventListener('release', function () { wakeLock = null; });
                log('wakeLockOn');
            }
        } catch (e) { log('wakeLockFailed', { message: e.message }); }
    }
    document.addEventListener('visibilitychange', function () {
        if (!document.hidden && !wakeLock && speakerOn) requestWakeLock();
    });

    function updateMediaSession() {
        if (!('mediaSession' in navigator)) return;
        navigator.mediaSession.metadata = new MediaMetadata({
            title: tr(speakerOn ? 'mediaListeningTitle' : 'mediaPausedTitle'),
            artist: tr('mediaArtist'),
            album: tr('mediaAlbum'),
            artwork: [{
                src: new URL('album-art.png', document.baseURI).href,
                sizes: '512x512',
                type: 'image/png'
            }]
        });
        try { navigator.mediaSession.playbackState = speakerOn ? 'playing' : 'paused'; } catch (e) { }
        try {
            navigator.mediaSession.setActionHandler('play', function () { setSpeaker(true); });
            navigator.mediaSession.setActionHandler('pause', function () { setSpeaker(false); });
        } catch (e) { }
    }

    // ================= 三联键动作 =================

    function setSpeaker(on) {
        if (speakerOn === on) return;
        speakerOn = on;
        if (on) {
            // 先预热再开下行：让音频元素在第一帧到达前就进入播放态，
            // 并清掉上一轮遗留的 nextPlayTime（否则开头又会出现失真段）
            warmUpAudio();
            resyncPlayback();
            requestWakeLock();
            log('listeningOn');
        } else {
            log('listeningOff');
        }
        sendMode();
        updateMediaSession();
        refreshUI();
    }

    playBtn.addEventListener('click', function () { heavyClick(); setSpeaker(true); });
    stopBtn.addEventListener('click', function () { heavyClick(); setSpeaker(false); });
    refreshBtn.addEventListener('click', function () { heavyClick(); forceReconnect('manualRefresh'); });
    backBtn.addEventListener('click', function () { keyClick(580, 0.02, 0.2); location.reload(); });
    volUp.addEventListener('click', function () { keyClick(1100, 0.02, 0.25); setVolume(volume + 0.1); });
    volDown.addEventListener('click', function () { keyClick(750, 0.02, 0.25); setVolume(volume - 0.1); });

    speakerBtn.addEventListener('click', function () {
        ensureAudioCtx();
        speakerBtn.classList.add('hidden');
        log('soundEnabled');
    });

    // ================= 转盘：播放滚动 + 拖拽惯性 =================

    function ptrAngle(e, rect) {
        var cx = e.touches ? e.touches[0].clientX : e.clientX;
        var cy = e.touches ? e.touches[0].clientY : e.clientY;
        return Math.atan2(cy - (rect.top + rect.height / 2), cx - (rect.left + rect.width / 2)) * 180 / Math.PI;
    }

    function wheelStart(e) {
        e.preventDefault();
        dragging = true; angularVelocity = 0;
        lastPtrAngle = ptrAngle(e, recess.getBoundingClientRect());
        lastPtrTime = performance.now();
        keyClick(580, 0.02, 0.15);
    }

    function wheelMove(e) {
        if (!dragging) return;
        e.preventDefault();
        var a = ptrAngle(e, recess.getBoundingClientRect());
        var now = performance.now();
        var d = a - lastPtrAngle;
        if (d > 180) d -= 360;
        if (d < -180) d += 360;
        var dt = (now - lastPtrTime) / 1000;
        if (dt > 0.005) angularVelocity = d / dt;
        angle = (angle + d) % 360;
        lastPtrAngle = a; lastPtrTime = now;
    }

    function wheelEnd() {
        if (!dragging) return;
        dragging = false;
        keyClick(420, 0.015, 0.15);
    }

    disc.addEventListener('mousedown', wheelStart);
    window.addEventListener('mousemove', wheelMove);
    window.addEventListener('mouseup', wheelEnd);
    disc.addEventListener('touchstart', wheelStart, { passive: false });
    window.addEventListener('touchmove', wheelMove, { passive: false });
    window.addEventListener('touchend', wheelEnd);

    // ================= 主循环：转盘 / 时间码 / 波形 / VU =================

    var lastAnim = performance.now();

    function drawWave(now) {
        var w = wave.width, h = wave.height;
        wctx.clearRect(0, 0, w, h);
        wctx.lineWidth = 1.5;
        var playing = isPlayingVisual();
        wctx.strokeStyle = playing ? '#30d158' : '#5a606a';
        wctx.beginPath();
        var mid = h / 2;
        for (var i = 0; i < scopeL.length; i++) {
            var x = i / (scopeL.length - 1) * w;
            var y = playing ? (mid + scopeL[i] * (h * 0.42)) : mid;
            if (i === 0) wctx.moveTo(x, y); else wctx.lineTo(x, y);
        }
        wctx.stroke();

        // VU：峰值衰减
        vuLevel *= 0.94;
        for (var b = 0; b < vuBars.length; b++) {
            if (playing) {
                var wobble = 0.75 + 0.25 * Math.sin(now * 0.012 + b * 1.1);
                var lvl = Math.min(1, vuLevel * wobble * (b + 3) / vuBars.length + 0.04);
                vuBars[b].style.height = Math.round(Math.min(100, Math.max(12, lvl * 100))) + '%';
            } else {
                vuBars[b].style.height = '15%';
            }
        }
    }

    function tick(now) {
        var dt = Math.min(0.1, (now - lastAnim) / 1000);
        lastAnim = now;
        var playing = isPlayingVisual();

        if (playing && !dragging) {
            angle = (angle + 140 * dt) % 360;
            listenSeconds += dt;
            oledTimecode.textContent = pad(Math.floor(listenSeconds / 3600)) + ':' + pad(Math.floor(listenSeconds / 60) % 60) + ':' + pad(Math.floor(listenSeconds) % 60);
        } else if (!playing && !dragging && Math.abs(angularVelocity) > 0.5) {
            angle = (angle + angularVelocity * dt) % 360;
            angularVelocity *= Math.pow(0.88, dt * 60);
        } else if (!playing && !wsConnected) {
            listenSeconds = 0;
            oledTimecode.textContent = '00:00:00';
        }

        disc.style.transform = 'rotate(' + angle + 'deg)';
        drawWave(now);
        requestAnimationFrame(tick);
    }
    requestAnimationFrame(tick);

    // ================= 启动 =================

    startBtn.addEventListener('click', function () {
        startScreen.classList.add('hidden');
        app.classList.remove('hidden');
        scheduleLayout();
        speakerOn = true;
        // 必须在 connect() 之前：音频元素要在第一帧到达前进入播放态，
        // 否则握手后约 100ms 就开始排程，头几十帧会落在它还没稳定的窗口里（表现为开头失真）
        warmUpAudio();
        requestWakeLock();
        updateMediaSession();
        refreshUI();
        log('startListening');
        connect();
        startLivenessWatchdog();
        // iOS 可能仍挂着 audio 元素播放失败，3 秒后检查给兜底按钮
        setTimeout(function () {
            if ((audioCtx && audioCtx.state !== 'running') || (mediaElementOutput && bgAudio.paused)) {
                speakerBtn.classList.remove('hidden');
            }
        }, 3000);
    });
})();
