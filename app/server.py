# -*- coding: utf-8 -*-
# 薄路由：静态内容 + Origin 校验 + 分发到 features.ROUTES
import json, os, sys, time, urllib.request
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlparse, parse_qs

BASE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, BASE)

from app import core
from app.features import ROUTES, find

HOST = "127.0.0.1"

class H(BaseHTTPRequestHandler):
    def log_message(self, *a):
        pass

    def _send(self, code, body, ctype="application/json; charset=utf-8"):
        if isinstance(body, dict):
            data = json.dumps(body, ensure_ascii=False).encode("utf-8")
        else:
            data = body if isinstance(body, bytes) else str(body).encode("utf-8")
        self.send_response(code)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(data)))
        self.send_header("Cache-Control", "no-store")
        self.end_headers()
        self.wfile.write(data)

    def _origin_ok(self):
        origin = self.headers.get("Origin")
        if not origin:
            return True
        try:
            o = urlparse(origin)
            return bool(o.netloc) and o.netloc == self.headers.get("Host", "")
        except Exception:
            return False

    def do_GET(self):
        u = urlparse(self.path)
        if u.path.startswith("/api/") and u.path != "/api/version" and not self._origin_ok():
            self._send(403, {"error": "forbidden"})
            return
        if u.path == "/favicon.ico":
            self.send_response(204)
            self.end_headers()
            return
        if u.path in ("/", "/index.html"):
            try:
                with open(os.path.join(BASE, "app", "index.html"), "rb") as f:
                    self._send(200, f.read(), "text/html; charset=utf-8")
            except Exception as ex:
                self._send(500, {"error": str(ex)})
            return
        fn = find("GET", u.path)
        if fn is None:
            self._send(404, {"error": "not found"})
            return
        q = parse_qs(u.query)
        try:
            out = fn(q)
            if isinstance(out, dict) and "_raw" in out:
                self._send(200 if not out.get("error") else 404, out["_raw"] if out["_raw"] is not None else b"", out.get("_ctype", "application/octet-stream"))
            else:
                self._send(200, out)
        except Exception as ex:
            self._send(500, {"error": str(ex)})

    def do_POST(self):
        u = urlparse(self.path)
        if not self._origin_ok():
            self._send(403, {"error": "forbidden"})
            return
        n = int(self.headers.get("Content-Length", 0) or 0)
        try:
            body = json.loads(self.rfile.read(n).decode("utf-8")) if n else {}
        except Exception:
            self._send(400, {"error": "bad json"})
            return
        fn = find("POST", u.path)
        if fn is None:
            self._send(404, {"error": "not found"})
            return
        try:
            out = fn(body)
            self._send(200, out)
        except Exception as ex:
            self._send(500, {"error": str(ex)})

def main():
    try:
        with urllib.request.urlopen("http://127.0.0.1:8765/api/version", timeout=2) as resp:
            if resp.status == 200:
                try:
                    info = json.loads(resp.read().decode("utf-8") or "{}")
                except Exception:
                    info = {}
                if info.get("ok"):
                    print("Mux UI already running on http://127.0.0.1:8765 (skip start)")
                    return
    except Exception:
        pass
    try:
        port = 8765
        srv = None
        for p in range(port, port + 10):
            try:
                srv = ThreadingHTTPServer((HOST, p), H)
                port = p
                break
            except OSError:
                continue
        if not srv:
            print("no free port")
            return
        print("MUX UI running at http://%s:%d" % (HOST, port))
        print("building file index for drag-drop...")
        core.start_background()
        srv.serve_forever()
    except KeyboardInterrupt:
        pass

if __name__ == "__main__":
    main()
