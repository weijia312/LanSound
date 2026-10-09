"""用 Chrome DevTools 协议量手机端页面的真实 DOM 尺寸，定位横向溢出。

用法：
    python tools/measure-phone.py <url> [宽x高]

实现说明：不走 Target.attachToTarget + sessionId（容易 "Session not found"），
而是用 /json/new 建一个标签页，直接连它自己的 webSocketDebuggerUrl。
"""
import base64
import json
import os
import socket
import struct
import subprocess
import sys
import tempfile
import time
import urllib.parse
import urllib.request

CHROME = r"C:\Program Files\Google\Chrome\Application\chrome.exe"
ARGS = sys.argv[1:]
# 支持 --js "<表达式>"：用自定义表达式替换内置度量（用于临时探针）
CUSTOM_JS = None
if "--js" in ARGS:
    i = ARGS.index("--js")
    CUSTOM_JS = ARGS[i + 1]
    del ARGS[i:i + 2]

# 支持 --png <路径>：用 CDP 截图。
# 必须走 Emulation.setDeviceMetricsOverride —— 直接给 chrome 传 --window-size
# 在 headless 下**不会**改变 CSS 视口宽度（页面仍按默认约 800px 布局），
# 截出来的图只是被裁了一刀，看起来像"内容溢出"，其实不是。
SHOT = None
if "--png" in ARGS:
    i = ARGS.index("--png")
    SHOT = ARGS[i + 1]
    del ARGS[i:i + 2]

URL = ARGS[0] if ARGS else "http://127.0.0.1:8899/_diag.html"
SIZE = ARGS[1] if len(ARGS) > 1 else "390x844"
W, H = (int(x) for x in SIZE.lower().split("x"))
PORT = 9222

MEASURE_JS = r"""
(function () {
  var o = [];
  function m(sel) {
    var e = document.querySelector(sel);
    if (!e) { o.push(sel + ' = MISSING'); return; }
    var r = e.getBoundingClientRect(), cs = getComputedStyle(e);
    o.push(sel + ' | x=' + r.left.toFixed(1) + ' w=' + r.width.toFixed(1) +
           ' right=' + r.right.toFixed(1) +
           ' | minW=' + cs.minWidth + ' pad=' + cs.paddingLeft + '/' + cs.paddingRight);
  }
  var de = document.documentElement;
  o.push('doc.clientW=' + de.clientWidth + ' doc.scrollW=' + de.scrollWidth +
         ' body.scrollW=' + document.body.scrollWidth + ' innerW=' + window.innerWidth);
  ['body','.device-viewport','.tp7-chassis','.screen-housing','.oled-content',
   '.oled-top','.oled-center','.oled-track','.wheel-container','.wheel-recess',
   '.transport-bar'].forEach(m);
  var over = [];
  document.querySelectorAll('body *').forEach(function (e) {
    var r = e.getBoundingClientRect();
    if (r.width > 0 && r.right > de.clientWidth + 0.5) {
      over.push((e.className || e.tagName) + '@' + r.right.toFixed(1) + ' w=' + r.width.toFixed(1));
    }
  });
  o.push('OVERFLOW(' + over.length + '): ' + (over.length ? over.slice(0, 12).join(' ;; ') : 'none'));
  return o.join('\n');
})()
"""


class WS:
    """极简 WebSocket 客户端，够跑 CDP 用。"""

    def __init__(self, url, timeout=20):
        u = urllib.parse.urlparse(url)
        self.s = socket.create_connection((u.hostname, u.port), timeout=timeout)
        key = base64.b64encode(os.urandom(16)).decode()
        self.s.sendall(
            f"GET {u.path} HTTP/1.1\r\nHost: {u.hostname}:{u.port}\r\n"
            f"Upgrade: websocket\r\nConnection: Upgrade\r\n"
            f"Sec-WebSocket-Key: {key}\r\nSec-WebSocket-Version: 13\r\n\r\n".encode())
        buf = b""
        while b"\r\n\r\n" not in buf:
            buf += self.s.recv(4096)
        if b"101" not in buf.split(b"\r\n")[0]:
            raise RuntimeError("ws handshake: " + buf.split(b"\r\n")[0].decode(errors="replace"))

    def send(self, obj):
        data = json.dumps(obj).encode()
        hdr = bytearray([0x81])
        n = len(data)
        if n < 126:
            hdr.append(0x80 | n)
        elif n < 65536:
            hdr.append(0x80 | 126); hdr += struct.pack(">H", n)
        else:
            hdr.append(0x80 | 127); hdr += struct.pack(">Q", n)
        mask = os.urandom(4)
        hdr += mask
        self.s.sendall(bytes(hdr) + bytes(b ^ mask[i % 4] for i, b in enumerate(data)))

    def _rd(self, n):
        out = b""
        while len(out) < n:
            c = self.s.recv(n - len(out))
            if not c:
                raise EOFError
            out += c
        return out

    def recv(self):
        b1, b2 = self._rd(2)
        ln = b2 & 0x7F
        if ln == 126:
            ln = struct.unpack(">H", self._rd(2))[0]
        elif ln == 127:
            ln = struct.unpack(">Q", self._rd(8))[0]
        return json.loads(self._rd(ln).decode())

    def call(self, mid, method, params=None):
        self.send({"id": mid, "method": method, "params": params or {}})
        while True:
            r = self.recv()
            if r.get("id") == mid:
                return r


def main():
    profile = tempfile.mkdtemp()
    chrome = subprocess.Popen(
        [CHROME, "--headless=new", "--disable-gpu", "--no-first-run",
         f"--remote-debugging-port={PORT}", f"--window-size={W},{H}",
         f"--user-data-dir={profile}", "about:blank"],
        stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    try:
        for _ in range(60):
            try:
                urllib.request.urlopen(f"http://127.0.0.1:{PORT}/json/version", timeout=2).read()
                break
            except Exception:
                time.sleep(0.25)
        else:
            print("  Chrome 未就绪")
            return 1

        # 建标签页并直接连它。
        # 注意：新版 Chrome 的 /json/new 只接受 PUT（GET 会 405），所以显式指定 method。
        target = None
        new_url = f"http://127.0.0.1:{PORT}/json/new?{urllib.parse.quote(URL, safe='')}"
        for method in ("PUT", "GET"):
            try:
                req = urllib.request.Request(new_url, method=method)
                target = json.loads(urllib.request.urlopen(req, timeout=10).read())
                break
            except Exception:
                continue
        if target is None:
            # 退路：连已有的 about:blank 标签页再导航过去
            targets = json.loads(urllib.request.urlopen(
                f"http://127.0.0.1:{PORT}/json/list", timeout=10).read())
            pages = [t for t in targets if t.get("type") == "page"]
            if not pages:
                print("  找不到可用的页面目标")
                return 1
            target = pages[0]

        ws = WS(target["webSocketDebuggerUrl"])

        ws.call(1, "Emulation.setDeviceMetricsOverride",
                {"width": W, "height": H, "deviceScaleFactor": 1, "mobile": True})
        ws.call(2, "Page.navigate", {"url": URL})
        time.sleep(3.0)

        r = ws.call(3, "Runtime.evaluate",
                    {"expression": CUSTOM_JS or MEASURE_JS, "returnByValue": True})
        val = r.get("result", {}).get("result", {}).get("value")
        print(val if val else json.dumps(r, ensure_ascii=False)[:900])

        if SHOT:
            shot = ws.call(4, "Page.captureScreenshot",
                           {"format": "png", "captureBeyondViewport": False})
            data = shot.get("result", {}).get("data")
            if data:
                with open(SHOT, "wb") as fh:
                    fh.write(base64.b64decode(data))
                print(f"saved: {SHOT}")
            else:
                print("  截图失败: " + json.dumps(shot, ensure_ascii=False)[:300])
        return 0
    finally:
        chrome.terminate()


if __name__ == "__main__":
    sys.exit(main())
