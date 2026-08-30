/* 任务控制台：日志/历史/结果摘要/提示音/打开目录/命令查看。setLog、showLogTab、updateConsoleStatus、
 * showTaskSummary、hideTaskSummary、beep、openDir、b64e 供其他模块调用，须保持全局。 */

/* ==================== 历史 ==================== */
let histItems = [];
async function loadHistory() {
  const d = await api('/api/history');
  const box = $('histBox');
  if (!d.items || !d.items.length) { box.innerHTML = '<div class="t-sec" style="padding:16px 16px;">暂无历史任务</div>'; return; }
  histItems = d.items;
  let h = '<div style="display:flex;justify-content:flex-end;margin:8px 16px 0;"><button class="btn small" id="btnHistCsv">' + ic('download') + '<span>导出 CSV</span></button></div>';
  h += '<div class="table-wrap" style="margin:8px 16px;"><table style="min-width:640px;"><tr><th>时间</th><th>类型</th><th>视频</th><th>状态</th><th style="width:150px"></th></tr>';
  d.items.forEach(function (it, i) {
    const dt = new Date(it.time).toLocaleString();
    h += '<tr><td class="mono" style="white-space:nowrap">' + esc(dt) + '</td><td>' + esc(it.type) + '</td><td class="mono" style="word-break:break-all">' + esc(it.video) + '</td><td>' + (it.status === 'done' ? '<span class="chip sm ok">' + ic('check') + '成功</span>' : '<span class="chip sm err">' + ic('xCircle') + '失败</span>') + '</td>' +
         '<td style="white-space:nowrap"><button class="btn small" onclick="histView(' + i + ')">日志</button> <button class="btn small" onclick="histRerun(' + i + ')">重跑</button> <button class="btn small ghost" data-open-dir="' + (it.video ? encodeURIComponent(it.video) : '') + '">打开</button></td></tr>';
  });
  h += '</table></div>';
  box.innerHTML = h;
  $('btnHistCsv').onclick = exportHistCsv;
}
function exportHistCsv() {
  const rows = [['时间', '类型', '视频', '状态']].concat(
    histItems.map(it => [new Date(it.time).toLocaleString(), it.type, it.video, it.status === 'done' ? '成功' : '失败']));
  const csv = '\ufeff' + rows.map(r => r.map(c => '"' + String(c == null ? '' : c).replace(/"/g, '""') + '"').join(',')).join('\r\n');
  const a = document.createElement('a');
  a.href = URL.createObjectURL(new Blob([csv], { type: 'text/csv;charset=utf-8' }));
  a.download = 'mux_history.csv';
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(a.href), 1000);
}
async function histView(i) {
  const it = histItems[i]; if (!it) return;
  const d = await api('/api/history?id=' + it.id);
  $('histLog').textContent = d.log || '(无日志)';
  $('histLogWrap').style.display = '';
}
async function histRerun(i) {
  const it = histItems[i]; if (!it) return;
  const r = await api('/api/rerun', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ id: it.id }) });
  if (r.error) { setStatus('重跑失败：' + r.error, 'err'); return; }
  setStatus('已用原参数重新提交任务', 'ok');
}
function histOpen(i) {
  const it = histItems[i];
  if (it && it.video) openDir(encodeURIComponent(it.video));
}
function openDir(enc) { fetch('/api/open?path=' + enc); }
/* 一次性事件委托：所有 data-open-dir 按钮统一走 openDir，路径含单引号不再断裂 */
/* mkvmerge 命令查看/复制（结果区的 data-cmd 按钮，命令 base64 存于属性） */
function b64e(s) { return btoa(unescape(encodeURIComponent(s))); }
function b64d(s) { try { return decodeURIComponent(escape(atob(s))); } catch (e) { return ''; } }
/* ==================== 统一日志 / 任务控制台（状态驱动：idle 收起为单行，运行自动展开） ==================== */
const logStore = { mux: '', batch: '', xt: '' };
let logTab = 'mux';
const logUi = { collapsed: true, stick: true };   // stick：用户向上翻历史时暂停自动跟随
function setConsoleCollapsed(collapsed) {
  logUi.collapsed = collapsed;
  const panel = $('consolePanel');
  if (panel) panel.classList.toggle('collapsed', collapsed);
  const bar = $('consoleCollapsed');
  if (bar) bar.setAttribute('aria-expanded', collapsed ? 'false' : 'true');
  if (!collapsed) { logUi.stick = true; renderLog(); }
}
function updateConsoleStatus() {
  const el = $('consoleStatus');
  if (!el) return;
  let txt = '尚未开始', cls = 'info';
  const single = getSingleTaskStatus();
  const batch = getBatchTaskStatus();
  if (single.running) { txt = '正在封装…'; cls = 'run'; }
  else if (batch.running) { txt = '正在批量封装…'; cls = 'run'; }
  else if (single.result) { txt = single.result.text; cls = single.result.cls; }
  el.textContent = txt;
  el.className = 'cc-status ' + cls;
}
function showLogTab(tab) {
  logTab = tab;
  document.querySelectorAll('.ltab').forEach(function (b) { b.classList.toggle('active', b.dataset.tab === tab); });
  var isHist = tab === 'hist';
  $('log').style.display = isHist ? 'none' : '';
  $('histBox').style.display = isHist ? '' : 'none';
  $('histLogWrap').style.display = 'none';
  if (isHist || logStore[tab]) setConsoleCollapsed(false);   // 切到有内容的 Tab 才展开，idle 保持紧凑
  if (isHist) loadHistory();
  else renderLog();
}
function setLog(tab, text) {
  logStore[tab] = text || '';
  if (!text) logUi.stick = true;   // 新任务清空日志：恢复跟随最新
  if (tab === logTab) {
    if (text && logUi.collapsed) setConsoleCollapsed(false);   // 日志开始输出 → 自动展开
    renderLog();
  }
}
function renderLog() {
  var el = $('log');
  el.textContent = logStore[logTab] || '';
  if (logUi.stick) el.scrollTop = el.scrollHeight;   // 用户向上翻看时不再强制拉底
}

/* ==================== 任务结果摘要（成功/失败后优先展示结果而非原始日志） ==================== */
function hideTaskSummary() {
  const el = $('taskSummary');
  if (el) { el.style.display = 'none'; el.innerHTML = ''; }
}
function showTaskSummary(cls, icon, title, opts) {
  opts = opts || {};
  const el = $('taskSummary');
  if (!el) return;
  let h = '<div class="ts-head ' + cls + '">' + ic(icon) + '<span>' + esc(title) + '</span>';
  if (opts.warn) h += '<span class="chip sm warn ts-warn">' + ic('alertTriangle') + '<span>' + esc(opts.warn) + '</span></span>';
  h += '</div>';
  if (opts.detail) h += '<div class="ts-meta">' + esc(opts.detail) + '</div>';
  if (opts.path) h += '<div class="ts-meta mono">' + esc(opts.path) + '</div>';
  if (opts.elapsed) h += '<div class="ts-meta">用时 ' + esc(opts.elapsed) + '</div>';
  h += '<div class="ts-actions">';
  if (opts.path) h += '<button class="btn small" data-open-dir="' + encodeURIComponent(opts.path) + '">' + ic('arrowUpRight') + '<span>打开输出目录</span></button>';
  if (opts.warn && opts.warnLines && opts.warnLines.length) h += '<button class="btn small" id="btnTsDetail">' + ic('eye') + '<span>查看详情</span></button>';
  h += '<button class="btn small" id="btnTsLog">' + ic('terminal') + '<span>查看日志</span></button>';
  if (opts.retry) h += '<button class="btn small" id="btnTsRetry">' + ic('refreshCw') + '<span>重试</span></button>';
  h += '</div>';
  if (opts.warnLines && opts.warnLines.length) h += '<pre class="log-pre ts-detail" id="tsDetail" style="display:none">' + esc(opts.warnLines.join('\n')) + '</pre>';
  el.innerHTML = h;
  el.style.display = '';
  setConsoleCollapsed(true);   // 摘要优先，原始日志默认收起（可经「查看日志」展开）
  $('btnTsLog').onclick = function () { setConsoleCollapsed(false); };
  const det = $('btnTsDetail');
  if (det) det.onclick = function () { const d = $('tsDetail'); if (d) d.style.display = d.style.display === 'none' ? '' : 'none'; };
  const retry = $('btnTsRetry');
  if (retry) retry.onclick = function () { hideTaskSummary(); $('btnStart').click(); };   // 复用开始封装的完整校验，参数可再改
}

/* ==================== 提示音 ==================== */
function beep() {
  try {
    if (!$('soundToggle').checked) return;
    var ctx = new (window.AudioContext || window.webkitAudioContext)();
    var o = ctx.createOscillator(), g = ctx.createGain();
    o.connect(g); g.connect(ctx.destination);
    o.frequency.value = 880; g.gain.value = .12;
    o.start(); o.stop(ctx.currentTime + .18);
  } catch (e) {}
}

/* ==================== 初始化（由 init.js bootstrap 统一调用，仅执行一次） ==================== */
function initConsole() {
document.addEventListener('click', function (e) {
  const el = e.target.closest('[data-open-dir]');
  if (el) openDir(el.dataset.openDir || '');
});
document.addEventListener('click', function (e) {
  const btn = e.target.closest('[data-cmd]');
  if (!btn) return;
  const cmd = b64d(btn.dataset.cmd || '');
  const old = document.getElementById('cmdPop');
  if (old) old.remove();
  if (btn.dataset.cmdOpen === '1') { btn.dataset.cmdOpen = ''; return; }   // 再点一次收起
  btn.dataset.cmdOpen = '1';
  const ov = document.createElement('div');
  ov.id = 'cmdPop';
  ov.style.cssText = 'position:fixed;inset:0;background:rgba(0,0,0,.55);z-index:120;display:flex;align-items:center;justify-content:center;';
  const box = document.createElement('div');
  box.style.cssText = 'background:var(--surface-1);border:1px solid var(--border);border-radius:12px;padding:18px 20px;max-width:860px;width:92%;box-shadow:0 10px 40px rgba(0,0,0,.5);';
  box.innerHTML = '<div style="display:flex;align-items:center;gap:10px;margin-bottom:10px;"><b>mkvmerge 封装命令</b><span class="t-cap">可直接复制到流水线/CI 复现本次封装</span><span style="flex:1"></span>'
    + '<button class="btn small" id="cmdCopy">' + ic('download') + '<span>复制</span></button>'
    + '<button class="btn small ghost" id="cmdClose">关闭</button></div>'
    + '<pre class="log-pre" style="max-height:50vh;white-space:pre-wrap;word-break:break-all;margin:0">' + esc(cmd) + '</pre>';
  ov.appendChild(box);
  document.body.appendChild(ov);
  ov.addEventListener('click', ev => { if (ev.target === ov) ov.remove(); });
  $('cmdClose').onclick = () => { ov.remove(); btn.dataset.cmdOpen = ''; };
  $('cmdCopy').onclick = async () => {
    try { await navigator.clipboard.writeText(cmd); $('cmdCopy').innerHTML = ic('check') + '<span>已复制</span>'; }
    catch (ex) { setStatus('复制失败：' + ex, 'err'); }
  };
});
$('log').addEventListener('scroll', function () {
  var el = $('log');
  logUi.stick = el.scrollTop + el.clientHeight >= el.scrollHeight - 24;
});
$('btnLogClear').onclick = function () { logStore[logTab] = ''; renderLog(); };
$('consoleCollapsed').onclick = function () { setConsoleCollapsed(false); };
$('btnLogFold').onclick = function () { setConsoleCollapsed(true); };
(function () {
  try { $('soundToggle').checked = localStorage.getItem('muxui_sound') !== '0'; } catch (e) {}
  $('soundToggle').onchange = function () { try { localStorage.setItem('muxui_sound', this.checked ? '1' : '0'); } catch (e) {} };
})();
}
