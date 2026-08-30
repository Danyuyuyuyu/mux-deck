/* 封装预设：数据（PRESETS/presetData/applyPreset/refreshPresetSel）+ dirty 提示 + 管理窗口
 * （openPresetManager/pm*）。PRESETS 全局唯一；旧预设历史字段（cfg_tool 等）保持兼容不迁移。 */

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
  // 同步套用到批量公共字段（映射逻辑在 batch.js，batch 域自持）
  applyPresetToBatchCommon(d);
  syncSubStatus(); refreshSticky();
  updatePresetHint();
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

/* ---- 跨模块轻量接口（其他域如需读取/应用预设，走这三个入口，不直接碰 PRESETS） ---- */
function getPresetList() { return PRESETS; }
function refreshPresetOptions() { refreshPresetSel(); updatePresetHint(); }
function applyPresetToCurrentTask(name) {
  if (!PRESETS[name]) return false;
  $('preset_sel').value = name;
  applyPreset(PRESETS[name]);
  updatePresetHint();
  rememberPreset();
  return true;
}

/* ==================== 预设记忆（本机浏览器级工作态） ====================
 * 记住上次选中的预设名，刷新后恢复选择器并自动套用（= 恢复上次工作状态）。
 * 只存名字、不存参数——参数永远以服务端 PRESETS 为准；名称失效（被删/改名）时回落到
 * 「选择预设…」并清掉记忆，与删除当前引用预设的既有语义一致。 */
function rememberPreset() {
  try { localStorage.setItem('muxui_preset', $('preset_sel').value || ''); } catch (e) {}
}
function restoreRememberedPreset() {
  let name = '';
  try { name = localStorage.getItem('muxui_preset') || ''; } catch (e) {}
  if (!name || !PRESETS[name]) {
    try { localStorage.removeItem('muxui_preset'); } catch (e) {}
    return;
  }
  $('preset_sel').value = name;
  applyPreset(PRESETS[name]);
  updatePresetHint();
}

/* 当前任务与所选预设的差异提示（轻量 dirty：仅状态展示，不拦截、不回写预设）。
 * 只比较两边共有的键：旧预设携带 cfg_tool 等历史字段时不误报。 */
function presetSnapshotEqual(a, b) {
  const ks = Object.keys(a).filter(k => Object.prototype.hasOwnProperty.call(b, k)).sort();
  const norm = o => JSON.stringify(ks.map(k => [k, o[k]]));
  return norm(a) === norm(b);
}
function updatePresetHint() {
  const el = $('presetHint');
  if (!el) return;
  const cur = $('preset_sel').value;
  if (!cur || !PRESETS[cur]) { el.textContent = ''; return; }
  el.textContent = presetSnapshotEqual(presetData(), PRESETS[cur]) ? ('已应用：' + cur) : (cur + ' · 已修改');
}
/* 预设覆盖的字段变更后刷新 dirty 提示（seg 三态在自身点击处理里同步） */

/* ==================== 封装预设管理窗口（右上角 → 封装 → 封装预设） ====================
 * 职责分离：主页面 preset_sel 负责"应用"，本窗口负责"管理"（新建/查看/编辑/重命名/删除）。
 * 数据层复用同一份 PRESETS 与 /api/presets(+/delete)，不建第二套状态。
 * 点击列表项 = 查看/编辑，不会修改当前任务；删除当前任务引用的预设只清引用、不动参数。 */
const PM_BLANK = { sc_name: 'SC', tc_name: 'TC', sc_default: '', tc_default: '', sc_forced: false, tc_forced: false,
  fonts_mode: 'subset', out_name_tmpl: '', title: '', fonts_dir: '', out_dir: '', chapters: '', backup: true, force: false };
const pmState = { editing: null };   // {orig: 已有预设名|null, isNew, mode: 'blank'|'task', base: 建基数据}
function pmClone(o) { return JSON.parse(JSON.stringify(o || {})); }
function openPresetManager() {
  openModal('presetModal', { display: 'block' });
  $('pmNote').textContent = '';
  if (!pmState.editing) {
    const names = Object.keys(PRESETS);
    pmState.editing = names.length ? { orig: names[0], isNew: false } : { orig: null, isNew: true, mode: 'blank', base: pmClone(PM_BLANK) };
  }
  pmRenderList(); pmRenderEditor();
}
function closePresetManager() { closeModal('presetModal'); }
function pmSelect(name) { pmState.editing = { orig: name, isNew: false }; $('pmNote').textContent = ''; pmRenderList(); pmRenderEditor(); }
function pmRenderList() {
  const box = $('pmList');
  const cur = $('preset_sel').value;
  let h = Object.keys(PRESETS).map(function (n) {
    const active = pmState.editing && !pmState.editing.isNew && pmState.editing.orig === n;
    return '<button type="button" class="pm-item' + (active ? ' active' : '') + '" data-name="' + esc(n) + '">' + ic('fileText') + '<span>' + esc(n) + '</span>' + (n === cur ? '<span class="pm-cur">当前</span>' : '') + '</button>';
  }).join('');
  h += '<button type="button" class="pm-item pm-new" id="pmNewBtn">' + ic('plus') + '<span>新建预设</span></button>';
  box.innerHTML = h;
  box.querySelectorAll('.pm-item[data-name]').forEach(b => { b.onclick = () => pmSelect(b.dataset.name); });
  $('pmNewBtn').onclick = function () { pmState.editing = { orig: null, isNew: true, mode: 'blank', base: pmClone(PM_BLANK) }; $('pmNote').textContent = ''; pmRenderList(); pmRenderEditor(); };
}
function pmField(id, label, inner) {
  return '<div class="field"><label for="' + id + '">' + label + '</label>' + inner + '</div>';
}
function pmRenderEditor() {
  const box = $('pmEditor');
  if (!pmState.editing) { box.innerHTML = '<div class="pm-empty t-sec">从左侧选择一个预设，或点「新建预设」</div>'; return; }
  const st = pmState.editing;
  const d = st.isNew ? pmClone(st.base) : pmClone(PRESETS[st.orig] || {});
  let h = '';
  if (st.isNew) {
    h += pmField('pmName', '新建方式',
      '<span class="pm-modes">' +
      '<label class="check"><input type="radio" name="pmNewMode" value="blank"' + (st.mode === 'blank' ? ' checked' : '') + '> 空白预设</label>' +
      '<label class="check"><input type="radio" name="pmNewMode" value="task"' + (st.mode === 'task' ? ' checked' : '') + '> 使用当前任务配置</label>' +
      '</span>');
  }
  h += '<div class="pm-group">基本信息</div>';
  h += pmField('pmName', '预设名称', '<input id="pmName" type="text" placeholder="如：字幕组标准" autocomplete="off">');
  h += '<div class="pm-group">字幕轨道</div>';
  h += pmField('pm_f_sc_name', 'SC 轨道名', '<input id="pm_f_sc_name" type="text" value="' + esc(d.sc_name || 'SC') + '" autocomplete="off">');
  h += pmField('pm_f_sc_default', 'SC 默认轨', '<select id="pm_f_sc_default"><option value="">自动</option><option value="1">是</option><option value="0">否</option></select>');
  h += '<div class="field"><label for="pm_f_sc_forced">SC 强制</label><input id="pm_f_sc_forced" type="checkbox" class="switch"' + (d.sc_forced ? ' checked' : '') + '></div>';
  h += pmField('pm_f_tc_name', 'TC 轨道名', '<input id="pm_f_tc_name" type="text" value="' + esc(d.tc_name || 'TC') + '" autocomplete="off">');
  h += pmField('pm_f_tc_default', 'TC 默认轨', '<select id="pm_f_tc_default"><option value="">自动</option><option value="1">是</option><option value="0">否</option></select>');
  h += '<div class="field"><label for="pm_f_tc_forced">TC 强制</label><input id="pm_f_tc_forced" type="checkbox" class="switch"' + (d.tc_forced ? ' checked' : '') + '></div>';
  h += '<div class="pm-group">字体</div>';
  h += pmField('pm_f_fonts_mode', '字体处理', '<select id="pm_f_fonts_mode"><option value="subset">子集化</option><option value="collect">仅收集</option></select>');
  h += pmField('pm_f_fonts_dir', '字体目录', '<input id="pm_f_fonts_dir" type="text" value="' + esc(d.fonts_dir || '') + '" autocomplete="off">');
  h += '<div class="t-cap" style="margin:-6px 0 10px 190px;">路径类字段：应用预设时会覆盖当前任务的对应输入</div>';
  h += '<div class="pm-group">输出 / 命名</div>';
  h += pmField('pm_f_out_name_tmpl', '命名模板', '<input id="pm_f_out_name_tmpl" type="text" value="' + esc(d.out_name_tmpl || '') + '" placeholder="留空 = 沿用源文件名" autocomplete="off">');
  h += pmField('pm_f_title', 'MKV 标题', '<input id="pm_f_title" type="text" value="' + esc(d.title || '') + '" autocomplete="off">');
  h += pmField('pm_f_out_dir', '输出目录', '<input id="pm_f_out_dir" type="text" value="' + esc(d.out_dir || '') + '" autocomplete="off">');
  h += '<div class="t-cap" style="margin:-6px 0 10px 190px;">应用预设会覆盖当前任务的输出位置（留空 = 不改变）</div>';
  h += pmField('pm_f_chapters', '章节文件', '<input id="pm_f_chapters" type="text" value="' + esc(d.chapters || '') + '" autocomplete="off">');
  h += '<div class="pm-group">其他</div>';
  h += '<div class="field"><label for="pm_f_backup">备份原件</label><input id="pm_f_backup" type="checkbox"' + (d.backup !== false ? ' checked' : '') + '></div>';
  h += '<div class="field"><label for="pm_f_force">强制封装</label><input id="pm_f_force" type="checkbox"' + (d.force ? ' checked' : '') + '></div>';
  h += '<div class="pm-actions">' +
    (st.isNew ? '' : '<button type="button" class="btn ghost danger" id="pmDeleteBtn">' + ic('trash') + '<span>删除</span></button>') +
    '<span style="flex:1"></span>' +
    '<button type="button" class="btn primary" id="pmSaveBtn">' + (st.isNew ? ic('plus') + '<span>创建预设</span>' : ic('check') + '<span>保存修改</span>') + '</button>' +
    '</div>';
  box.innerHTML = h;
  $('pm_f_sc_default').value = String(d.sc_default || '');
  $('pm_f_tc_default').value = String(d.tc_default || '');
  $('pm_f_fonts_mode').value = d.fonts_mode === 'collect' ? 'collect' : 'subset';
  if (st.isNew) {
    box.querySelectorAll('input[name=pmNewMode]').forEach(r => {
      r.addEventListener('change', function () {
        const nm = $('pmName') ? $('pmName').value : '';   // 切换建基方式时保留已填名称
        st.mode = this.value;
        st.base = pmClone(st.mode === 'task' ? presetData() : PM_BLANK);
        pmRenderEditor();
        if (nm) $('pmName').value = nm;
      });
    });
  }
  $('pmSaveBtn').onclick = pmSave;
  const del = $('pmDeleteBtn');
  if (del) del.onclick = pmDelete;
}
async function pmSave() {
  const st = pmState.editing;
  if (!st) return;
  const name = $('pmName').value.trim();
  if (!name) { $('pmNote').textContent = '请填写预设名称'; $('pmNote').style.color = 'var(--danger)'; return; }
  const data = {
    sc_name: $('pm_f_sc_name').value.trim() || 'SC', tc_name: $('pm_f_tc_name').value.trim() || 'TC',
    sc_default: $('pm_f_sc_default').value, tc_default: $('pm_f_tc_default').value,
    sc_forced: $('pm_f_sc_forced').checked, tc_forced: $('pm_f_tc_forced').checked,
    fonts_mode: $('pm_f_fonts_mode').value || 'subset',
    out_name_tmpl: $('pm_f_out_name_tmpl').value.trim(), title: $('pm_f_title').value.trim(),
    fonts_dir: $('pm_f_fonts_dir').value.trim(), out_dir: $('pm_f_out_dir').value.trim(), chapters: $('pm_f_chapters').value.trim(),
    backup: $('pm_f_backup').checked, force: $('pm_f_force').checked,
  };
  if (st.isNew && st.mode === 'task' && st.base && st.base.cfg_tool !== undefined) data.cfg_tool = st.base.cfg_tool;   // 从当前任务新建：与旧「保存当前为预设」语义一致
  if (!st.isNew) { const od = PRESETS[st.orig] || {}; if ('cfg_tool' in od) data.cfg_tool = od.cfg_tool; }             // 编辑已有：保留历史全局字段不迁移
  try {
    const r = await api('/api/presets', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ name, data }) });
    if (r.error) { pmNoteErr(r.error); return; }
    PRESETS = r.presets || {};
    if (!st.isNew && st.orig && st.orig !== name && PRESETS[st.orig]) {   // 重命名 = 新名保存 + 旧名删除
      const rd = await api('/api/presets/delete', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ name: st.orig }) });
      if (!rd.error) PRESETS = rd.presets || PRESETS;
    }
    const applied = $('preset_sel').value;
    if (applied === st.orig && st.orig !== name) $('preset_sel').value = PRESETS[name] ? name : '';   // 当前任务引用跟随重命名
    refreshPresetSel(); updatePresetHint(); rememberPreset();
    pmState.editing = { orig: name, isNew: false };
    pmRenderList(); pmRenderEditor();
    pmNoteOk('✓ 已保存（' + name + '）');
  } catch (ex) { pmNoteErr('保存失败：' + ex); }
}
async function pmDelete() {
  const st = pmState.editing;
  if (!st || st.isNew) return;
  const name = st.orig;
  if (!confirm('删除“' + name + '”？\n此操作不会改变当前任务中已经应用的参数。')) return;
  try {
    const r = await api('/api/presets/delete', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ name }) });
    if (r.error) { pmNoteErr(r.error); return; }
    PRESETS = r.presets || {};
    if ($('preset_sel').value === name) $('preset_sel').value = '';   // 只清引用；当前任务参数保持不变
    refreshPresetSel(); updatePresetHint(); rememberPreset();
    pmState.editing = Object.keys(PRESETS).length ? { orig: Object.keys(PRESETS)[0], isNew: false } : null;
    pmRenderList(); pmRenderEditor();
    pmNoteOk('✓ 已删除（' + name + '）');
  } catch (ex) { pmNoteErr('删除失败：' + ex); }
}
function pmNoteOk(t) { const el = $('pmNote'); el.textContent = t; el.style.color = 'var(--success)'; }
function pmNoteErr(t) { const el = $('pmNote'); el.textContent = t; el.style.color = 'var(--danger)'; }

/* ==================== 初始化（由 init.js bootstrap 统一调用，仅执行一次） ==================== */
function initPresets() {
$('preset_sel').onchange = function () { if (PRESETS[this.value]) applyPreset(PRESETS[this.value]); updatePresetHint(); rememberPreset(); };
['sc_name', 'tc_name', 'fonts_dir', 'out_dir', 'out_name_tmpl', 'title'].forEach(id => $(id).addEventListener('input', updatePresetHint));
['sc_forced', 'tc_forced', 'backup', 'force', 'fonts_mode'].forEach(id => $(id).addEventListener('change', updatePresetHint));
$('pmClose').onclick = closePresetManager;
}
