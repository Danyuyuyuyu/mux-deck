# -*- coding: utf-8 -*-
# 冒烟测试专用后端拉起器：绕过 server.main() 的"已在运行即跳过"逻辑，
# 在指定端口（默认 8799）直接起一个当前代码的服务实例，用于测试新版前端。
# 用法：py -3 tests/smoke/smoke_server.py  （端口可用环境变量 MUXUI_PORT 覆盖）
import sys, os
from http.server import ThreadingHTTPServer

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
sys.path.insert(0, ROOT)

from app.server import H  # noqa: E402

srv = ThreadingHTTPServer(('127.0.0.1', int(os.environ.get('MUXUI_PORT', '8799'))), H)
print('mux-ui smoke server on http://127.0.0.1:%d' % srv.server_address[1], flush=True)
srv.serve_forever()
