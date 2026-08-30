/* 右上角设置域：主题/强调色 popup、全局设置 modal、备份清理、首次工作目录引导。
 * 「封装预设」入口只调 openPresetManager()（实现在 features/presets.js）；环境检测入口调 env.js 的 openEnv()。 */

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
    '<div class="pop-title">封装</div>' +
    '<button type="button" class="pref menu" id="popPresetBtn"><span data-ic="list" aria-hidden="true"></span>封装预设</button>' +
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
    if (b.id === 'popPresetBtn') { pop.classList.remove('open'); btn.setAttribute('aria-expanded', 'false'); openPresetManager(); return; }
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
};
