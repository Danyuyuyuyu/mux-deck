/* ==================== 图标（Lucide 风格，内联 SVG） ==================== */
const ICONS = {
  film:'<rect x="3" y="3" width="18" height="18" rx="2"/><path d="M7 3v18"/><path d="M17 3v18"/><path d="M3 7.5h4"/><path d="M3 12h18"/><path d="M3 16.5h4"/><path d="M17 7.5h4"/><path d="M17 16.5h4"/>',
  clapperboard:'<path d="M20.2 6 3 11l-.9-2.4c-.3-1.1.3-2.2 1.3-2.5l13.5-4c1.1-.3 2.2.3 2.5 1.3Z"/><path d="m6.2 5.3 3.1 3.9"/><path d="m12.4 3.4 3.1 4"/><path d="M3 11h18v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2Z"/>',
  captions:'<rect x="3" y="5" width="18" height="14" rx="2"/><path d="M7 15v-4"/><path d="M11 15v-4"/><path d="m15 15 2-4 2 4"/>',
  fileText:'<path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><path d="M14 2v6h6"/><path d="M8 13h8"/><path d="M8 17h5"/>',
  type:'<path d="M4 7V5h16v2"/><path d="M9 20h6"/><path d="M12 5v15"/>',
  music:'<path d="M9 18V5l12-2v13"/><circle cx="6" cy="18" r="3"/><circle cx="18" cy="16" r="3"/>',
  folder:'<path d="M20 20a2 2 0 0 0 2-2V8a2 2 0 0 0-2-2h-7.9a2 2 0 0 1-1.69-.9L9.6 3.9A2 2 0 0 0 7.93 3H4a2 2 0 0 0-2 2v13a2 2 0 0 0 2 2Z"/>',
  folderOpen:'<path d="m6 14 1.5-2.9A2 2 0 0 1 9.24 10H20a2 2 0 0 1 1.94 2.5l-1.54 6a2 2 0 0 1-1.95 1.5H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h3.9a2 2 0 0 1 1.69.9l.81 1.2a2 2 0 0 0 1.67.9H18a2 2 0 0 1 2 2v2"/>',
  folderOutput:'<path d="M2 7.5V5a2 2 0 0 1 2-2h3.9a2 2 0 0 1 1.69.9l.81 1.2a2 2 0 0 0 1.67.9H20a2 2 0 0 1 2 2v10a2 2 0 0 1-2 2H4a2 2 0 0 1-2-1.5Z"/><path d="M13 13h6"/><path d="m16 10 3 3-3 3"/>',
  drive:'<line x1="22" y1="12" x2="2" y2="12"/><path d="M5.45 5.11 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.45-6.89A2 2 0 0 0 16.76 4H7.24a2 2 0 0 0-1.79 1.11Z"/>',
  check:'<path d="M20 6 9 17l-5-5"/>',
  checkCircle:'<circle cx="12" cy="12" r="10"/><path d="m9 12 2 2 4-4"/>',
  alertTriangle:'<path d="m21.73 18-8-14a2 2 0 0 0-3.48 0l-8 14A2 2 0 0 0 4 21h16a2 2 0 0 0 1.73-3Z"/><path d="M12 9v4"/><path d="M12 17h.01"/>',
  xCircle:'<circle cx="12" cy="12" r="10"/><path d="m15 9-6 6"/><path d="m9 9 6 6"/>',
  info:'<circle cx="12" cy="12" r="10"/><path d="M12 16v-4"/><path d="M12 8h.01"/>',
  loader:'<path d="M21 12a9 9 0 1 1-6.219-8.56"/>',
  list:'<path d="M3 12h.01"/><path d="M3 18h.01"/><path d="M3 6h.01"/><path d="M8 12h13"/><path d="M8 18h13"/><path d="M8 6h13"/>',
  eye:'<path d="M2 12s3.5-7 10-7 10 7 10 7-3.5 7-10 7-10-7-10-7Z"/><circle cx="12" cy="12" r="3"/>',
  image:'<rect x="3" y="3" width="18" height="18" rx="2"/><circle cx="9" cy="9" r="2"/><path d="m21 15-3.086-3.086a2 2 0 0 0-2.828 0L6 21"/>',
  grid:'<rect x="3" y="3" width="7" height="7" rx="1"/><rect x="14" y="3" width="7" height="7" rx="1"/><rect x="3" y="14" width="7" height="7" rx="1"/><rect x="14" y="14" width="7" height="7" rx="1"/>',
  download:'<path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><path d="m7 10 5 5 5-5"/><path d="M12 15V3"/>',
  plus:'<path d="M5 12h14"/><path d="M12 5v14"/>',
  sparkles:'<path d="m12 3-1.9 5.8a2 2 0 0 1-1.287 1.288L3 12l5.813 1.912A2 2 0 0 1 10.1 15.2L12 21l1.9-5.8a2 2 0 0 1 1.287-1.288L21 12l-5.813-1.912A2 2 0 0 1 13.9 8.8Z"/>',
  trash:'<path d="M3 6h18"/><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6"/><path d="M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/><path d="M10 11v6"/><path d="M14 11v6"/>',
  refreshCw:'<path d="M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8"/><path d="M21 3v5h-5"/><path d="M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16"/><path d="M8 16H3v5"/>',
  refreshCcw:'<path d="M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8"/><path d="M3 3v5h5"/>',
  sliders:'<path d="M21 4h-7"/><path d="M10 4H3"/><path d="M21 12h-9"/><path d="M8 12H3"/><path d="M21 20h-5"/><path d="M12 20H3"/><path d="M14 2v4"/><path d="M8 10v4"/><path d="M16 18v4"/>',
  moon:'<path d="M12 3a6 6 0 0 0 9 9 9 9 0 1 1-9-9Z"/>',
  sun:'<circle cx="12" cy="12" r="4"/><path d="M12 2v2"/><path d="M12 20v2"/><path d="m4.93 4.93 1.41 1.41"/><path d="m17.66 17.66 1.41 1.41"/><path d="M2 12h2"/><path d="M20 12h2"/><path d="m6.34 17.66-1.41 1.41"/><path d="m19.07 4.93-1.41 1.41"/>',
  clock:'<circle cx="12" cy="12" r="10"/><polyline points="12 6 12 12 16 14"/>',
  layers:'<path d="m12.83 2.18a2 2 0 0 0-1.66 0L2.6 6.08a1 1 0 0 0 0 1.83l8.58 3.91a2 2 0 0 0 1.66 0l8.58-3.9a1 1 0 0 0 0-1.83Z"/><path d="m22 17.65-9.17 4.16a2 2 0 0 1-1.66 0L2 17.65"/><path d="m22 12.65-9.17 4.16a2 2 0 0 1-1.66 0L2 12.65"/>',
  play:'<polygon points="6 3 20 12 6 21 6 3"/>',
  square:'<rect x="4" y="4" width="16" height="16" rx="2"/>',
  search:'<circle cx="11" cy="11" r="8"/><path d="m21 21-4.3-4.3"/>',
  arrowUpRight:'<path d="M7 7h10v10"/><path d="M7 17 17 7"/>',
  arrowUp:'<path d="m5 12 7-7 7 7"/><path d="M12 19V5"/>',
  terminal:'<polyline points="4 17 10 11 4 5"/><line x1="12" y1="19" x2="20" y2="19"/>',
  shieldCheck:'<path d="M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z"/><path d="m9 12 2 2 4-4"/>',
  chevronDown:'<path d="m6 9 6 6 6-6"/>'
};
function ic(name, cls) {
  const d = ICONS[name] || ICONS[String(name || '').replace(/-([a-z])/g, function (m, l) { return l.toUpperCase(); })];
  if (!d) return '';
  return '<svg class="ic' + (cls ? ' ' + cls : '') + '" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' + d + '</svg>';
}
document.querySelectorAll('[data-ic]').forEach(function (el) { el.innerHTML = ic(el.dataset.ic); });

/* ==================== 基础工具 ==================== */
const $ = id => document.getElementById(id);
function esc(s) { return (s || '').replace(/&/g,'&amp;').replace(/"/g,'&quot;').replace(/</g,'&lt;').replace(/>/g,'&gt;'); }
function fireChange(el) { el.dispatchEvent(new Event('change', { bubbles: true })); }
const FILTERS = {
  video: ['.mkv','.mp4','.m2ts','.ts','.avi','.mov','.webm','.flv','.wmv','.m4v'],
  sub:   ['.ass','.ssa','.srt'],
  font:  ['.ttf','.otf','.ttc','.otc','.woff','.woff2'],
  audio: ['.mka','.flac','.aac','.m4a','.mp3','.opus','.ogg','.wav','.ac3','.dts','.eac3'],
  any: null, dir: null
};
const VEXT = new Set(FILTERS.video), SEXT = new Set(FILTERS.sub), FEXT = new Set(FILTERS.font);
function isScName(n) { return /(?:^|[._\- ])(?:sc|chs|jpsc)(?:[._\- ]|$)/i.test(n); }
function isTcName(n) { return /(?:^|[._\- ])(?:tc|cht|jptc)(?:[._\- ]|$)/i.test(n); }
const BR = { setter: null, filter: 'any', path: 'D:\\Video' };

async function api(url, opts) {
  try {
    const r = await fetch(url, opts);
    setOffline(false);
    return r.json();
  } catch (ex) {
    if (ex instanceof TypeError) setOffline(true); // fetch 网络层失败 = 服务不可达
    throw ex;
  }
}
/* ---------- 服务断线探测：横幅 + 每 3s 重试，恢复自动隐藏 ---------- */
let __offline = null;
function setOffline(bad) {
  if (__offline === bad) return;
  __offline = bad;
  const bar = document.getElementById('offlineBar');
  if (bar) bar.style.display = bad ? 'block' : 'none';
  if (bad && !setOffline._timer) {
    setOffline._timer = setInterval(async () => {
      try {
        const r = await fetch('/api/version');
        if (r.ok) { clearInterval(setOffline._timer); setOffline._timer = null; setOffline(false); }
      } catch (e) { /* 仍在断线 */ }
    }, 3000);
  }
}
/* 初始探测一次：打开页面时后端就没在跑也能立刻给出提示 */
fetch('/api/version').then(r => { if (r.ok) setOffline(false); }).catch(() => setOffline(true));
function truncMid(t, max) {
  t = String(t || '');
  var pi = t.search(/[A-Za-z]:\\/);
  if (pi >= 0) {
    var head = t.slice(0, pi);
    var path = t.slice(pi);
    return (head.length > 10 ? truncMid(head, 10) : head) + path;
  }
  if (t.length <= max) return t;
  const keep = Math.max(4, Math.floor((max - 1) / 2));
  return t.slice(0, keep) + '…' + t.slice(t.length - keep);
}
const STATUS_ICON = { ok:'checkCircle', err:'xCircle', run:'loader', '':'info' };
function setStatus(msg, cls) {
  const s = $('status');
  const k = cls || '';
  s.innerHTML = ic(STATUS_ICON[k] || 'info', k === 'run' ? 'spin' : '') + '<span>' + esc(truncMid(String(msg || ''), 36)) + '</span>';
  s.className = k;
  s.title = String(msg || '');
}
function setResult(msg) { $('result').textContent = msg; }

/* ==================== 设置面板（主题 / 强调色） ==================== */
(function () {
  var prefs = {};
  try { prefs = JSON.parse(localStorage.getItem('muxui_prefs') || '{}'); } catch (e) {}
  document.body.dataset.theme = prefs.theme || 'dark';
  document.body.dataset.accent = prefs.accent || 'blue';
  var DOT = { blue:'#4F8DFF', purple:'#A98BF5', green:'#3FB97F', orange:'#F3B64D', pink:'#F57FB8' };
  var pop = $('settingsPop');
  var html =
    '<div class="pop-title">主题</div>' +
    '<div class="pop-row">' +
      '<button type="button" class="pref theme" data-v="dark" data-ic="moon">深色</button>' +
      '<button type="button" class="pref theme" data-v="light" data-ic="sun">浅色</button>' +
    '</div>' +
    '<div class="pop-title">强调色</div>' +
    '<div class="pop-row dots">' +
      Object.keys(DOT).map(function (a) { return '<button type="button" class="pref dot" data-v="' + a + '" style="--d:' + DOT[a] + '" aria-label="强调色 ' + a + '"></button>'; }).join('') +
    '</div>' +
    '<div class="pop-title">系统</div>' +
    '<button type="button" class="pref menu" id="popEnvBtn"><span data-ic="sliders" aria-hidden="true"></span>环境检测 / 安装组件</button>';
  pop.innerHTML = html;
  pop.querySelectorAll('[data-ic]').forEach(function (el) { el.innerHTML = ic(el.dataset.ic); });
  var btn = $('btnSettings');
  btn.onclick = function (e) { e.stopPropagation(); var open = pop.classList.toggle('open'); btn.setAttribute('aria-expanded', open ? 'true' : 'false'); };
  document.addEventListener('click', function (e) { if (!pop.contains(e.target) && e.target !== btn) { pop.classList.remove('open'); btn.setAttribute('aria-expanded', 'false'); } });
  function sync() {
    pop.querySelectorAll('.pref.theme').forEach(function (b) { b.classList.toggle('active', b.dataset.v === document.body.dataset.theme); });
    pop.querySelectorAll('.pref.dot').forEach(function (b) { b.classList.toggle('active', b.dataset.v === document.body.dataset.accent); });
  }
  function save() {
    try { localStorage.setItem('muxui_prefs', JSON.stringify({ theme: document.body.dataset.theme, accent: document.body.dataset.accent })); } catch (e) {}
  }
  pop.addEventListener('click', function (e) {
    var b = e.target.closest('.pref'); if (!b) return;
    if (b.id === 'popEnvBtn') { pop.classList.remove('open'); btn.setAttribute('aria-expanded', 'false'); openEnv(); return; }
    if (b.classList.contains('theme')) document.body.dataset.theme = b.dataset.v;
    else document.body.dataset.accent = b.dataset.v;
    sync(); save();
  });
  sync();
})();

/* ==================== 模式切换 ==================== */
function switchMode(mode) {
  document.querySelectorAll('.mode').forEach(function (m) { m.classList.toggle('active', m.id === 'mode-' + mode); });
  document.querySelectorAll('.mode-tab').forEach(function (b) { b.classList.toggle('active', b.dataset.mode === mode); });
  refreshSticky();
  refreshBatchSticky();
}

/* ==================== 视频文件卡片 ==================== */
function pickVideoPath(v) { $('video').value = v; fireChange($('video')); }
function renderVideoCard() {
  const card = $('videoCard');
  const v = ($('video') && $('video').value || '').trim(); // #video 由本函数创建，首次调用时可能不存在
  const browse = function (e) { if (e) e.stopPropagation(); openBrowser(pickVideoPath, 'video', $('video').value, 'video'); };
  if (!v) {
    card.className = 'file-card empty';
    card.innerHTML =
      '<span class="file-ic" data-ic="film"></span>' +
      '<div class="file-title">选择视频</div>' +
      '<div class="t-sec">将 MKV 文件拖拽到这里，或点击浏览</div>' +
      '<button type="button" class="btn" id="cardBrowse"><span data-ic="folder-open"></span>浏览文件</button>' +
      '<input id="video" type="text" class="visually-hidden" autocomplete="off">';
    card.querySelector('[data-ic="film"]').innerHTML = ic('film');
    card.querySelector('[data-ic="folder-open"]').innerHTML = ic('folder-open');
    card.onclick = function () { if (window.getSelection().toString()) return; browse(); };
    card.onkeydown = function (e) { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); browse(); } };
    $('cardBrowse').onclick = browse;
  } else {
    const name = v.split(/[\\/]/).pop() || v;
    const ext = name.slice(name.lastIndexOf('.') + 1).toLowerCase() || '?';
    card.className = 'file-card';
    card.onclick = null;
    card.innerHTML =
      '<span class="file-ic" data-ic="film"></span>' +
      '<div class="file-meta">' +
        '<div class="file-name" title="' + esc(v) + '">' + esc(name) + '</div>' +
        '<div class="file-path">' + esc(v) + '</div>' +
      '</div>' +
      '<span class="chip ok"><span data-ic="check"></span>已识别</span>' +
      '<div class="file-actions">' +
        '<button type="button" class="btn small" id="cardReplace"><span data-ic="refreshCw"></span>更换</button>' +
        '<button type="button" class="btn small ghost" id="cardRemove"><span data-ic="trash"></span>移除</button>' +
      '</div>' +
      '<input id="video" type="text" class="visually-hidden" autocomplete="off">';
    card.querySelector('[data-ic="film"]').innerHTML = ic('film');
    card.querySelector('[data-ic="check"]').innerHTML = ic('check');
    card.querySelector('[data-ic="refreshCw"]').innerHTML = ic('refreshCw');
    card.querySelector('[data-ic="trash"]').innerHTML = ic('trash');
    $('cardReplace').onclick = function (e) { e.stopPropagation(); browse(); };
    $('cardRemove').onclick = function (e) { e.stopPropagation(); pickVideoPath(''); };
    $('video').value = v; // 重建的隐藏输入框必须回填，否则拖放/浏览后值蒸发（探针/粘条读到空值）
  }
  wireVideo();
}
let lastVideo = '';
function wireVideo() {
  const inp = $('video');
  inp.onchange = function () {
    lastResult = null;   // 输入变更：清除上次任务结果，恢复静态状态
    const v = inp.value.trim();
    if (v && v !== lastVideo) {
      // 视频已更换：旧字幕属于旧视频，清空防止重新匹配时张冠李戴
      $('sc_sub').value = ''; $('tc_sub').value = '';
      $('sc_enc').textContent = ''; $('tc_enc').textContent = '';
      $('sc_name').value = 'SC'; $('tc_name').value = 'TC';
      syncSubStatus();
      setStatus('视频已更换，字幕已清空；可点「自动匹配字幕」重新匹配', 'ok');
    }
    lastVideo = v;
    trackSel.audio.clear(); trackSel.sub.clear();
    trackSel.allAudio = []; trackSel.allSub = []; trackSel.keepAtt = false;
    $('probeBox').innerHTML = '';
    renderVideoCard();
    refreshSticky();
  };
  inp.onkeydown = function (e) { if (e.key === 'Enter') inp.blur(); };
}

/* ==================== 状态刷新（粘性操作条） ==================== */
let lastResult = null;   // 最近一次任务结果（完成/失败/停止），输入变更后清除
function refreshSticky() {
  const note = $('stickyNote'), txt = note.querySelector('.sticky-txt');
  const btn = $('btnStart');
  if (job) return;
  if (lastResult) {
    note.className = 'sticky-note ' + lastResult.cls;
    note.firstElementChild.innerHTML = ic(lastResult.icon);
    txt.textContent = lastResult.text;
    btn.disabled = false;
    return;
  }
  const video = $('video').value.trim(), sc = $('sc_sub').value.trim(), tc = $('tc_sub').value.trim();
  if (!video) {
    note.className = 'sticky-note info';
    note.firstElementChild.innerHTML = ic('info');
    txt.textContent = '尚未选择视频';
    btn.disabled = true;
  } else if (!sc && !tc) {
    note.className = 'sticky-note warn';
    note.firstElementChild.innerHTML = ic('alertTriangle');
    txt.textContent = '未提供字幕：将保留源字幕与源字体（无新字幕时不做字体子集化）';
    btn.disabled = false;
  } else {
    note.className = 'sticky-note ok';
    note.firstElementChild.innerHTML = ic('checkCircle');
    txt.textContent = '所有资源准备完成';
    btn.disabled = false;
  }
}
let lastBatchResult = null;   // 最近一次批量任务结果，列表变更后清除
function refreshBatchSticky() {
  const note = $('batchStickyNote'), txt = note.querySelector('.sticky-txt');
  const btn = $('btnBatchStart');
  if (bJob) return;
  if (lastBatchResult) {
    note.className = 'sticky-note ' + lastBatchResult.cls;
    note.firstElementChild.innerHTML = ic(lastBatchResult.icon);
    txt.textContent = lastBatchResult.text;
    btn.disabled = false;
    return;
  }
  const filled = batchItems.filter(function (it) { return (it.video || '').trim(); }).length;
  if (!filled) {
    note.className = 'sticky-note info';
    note.firstElementChild.innerHTML = ic('info');
    txt.textContent = '批量列表为空';
    btn.disabled = true;
  } else {
    note.className = 'sticky-note ok';
    note.firstElementChild.innerHTML = ic('checkCircle');
    txt.textContent = '已就绪 ' + filled + ' 个任务 · 全部资源准备就绪';
    btn.disabled = false;
  }
}
function syncSubStatus() {
  ['sc', 'tc'].forEach(function (kind) {
    const el = $(kind + 'Status'), txt = el.querySelector('.sub-status-txt');
    const sub = $(kind + '_sub').value.trim();
    if (sub) { el.className = 'sub-status on'; el.firstElementChild.innerHTML = ic('check'); txt.textContent = '已识别'; }
    else { el.className = 'sub-status off'; el.firstElementChild.innerHTML = ic('info'); txt.textContent = '未设置'; }
  });
  syncDefaultBadge();
}
/* SC/TC「默认轨道 / 可选」徽章随字幕填写动态切换（Q10） */
function syncDefaultBadge() {
  const sc = $('sc_sub').value.trim(), tc = $('tc_sub').value.trim();
  const scB = $('scDefaultBadge'), tcB = $('tcDefaultBadge');
  if (sc) {
    scB.textContent = '默认轨道'; scB.className = 'chip sm ok';
    tcB.textContent = '可选'; tcB.className = 'chip sm info';
  } else if (tc) {
    scB.textContent = '可选'; scB.className = 'chip sm info';
    tcB.textContent = '默认轨道'; tcB.className = 'chip sm ok';
  } else {
    scB.textContent = '默认轨道'; scB.className = 'chip sm ok';
    tcB.textContent = '可选'; tcB.className = 'chip sm info';
  }
}
/* 运行中：把顶部状态镜像到粘性操作条 + 同步进度条 */
(function () {
  new MutationObserver(function () {
    const s = $('status');
    const t = s.textContent.trim();
    if ((job || bJob) && t && t.indexOf('服务已就绪') !== 0 && t.indexOf('连接中') !== 0) {
      const note = job ? $('stickyNote') : $('batchStickyNote');
      const clsMap = { ok:'ok', err:'err', run:'run', '':'info' };
      const cls = clsMap[s.className] || 'info';
      const iconMap = { ok:'checkCircle', err:'xCircle', run:'loader', info:'info' };
      note.className = 'sticky-note ' + cls;
      note.firstElementChild.innerHTML = ic(iconMap[cls]);
      note.querySelector('.sticky-txt').textContent = t;
    }
  }).observe($('status'), { childList: true, characterData: true, subtree: true, attributes: true, attributeFilter: ['class'] });
  new MutationObserver(function () {
    const wrap = $('singleBarWrap');
    if (wrap.style.display !== 'none') { $('stickyBarWrap').style.display = ''; $('stickyBar').style.width = $('singleBar').style.width; }
    else { $('stickyBarWrap').style.display = 'none'; }
  }).observe($('singleBarWrap'), { attributes: true, attributeFilter: ['style'] });
  new MutationObserver(function () {
    if ($('batchProgress').style.display !== 'none') {
      $('batchStickyBarWrap').style.display = '';
      $('batchStickyBar').style.width = $('batchBar').style.width;
    }
  }).observe($('batchBar'), { attributes: true, attributeFilter: ['style'] });
})();

/* ==================== 文件浏览器 ==================== */
const CFG = { scanRoot: 'D:\\Video' };
function openBrowser(setter, filter, startPath, slot) {
  BR.setter = setter; BR.filter = filter; BR.slot = slot || filter || 'generic';
  BR.path = startPath || localStorage.getItem('muxui_ld_' + BR.slot) || localStorage.getItem('muxui_lastdir') || CFG.scanRoot;
  $('browserModal').style.display = 'block';
  showBrowser();
}
$('mbClose').onclick = () => $('browserModal').style.display = 'none';
$('mbUp').onclick = () => { BR.path = BR.path.replace(/\\+$/, '').replace(/[^\\/]+$/, '') || ''; showBrowser(); }; // 到盘根后再向上进入盘符列表
$('mbGo').onclick = () => { BR.path = $('mbPathInput').value.trim() || BR.path; showBrowser(); };
$('mbPathInput').onkeydown = e => { if (e.key === 'Enter') $('mbGo').click(); };
$('mbUseDir').onclick = () => { BR.setter(BR.path.replace(/\\+$/, '')); $('browserModal').style.display = 'none'; };
async function showBrowser() {
  let d;
  try {
    d = await api('/api/list?path=' + encodeURIComponent(BR.path));
  } catch (ex) {
    $('mbPath').textContent = '加载失败';
    $('mbHint').textContent = '连接失败：' + ex + '（请检查服务是否运行）';
    return;
  }
  $('mbPath').textContent = d.path || '（选择驱动器）';
  $('mbPathInput').value = d.path;
  const body = $('mbBody'); body.innerHTML = '';
  const ext = FILTERS[BR.filter];
  $('mbUseDir').style.display = (BR.filter === 'dir' && d.path) ? '' : 'none';
  $('mbHint').textContent = d.error ? ('错误: ' + d.error) : '';
  if (d.path && !d.error) { try { localStorage.setItem('muxui_lastdir', d.path); localStorage.setItem('muxui_ld_' + (BR.slot || 'generic'), d.path); } catch (e) {} } // 记住上次浏览目录（分槽位）
  const itemCls = (n, dir) => '<span class="it">' + ic(dir ? 'folder' : 'fileText') + '<span>' + esc(n) + '</span></span>';
  if (d.drives) d.drives.forEach(dr => {
    const b = document.createElement('button'); b.className = 'mb-item dir';
    b.innerHTML = itemCls(dr, false) + ic('chevronDown');
    b.onclick = () => { BR.path = dr; showBrowser(); };
    body.appendChild(b);
  });
  d.dirs.forEach(n => {
    const b = document.createElement('button'); b.className = 'mb-item dir';
    b.innerHTML = itemCls(n, true);
    b.onclick = () => { BR.path = (d.path ? d.path.replace(/\\+$/, '') + '\\' : '') + n; showBrowser(); };
    body.appendChild(b);
  });
  d.files.forEach(f => {
    const [name, sz] = f;
    if (ext && !ext.includes(name.slice(name.lastIndexOf('.')).toLowerCase())) return;
    const b = document.createElement('button'); b.className = 'mb-item';
    b.innerHTML = itemCls(name, false) + (sz >= 0 ? '<span class="sz">' + (sz / 1048576).toFixed(1) + ' MB</span>' : '');
    b.onclick = () => { BR.setter((d.path ? d.path.replace(/\\+$/, '') + '\\' : '') + name); $('browserModal').style.display = 'none'; };
    body.appendChild(b);
  });
}

/* ==================== 重置单个封装 ==================== */
$('btnSingleReset').onclick = () => {
  if (job) { setStatus('封装任务进行中，不能重置', 'err'); return; }
  if (!confirm('确定重置单个封装的全部设置？')) return;
  pickVideoPath(''); // 清视频并联动：轨道选择/探测结果清空、卡片重渲染、粘性条刷新
  $('sc_sub').value = ''; $('tc_sub').value = '';
  $('sc_name').value = 'SC'; $('tc_name').value = 'TC';
  $('sc_enc').textContent = ''; $('tc_enc').textContent = '';
  $('fonts_dir').value = '';
  $('audio').value = ''; $('audio_lang').value = ''; $('audio_name').value = '';
  $('out_dir').value = '';
  $('backup').checked = true; $('force').checked = false;
  $('fontCheckBox').innerHTML = '';
  setResult('');
  syncSubStatus();
  refreshSticky();
  setStatus('已重置单个封装设置', 'ok');
};

/* ==================== 自动匹配字幕（单个） ==================== */
$('btnAutoMatch').onclick = async () => {
  const v = $('video').value.trim();
  if (!v) { alert('请先选择视频文件'); return; }
  $('btnAutoMatch').disabled = true;
  try {
    const m = await api('/api/match_subs?path=' + encodeURIComponent(v));
    if ($('video').value.trim() !== v) return; // 视频已变更，丢弃过期结果
    const scHad = !!$('sc_sub').value.trim(), tcHad = !!$('tc_sub').value.trim();
    let sc = false, tc = false;
    if (m.sc && !scHad) { $('sc_sub').value = m.sc; autoTrackName('sc_sub', 'sc_name', 'sc'); sc = true; }
    if (m.tc && !tcHad) { $('tc_sub').value = m.tc; autoTrackName('tc_sub', 'tc_name', 'tc'); tc = true; }
    syncSubStatus();
    lastResult = null; refreshSticky();   // 字幕已填充：同步底部操作栏状态
    if (sc || tc) {
      setStatus('字幕匹配完成：已填充 ' + (sc ? '简体' : '') + (sc && tc ? ' + ' : '') + (tc ? '繁体' : ''), 'ok');
    } else if (m.sc || m.tc) {
      setStatus('匹配到的字幕槽位已有内容，未覆盖（重置后可重新填充）', 'ok');
    } else {
      setStatus('未匹配到任何字幕（简 0 / 繁 0）', 'err');
    }
  } catch (ex) {
    setStatus('字幕匹配失败：' + ex, 'err');
  } finally {
    $('btnAutoMatch').disabled = false;
  }
};

/* ==================== 查看轨道 ==================== */
const trackSel = { audio: new Set(), sub: new Set(), keepAtt: false, allAudio: [], allSub: [] };
function toggleSel(id, kind) { const set = kind === 'audio' ? trackSel.audio : trackSel.sub; if (set.has(id)) set.delete(id); else set.add(id); }
function toggleAtt(v) { trackSel.keepAtt = v; }
$('btnProbe').onclick = async () => {
  const v = $('video').value.trim();
  if (!v) { alert('请先选择视频文件'); return; }
  const d = await api('/api/probe?path=' + encodeURIComponent(v));
  if ($('video').value.trim() !== v) return; // 视频已变更，丢弃过期结果
  const box = $('probeBox');
  if (d.error) { box.innerHTML = '<div class="chip err" style="margin-top:8px">' + ic('xCircle') + '<span>' + esc(d.error) + '</span></div>'; return; }
  trackSel.audio.clear(); trackSel.sub.clear();
  trackSel.allAudio = []; trackSel.allSub = [];
  let h = '<div class="table-wrap" style="margin-top:12px;"><table style="min-width:620px;"><tr><th style="width:34px"></th><th>ID</th><th>类型</th><th>语言</th><th>名称</th><th>默认</th><th>编码</th></tr>';
  d.tracks.forEach(t => {
    let cb = '';
    if (t.type === 'video') { cb = '<input type="checkbox" checked disabled title="视频轨必须保留">'; }
    else if (t.type === 'audio') { trackSel.allAudio.push(t.id); trackSel.audio.add(t.id); cb = '<input type="checkbox" checked onchange="toggleSel(' + t.id + ',\'audio\')">'; }
    else if (t.type === 'subtitles') { trackSel.allSub.push(t.id); cb = '<input type="checkbox" onchange="toggleSel(' + t.id + ',\'sub\')">'; }
    const dcol = { video:'var(--run-line)', audio:'var(--ok-line)', subtitles:'var(--warn-line)' }[t.type] || 'var(--border)';
    h += '<tr><td>' + cb + '</td><td class="mono">' + t.id + '</td><td><span class="tdot" style="background:' + dcol + '"></span>' + t.type + '</td><td class="mono">' + esc(t.lang) + '</td><td>' + esc(t.name || '') + '</td><td>' + (t.default ? esc('✔') : '') + '</td><td class="mono">' + esc(t.codec || '') + '</td></tr>';
  });
  h += '</table></div>';
  h += '<div class="field" style="margin-top:12px;"><label class="check"><input type="checkbox" onchange="toggleAtt(this.checked)"> 保留源附件（字体/封面）</label><span class="t-cap">源附件 ' + d.attachments + ' 个；不勾选则用新子集字体重建</span></div>';
  h += '<div class="t-sec">音轨默认全保留（取消勾选即去除）；源字幕默认不保留（会用新字幕替换），需保留源字幕请勾选。</div>';
  box.innerHTML = h;
};

/* ==================== 字幕编码检查 ==================== */
$('btnPrepSubs').onclick = async () => {
  const sc = $('sc_sub').value.trim(), tc = $('tc_sub').value.trim();
  if (!sc && !tc) { alert('请先填写字幕路径'); return; }
  $('btnPrepSubs').disabled = true;
  try {
    const r = await api('/api/prep_subs', { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify({sc, tc}) });
    const show = (key, el) => {
      const d = r[key];
      if (!d) return;
      const label = $(el + '_enc');
      if (d.ambiguous) { label.textContent = 'GBK/BIG5 歧义→按简/繁槽判定 ⚠'; }
      else if (d.converted) { label.textContent = d.encoding.toUpperCase() + ' → UTF-8 ✓'; }
      else if (d.encoding === 'utf-8') { label.textContent = 'UTF-8 ✓'; }
      else { label.textContent = d.error ? ('错误: ' + d.error) : ''; }
      if (d.converted && d.path) { $(el + '_sub').value = d.path; }
      syncSubStatus();
    };
    show('sc','sc'); show('tc','tc');
  } catch (ex) {
    const msg = '连接失败：' + ex;
    if (sc) $('sc_enc').textContent = msg;
    if (tc) $('tc_enc').textContent = msg;
  } finally {
    $('btnPrepSubs').disabled = false;
  }
};

/* ==================== 字体体检 ==================== */
$('btnCheckFonts').onclick = async () => {
  const subs = [$('sc_sub').value.trim(), $('tc_sub').value.trim()].filter(Boolean);
  const fonts_dir = $('fonts_dir').value.trim();
  if (!subs.length) { alert('请先填写字幕路径'); return; }
  $('btnCheckFonts').disabled = true;
  $('fontCheckBox').innerHTML = '<div class="chip run" style="margin-top:8px">' + ic('loader', 'spin') + '<span>正在检查字体，请稍候…</span></div>';
  try {
    const r = await api('/api/check_fonts', { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify({subs, fonts_dir}) });
    renderFontCheck(r, subs, fonts_dir);
  } catch (ex) {
    $('fontCheckBox').innerHTML = '<div class="chip err" style="margin-top:8px">' + ic('xCircle') + '<span>连接失败：' + esc(ex) + '</span></div>';
  } finally {
    $('btnCheckFonts').disabled = false;
  }
};
/* 体检结果渲染（含缺字体时的补给入口） */
function renderFontCheck(r, subs, fonts_dir) {
  const box = $('fontCheckBox');
  if (r.error) { box.innerHTML = '<div class="chip warn" style="margin-top:8px">' + ic('alertTriangle') + '<span>' + esc(r.error) + '</span></div>' + (r.log ? '<pre class="log-pre">' + esc(r.log) + '</pre>' : ''); return; }
  if (r.ok && !r.missing.length) {
    box.innerHTML = '<div class="chip ok" style="margin-top:8px">' + ic('checkCircle') + '<span>字体齐全，可正常封装</span></div>';
    return;
  }
  let html = '<div class="chip warn" style="margin-top:8px">' + ic('alertTriangle') + '<span>缺少 ' + r.missing.length + ' 个字体</span></div><pre class="log-pre">' + esc(r.missing.join('\n')) + '</pre>';
  html += '<div style="margin-top:8px;display:flex;gap:8px;align-items:center;flex-wrap:wrap">'
       +  '<input id="fs_source" class="ipt" style="flex:1;min-width:220px" placeholder="备份字体目录（如 D:\\Video\\Font）" value="' + esc(localStorage.getItem('muxui_fs_source') || 'D:\\Video\\Font') + '">'
       +  '<button class="btn" id="btnFontSupply">' + ic('download') + '<span>从备份目录补给</span></button></div>';
  box.innerHTML = html;
  $('btnFontSupply').onclick = async () => {
    const source_dir = $('fs_source').value.trim();
    if (!source_dir) { alert('请填写备份字体目录'); return; }
    localStorage.setItem('muxui_fs_source', source_dir);
    $('btnFontSupply').disabled = true;
    $('fontCheckBox').innerHTML = '<div class="chip run" style="margin-top:8px">' + ic('loader', 'spin') + '<span>补给中：检索备份目录、复制字体、复检…</span></div>';
    try {
      const r2 = await api('/api/font_supply', { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify({subs, fonts_dir, source_dir}) });
      if (r2.error) { $('fontCheckBox').innerHTML = '<div class="chip err" style="margin-top:8px">' + ic('xCircle') + '<span>' + esc(r2.error) + '</span></div>'; return; }
      let parts = [];
      (r2.supplied || []).forEach(s => parts.push('已补给：' + s.missing + '  ←  ' + s.file));
      (r2.skipped_dup || []).forEach(s => parts.push('目标目录已有同族样式，跳过：' + s.missing + '（' + s.key + '）'));
      (r2.not_found || []).forEach(n => parts.push('备份目录也找不到：' + n));
      const rc = r2.recheck || {};
      const head = rc.ok
        ? '<div class="chip ok" style="margin-top:8px">' + ic('checkCircle') + '<span>补给完成，复检通过 ✓</span></div>'
        : '<div class="chip warn" style="margin-top:8px">' + ic('alertTriangle') + '<span>补给后仍缺字体：</span></div>';
      $('fontCheckBox').innerHTML = head + (parts.length ? '<pre class="log-pre">' + esc(parts.join('\n')) + '</pre>' : '') + (rc.missing && rc.missing.length ? '<pre class="log-pre">' + esc(rc.missing.join('\n')) + '</pre>' : '');
    } catch (ex) {
      $('fontCheckBox').innerHTML = '<div class="chip err" style="margin-top:8px">' + ic('xCircle') + '<span>补给失败：' + esc(ex) + '</span></div>';
    }
  };
}

/* ==================== 单个封装 ==================== */
let job = null, timer = null;
$('btnStart').onclick = async () => {
  if (job) {
    setStatus('正在停止…', 'run');
    await api('/api/stop', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ id: job }) });
    return;
  }
  const body = {
    video: $('video').value.trim(), sc_sub: $('sc_sub').value.trim(), tc_sub: $('tc_sub').value.trim(),
    sc_name: $('sc_name').value.trim() || 'SC', tc_name: $('tc_name').value.trim() || 'TC',
    fonts_dir: $('fonts_dir').value.trim(),
    audio: $('audio').value.trim(),
    audio_tracks: (trackSel.allAudio.length === 0) ? '' : (trackSel.audio.size === 0) ? 'none' : (trackSel.audio.size < trackSel.allAudio.length) ? [...trackSel.audio].join(',') : '',
    subtitle_tracks: trackSel.sub.size ? [...trackSel.sub].join(',') : '',
    keep_attachments: trackSel.keepAtt,
    audio_lang: $('audio_lang').value.trim(), audio_name: $('audio_name').value.trim(),
    out_dir: $('out_dir').value.trim(), force: $('force').checked, no_backup: !$('backup').checked,
    sc_default: $('sc_default').value, tc_default: $('tc_default').value,
    sc_forced: $('sc_forced').checked, tc_forced: $('tc_forced').checked
  };
  if (!body.video) { alert('请选择视频文件'); return; }
  if (!body.sc_sub && !body.tc_sub && !confirm('未提供任何字幕，将保留源字幕与源字体（无新字幕时不做字体子集化）。继续？')) return;
  setStatus('正在提交…', 'run'); setResult('');
  const r = await api('/api/mux', { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify(body) });
  if (r.error) { setStatus('错误：' + r.error, 'err'); return; }
  job = r.job; $('btnStart').disabled = false; $('btnStart').innerHTML = ic('square') + '<span>停止封装</span>'; $('btnStart').classList.add('danger'); $('btnStart').classList.remove('primary');
  showLogTab('mux'); setLog('mux', '');
  $('singleBarWrap').style.display = ''; $('singleBar').style.width = '0%';
  poll();
};
async function poll() {
  let failCount = 0;
  timer = setInterval(async () => {
    try {
      const s = await api('/api/job?id=' + job);
      failCount = 0;
      setLog('mux', s.log);
      if (s.progress != null) $('singleBar').style.width = s.progress + '%';
      if (s.status === 'done') {        clearInterval(timer); timer = null; job = null;
        $('btnStart').disabled = false; $('btnStart').innerHTML = ic('play') + '<span>开始封装</span>'; $('btnStart').classList.add('primary'); $('btnStart').classList.remove('danger');
        $('singleBarWrap').style.display = 'none'; setStatus('封装完成', 'ok'); beep();
        lastResult = { cls: 'ok', icon: 'checkCircle', text: '封装完成' };
        $('result').innerHTML = '<span class="t-sec">输出：</span><code class="mono" style="color:var(--text-primary)">' + esc(s.result || '') + '</code> <button class="btn small" data-open-dir="' + encodeURIComponent(s.result || '') + '">' + ic('arrowUpRight') + '打开文件夹</button>';
        refreshSticky();
      }
      else if (s.status === 'error') {
        clearInterval(timer); timer = null; job = null;
        $('btnStart').disabled = false; $('btnStart').innerHTML = ic('play') + '<span>开始封装</span>'; $('btnStart').classList.add('primary'); $('btnStart').classList.remove('danger');
        $('singleBarWrap').style.display = 'none';
        const reason = s.reason || ('退出码 ' + (s.exit ?? '?'));
        setStatus('封装失败：' + reason, 'err');
        lastResult = { cls: 'err', icon: 'xCircle', text: '封装失败：' + reason };
        $('result').innerHTML = '<span class="chip err">' + ic('xCircle') + '<span>封装失败：' + esc(reason) + '</span></span>';
        refreshSticky();
      }
      else if (s.status === 'killed') {
        clearInterval(timer); timer = null; job = null;
        $('btnStart').disabled = false; $('btnStart').innerHTML = ic('play') + '<span>开始封装</span>'; $('btnStart').classList.add('primary'); $('btnStart').classList.remove('danger');
        $('singleBarWrap').style.display = 'none'; setStatus('已停止', 'err');
        lastResult = { cls: 'info', icon: 'info', text: '任务已停止' };
        $('result').innerHTML = '<span class="chip info">' + ic('info') + '<span>任务已停止</span></span>';
        refreshSticky();
      }
      else {
        // 运行中：sticky 栏随轮询自动刷新当前状态（子集化/封装分阶段 + 进度）
        const note = $('stickyNote');
        note.className = 'sticky-note run';
        note.firstElementChild.innerHTML = ic('loader', 'spin');
        const lg = s.log || '';
        let stage;
        if (s.progress != null) stage = '封装中 ' + s.progress + '%';
        else if (/Muxing/i.test(lg)) stage = '封装中…';
        else if (/Subset tool|subsetting|assfonts|AFS:/i.test(lg)) stage = '子集化中…';
        else stage = '处理中…';
        note.querySelector('.sticky-txt').textContent = stage;
      }
    } catch (ex) {
      failCount++;
      if (failCount >= 5) {
        clearInterval(timer); timer = null; job = null;
        $('btnStart').disabled = false; $('btnStart').innerHTML = ic('play') + '<span>开始封装</span>'; $('btnStart').classList.add('primary'); $('btnStart').classList.remove('danger');
        $('singleBarWrap').style.display = 'none';
        setStatus('连接丢失，请刷新', 'err');
        lastResult = { cls: 'err', icon: 'xCircle', text: '连接丢失，请刷新' };
        $('result').innerHTML = '<span class="chip err">' + ic('xCircle') + '<span>连接丢失，请刷新页面后重试</span></span>';
        refreshSticky();
      }
    }
  }, 1200);
}
/* ==================== 拖放识别 ==================== */
function pickDropCandidates(entries, done) {
  const ov = document.createElement('div');
  ov.style.cssText = 'position:fixed;inset:0;background:rgba(0,0,0,.55);z-index:999;display:flex;align-items:center;justify-content:center;';
  const box = document.createElement('div');
  box.style.cssText = 'background:var(--surface-1,#20202a);border:1px solid var(--border,#333);border-radius:10px;padding:16px 18px;max-width:760px;width:94%;max-height:82vh;overflow:auto;font-size:14px;color:var(--text-primary,#eee);';
  const title = document.createElement('div');
  title.style.cssText = 'font-weight:600;margin-bottom:6px;';
  title.textContent = '拖入的文件在多个位置命中同名文件，请选择使用哪一个：';
  const sub = document.createElement('div');
  sub.style.cssText = 'color:var(--text-muted,#999);font-size:12px;margin-bottom:12px;';
  sub.textContent = '名字相同但路径不同（附文件大小参考）；每个文件都要选一次，或点取消放弃这些文件。';
  const picks = {};
  const doneBtn = document.createElement('button');
  doneBtn.textContent = '确定';
  doneBtn.style.cssText = 'margin-top:12px;padding:6px 18px;';
  const cancelBtn = document.createElement('button');
  cancelBtn.textContent = '取消（不处理这些文件）';
  cancelBtn.style.cssText = 'margin-top:12px;margin-left:8px;padding:6px 12px;';
  const close = () => { ov.remove(); };
  doneBtn.onclick = () => { close(); done(picks); };
  cancelBtn.onclick = () => { close(); done(null); };
  box.append(title, sub);
  entries.forEach((en, idx) => {
    const row = document.createElement('div');
    row.style.cssText = 'border-top:1px solid var(--border,#333);padding:8px 0;';
    const head = document.createElement('div');
    head.textContent = (idx + 1) + '. ' + en.name;
    head.style.cssText = 'font-weight:600;margin-bottom:6px;';
    row.appendChild(head);
    en.cands.forEach((c, ci) => {
      const rb = document.createElement('label');
      rb.style.cssText = 'display:flex;align-items:center;gap:8px;padding:4px 0;cursor:pointer;font-size:13px;';
      const radio = document.createElement('input');
      radio.type = 'radio'; radio.name = 'droppick' + idx;
      if (ci === 0) radio.checked = true;
      radio.onchange = () => { picks[en.name] = { path: c.path, ext: en.ext }; };
      const txt = document.createElement('span');
      txt.textContent = c.path + (c.size >= 0 ? '  （' + (c.size / 1048576).toFixed(1) + ' MB）' : '');
      txt.style.cssText = 'font-family:monospace;word-break:break-all;color:var(--text-secondary,#ccc);';
      rb.append(radio, txt);
      row.appendChild(rb);
    });
    box.appendChild(row);
    if (en.cands.length) picks[en.name] = { path: en.cands[0].path, ext: en.ext }; // 预选第一项
  });
  box.append(doneBtn, cancelBtn);
  ov.appendChild(box);
  document.body.appendChild(ov);
}
let dragDepth = 0;
window.addEventListener('dragenter', e => { e.preventDefault(); dragDepth++; $('dropOverlay').style.display = 'block'; });
window.addEventListener('dragover', e => e.preventDefault());
window.addEventListener('dragleave', e => { e.preventDefault(); dragDepth--; if (dragDepth <= 0) { dragDepth = 0; $('dropOverlay').style.display = 'none'; } });
window.addEventListener('drop', async e => {
  e.preventDefault(); dragDepth = 0; $('dropOverlay').style.display = 'none';
  if ($('browserModal').style.display === 'block') { setStatus('文件浏览器已打开，请先关闭再拖放', 'err'); return; }
  const files = [...(e.dataTransfer.files || [])];
  if (!files.length) return;
  const names = files.map(f => f.name);
  setStatus('正在识别拖入的文件…', 'run');
  let res;
  try {
    res = await api('/api/drop', { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify({names}) });
  } catch (ex) {
    setStatus('识别失败（服务器不可用？）：' + ex, 'err');
    return;
  }
  const dropSizes = {};
  files.forEach(f => { dropSizes[f.name] = f.size; });
  const vids = [], subs = [], notFound = [], ambiguous = [];
  for (const n of names) {
    let cands = res[n] || [];
    if (cands.length && typeof cands[0] === 'string') cands = cands.map(p => ({ path: p, size: -1 })); // 旧服务端兼容（未重启时的字符串索引）
    if (!cands.length) { notFound.push(n); continue; }
    const ext = n.slice(n.lastIndexOf('.')).toLowerCase();
    if (!VEXT.has(ext) && !SEXT.has(ext)) continue;
    let pick = null;
    if (cands.length === 1) pick = cands[0].path;
    else if (dropSizes[n] > 0) {
      const sz = cands.filter(c => c.size === dropSizes[n]);
      if (sz.length === 1) pick = sz[0].path; // 大小唯一命中：直接采用
    }
    if (pick) { if (VEXT.has(ext)) vids.push(pick); else subs.push(pick); }
    else ambiguous.push({ name: n, ext, cands });
  }
  if (ambiguous.length) {
    const picks = await new Promise(resolve => pickDropCandidates(ambiguous, resolve));
    if (!picks) { setStatus('已取消歧义文件选择', 'err'); return; }
    for (const p of Object.values(picks)) { if (VEXT.has(p.ext)) vids.push(p.path); else subs.push(p.path); }
  }
  if (!vids.length && !subs.length && !notFound.length) { setStatus('未能识别拖入的文件', 'err'); return; }
  if (notFound.length) {
    setStatus('未找到：' + notFound.join('、') + '，请手动选择', 'err');
  }
  if (notFound.length && !vids.length && !subs.length) {
    openBrowser(pickVideoPath, 'any', $('video').value, 'video');
    return;
  }
  if (vids.length === 1 && subs.length <= 2) {
    $('video').value = vids[0]; fireChange($('video'));
    let plainSub = false;
    for (const s of subs) {
      const base = s.slice(s.lastIndexOf('\\') + 1);
      if (isTcName(base)) { $('tc_sub').value = s; autoTrackName('tc_sub', 'tc_name', 'tc'); }
      else { if (!isScName(base)) plainSub = true; $('sc_sub').value = s; autoTrackName('sc_sub', 'sc_name', 'sc'); }
    }
    syncSubStatus();
    lastResult = null; refreshSticky();
    setStatus('已填充：' + vids[0] + (plainSub ? ' · 无简/繁标识的字幕已按简体处理' : ''), 'ok');
    window.scrollTo({top: 0, behavior: 'smooth'});
  } else if (vids.length >= 1) {
    setStatus('正在匹配字幕…', 'run');
    switchMode('batch');
    const ms = await Promise.all(vids.map(v => api('/api/match_subs?path=' + encodeURIComponent(v))));
    vids.forEach((v, i) => addBatchVideo(v, subs, ms[i]));
    renderBatch();
    setStatus('已添加 ' + vids.length + ' 个视频到批量列表', 'ok');
    $('batchList').scrollIntoView({behavior: 'smooth'});
  } else {
    let plainSub = false;
    for (const s of subs) {
      const base = s.slice(s.lastIndexOf('\\') + 1);
      if (isTcName(base)) { $('tc_sub').value = s; autoTrackName('tc_sub', 'tc_name', 'tc'); }
      else { if (!isScName(base)) plainSub = true; $('sc_sub').value = s; autoTrackName('sc_sub', 'sc_name', 'sc'); }
    }
    syncSubStatus();
    lastResult = null; refreshSticky();
    setStatus('已填充字幕' + (plainSub ? ' · 无简/繁标识的字幕已按简体处理' : ''), 'ok');
  }
});
/* ==================== 自动轨道名 ==================== */
function pickNameToken(path, kind) {
  var base = (path || '').split(/[\\/]/).pop() || '';
  var m = base.match(/(?:^|[._\- ])(sc|chs|jpsc|tc|cht|jptc)(?:[._\- ]|$)/i);
  if (!m) return '';
  var tok = m[1].toLowerCase();
  var isSc = tok === 'sc' || tok === 'chs' || tok === 'jpsc';
  var isTc = tok === 'tc' || tok === 'cht' || tok === 'jptc';
  if ((kind === 'sc' && isSc) || (kind === 'tc' && isTc)) return m[1].toUpperCase();
  return '';
}
function autoTrackName(subField, nameField, kind) {
  var tok = pickNameToken($(subField).value, kind);
  if (tok) $(nameField).value = tok;
}
$('sc_sub').addEventListener('change', function () { lastResult = null; autoTrackName('sc_sub', 'sc_name', 'sc'); syncSubStatus(); });
$('tc_sub').addEventListener('change', function () { lastResult = null; autoTrackName('tc_sub', 'tc_name', 'tc'); syncSubStatus(); });
$('sc_sub').addEventListener('input', syncSubStatus);
$('tc_sub').addEventListener('input', syncSubStatus);
$('sc_name').addEventListener('input', function () { });
$('btnSc').onclick = () => openBrowser(v => { $('sc_sub').value = v; autoTrackName('sc_sub', 'sc_name', 'sc'); syncSubStatus(); }, 'sub', $('sc_sub').value, 'sub');
$('btnTc').onclick = () => openBrowser(v => { $('tc_sub').value = v; autoTrackName('tc_sub', 'tc_name', 'tc'); syncSubStatus(); }, 'sub', $('tc_sub').value, 'sub');
$('btnFonts').onclick = () => openBrowser(v => $('fonts_dir').value = v, 'dir', $('fonts_dir').value, 'fonts');
$('btnAudio').onclick = () => openBrowser(v => $('audio').value = v, 'audio', $('audio').value, 'audio');
$('btnOut').onclick = () => openBrowser(v => $('out_dir').value = v, 'dir', $('out_dir').value, 'out');
/* ==================== 历史 ==================== */
let histItems = [];
async function loadHistory() {
  const d = await api('/api/history');
  const box = $('histBox');
  if (!d.items || !d.items.length) { box.innerHTML = '<div class="t-sec" style="padding:16px 16px;">暂无历史任务</div>'; return; }
  histItems = d.items;
  let h = '<div class="table-wrap" style="margin:8px 16px;"><table style="min-width:640px;"><tr><th>时间</th><th>类型</th><th>视频</th><th>状态</th><th style="width:150px"></th></tr>';
  d.items.forEach(function (it, i) {
    const dt = new Date(it.time).toLocaleString();
    h += '<tr><td class="mono" style="white-space:nowrap">' + esc(dt) + '</td><td>' + esc(it.type) + '</td><td class="mono" style="word-break:break-all">' + esc(it.video) + '</td><td>' + (it.status === 'done' ? '<span class="chip sm ok">' + ic('check') + '成功</span>' : '<span class="chip sm err">' + ic('xCircle') + '失败</span>') + '</td>' +
         '<td style="white-space:nowrap"><button class="btn small" onclick="histView(' + i + ')">日志</button> <button class="btn small" onclick="histRerun(' + i + ')">重跑</button> <button class="btn small ghost" data-open-dir="' + (it.video ? encodeURIComponent(it.video) : '') + '">打开</button></td></tr>';
  });
  h += '</table></div>';
  box.innerHTML = h;
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
document.addEventListener('click', function (e) {
  const el = e.target.closest('[data-open-dir]');
  if (el) openDir(el.dataset.openDir || '');
});
/* ==================== 统一日志 / 任务控制台 ==================== */
const logStore = { mux: '', batch: '', xt: '' };
let logTab = 'mux';
function showLogTab(tab) {
  logTab = tab;
  document.querySelectorAll('.ltab').forEach(function (b) { b.classList.toggle('active', b.dataset.tab === tab); });
  var isHist = tab === 'hist';
  $('log').style.display = isHist ? 'none' : '';
  $('histBox').style.display = isHist ? '' : 'none';
  $('histLogWrap').style.display = 'none';
  if (isHist) loadHistory();
  else renderLog();
}
function setLog(tab, text) { logStore[tab] = text || ''; if (tab === logTab) renderLog(); }
function renderLog() { var el = $('log'); el.textContent = logStore[logTab] || ''; el.scrollTop = el.scrollHeight; }
$('btnLogClear').onclick = function () { logStore[logTab] = ''; renderLog(); };

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
(function () {
  try { $('soundToggle').checked = localStorage.getItem('muxui_sound') !== '0'; } catch (e) {}
  $('soundToggle').onchange = function () { try { localStorage.setItem('muxui_sound', this.checked ? '1' : '0'); } catch (e) {} };
})();

/* ==================== 高级选项折叠 ==================== */
(function () {
  const toggle = $('advToggle'), body = $('advBody');
  toggle.onclick = function () {
    const open = body.classList.toggle('show');
    toggle.classList.toggle('open', open);
    toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
  };
})();
/* ==================== 初始启动：设置工作目录 ==================== */
function showSetup() {
  $('setup_scan').value = CFG.scanRoot || '';
  $('setupErr').textContent = '';
  $('setupModal').style.display = 'flex';
}
$('btnSetupBrowse').onclick = () => openBrowser(v => { $('setup_scan').value = v; $('setupErr').textContent = ''; }, 'dir', $('setup_scan').value, 'cfg');
$('btnSetupSave').onclick = async () => {
  const p = $('setup_scan').value.trim();
  if (!p) { $('setupErr').textContent = '请输入工作目录（可点「浏览」选择）'; return; }
  $('btnSetupSave').disabled = true;
  try {
    const r = await api('/api/config', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ scan_root: p }) });
    if (r.error) { $('setupErr').textContent = '保存失败：' + r.error; return; }
    CFG.scanRoot = r.scan_root || p;
    $('setupModal').style.display = 'none';
    $('cfg_scan').value = CFG.scanRoot;
    setStatus('工作目录已设置：' + CFG.scanRoot + '（索引构建中）', 'ok');
  } catch (ex) {
    $('setupErr').textContent = '保存失败：' + ex;
  } finally {
    $('btnSetupSave').disabled = false;
  }
};
$('setupSkip').onclick = () => {
  $('setupModal').style.display = 'none';
  setStatus('未设置工作目录：拖放识别暂不可用，可在「高级选项 → 工作目录」随时设置', 'err');
};
