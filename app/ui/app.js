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
  chevronDown:'<path d="m6 9 6 6 6-6"/>',
  moreHorizontal:'<circle cx="12" cy="12" r="1"/><circle cx="19" cy="12" r="1"/><circle cx="5" cy="12" r="1"/>'
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

/* ===== 底部状态条时间信息（耗时 / 预计剩余；纯展示，不影响任务逻辑） ===== */
let stickyStartTs = 0;   // 本轮任务开始时刻
function fmtDur(ms) {
  const t = Math.max(0, Math.floor(ms / 1000));
  const p = n => String(n).padStart(2, '0');
  return p(Math.floor(t / 3600)) + ':' + p(Math.floor((t % 3600) / 60)) + ':' + p(t % 60);
}
function stickyTimesRunning(progress) {
  const elapsed = Date.now() - stickyStartTs;
  $('stickyElapsed').textContent = fmtDur(elapsed);
  // 线性外推：按已耗时长度的进度占比推算剩余（progress 为空=阶段未报进度，先显示计算中）
  if (progress != null && progress > 0) $('stickyEta').textContent = fmtDur(elapsed / progress * (100 - progress));
  else $('stickyEta').textContent = '计算中…';
}
function stickyTimesFreeze() { $('stickyEta').textContent = '--:--:--'; }   // 终态：耗时定格，剩余不再显示

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
    '<button type="button" class="pref menu" id="popGlobalBtn"><span data-ic="sliders" aria-hidden="true"></span>全局设置（工作目录 / 工具）</button>' +
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
    if (b.id === 'popGlobalBtn') { pop.classList.remove('open'); btn.setAttribute('aria-expanded', 'false'); openGlobal(); return; }
    if (b.classList.contains('theme')) document.body.dataset.theme = b.dataset.v;
    else document.body.dataset.accent = b.dataset.v;
    sync(); save();
  });
  sync();
})();

/* ==================== 全局设置（应用级配置：工作目录 / 子集化工具 / 备份清理） ==================== */
/* 字段 id（cfg_scan / cfg_tool / btnBackups）与存储机制（/api/config）不变，仅从任务表单移入设置弹窗 */
function updateGlobalSummary() {
  const el = $('globalSummary');
  if (!el) return;
  const dir = $('cfg_scan').value.trim() || '未设置';
  const tool = $('cfg_tool').value === 'assfonts' ? 'assfonts' : 'AssFontSubset';
  el.textContent = '工作目录 ' + dir + ' · 子集化工具 ' + tool;
}
function openGlobal() {
  $('cfg_scan').value = CFG.scanRoot || $('cfg_scan').value;
  updateGlobalSummary();
  $('globalModal').style.display = 'flex';
}
$('globalClose').onclick = () => { $('globalModal').style.display = 'none'; };
$('btnOpenSettings').onclick = openGlobal;

/* ==================== 模式切换 ==================== */
function switchMode(mode) {
  document.querySelectorAll('.mode').forEach(function (m) { m.classList.toggle('active', m.id === 'mode-' + mode); });
  document.querySelectorAll('.mode-tab').forEach(function (b) { b.classList.toggle('active', b.dataset.mode === mode); });
  document.body.classList.toggle('single-active', mode === 'single');   // 单封装固定状态条：给页尾让位
  document.body.classList.toggle('batch-active', mode === 'batch');     // 批量固定状态条同理
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
    card.className = 'file-card compact';   // 已选择视频：收缩为紧凑摘要行，减少纵向占用
    card.onclick = null;
    card.innerHTML =
      '<span class="file-ic" data-ic="film"></span>' +
      '<div class="file-meta">' +
        '<div class="file-name" title="' + esc(v) + '">' + esc(name) + '</div>' +
        '<div class="file-path" title="' + esc(v) + '">' + esc(truncMid(v, 52)) + '</div>' +
        '<div class="file-trackinfo" id="videoTrackInfo"></div>' +
      '</div>' +
      '<div class="file-actions">' +
        '<button type="button" class="btn small" id="cardReplace"><span data-ic="refreshCw"></span>更换</button>' +
        '<button type="button" class="btn small ghost" id="cardRemove" title="移除视频" aria-label="移除视频"><span data-ic="trash"></span>移除</button>' +
      '</div>' +
      '<input id="video" type="text" class="visually-hidden" autocomplete="off">';
    card.querySelector('[data-ic="film"]').innerHTML = ic('film');
    card.querySelector('[data-ic="refreshCw"]').innerHTML = ic('refreshCw');
    card.querySelector('[data-ic="trash"]').innerHTML = ic('trash');
    $('cardReplace').onclick = function (e) { e.stopPropagation(); browse(); };
    $('cardRemove').onclick = function (e) { e.stopPropagation(); pickVideoPath(''); };
    $('video').value = v; // 重建的隐藏输入框必须回填，否则拖放/浏览后值蒸发（探针/粘条读到空值）
    renderVideoTrackInfo();
  }
  wireVideo();
}
let lastVideo = '';
function wireVideo() {
  const inp = $('video');
  inp.onchange = async function () {
    lastResult = null;   // 输入变更：清除上次任务结果，恢复静态状态
    hideTaskSummary();
    const v = inp.value.trim();
    const replaced = v && v !== lastVideo;
    if (replaced) {
      // 视频已更换：旧字幕属于旧视频，清空防止重新匹配时张冠李戴
      $('sc_sub').value = ''; $('tc_sub').value = '';
      $('sc_enc').textContent = ''; $('tc_enc').textContent = '';
      $('sc_name').value = 'SC'; $('tc_name').value = 'TC';
      subCheckUi.sc = null; subCheckUi.tc = null;   // 换视频后旧体检结果作废
      subCheckSig.sc = ''; subCheckSig.tc = '';
      fontSig = '';
      hidePreflightIssues();
      syncSubStatus();
      setStatus('视频已更换，字幕已清空；正在自动识别字幕与字体目录…', 'run');
    }
    lastVideo = v;
    trackSel.audio.clear(); trackSel.sub.clear();
    trackSel.allAudio = []; trackSel.allSub = []; trackSel.keepAtt = false;
    $('probeBox').innerHTML = '';
    renderVideoCard();
    if (!v) probeCache = null;
    else if (!probeCache || probeCache.video !== v) autoProbe(v);   // 自动探测媒体信息（紧凑摘要行 + {res} 预览）
    refreshOutPreview();
    if (!replaced) hidePreflightIssues();
    refreshSticky();
    if (replaced) {
      // 新视频自动识别（与批量添加文件同一入口，见 identify.js）；识别期间视频又被更换则丢弃结果
      const id = await identify(v);
      if (inp.value.trim() !== v) return;
      applyIdentify($('sc_sub'), $('tc_sub'), $('fonts_dir'), id, $('chapters'));   // 已有值不覆盖
      autoTrackName('sc_sub', 'sc_name', 'sc');
      autoTrackName('tc_sub', 'tc_name', 'tc');
      syncSubStatus();
      lastResult = null; refreshSticky();
      const hits = [id.sc && '简体字幕', id.tc && '繁体字幕', id.fontsDir && '字体目录', id.chapters && '章节'].filter(Boolean);
      if (hits.length) setStatus('已自动识别：' + hits.join('、'), 'ok');
      else setStatus('未自动识别到字幕与字体目录，可手动填写或点「自动匹配字幕」重试', 'info');
    }
  };
  inp.onkeydown = function (e) { if (e.key === 'Enter') inp.blur(); };
}

/* ==================== 状态刷新（粘性操作条） ==================== */
let lastResult = null;   // 最近一次任务结果（完成/失败/停止），输入变更后清除
function refreshSticky() {
  updateConsoleStatus();
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
  updateConsoleStatus();
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
    txt.textContent = '已准备 ' + filled + ' 个文件';
    btn.disabled = false;
  }
}
function syncSubStatus() {
  ['sc', 'tc'].forEach(function (kind) {
    const el = $(kind + 'Status'), txt = el.querySelector('.sub-status-txt');
    const sub = $(kind + '_sub').value.trim();
    if (sub) { el.className = 'sub-status on'; el.firstElementChild.innerHTML = ic('check'); txt.textContent = '已加载'; }
    else { el.className = 'sub-status off'; el.firstElementChild.innerHTML = ic('info'); txt.textContent = '未选择字幕'; }
  });
  syncDefaultBadge();
  ['sc', 'tc'].forEach(renderSubCard);
  syncSegControls();
  updateFontsSummary();
  updateAudioSummary();
}
/* 字幕轨道卡：Header 一行承载语言/加载状态/默认轨/强制/折叠；Body 含文件区与轨道设置 */
function renderSubCard(kind) {
  const sub = $(kind + '_sub').value.trim();
  const card = $(kind + 'Card');
  if (!card) return;
  const nameEl = $(kind + 'FileName');
  const pathEl = $(kind + 'FilePath');
  const headFile = $(kind + 'HeadFile');
  const pick = $(kind === 'sc' ? 'btnScPick' : 'btnTcPick');
  const encRow = $(kind + 'EncRow');
  if (encRow) encRow.style.display = ($(kind + '_enc').textContent || '').trim() ? '' : 'none';
  renderCheckRow(kind);
  renderFontRow(kind);
  const wasFilled = card.dataset.filled === '1';
  if (sub) {
    card.classList.add('filled');
    const fname = sub.split(/[\\/]/).pop() || sub;
    nameEl.textContent = fname;
    nameEl.title = sub;
    pathEl.textContent = sub;
    pathEl.title = sub;
    if (headFile) { headFile.textContent = fname; headFile.title = sub; }
    if (pick) { pick.className = 'btn small'; pick.innerHTML = ic('refreshCw') + '<span>更换</span>'; }
  } else {
    card.classList.remove('filled');
    nameEl.textContent = '尚未选择字幕文件';
    nameEl.title = '';
    pathEl.textContent = '';
    pathEl.title = '';
    if (headFile) { headFile.textContent = ''; headFile.title = ''; }
    if (pick) { pick.className = 'btn small primary'; pick.innerHTML = ic('fileText') + '<span>选择字幕</span>'; }
  }
  // 空卡与刚填充/刚移除的卡保持展开（提供操作入口），其余尊重用户折叠状态
  if (wasFilled !== (sub ? '1' : '0')) setSubCardOpen(kind, true);
  card.dataset.filled = sub ? '1' : '0';
}
/* 内容体检摘要行（btnSubCheck 写入 subCheckUi 后经此渲染） */
function renderCheckRow(kind) {
  const row = $(kind + 'CheckRow');
  if (!row) return;
  const st = subCheckUi[kind];
  if (!st) { row.style.display = 'none'; row.innerHTML = ''; return; }
  row.style.display = '';
  row.className = 'sub-status ' + st.cls;
  row.innerHTML = ic(st.icon) + '<span>' + esc(st.text) + '</span>';
}
/* 字体体检摘要行（全局体检结果，字幕已加载时才显示） */
function renderFontRow(kind) {
  const row = $(kind + 'FontRow');
  if (!row) return;
  const st = fontState.status;
  if (st === 'idle' || st === 'loading' || st === 'unknown' || !$(kind + '_sub').value.trim()) { row.style.display = 'none'; return; }
  row.style.display = '';
  if (st === 'ok') { row.className = 'sub-status on'; row.innerHTML = ic('checkCircle') + '<span>字体完整</span>'; }
  else if (st === 'warn') { row.className = 'sub-status warn'; row.innerHTML = ic('alertTriangle') + '<span>缺少 ' + fontState.missing + ' 个字体</span>'; }
  else { row.className = 'sub-status err'; row.innerHTML = ic('xCircle') + '<span>字体检查失败</span>'; }
}
/* 卡片折叠/展开（Header 触发；纯 UI 状态，不影响任何数据） */
const subCardUi = { sc: { open: true }, tc: { open: true } };
function setSubCardOpen(kind, open) {
  subCardUi[kind].open = open;
  const card = $(kind + 'Card');
  if (!card) return;
  card.classList.toggle('open', open);
  const head = card.querySelector('.sub-head');
  if (head) head.setAttribute('aria-expanded', open ? 'true' : 'false');
}
function toggleSubCard(kind) { setSubCardOpen(kind, !subCardUi[kind].open); }
/* SC/TC「默认轨道 / 可选」徽章随字幕填写与旗标选择动态切换；Forced 摘要 chip 同步 */
function syncDefaultBadge() {
  const sc = $('sc_sub').value.trim(), tc = $('tc_sub').value.trim();
  const scSel = $('sc_default').value, tcSel = $('tc_default').value;
  const setBadge = (el, state) => {
    if (state === 'yes' || state === 'auto-yes') { el.textContent = '默认轨'; el.className = 'chip sm track-default'; }
    else { el.textContent = '非默认'; el.className = 'chip sm info'; }
  };
  // 值语义不变：'' 自动 / '1' 强制默认 / '0' 明确非默认；自动判定沿用原规则（有 SC 则 SC，仅 TC 则 TC）
  setBadge($('scDefaultBadge'), scSel === '1' ? 'yes' : scSel === '0' ? 'no' : (sc || !tc ? 'auto-yes' : 'auto-no'));
  setBadge($('tcDefaultBadge'), tcSel === '1' ? 'yes' : tcSel === '0' ? 'no' : (tc && !sc ? 'auto-yes' : 'auto-no'));
  const scF = $('scForcedChip'), tcF = $('tcForcedChip');
  if (scF) scF.style.display = $('sc_forced').checked ? '' : 'none';
  if (tcF) tcF.style.display = $('tc_forced').checked ? '' : 'none';
  // 「自动」是配置方式，Header 徽章是最终结果；此处给自动模式一个轻量判定结果提示
  const scYes = scSel === '1' || (scSel === '' && (sc || !tc));
  const tcYes = tcSel === '1' || (tcSel === '' && (tc && !sc));
  const scHint = $('scAutoHint'), tcHint = $('tcAutoHint');
  if (scHint) scHint.textContent = scSel === '' ? ('自动判定：' + (scYes ? '默认轨' : '非默认')) : '';
  if (tcHint) tcHint.textContent = tcSel === '' ? ('自动判定：' + (tcYes ? '默认轨' : '非默认')) : '';
}
/* 运行中：把顶部状态镜像到粘性操作条 + 同步进度条 */
(function () {
  new MutationObserver(function () {
    const s = $('status');
    updateConsoleStatus();   // 控制台折叠条状态与顶部状态联动（含运行中实时文案）
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
  // 单封装面板内进度条已删除，进度直接写底部状态条（onAny 内）；批量同理由 batch.js 直写
})();

/* ==================== 文件浏览器 ==================== */
const CFG = { scanRoot: 'D:\\Video' };
function openBrowser(setter, filter, startPath, slot, dirSetter) {
  BR.setter = setter; BR.filter = filter; BR.slot = slot || filter || 'generic'; BR.dirSetter = dirSetter || null;
  BR.path = startPath || localStorage.getItem('muxui_ld_' + BR.slot) || localStorage.getItem('muxui_lastdir') || CFG.scanRoot;
  $('browserModal').style.display = 'block';
  showBrowser();
}
$('mbClose').onclick = () => $('browserModal').style.display = 'none';
$('mbUp').onclick = () => { BR.path = BR.path.replace(/\\+$/, '').replace(/[^\\/]+$/, '') || ''; showBrowser(); }; // 到盘根后再向上进入盘符列表
$('mbGo').onclick = () => { BR.path = $('mbPathInput').value.trim() || BR.path; showBrowser(); };
$('mbPathInput').onkeydown = e => { if (e.key === 'Enter') $('mbGo').click(); };
$('mbUseDir').onclick = () => {
  const fn = BR.dirSetter || BR.setter;
  fn(BR.path.replace(/\\+$/, ''));
  $('browserModal').style.display = 'none';
};
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
  $('mbUseDir').style.display = ((BR.filter === 'dir' || BR.dirSetter) && d.path) ? '' : 'none';
  $('mbUseDir').innerHTML = (BR.dirSetter && BR.filter !== 'dir')
    ? ic('folderOutput') + '<span>添加此目录全部视频</span>'
    : ic('check') + '<span>使用此目录</span>';
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
  $('chapters').value = '';
  $('out_name_tmpl').value = '';
  $('title').value = '';
  $('audio').value = ''; $('audio_lang').value = ''; $('audio_name').value = '';
  $('out_dir').value = '';
  $('backup').checked = true; $('force').checked = false;
  $('fontCheckBox').innerHTML = '';
  $('subCheckBox').innerHTML = '';   // 内容体检明细区一并清空（与卡片摘要行同步复位）
  subCheckUi.sc = null; subCheckUi.tc = null;
  subCheckSig.sc = ''; subCheckSig.tc = '';
  fontState = { status: 'idle', missing: 0 };
  fontSig = '';
  hideTaskSummary();
  hidePreflightIssues();
  setResult('');
  // 底部状态条一并复位（与启动时初态一致）
  $('stickyProgress').classList.remove('run');
  $('stickyBar').style.width = '0%';
  $('stickyPct').textContent = '--';
  $('stickyElapsed').textContent = '--:--:--';
  $('stickyEta').textContent = '--:--:--';
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
    const id = await identify(v);   // 统一识别：字幕 + 字体目录（识别逻辑见 identify.js）
    if ($('video').value.trim() !== v) return; // 视频已变更，丢弃过期结果
    const scHad = !!$('sc_sub').value.trim(), tcHad = !!$('tc_sub').value.trim();
    let sc = false, tc = false;
    if (id.sc && !scHad) { $('sc_sub').value = id.sc; autoTrackName('sc_sub', 'sc_name', 'sc'); sc = true; }
    if (id.tc && !tcHad) { $('tc_sub').value = id.tc; autoTrackName('tc_sub', 'tc_name', 'tc'); tc = true; }
    syncSubStatus();
    const fontFound = !!(id.fontsDir && !$('fonts_dir').value.trim() && ($('fonts_dir').value = id.fontsDir, true));
    const chapFound = !!(id.chapters && !$('chapters').value.trim() && ($('chapters').value = id.chapters, true));
    lastResult = null; refreshSticky();   // 字幕已填充：同步底部操作栏状态
    const extra = (fontFound ? ' · 已自动识别字体目录' : '') + (chapFound ? ' · 已自动识别章节' : '');
    if (sc || tc) {
      setStatus('字幕匹配完成：已填充 ' + (sc ? '简体' : '') + (sc && tc ? ' + ' : '') + (tc ? '繁体' : '') + extra, 'ok');
    } else if (id.sc || id.tc) {
      setStatus('匹配到的字幕槽位已有内容，未覆盖（重置后可重新填充）' + extra, 'ok');
    } else {
      setStatus('未匹配到任何字幕（简 0 / 繁 0）' + extra, 'err');
    }
  } catch (ex) {
    setStatus('字幕匹配失败：' + ex, 'err');
  } finally {
    $('btnAutoMatch').disabled = false;
  }
};

/* ==================== 查看轨道 / 视频媒体信息自动探测 ==================== */
let probeCache = null;   // { video, data } 最近一次 probe 结果（紧凑摘要行 / 输出预览 {res} / 查看轨道共用）
function renderVideoTrackInfo() {
  const el = $('videoTrackInfo');
  if (!el) return;
  const d = probeCache && probeCache.data;
  if (!d) { el.textContent = ''; return; }
  if (d.error) { el.textContent = '⚠ 无法读取完整媒体信息（仍可封装，轨道信息可能不完整）'; el.className = 'file-trackinfo warn'; return; }
  el.className = 'file-trackinfo';
  const cnt = { video: 0, audio: 0, subtitles: 0 };
  (d.tracks || []).forEach(t => { if (cnt[t.type] !== undefined) cnt[t.type]++; });
  el.textContent = '视频 ' + cnt.video + ' · 音轨 ' + cnt.audio + ' · 字幕轨 ' + cnt.subtitles + ' · 附件 ' + (d.attachments || 0);
}
async function fetchProbe(v) {
  const d = await api('/api/probe?path=' + encodeURIComponent(v));
  if ($('video').value.trim() !== v) return null;   // 视频已变更，丢弃过期结果
  probeCache = { video: v, data: d };
  renderVideoTrackInfo();
  refreshOutPreview();   // {res} 高度就绪后输出预览自动补全
  return d;
}
async function autoProbe(v) {
  if (!v) return;
  const el = $('videoTrackInfo');
  if (el) el.innerHTML = ic('loader', 'spin') + '<span> 正在读取媒体信息…</span>';
  try { await fetchProbe(v); } catch (ex) { probeCache = null; renderVideoTrackInfo(); }
}
const trackSel = { audio: new Set(), sub: new Set(), keepAtt: false, allAudio: [], allSub: [] };
function toggleSel(id, kind) { const set = kind === 'audio' ? trackSel.audio : trackSel.sub; if (set.has(id)) set.delete(id); else set.add(id); }
function toggleAtt(v) { trackSel.keepAtt = v; }
$('btnProbe').onclick = async () => {
  const v = $('video').value.trim();
  if (!v) { alert('请先选择视频文件'); return; }
  const d = await fetchProbe(v);
  if (!d) return;
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
    syncSubStatus();   // 编码摘要行随错误文案一并刷新
  } finally {
    $('btnPrepSubs').disabled = false;
  }
};

/* ==================== 字幕内容体检（时间轴/CPS/行宽/样式，纯文本分析） ==================== */
const SUBCHECK_TYPE = { overlap: '时间重叠', empty: '空台词', bad_time: '时间错误', bad_style: '坏样式', cps: 'CPS 超速', long_line: '单行过长' };
const subCheckUi = { sc: null, tc: null };   // 各轨体检摘要（写入字幕卡摘要行，结果明细仍在 subCheckBox）
const subCheckSig = { sc: '', tc: '' };      // 体检时的字幕路径：路径变更即结果过期（preflight 降级为 info）
function setSubCheckUi(kind, cls, icon, text) { subCheckUi[kind] = { cls, icon, text }; subCheckSig[kind] = $(kind + '_sub').value.trim(); renderSubCard(kind); }
$('btnSubCheck').onclick = async () => {
  const subs = [['sc', $('sc_sub').value.trim()], ['tc', $('tc_sub').value.trim()]].filter(x => x[1]);
  if (!subs.length) { alert('请先填写字幕路径'); return; }
  $('btnSubCheck').disabled = true;
  $('subCheckBox').innerHTML = '<div class="chip run" style="margin-top:8px">' + ic('loader', 'spin') + '<span>正在分析…</span></div>';
  const blocks = [];
  try {
    for (const [kind, sub] of subs) {
      let r;
      try {
        r = await api('/api/sub_check', { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify({ sub }) });
      } catch (ex) {
        blocks.push('<div class="chip err" style="margin-top:8px">' + ic('xCircle') + '<span>' + esc(pvBaseName ? pvBaseName(sub) : sub) + ' 连接失败：' + esc(ex) + '</span></div>');
        setSubCheckUi(kind, 'err', 'xCircle', '体检失败（连接失败）');
        continue;
      }
      const name = esc(sub.split(/[\\/]/).pop());
      if (r.error) {
        blocks.push('<div class="chip err" style="margin-top:8px">' + ic('xCircle') + '<span>' + name + '：' + esc(r.error) + '</span></div>');
        setSubCheckUi(kind, 'err', 'xCircle', '体检失败');
        continue;
      }
      if (r.status === 'ok') {
        blocks.push('<div class="chip ok" style="margin-top:8px">' + ic('checkCircle') + '<span>' + name + '：内容体检通过（' + r.dialogue + ' 行 Dialogue）</span></div>');
        setSubCheckUi(kind, 'on', 'checkCircle', '体检通过');
        continue;
      }
      const cnt = r.counts || {};
      const parts = Object.keys(SUBCHECK_TYPE).filter(k => cnt[k]).map(k => SUBCHECK_TYPE[k] + ' ' + cnt[k]);
      const total = r.total_issues || 0;
      let h = '<div class="chip warn" style="margin-top:8px">' + ic('alertTriangle') + '<span>' + name + '：' + (parts.join(' · ') || (total + ' 项预警')) + '（' + r.dialogue + ' 行）</span></div>';
      h += '<details class="check-detail"><summary>展开明细</summary><pre class="log-pre">' + esc(r.issues.map(i => '第' + i.line + '行 [' + (SUBCHECK_TYPE[i.type] || i.type) + '] ' + i.detail).join('\n')) + (r.truncated ? '\n…（仅显示前 200 条）' : '') + '</pre></details>';
      blocks.push(h);
      setSubCheckUi(kind, 'warn', 'alertTriangle', total + ' 项预警');
    }
    $('subCheckBox').innerHTML = blocks.join('');
  } finally {
    $('btnSubCheck').disabled = false;
  }
};

/* ==================== 字体体检 ==================== */
let fontState = { status: 'idle', missing: 0 };   // 体检状态（字体折叠摘要 + 字幕卡摘要行引用）
let fontSig = '';                                 // 体检时的 [sc, tc, fonts_dir]：任一变更即结果过期
function markFontChecked() { fontSig = JSON.stringify([$('sc_sub').value.trim(), $('tc_sub').value.trim(), $('fonts_dir').value.trim()]); }
function refreshFontSummaryUI() {
  updateFontsSummary();
  ['sc', 'tc'].forEach(renderSubCard);
}
$('btnCheckFonts').onclick = async () => {
  const subs = [$('sc_sub').value.trim(), $('tc_sub').value.trim()].filter(Boolean);
  const fonts_dir = $('fonts_dir').value.trim();
  if (!subs.length) { alert('请先填写字幕路径'); return; }
  $('btnCheckFonts').disabled = true;
  fontState = { status: 'loading', missing: 0 };
  refreshFontSummaryUI();
  $('fontCheckBox').innerHTML = '<div class="chip run" style="margin-top:8px">' + ic('loader', 'spin') + '<span>正在检查字体，请稍候…</span></div>';
  try {
    const r = await api('/api/check_fonts', { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify({subs, fonts_dir}) });
    renderFontCheck(r, subs, fonts_dir);
    markFontChecked();
  } catch (ex) {
    fontState = { status: 'error', missing: 0 };
    refreshFontSummaryUI();
    $('fontCheckBox').innerHTML = '<div class="chip err" style="margin-top:8px">' + ic('xCircle') + '<span>连接失败：' + esc(ex) + '</span></div>';
  } finally {
    $('btnCheckFonts').disabled = false;
  }
};
/* 体检结果渲染（含缺字体时的补给入口） */
function renderFontCheck(r, subs, fonts_dir) {
  const box = $('fontCheckBox');
  if (r.error) {
    fontState = { status: 'error', missing: 0 };
    refreshFontSummaryUI();
    box.innerHTML = '<div class="chip warn" style="margin-top:8px">' + ic('alertTriangle') + '<span>' + esc(r.error) + '</span></div>' + (r.log ? '<pre class="log-pre">' + esc(r.log) + '</pre>' : '');
    return;
  }
  const missing = r.missing || [];
  if (r.ok && !missing.length) {
    fontState = { status: 'ok', missing: 0 };
    refreshFontSummaryUI();
    box.innerHTML = '<div class="chip ok" style="margin-top:8px">' + ic('checkCircle') + '<span>字体齐全，可正常封装</span></div>';
    markFontChecked();
    return;
  }
  if (!r.ok && !r.error && !missing.length) {
    fontState = { status: 'unknown', missing: 0 };   // 响应不完整：不判定，避免误报
    refreshFontSummaryUI();
    box.innerHTML = '<div class="chip info" style="margin-top:8px">' + ic('info') + '<span>字体体检结果不可确认，请重试</span></div>';
    return;
  }
  fontState = { status: 'warn', missing: missing.length };
  refreshFontSummaryUI();
  let html = '<div class="chip warn" style="margin-top:8px">' + ic('alertTriangle') + '<span>缺少 ' + missing.length + ' 个字体</span></div><pre class="log-pre">' + esc(missing.join('\n')) + '</pre>';
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
      fontState = rc.ok ? { status: 'ok', missing: 0 } : { status: 'warn', missing: (rc.missing || []).length };
      refreshFontSummaryUI();
      markFontChecked();
      const head = rc.ok
        ? '<div class="chip ok" style="margin-top:8px">' + ic('checkCircle') + '<span>补给完成，复检通过 ✓</span></div>'
        : '<div class="chip warn" style="margin-top:8px">' + ic('alertTriangle') + '<span>补给后仍缺字体：</span></div>';
      $('fontCheckBox').innerHTML = head + (parts.length ? '<pre class="log-pre">' + esc(parts.join('\n')) + '</pre>' : '') + (rc.missing && rc.missing.length ? '<pre class="log-pre">' + esc(rc.missing.join('\n')) + '</pre>' : '');
    } catch (ex) {
      $('fontCheckBox').innerHTML = '<div class="chip err" style="margin-top:8px">' + ic('xCircle') + '<span>补给失败：' + esc(ex) + '</span></div>';
    }
  };
}

/* ==================== 单个封装（封装前检查 + 提交） ==================== */
let job = null;
$('btnStart').onclick = async () => {
  if (job) {
    setStatus('正在停止…', 'run');
    await api('/api/stop', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ id: job }) });
    return;
  }
  if (!$('video').value.trim()) { alert('请选择视频文件'); return; }
  hidePreflightIssues();
  setStatus('正在检查封装条件…', 'run');
  let pf;
  try { pf = await getPreflightResult(); }
  catch (ex) { startMuxTask(); return; }   // 检查自身异常不阻断任务（保持原有可用性）
  if (pf.blocking.length) {
    showPreflightIssues(pf);   // 阻断项就近列出（含修复入口），不打扰其他区域
    setStatus('有 ' + pf.blocking.length + ' 项问题需要处理', 'err');
    return;
  }
  // 仅“未提供字幕”一项提醒时沿用原有原生确认（一次确认，不叠加弹窗）
  const noSubOnly = pf.warnings.length === 1 && pf.warnings[0].code === 'no_subtitle';
  if (pf.warnings.length && !noSubOnly) { openPreflightModal(pf); return; }
  if (noSubOnly && !confirm('未提供任何字幕，将保留源字幕与源字体（无新字幕时不做字体子集化）。继续？')) return;
  startMuxTask();
};

async function startMuxTask() {
  const common = buildMuxCommon('');   // 公共参数（字体/输出/备份/旗标，与批量同一份逻辑，见 task.js）
  const body = Object.assign({
    video: $('video').value.trim(), sc_sub: $('sc_sub').value.trim(), tc_sub: $('tc_sub').value.trim(),
    sc_name: $('sc_name').value.trim() || 'SC', tc_name: $('tc_name').value.trim() || 'TC',
    audio: $('audio').value.trim(),
    chapters: $('chapters').value.trim(),
    audio_tracks: (trackSel.allAudio.length === 0) ? '' : (trackSel.audio.size === 0) ? 'none' : (trackSel.audio.size < trackSel.allAudio.length) ? [...trackSel.audio].join(',') : '',
    subtitle_tracks: trackSel.sub.size ? [...trackSel.sub].join(',') : '',
    keep_attachments: trackSel.keepAtt,
    audio_lang: $('audio_lang').value.trim(), audio_name: $('audio_name').value.trim()
  }, common);
  setStatus('正在提交…', 'run'); setResult(''); hideTaskSummary(); hidePreflightIssues();
  const r = await api('/api/mux', { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify(body) });
  if (r.error) { setStatus('错误：' + r.error, 'err'); return; }
  job = r.job;
  stickyStartTs = Date.now();
  $('stickyProgress').classList.add('run');   // 进度条流动高光
  $('stickyBar').style.width = '0%';
  $('stickyPct').textContent = '--';
  $('stickyElapsed').textContent = '00:00:00';
  $('stickyEta').textContent = '计算中…';
  setRunButton($('btnStart'), true, '停止封装', '开始封装');
  showLogTab('mux'); setLog('mux', '');
  const fin = (s, lastR, statusMsg, statusCls, resultHtml) => {
    job = null;
    setRunButton($('btnStart'), false, '停止封装', '开始封装');
    $('stickyProgress').classList.remove('run');
    stickyTimesFreeze();   // 耗时定格、剩余清空（进度条与百分比一致定格，下次启动时归零）
    setStatus(statusMsg, statusCls);
    lastResult = lastR;
    if (resultHtml) $('result').innerHTML = resultHtml;
    refreshSticky();
  };
  startTaskPolling({
    job, interval: 1200,
    onAny: s => { setLog('mux', s.log); if (s.progress != null) $('stickyBar').style.width = s.progress + '%'; },
    onTick: s => {
      setStickyRun($('stickyNote'), muxStage(s).label);   // 运行中：分阶段 + 进度
      stickyTimesRunning(s.progress);
      if (s.progress != null) $('stickyPct').textContent = s.progress + '%';
    },
    onDone: s => { beep(); $('stickyPct').textContent = '100%'; fin(s, { cls: 'ok', icon: 'checkCircle', text: '封装完成' }, '封装完成', 'ok',
      '<span class="t-sec">输出：</span><code class="mono" style="color:var(--text-primary)">' + esc(s.result || '') + '</code> <button class="btn small" data-open-dir="' + encodeURIComponent(s.result || '') + '">' + ic('arrowUpRight') + '打开文件夹</button>'
      + (s.qc ? '<span class="chip sm ' + (s.qc.status === 'ok' ? 'ok' : 'warn') + '" title="' + esc(((s.qc.warn || [])).join('\n')) + '">QC' + (s.qc.status === 'ok' ? '通过' : '预警' + (s.qc.warn || []).length) + '</span>' : '')
      + (s.cmd ? ' <button class="btn small" data-cmd="' + b64e(s.cmd) + '">' + ic('terminal') + '查看命令</button>' : ''));
      showTaskSummary('ok', 'checkCircle', '封装完成', { path: s.result, elapsed: $('stickyElapsed').textContent,
        warn: (s.qc && s.qc.status !== 'ok') ? ((s.qc.warn || []).length + ' 个警告') : '', warnLines: (s.qc && s.qc.warn) || [] }); },
    onError: s => { const reason = s.reason || ('退出码 ' + (s.exit ?? '?')); fin(s, { cls: 'err', icon: 'xCircle', text: '封装失败：' + reason }, '封装失败：' + reason, 'err',
      '<span class="chip err">' + ic('xCircle') + '<span>封装失败：' + esc(reason) + '</span></span>');
      showTaskSummary('err', 'xCircle', '封装失败', { detail: reason, elapsed: $('stickyElapsed').textContent, retry: true }); },
    onKilled: s => { fin(s, { cls: 'info', icon: 'info', text: '任务已停止' }, '已停止', 'err',
      '<span class="chip info">' + ic('info') + '<span>任务已停止</span></span>');
      showTaskSummary('info', 'info', '任务已停止', { elapsed: $('stickyElapsed').textContent }); },
    onLost: () => fin(null, { cls: 'err', icon: 'xCircle', text: '连接丢失，请刷新' }, '连接丢失，请刷新', 'err',
      '<span class="chip err">' + ic('xCircle') + '<span>连接丢失，请刷新页面后重试</span></span>')
  });
};

/* ==================== 封装前检查（preflight） ====================
 * 汇总既有状态与轻量检查，返回 { blocking, warnings, info }；不重跑昂贵检查：
 * 字幕内容/字体体检仅在结果未过期时计为 warning，过期或未跑降级为 info 引导用户手动运行。 */
function subCheckFresh(kind) { return !!subCheckUi[kind] && subCheckSig[kind] === $(kind + '_sub').value.trim(); }
function fontCheckFresh() {
  return (fontState.status === 'ok' || fontState.status === 'warn' || fontState.status === 'error') &&
    fontSig === JSON.stringify([$('sc_sub').value.trim(), $('tc_sub').value.trim(), $('fonts_dir').value.trim()]);
}
async function getPreflightResult() {
  const video = $('video').value.trim();
  const sc = $('sc_sub').value.trim(), tc = $('tc_sub').value.trim();
  const outDir = $('out_dir').value.trim();
  const items = [];
  const add = (type, code, title, description, source, action) => items.push({ type, code, title, description, source, action: action || '' });
  let fs = {};
  try {
    fs = await api('/api/preflight_fs', { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify({ video: video, sc: sc, tc: tc, out_dir: outDir }) }) || {};
  } catch (ex) { fs = {}; }
  let out = null;
  try {
    out = await api('/api/out_preview', { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify({ video: video, template: $('out_name_tmpl').value.trim(), title: $('title').value.trim(), out_dir: outDir, height: videoHeight() }) });
  } catch (ex) { out = null; }

  /* 视频 */
  if (!video) add('error', 'no_video', '尚未选择视频', '选择视频后才能开始封装。', 'video', 'pick_video');
  else if (fs.video_ok === false) add('error', 'video_missing', '无法读取视频文件', '文件可能已移动、被删除或无访问权限。', 'video', 'pick_video');
  else if (probeCache && probeCache.video === video && probeCache.data && probeCache.data.error)
    add('warning', 'media_info', '无法读取完整媒体信息', '仍可尝试封装，但轨道信息可能不完整。', 'video', '');
  /* 字幕文件与状态 */
  if (sc && fs.sc_ok === false) add('error', 'sub_missing_sc', '简体字幕文件不存在', '文件可能已被移动或删除，请重新选择。', 'subtitle_sc', 'pick_sub_sc');
  if (tc && fs.tc_ok === false) add('error', 'sub_missing_tc', '繁体字幕文件不存在', '文件可能已被移动或删除，请重新选择。', 'subtitle_tc', 'pick_sub_tc');
  if (!sc && !tc) add('warning', 'no_subtitle', '未提供字幕', '将保留源字幕与源字体（无新字幕时不做字体子集化）。', 'subtitle', '');
  [['sc', '简体'], ['tc', '繁体']].forEach(function (pair) {
    const kind = pair[0], label = pair[1];
    if (!$(kind + '_sub').value.trim()) return;
    const enc = ($(kind + '_enc').textContent || '').trim();
    if (enc.indexOf('错误') === 0) add('warning', 'enc_' + kind, label + '字幕编码检查未通过', enc, 'subtitle_' + kind, '');
    else if (enc.indexOf('歧义') >= 0) add('warning', 'enc_ambig_' + kind, label + '字幕编码存在歧义', enc, 'subtitle_' + kind, '');
    const st = subCheckUi[kind];
    if (st) {
      if (st.cls === 'warn') add('warning', 'subcheck_' + kind, label + '字幕内容体检有预警', st.text + '（校对参考，不影响封装）', 'subtitle_' + kind, 'view_subcheck');
      else if (st.cls === 'err') add('warning', 'subcheck_err_' + kind, label + '字幕内容体检失败', '未能完成内容分析，可重新运行体检。', 'subtitle_' + kind, '');
      else if (!subCheckFresh(kind)) add('info', 'subcheck_stale_' + kind, label + '字幕内容已变更', '先前的体检结果已过期，可在字幕区重新运行。', 'subtitle_' + kind, '');
    } else add('info', 'subcheck_none_' + kind, label + '字幕尚未内容体检', '可在字幕区运行「内容体检」。', 'subtitle_' + kind, '');
  });
  /* 字体 */
  if (sc || tc) {
    if (fontState.status === 'warn') add('warning', 'missing_fonts', '缺少 ' + fontState.missing + ' 个字体', '字幕可以继续封装，但播放效果可能异常。', 'fonts', 'view_fonts');
    else if (fontState.status === 'error') add('warning', 'font_check_failed', '字体体检失败', '未能确认字体是否齐全，可重试体检。', 'fonts', '');
    else if (!fontCheckFresh()) add('info', 'fonts_not_checked', '字体尚未检查', '可在「字体设置」中运行字体体检。', 'fonts', '');
  }
  /* 输出 */
  if (outDir && fs.out_dir_ok === false) add('error', 'out_dir_missing', '输出目录不存在', '请检查输出目录路径。', 'output', 'pick_out');
  else if (outDir && fs.out_dir_writable === false) add('error', 'out_dir_unwritable', '输出目录不可写', '没有写入权限或路径无效。', 'output', 'pick_out');
  if (out && out.error) add('warning', 'out_unknown', '输出路径预览失败', out.error, 'output', '');
  else if (out && out.full) {
    if (out.unresolved_res) add('info', 'res_unresolved', '{res} 暂无法解析', '读取视频信息后自动补全。', 'output', '');
    if (out.exists) add('warning', 'out_exists', '目标文件已存在', out.full + '（继续封装将覆盖该文件）', 'output', '');
    if (out.replace) {
      const backupOn = $('backup').checked;
      add('warning', 'replace_source', '将替换原视频', '输出目录为空：原文件将' + (backupOn ? '备份到 __mux_tmp_manual 后替换。' : '被直接替换。'), 'output', '');
      if (!backupOn) add('warning', 'no_backup_replace', '原文件可能无法恢复', '替换原视频且未启用备份。', 'output', 'enable_backup');
    }
  }
  /* 工具 / 环境 */
  if (ENV.overall === 'broken') add('error', 'env_broken', '封装组件缺失', '必需组件不可用，请打开环境检测安装。', 'env', 'open_env');
  else if (ENV.overall === 'partial' && (sc || tc) && $('fonts_mode').value === 'subset')
    add('warning', 'tool_partial', '子集化组件部分缺失', '可选组件不可用，子集化可能回退或跳过。', 'env', 'open_env');
  if ($('force').checked) add('warning', 'force_enabled', '已启用强制封装', '源视频已有字体附件时将强制重建附件。', 'task', '');
  return {
    blocking: items.filter(function (i) { return i.type === 'error'; }),
    warnings: items.filter(function (i) { return i.type === 'warning'; }),
    info: items.filter(function (i) { return i.type === 'info'; })
  };
}

/* --- 渲染：条目 / 阻断条 / 确认弹窗 --- */
const PF_ACTION_LABEL = { pick_video: '重新选择', pick_sub_sc: '更换字幕', pick_sub_tc: '更换字幕', pick_out: '重新选择',
  view_fonts: '前往字体设置', view_subcheck: '查看体检结果', open_env: '前往设置', enable_backup: '启用备份' };
function pfItemHtml(it, withAction) {
  const icn = it.type === 'error' ? 'xCircle' : it.type === 'warning' ? 'alertTriangle' : 'info';
  let h = '<div class="pf-item ' + it.type + '"><span class="pf-ic">' + ic(icn) + '</span><div class="pf-main"><div class="pf-title">' + esc(it.title) + '</div>'
    + (it.description ? '<div class="pf-desc">' + esc(it.description) + '</div>' : '') + '</div>';
  if (withAction && it.action) h += '<button type="button" class="btn small" data-pf-action="' + esc(it.action) + '">' + esc(PF_ACTION_LABEL[it.action] || '处理') + '</button>';
  return h + '</div>';
}
function hidePreflightIssues() {
  const el = $('preflightBox');
  if (el) { el.style.display = 'none'; el.innerHTML = ''; }
}
function showPreflightIssues(pf) {
  const el = $('preflightBox');
  if (!el) return;
  let h = '<div class="pf-strip-head">' + ic('xCircle') + '<span>还有 ' + pf.blocking.length + ' 项需要处理，已阻止开始封装</span></div>'
    + pf.blocking.map(function (it) { return pfItemHtml(it, true); }).join('');
  if (pf.warnings.length) h += '<div class="pf-strip-note">另有 ' + pf.warnings.length + ' 项提醒，处理阻断项后开始时会再确认。</div>';
  el.innerHTML = h;
  el.style.display = '';
}
function openPreflightModal(pf) {
  $('pfHead').textContent = '发现 ' + pf.warnings.length + ' 个需要确认的项目';
  $('pfWarnList').innerHTML = pf.warnings.map(function (it) { return pfItemHtml(it, true); }).join('');
  const danger = pf.warnings.some(function (w) { return w.code === 'no_backup_replace'; });
  $('pfDanger').style.display = danger ? '' : 'none';
  $('pfDanger').innerHTML = ic('alertTriangle') + '<span>将覆盖原视频且未启用备份，原文件可能无法恢复。</span>';
  const hasInfo = pf.info.length > 0;
  $('pfToggleAll').style.display = hasInfo ? '' : 'none';
  $('pfInfoSec').style.display = 'none';
  $('pfInfoList').innerHTML = pf.info.map(function (it) { return pfItemHtml(it, false); }).join('');
  $('pfModal').style.display = 'flex';
}
$('pfCancel').onclick = function () { $('pfModal').style.display = 'none'; };
$('pfClose').onclick = function () { $('pfModal').style.display = 'none'; };
$('pfProceed').onclick = function () { $('pfModal').style.display = 'none'; startMuxTask(); };
$('pfToggleAll').onclick = function () {
  const sec = $('pfInfoSec');
  sec.style.display = sec.style.display === 'none' ? '' : 'none';
};
/* 修复动作统一分发（就近跳转，不堆 Alert） */
document.addEventListener('click', function (e) {
  const el = e.target.closest('[data-pf-action]');
  if (!el) return;
  const a = el.dataset.pfAction;
  if (a === 'pick_video') openBrowser(pickVideoPath, 'video', $('video').value, 'video');
  else if (a === 'pick_sub_sc') browseSub('sc');
  else if (a === 'pick_sub_tc') browseSub('tc');
  else if (a === 'pick_out') openBrowser(function (v) { $('out_dir').value = v; scheduleOutPreview(); }, 'dir', $('out_dir').value, 'out');
  else if (a === 'view_fonts') { toggleCollapse('fontsSec', true); $('fontsSec').scrollIntoView({ behavior: 'smooth', block: 'center' }); }
  else if (a === 'view_subcheck') $('subCheckBox').scrollIntoView({ behavior: 'smooth', block: 'center' });
  else if (a === 'open_env') openEnv();
  else if (a === 'enable_backup') { $('backup').checked = true; }
});
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
    const ms = await Promise.all(vids.map(v => identify(v)));
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
/* 手动填字幕（手输 change/input、浏览按钮）后同步粘性操作栏与字幕状态 */
function onManualSub(subField, nameField, kind) {
  lastResult = null;
  autoTrackName(subField, nameField, kind);
  const row = $(kind + 'FileInputRow');
  if (row && $(subField).value.trim()) row.style.display = 'none';   // 路径已填：回到文件信息展示
  syncSubStatus();
  refreshSticky();
}
$('sc_sub').addEventListener('change', function () { onManualSub('sc_sub', 'sc_name', 'sc'); });
$('tc_sub').addEventListener('change', function () { onManualSub('tc_sub', 'tc_name', 'tc'); });
$('sc_sub').addEventListener('input', function () { lastResult = null; syncSubStatus(); refreshSticky(); });
$('tc_sub').addEventListener('input', function () { lastResult = null; syncSubStatus(); refreshSticky(); });
$('sc_name').addEventListener('input', function () { });
/* 字幕选择：摘要区「选择字幕」（主操作）与编辑区「浏览」共用同一入口 */
function browseSub(kind) {
  openBrowser(v => { $(kind + '_sub').value = v; fireChange($(kind + '_sub')); }, 'sub', $(kind + '_sub').value, 'sub');
}
$('btnSc').onclick = () => browseSub('sc');
$('btnTc').onclick = () => browseSub('tc');
$('btnScPick').onclick = () => browseSub('sc');
$('btnTcPick').onclick = () => browseSub('tc');
/* 移除字幕：清空输入并联动状态（轨道名复位默认、编码徽章/体检摘要清除、sticky 刷新）；摘要区删除图标共用 */
function clearSub(kind) {
  $(kind + '_sub').value = '';
  $(kind + '_name').value = kind.toUpperCase();
  $(kind + '_enc').textContent = '';
  subCheckUi[kind] = null;
  subCheckSig[kind] = '';
  fireChange($(kind + '_sub'));
}
/* 删除字幕轨 / 编辑文件路径：收入 ⋯ 菜单（危险与低频操作不长期暴露） */
function toggleMoreMenu(kind, force) {
  const btn = $(kind === 'sc' ? 'btnScMore' : 'btnTcMore'), menu = $(kind + 'MoreMenu');
  if (!btn || !menu) return;
  const open = typeof force === 'boolean' ? force : menu.style.display !== 'block';
  menu.style.display = open ? 'block' : 'none';
  btn.setAttribute('aria-expanded', open ? 'true' : 'false');
}
$('btnScClear').onclick = () => { toggleMoreMenu('sc', false); clearSub('sc'); };
$('btnTcClear').onclick = () => { toggleMoreMenu('tc', false); clearSub('tc'); };
/* Header 折叠：点击/键盘均可；Header 内按钮与 ⋯ 菜单不触发折叠 */
['sc', 'tc'].forEach(function (kind) {
  const head = $(kind + 'Card').querySelector('.sub-head');
  head.addEventListener('click', function (e) { if (e.target.closest('button') || e.target.closest('.more-wrap')) return; toggleSubCard(kind); });
  head.addEventListener('keydown', function (e) {
    if ((e.key === 'Enter' || e.key === ' ') && !e.target.closest('button') && !e.target.closest('.more-wrap')) { e.preventDefault(); toggleSubCard(kind); }
  });
});
/* 文件区展示模式优先；「编辑文件路径」（⋯ 菜单）按需展开输入行 */
function toggleManualPath(kind) {
  const row = $(kind + 'FileInputRow');
  if (!row) return;
  const show = row.style.display === 'none';
  row.style.display = show ? '' : 'none';
  if (show) { setSubCardOpen(kind, true); $(kind + '_sub').focus(); }
}
$('btnScManual').onclick = () => { toggleMoreMenu('sc', false); toggleManualPath('sc'); };
$('btnTcManual').onclick = () => { toggleMoreMenu('tc', false); toggleManualPath('tc'); };
$('btnScMore').onclick = function (e) { e.stopPropagation(); toggleMoreMenu('sc'); };
$('btnTcMore').onclick = function (e) { e.stopPropagation(); toggleMoreMenu('tc'); };
document.addEventListener('click', function (e) {
  if (!e.target.closest('.more-wrap')) { toggleMoreMenu('sc', false); toggleMoreMenu('tc', false); }
});
/* 默认轨三态 segmented control：写回隐藏域（'' 自动 / '1' 是 / '0' 否，语义与原 select 一致） */
function syncSegControls() {
  ['sc', 'tc'].forEach(function (kind) {
    const seg = $(kind + '_default_seg');
    if (!seg) return;
    const val = $(kind + '_default').value;
    seg.querySelectorAll('.seg-btn').forEach(b => b.classList.toggle('active', b.dataset.v === val));
  });
}
document.querySelectorAll('.seg').forEach(function (seg) {
  seg.addEventListener('click', function (e) {
    const b = e.target.closest('.seg-btn');
    if (!b) return;
    const kind = seg.id === 'sc_default_seg' ? 'sc' : 'tc';
    $(kind + '_default').value = b.dataset.v;
    syncSegControls();
    syncDefaultBadge();   // 摘要徽章即时反映
  });
});
$('sc_forced').addEventListener('change', syncDefaultBadge);
$('tc_forced').addEventListener('change', syncDefaultBadge);
$('btnFonts').onclick = () => openBrowser(v => { $('fonts_dir').value = v; updateFontsSummary(); }, 'dir', $('fonts_dir').value, 'fonts');
$('btnAudio').onclick = () => openBrowser(v => { $('audio').value = v; updateAudioSummary(); }, 'audio', $('audio').value, 'audio');
$('btnOut').onclick = () => openBrowser(v => { $('out_dir').value = v; scheduleOutPreview(); }, 'dir', $('out_dir').value, 'out');
$('btnChapters').onclick = () => openBrowser(v => $('chapters').value = v, 'any', $('chapters').value, 'chapters');

/* ==================== 输出预览（服务端 /api/out_preview 复用 mux_cli.resolve_out_name，与实际封装同一套规则） ==================== */
let outPreviewTimer = null;
function scheduleOutPreview() {
  clearTimeout(outPreviewTimer);
  outPreviewTimer = setTimeout(refreshOutPreview, 300);
}
function videoHeight() { return (probeCache && probeCache.data && probeCache.data.video_height) || 0; }
async function refreshOutPreview() {
  const box = $('outPreview');
  if (!box) return;
  const video = $('video').value.trim();
  const tmpl = $('out_name_tmpl').value.trim();
  const outDir = $('out_dir').value.trim();
  if (!video) { box.innerHTML = '<span class="t-cap">选择视频后此处实时预览输出文件</span>'; return; }
  try {
    const r = await api('/api/out_preview', { method:'POST', headers:{'Content-Type':'application/json'},
      body: JSON.stringify({ video: video, template: tmpl, title: $('title').value.trim(), out_dir: outDir, height: videoHeight() }) });
    if ($('video').value.trim() !== video) return;   // 已换视频，丢弃过期结果
    if (!r || r.error || !r.full) { box.innerHTML = '<span class="t-cap">输出预览不可用' + (r && r.error ? '（' + esc(r.error) + '）' : '') + '</span>'; return; }
    let h = '<span class="op-label">输出预览</span>';
    if (r.replace) h += '<span class="chip sm warn">' + ic('alertTriangle') + '<span>将替换原视频</span></span>';
    if (r.exists) h += '<span class="chip sm warn">' + ic('alertTriangle') + '<span>目标文件已存在</span></span>';
    if (r.unresolved_res) h += '<span class="chip sm info">{res} 待视频信息</span>';
    h += '<span class="op-path">' + esc(r.full) + '</span>';
    box.innerHTML = h;
  } catch (ex) {
    box.innerHTML = '<span class="t-cap">输出预览不可用（' + esc(ex) + '）</span>';
  }
}
$('out_dir').addEventListener('input', scheduleOutPreview);
$('out_dir').addEventListener('change', scheduleOutPreview);
$('out_name_tmpl').addEventListener('input', scheduleOutPreview);
$('out_name_tmpl').addEventListener('change', scheduleOutPreview);
$('title').addEventListener('input', scheduleOutPreview);   // {title} 占位符：标题变化实时反映到输出预览

/* ==================== 章节编辑器（OGM 明文，可从源视频提取/加载文件/保存回填） ==================== */
function chEditToText(chs) {
  return chs.map((c, i) => {
    const ts = String(c.time).replace(",", ".");
    const m = ts.match(/^(\d+):(\d{1,2}):(\d{2})\.(\d+)/);
    const norm = m ? (m[1].padStart(2, "0") + ":" + m[2].padStart(2, "0") + ":" + m[3] + "." + (m[4] + "000").slice(0, 3)) : ts;
    return "CHAPTER" + String(i + 1).padStart(2, "0") + "=" + norm + "\nCHAPTER" + String(i + 1).padStart(2, "0") + "NAME=" + (c.name || "");
  }).join("\n");
}
$('btnChEdit').onclick = () => {
  $('chEditNote').textContent = '';
  if ($('chapters').value.trim()) {
    $('btnChLoadFile').click();   // 已填章节文件 → 直接加载内容
    return;
  }
  $('chEditText').value = '';
  $('chEditModal').style.display = 'flex';
};
$('chEditClose').onclick = () => { $('chEditModal').style.display = 'none'; };
$('btnChFromVideo').onclick = async () => {
  const v = $('video').value.trim();
  if (!v) { $('chEditNote').textContent = '请先在主流程选择视频（从源视频提取章节需要）'; return; }
  $('chEditNote').textContent = '正在从源视频提取章节…';
  try {
    const r = await api('/api/chapters/extract', { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify({ video: v }) });
    if (r.error) { $('chEditNote').textContent = '提取失败：' + r.error; return; }
    if (!r.chapters.length) { $('chEditNote').textContent = r.note || '源视频没有章节'; return; }
    $('chEditText').value = chEditToText(r.chapters);
    $('chEditNote').textContent = '已提取 ' + r.chapters.length + ' 章';
  } catch (ex) { $('chEditNote').textContent = '提取失败：' + ex; }
};
$('btnChLoadFile').onclick = () => {
  const p = $('chapters').value.trim();
  if (!p) {
    openBrowser(v2 => { if (v2) { $('chapters').value = v2; $('btnChLoadFile').click(); } }, 'any', p, 'chapters');
    return;
  }
  api('/api/chapters/parse', { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify({ path: p }) })
    .then(r => {
      if (r.error) { $('chEditNote').textContent = '加载失败：' + r.error; return; }
      $('chEditText').value = chEditToText(r.chapters);
      $('chEditNote').textContent = '已加载 ' + r.chapters.length + ' 章';
      $('chEditModal').style.display = 'flex';
    })
    .catch(ex => { $('chEditNote').textContent = '加载失败：' + ex; });
};
$('btnChSave').onclick = async () => {
  const txt = $('chEditText').value.trim();
  if (!txt) { $('chEditNote').textContent = '章节内容为空'; return; }
  const chs = [];
  let cur = null;
  for (const ln of txt.split(/\r?\n/)) {
    const l = ln.trim();
    if (!l) continue;
    let m = l.match(/^CHAPTER(\d+)=(.+)$/i);
    if (m) { if (cur) chs.push(cur); cur = { time: m[2].trim(), name: "" }; continue; }
    m = l.match(/^CHAPTER\d+NAME=(.+)$/i);
    if (m && cur) { cur.name = m[1].trim(); continue; }
    $('chEditNote').textContent = '无法识别的行：' + l.slice(0, 40);
    return;
  }
  if (cur) chs.push(cur);
  if (!chs.length) { $('chEditNote').textContent = '未解析到任何章节'; return; }
  try {
    const r = await api('/api/chapters/save', { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify({ chapters: chs }) });
    if (r.error) { $('chEditNote').textContent = '保存失败：' + r.error; return; }
    $('chapters').value = r.path;
    $('chEditModal').style.display = 'none';
    setStatus('章节已保存（' + r.count + ' 章）并填入章节文件框', 'ok');
  } catch (ex) { $('chEditNote').textContent = '保存失败：' + ex; }
};
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
document.addEventListener('click', function (e) {
  const el = e.target.closest('[data-open-dir]');
  if (el) openDir(el.dataset.openDir || '');
});
/* mkvmerge 命令查看/复制（结果区的 data-cmd 按钮，命令 base64 存于属性） */
function b64e(s) { return btoa(unescape(encodeURIComponent(s))); }
function b64d(s) { try { return decodeURIComponent(escape(atob(s))); } catch (e) { return ''; } }
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
  if (job) { txt = '正在封装…'; cls = 'run'; }
  else if (bJob) { txt = '正在批量封装…'; cls = 'run'; }
  else if (lastResult) { txt = lastResult.text; cls = lastResult.cls; }
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
$('log').addEventListener('scroll', function () {
  var el = $('log');
  logUi.stick = el.scrollTop + el.clientHeight >= el.scrollHeight - 24;
});
$('btnLogClear').onclick = function () { logStore[logTab] = ''; renderLog(); };
$('consoleCollapsed').onclick = function () { setConsoleCollapsed(false); };
$('btnLogFold').onclick = function () { setConsoleCollapsed(true); };

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

/* ==================== 折叠分区（字体设置 / 外部音轨）+ 摘要 ==================== */
function toggleCollapse(id, force) {
  const root = $(id);
  if (!root) return;
  const open = typeof force === 'boolean' ? force : !root.classList.contains('open');
  root.classList.toggle('open', open);
  const btn = root.querySelector('.collapse-toggle');
  if (btn) btn.setAttribute('aria-expanded', open ? 'true' : 'false');
}
function updateFontsSummary() {
  const el = $('fontsSummary');
  if (!el) return;
  const dir = $('fonts_dir').value.trim();
  let text, cls = '';
  if (fontState.status === 'loading') { text = '正在检查字体…'; cls = 'run'; }
  else if (fontState.status === 'error') { text = '字体体检失败'; cls = 'err'; }
  else if (fontState.status === 'ok') { text = (dir ? '已设置字体目录 · ' : '自动 · ') + '字体齐全'; cls = 'ok'; }
  else if (fontState.status === 'warn') { text = '检测到 ' + fontState.missing + ' 个缺失字体'; cls = 'warn'; }
  else text = dir ? '已设置字体目录' : '自动';
  el.textContent = text;
  el.className = 'c-summary' + (cls ? ' ' + cls : '');
}
function updateAudioSummary() {
  const el = $('audioSummary');
  if (!el) return;
  el.textContent = $('audio').value.trim() ? '已添加 1 条' : '未添加';
  el.className = 'c-summary';
}
/* 程序赋值 fonts_dir / audio 不触发 input/change，输入监听之外经 syncSubStatus 兜底刷新 */
$('fonts_dir').addEventListener('input', updateFontsSummary);
$('fonts_dir').addEventListener('change', updateFontsSummary);
$('audio').addEventListener('input', updateAudioSummary);
$('audio').addEventListener('change', updateAudioSummary);

/* ==================== 封装预设（保存/套用/删除，存服务端 config.json） ==================== */
let PRESETS = {};
function presetData() {
  return {
    sc_name: $('sc_name').value.trim() || 'SC', tc_name: $('tc_name').value.trim() || 'TC',
    sc_default: $('sc_default').value, tc_default: $('tc_default').value,
    sc_forced: $('sc_forced').checked, tc_forced: $('tc_forced').checked,
    fonts_dir: $('fonts_dir').value.trim(), out_dir: $('out_dir').value.trim(),
    chapters: $('chapters').value.trim(),
    backup: $('backup').checked, force: $('force').checked,
    cfg_tool: $('cfg_tool').value, fonts_mode: $('fonts_mode').value,
    out_name_tmpl: $('out_name_tmpl').value.trim(), title: $('title').value.trim(),
  };
}
function applyPreset(d) {
  if (!d || typeof d !== 'object') return;
  if (d.sc_name) $('sc_name').value = d.sc_name;
  if (d.tc_name) $('tc_name').value = d.tc_name;
  $('sc_default').value = d.sc_default || '';
  $('tc_default').value = d.tc_default || '';
  $('sc_forced').checked = !!d.sc_forced;
  $('tc_forced').checked = !!d.tc_forced;
  if (d.fonts_dir) $('fonts_dir').value = d.fonts_dir;
  if (d.out_dir) $('out_dir').value = d.out_dir;
  if (d.chapters) $('chapters').value = d.chapters;
  if (d.out_name_tmpl) $('out_name_tmpl').value = d.out_name_tmpl;
  if (d.title) $('title').value = d.title;
  $('backup').checked = d.backup !== false;
  $('force').checked = !!d.force;
  if (d.cfg_tool) { $('cfg_tool').value = d.cfg_tool; fireChange($('cfg_tool')); }
  if (d.fonts_mode) $('fonts_mode').value = d.fonts_mode;
  // 同步套用到批量公共字段（有对应项才写）
  const bm = { fonts_mode: 'b_fonts_mode', out_name_tmpl: 'b_out_name_tmpl', title: 'b_title',
               sc_default: 'b_sc_default', tc_default: 'b_tc_default', sc_forced: 'b_sc_forced', tc_forced: 'b_tc_forced' };
  Object.keys(bm).forEach(k => { if (d[k] !== undefined && d[k] !== '' && $(bm[k])) { if (bm[k].endsWith('_forced')) $(bm[k]).checked = !!d[k]; else $(bm[k]).value = d[k]; } });
  if (d.fonts_dir && $('b_fonts')) $('b_fonts').value = d.fonts_dir;
  syncSubStatus(); refreshSticky();
  setStatus('已套用预设（含批量公共选项）', 'ok');
}
function refreshPresetSel() {
  const sel = $('preset_sel');
  const cur = sel.value;
  sel.innerHTML = '<option value="">选择预设…</option>' +
    Object.keys(PRESETS).map(n => '<option value="' + esc(n) + '">' + esc(n) + '</option>').join('');
  if (PRESETS[cur]) sel.value = cur;
}
async function loadPresets() {
  try {
    const r = await api('/api/presets');
    PRESETS = r.presets || {};
    refreshPresetSel();
  } catch (e) { /* 断线由横幅提示 */ }
}
$('preset_sel').onchange = function () { if (PRESETS[this.value]) applyPreset(PRESETS[this.value]); };
$('btnPresetSave').onclick = async () => {
  const name = (prompt('预设名称：') || '').trim();
  if (!name) return;
  try {
    const r = await api('/api/presets', { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify({ name, data: presetData() }) });
    if (r.error) { setStatus('预设保存失败：' + r.error, 'err'); return; }
    PRESETS = r.presets || {};
    refreshPresetSel();
    $('preset_sel').value = name;
    setStatus('预设已保存：' + name, 'ok');
  } catch (ex) { setStatus('预设保存失败：' + ex, 'err'); }
};
$('btnPresetDel').onclick = async () => {
  const name = $('preset_sel').value;
  if (!name) { alert('请先在下方选择要删除的预设'); return; }
  if (!confirm('确定删除预设「' + name + '」？')) return;
  try {
    const r = await api('/api/presets/delete', { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify({ name }) });
    if (r.error) { setStatus('预设删除失败：' + r.error, 'err'); return; }
    PRESETS = r.presets || {};
    refreshPresetSel();
    setStatus('预设已删除：' + name, 'ok');
  } catch (ex) { setStatus('预设删除失败：' + ex, 'err'); }
};

/* ==================== 备份清理（替换模式的 __mux_tmp_manual） ==================== */
function fmtSize(n) {
  if (n >= 1073741824) return (n / 1073741824).toFixed(2) + ' GB';
  if (n >= 1048576) return (n / 1048576).toFixed(1) + ' MB';
  return (n / 1024).toFixed(1) + ' KB';
}
function backupsLoad() {
  const list = $('backupsList');
  return api('/api/backups').then(r => {
    const items = r.items || [];
    if (!items.length) {
      list.innerHTML = '<div class="t-sec" style="padding:8px 0;">没有记录到备份目录（替换模式封装后才会产生）</div>';
      return;
    }
    list.innerHTML = items.map((it, i) =>
      '<label class="check" style="display:flex;align-items:center;gap:8px;padding:6px 0;">'
      + '<input type="checkbox" class="bk-check" data-path="' + esc(it.path) + '" style="width:auto;height:auto">'
      + '<span style="flex:1;word-break:break-all;" class="mono t-cap">' + esc(it.path) + '</span>'
      + '<span class="chip sm info">' + fmtSize(it.size) + '</span></label>').join('');
    $('backupsNote').textContent = '共 ' + items.length + ' 个目录，合计 ' + fmtSize(items.reduce((s, x) => s + x.size, 0));
  }).catch(ex => { $('backupsNote').textContent = '加载失败：' + ex; });
}
$('btnBackups').onclick = () => { $('backupsModal').style.display = 'flex'; $('backupsNote').textContent = ''; backupsLoad(); };
$('backupsClose').onclick = () => { $('backupsModal').style.display = 'none'; };
$('btnBackupsClean').onclick = async () => {
  const paths = [...document.querySelectorAll('#backupsList .bk-check')].filter(c => c.checked).map(c => c.dataset.path);
  if (!paths.length) { $('backupsNote').textContent = '请先勾选要删除的目录'; return; }
  if (!confirm('确定删除勾选的 ' + paths.length + ' 个备份目录？删除后无法找回这些原件！')) return;
  $('btnBackupsClean').disabled = true;
  try {
    const r = await api('/api/backups/clean', { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify({ paths }) });
    if (r.error) { $('backupsNote').textContent = '清理失败：' + r.error; return; }
    setStatus('已清理 ' + (r.cleaned || []).length + ' 个备份目录' + ((r.errors || []).length ? '（' + r.errors.length + ' 个失败）' : ''), (r.errors || []).length ? 'err' : 'ok');
    backupsLoad();
  } catch (ex) { $('backupsNote').textContent = '清理失败：' + ex; }
  finally { $('btnBackupsClean').disabled = false; }
};
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
    updateGlobalSummary();
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
