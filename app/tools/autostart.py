# -*- coding: utf-8 -*-
# autostart.py — 开机自启安装/卸载（install_autostart.bat 的 Python 继任者）
# 用法: py -3 app\tools\autostart.py install | uninstall
import json, os, subprocess, sys, time, urllib.request

BASE = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRV = os.path.join(BASE, "app", "server.py")
VBS = os.path.join(os.environ.get("APPDATA", ""), "Microsoft", "Windows", "Start Menu", "Programs", "Startup", "muxui.vbs")

def server_up(timeout=1):
    try:
        with urllib.request.urlopen("http://127.0.0.1:8765/api/version", timeout=timeout) as r:
            if r.status == 200:
                info = json.loads(r.read().decode("utf-8") or "{}")
                return bool(info.get("ok"))
    except Exception:
        pass
    return False

def find_pythonw():
    # 优先与当前解释器同目录的 pythonw.exe，其次 pyw 启动器，最后 PATH 上的 pythonw
    cand = os.path.join(os.path.dirname(sys.executable), "pythonw.exe")
    if os.path.isfile(cand):
        return cand
    for name in ("pyw", "pythonw"):
        p = subprocess.run(["where", name], capture_output=True)
        if p.returncode == 0:
            first = p.stdout.decode("mbcs", errors="replace").splitlines()[0].strip()
            if first and os.path.isfile(first):
                return first
    return ""

def install():
    pyw = find_pythonw()
    if not pyw:
        print("pythonw 未找到。请安装 Python 3 并勾选 Add to PATH。", flush=True)
        sys.exit(1)
    os.makedirs(os.path.dirname(VBS), exist_ok=True)
    cmd = '"%s" "%s"' % (pyw, SRV)
    with open(VBS, "w", encoding="ascii") as f:
        f.write('Set ws = CreateObject("WScript.Shell")\n')
        f.write('ws.Run "%s", 0, False\n' % cmd.replace('"', '""'))
    print("自启项已写入（下次登录生效）: " + VBS, flush=True)
    if server_up():
        print("服务已在运行: http://127.0.0.1:8765", flush=True)
        return
    print("正在启动服务...", flush=True)
    subprocess.Popen([pyw, SRV], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    for _ in range(10):
        if server_up():
            print("服务已启动: http://127.0.0.1:8765", flush=True)
            print("卸载自启：py -3 scripts\\autostart.py uninstall", flush=True)
            return
        time.sleep(1)
    print("服务 10 秒内未响应（http://127.0.0.1:8765/api/version）。", flush=True)
    print("请检查：pythonw 可用、端口 8765 空闲、server.py 能正常启动。", flush=True)
    sys.exit(1)

def uninstall():
    if os.path.isfile(VBS):
        os.remove(VBS)
        print("已删除自启项: " + VBS, flush=True)
    else:
        print("自启项不存在，无需删除。", flush=True)

if __name__ == "__main__":
    action = sys.argv[1] if len(sys.argv) > 1 else ""
    if action == "install":
        install()
    elif action == "uninstall":
        uninstall()
    else:
        print("用法: py -3 scripts\\autostart.py install | uninstall", flush=True)
        sys.exit(1)
