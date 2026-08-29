# tests/ — 测试

## jsdom 整页回归（前端交互）

零浏览器依赖：jsdom 加载 `app/ui` 全部前端（外部 script 内联组装 + mock fetch 按 URL 分发假数据），验证弹窗/点击/轮询/底部状态条等全交互。

```powershell
cd tests\jsdom
npm install          # 首次（可用 --registry=https://registry.npmmirror.com）
npm test             # 即 node test.js，末尾输出 PASS 总数，非 0 退出码 = 有失败
```

## 纯函数单元测试（Python，stdlib unittest）

覆盖 mux_cli 的命名模板解析、命令行转义、ASS 时间解析与内容体检判定：

```powershell
py -3 -m unittest discover -s tests -v
```

## 服务冒烟（端到端，需服务运行中）

```powershell
py -3 app\tools\smoke_test.py    # 13 项端点/页面断言，服务未运行会提示
```

约定：改前端跑 jsdom 回归；改后端先 py_compile 全量 + unittest，再重启服务跑冒烟；涉及真实封装链路时用测试素材做一次端到端。
