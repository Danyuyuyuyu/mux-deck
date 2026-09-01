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
    use_sys_fonts: $('use_sys_fonts').checked,
    cfg_tool: $('cfg_tool').value, fonts_mode: $('fonts_mode').value,
    out_name_tmpl: $('out_name_tmpl').value.trim(), title: $('title').value.trim(),
    postcmd: $('postcmd').value.trim(),
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
  if (d.postcmd !== undefined) $('postcmd').value = d.postcmd;   // 空串也套用（预设明确清空 = 任务级回落全局默认）
  $('backup').checked = d.backup !== false;
  $('force').checked = !!d.force;
  if (d.use_sys_fonts !== undefined) $('use_sys_fonts').checked = !!d.use_sys_fonts;   // 旧预设无此字段不动
  if (d.cfg_tool) { $('cfg_tool').value = d.cfg_tool; fireChange($('cfg_tool')); }
  if (d.fonts_mode) $('fonts_mode').value = d.fonts_mode;
  // 同步套用到批量公共字段（映射逻辑在 batch.js，batch 域自持）
  applyPresetToBatchCommon(d);
  syncSubStatus(); refreshSticky();
  updatePresetHint();
  setStatus('已套用预设（含批量公共选项）', 'ok');
}
function refreshPresetSel() {
  ['preset_sel', 'b_preset_sel'].forEach(function (id) {
    const sel = $(id);
    if (!sel) return;
    const cur = sel.value;
    sel.innerHTML = '<option value="">选择预设…</option>' +
      Object.keys(PRESETS).map(n => '<option value="' + esc(n) + '">' + esc(n) + '</option>').join('');
    if (PRESETS[cur]) sel.value = cur;
  });
}
async function loadPresets() {
  try {
    const r = await api('/api/presets');
    PRESETS = r.presets || {};
    refreshPresetSel();
  } catch (e) { /* 断线由横幅提示 */ }
}

/* ---- 跨模块轻量接口（其他域如需读取/应用预设，走这些入口，不直接碰 PRESETS） ---- */
function getPresetList() { return PRESETS; }
function refreshPresetOptions() { refreshPresetSel(); updatePresetHint(); }

/* ==================== 当前任务的预设来源（presets 域私有会话状态） ====================
 * currentId：当前任务最后一次明确应用的预设名；null = 自定义配置。
 * snapshot：应用时刻的预设数据深拷贝（lastAppliedPresetSnapshot）——任务修改只标脏，
 * 不回写预设；current task 与 preset 之间永远通过 copy 传递，无共享可变引用。
 * 只有「解除预设」或删除来源预设才清空 currentId；普通修改不清空，只置 dirty。 */
const presetSession = { currentId: null, snapshot: null };

function isCurrentTaskPresetDirty() {
  if (!presetSession.currentId) return false;
  // 与应用时刻的快照比较（仅共有键；旧预设历史字段 cfg_tool 不误报）
  return !presetSnapshotEqual(presetData(), presetSession.snapshot || {});
}
function getCurrentPresetInfo() {
  const id = presetSession.currentId && PRESETS[presetSession.currentId] ? presetSession.currentId : null;
  return { id: id, dirty: id ? isCurrentTaskPresetDirty() : false };
}
/* 唯一应用入口：把已确定的预设配置 copy 进当前任务并登记来源/快照。
 * 列表点击、高级选项下拉之外的任何"应用"都必须经过这里。 */
function applyPresetToCurrentTask(name) {
  if (!name || !PRESETS[name]) return false;
  $('preset_sel').value = name;
  if ($('b_preset_sel')) $('b_preset_sel').value = name;
  applyPreset(PRESETS[name]);
  presetSession.currentId = name;
  presetSession.snapshot = pmClone(PRESETS[name]);
  updatePresetHint();
  rememberPreset();
  return true;
}
/* 解除预设：只摘掉来源标记，当前任务参数原样保留（变为自定义配置） */
function detachCurrentPreset() {
  presetSession.currentId = null;
  presetSession.snapshot = null;
  $('preset_sel').value = '';
  if ($('b_preset_sel')) $('b_preset_sel').value = '';
  updatePresetHint();
  rememberPreset();
  setStatus('已解除预设：当前任务参数保留（自定义配置）', 'ok');
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
  applyPresetToCurrentTask(name);   // 走唯一应用入口：登记来源 + 快照
}

/* 当前任务与所选预设的差异提示（轻量 dirty：仅状态展示，不拦截、不回写预设）。
 * 只比较两边共有的键：旧预设携带 cfg_tool 等历史字段时不误报。 */
function presetSnapshotEqual(a, b) {
  const ks = Object.keys(a).filter(k => Object.prototype.hasOwnProperty.call(b, k)).sort();
  const norm = o => JSON.stringify(ks.map(k => [k, o[k]]));
  return norm(a) === norm(b);
}
function updatePresetHint() {
  const info = getCurrentPresetInfo();
  const el = $('presetHint');
  if (el) el.textContent = info.id ? (info.id + (info.dirty ? ' · 已修改' : ' · 已应用')) : '';
  if (typeof renderPresetStatus === 'function') renderPresetStatus();   // single 页顶部状态区（single.js）
}
/* 预设覆盖的字段变更后刷新 dirty 提示（seg 三态在自身点击处理里同步） */

/* ==================== 封装预设管理窗口（右上角 → 封装 → 封装预设） ====================
 * 职责分离：主页面 preset_sel 负责"应用"，本窗口负责"管理"（新建/查看/编辑/重命名/删除）。
 * 数据层复用同一份 PRESETS 与 /api/presets(+/delete)，不建第二套状态。
 * 点击列表项 = 查看/编辑，不会修改当前任务；删除当前任务引用的预设只清引用、不动参数。 */
const PM_BLANK = { sc_name: 'SC', tc_name: 'TC', sc_default: '', tc_default: '', sc_forced: false, tc_forced: false,
  fonts_mode: 'subset', out_name_tmpl: '', title: '', fonts_dir: '', out_dir: '', chapters: '', backup: true, force: false, use_sys_fonts: false, postcmd: '' };
const pmState = { editing: null };   // {orig: 已有预设名|null, isNew, mode: 'blank'|'task', base: 建基数据}
let pmEditorDirty = false;           // 编辑器有未保存修改（切换列表项时提示保护；Footer 按钮组随之切换）
function pmClone(o) { return JSON.parse(JSON.stringify(o || {})); }
/* 空名预设的 UI fallback 编号（仅显示，不回写持久化名称；对象键唯一故空名至多一条） */
function pmNameMap() {
  const m = {}; let n = 0;
  Object.keys(PRESETS).forEach(k => { m[k] = (k && k.trim()) ? k : ('未命名预设 ' + (++n)); });
  return m;
}
function pmDisplayName(name) { return pmNameMap()[name] || '未命名预设'; }
function openPresetManager() {
  openModal('presetModal', { display: 'block' });
  $('pmNote').textContent = '';
  if (!pmState.editing) {
    // 默认选中：当前任务来源预设 > 第一条；没有预设则进入新建
    const preferred = presetSession.currentId && PRESETS[presetSession.currentId] ? presetSession.currentId : Object.keys(PRESETS)[0];
    pmState.editing = preferred ? { orig: preferred, isNew: false } : { orig: null, isNew: true, mode: 'blank', base: pmClone(PM_BLANK) };
  }
  pmEditorDirty = false;
  pmRenderList(); pmRenderEditor();
}
function closePresetManager() { closeModal('presetModal'); }
/* 切换保护：编辑器有未保存修改时，切换/新建前确认丢弃 */
function pmGuardUnsaved() {
  if (pmState.editing && pmEditorDirty && !confirm('当前预设的修改尚未保存，切换后将丢弃这些修改。继续？')) return false;
  pmEditorDirty = false;
  return true;
}
function pmSelect(name) {
  if (!pmGuardUnsaved()) return;
  pmState.editing = { orig: name, isNew: false };
  $('pmNote').textContent = '';
  pmRenderList(); pmRenderEditor();
}
/* dirty 翻转时刷新编辑器头部指示与 Footer 按钮组（同值不重渲染） */
function pmMarkDirty() {
  if (pmEditorDirty) return;
  pmEditorDirty = true;
  pmRenderEdHead();
  pmRenderFooter();
}
function pmRenderList() {
  const box = $('pmList');
  const names = pmNameMap();
  box.innerHTML = Object.keys(PRESETS).map(function (n) {
    // 两个状态不混淆：selected = 正在编辑（accent bar）；current = 当前任务来源（✓ 徽章）
    const selected = pmState.editing && !pmState.editing.isNew && pmState.editing.orig === n;
    const current = presetSession.currentId === n;
    return '<button type="button" class="pm-item' + (selected ? ' selected' : '') + '" data-name="' + esc(n) + '">' + ic('fileText') +
      '<span class="pm-item-name">' + esc(names[n]) + '</span>' +
      (current ? '<span class="pm-cur">' + ic('check') + '<span>当前任务</span></span>' : '') + '</button>';
  }).join('');
  box.querySelectorAll('.pm-item[data-name]').forEach(b => { b.onclick = () => pmSelect(b.dataset.name); });
}
/* 编辑器头部：正在编辑：名称 + 未保存小圆点指示 */
function pmRenderEdHead() {
  const el = $('pmEdHead');
  if (!el) return;
  const st = pmState.editing;
  if (!st) { el.innerHTML = ''; return; }
  let nm;
  if (st.isNew) { const v = $('pmName') ? $('pmName').value.trim() : ''; nm = v || '新预设'; }
  else nm = pmDisplayName(st.orig);
  el.innerHTML = '<span class="pm-ed-cap">正在编辑：</span><span class="pm-ed-name">' + esc(nm) + '</span>' +
    (pmEditorDirty ? '<span class="pm-ed-dirty" title="有未保存的修改">● 未保存</span>' : '');
}
function pmField(id, label, inner) {
  return '<div class="field pm-field"><label for="' + id + '">' + label + '</label>' + inner + '</div>';
}
/* SC / TC 轨道配置卡：badge 配色沿用主页面 sub-badge；默认轨为主页面同款三态 segmented（写隐藏域） */
function pmTrackCard(kind, badge, langLabel, d) {
  const fid = 'pm_f_' + kind;
  const dv = String(d[kind + '_default'] || '');
  const segBtn = v => '<button type="button" class="seg-btn' + (dv === v ? ' active' : '') + '" data-v="' + v + '">' + (v === '' ? '自动' : (v === '1' ? '是' : '否')) + '</button>';
  return '<div class="pm-track-card">' +
    '<div class="pm-track-head"><span class="sub-badge ' + kind + '">' + badge + '</span><span class="pm-track-title">' + langLabel + '</span></div>' +
    '<div class="pm-track-row"><label for="' + fid + '_name">轨道名</label><input id="' + fid + '_name" type="text" value="' + esc(d[kind + '_name'] || badge) + '" autocomplete="off"></div>' +
    '<div class="pm-track-row"><span class="set-label">默认轨</span>' +
      '<div class="seg" id="' + fid + '_default_seg" role="radiogroup" aria-label="' + langLabel + '默认轨">' + segBtn('') + segBtn('1') + segBtn('0') + '</div>' +
      '<input id="' + fid + '_default" type="hidden" value="' + esc(dv) + '"></div>' +
    '<div class="pm-track-row"><label for="' + fid + '_forced">强制字幕</label><input id="' + fid + '_forced" type="checkbox" class="switch"' + (d[kind + '_forced'] ? ' checked' : '') + ' aria-label="' + langLabel + '强制字幕"></div>' +
    '</div>';
}
function pmRenderEditor() {
  const box = $('pmEditor');
  if (!pmState.editing) {
    box.innerHTML = '<div class="pm-empty t-sec">从左侧选择一个预设，或点「新建预设」</div>';
    pmRenderEdHead(); pmRenderFooter();
    return;
  }
  const st = pmState.editing;
  const d = st.isNew ? pmClone(st.base) : pmClone(PRESETS[st.orig] || {});
  let h = '';
  /* ---- 基本信息 ---- */
  h += '<div class="pm-sec"><div class="pm-sec-title">基本信息</div>';
  if (st.isNew) {
    h += pmField('pmNewMode', '新建方式',
      '<span class="pm-modes">' +
      '<label class="check"><input type="radio" name="pmNewMode" value="blank"' + (st.mode === 'blank' ? ' checked' : '') + '> 空白预设</label>' +
      '<label class="check"><input type="radio" name="pmNewMode" value="task"' + (st.mode === 'task' ? ' checked' : '') + '> 使用当前任务配置</label>' +
      '</span>');
  }
  h += pmField('pmName', '预设名称', '<input id="pmName" type="text" placeholder="如：字幕组标准" autocomplete="off">');
  h += '</div>';
  /* ---- 字幕轨道（SC / TC 双卡） ---- */
  h += '<div class="pm-sec"><div class="pm-sec-title">字幕轨道</div><div class="pm-track-grid">' +
    pmTrackCard('sc', 'SC', '简体中文', d) + pmTrackCard('tc', 'TC', '繁體中文', d) + '</div></div>';
  /* ---- 字体 ---- */
  h += '<div class="pm-sec"><div class="pm-sec-title">字体</div>';
  h += pmField('pm_f_fonts_mode', '字体处理', '<select id="pm_f_fonts_mode"><option value="subset">子集化</option><option value="collect">仅收集</option></select>');
  h += '<div class="field pm-field"><label for="pm_f_fonts_dir">字体目录</label>' +
    '<input id="pm_f_fonts_dir" type="text" value="' + esc(d.fonts_dir || '') + '" autocomplete="off">' +
    '<button type="button" class="btn icon-btn" id="pm_f_fonts_dir_btn" aria-label="浏览字体目录">' + ic('folderOpen') + '</button></div>';
  h += '<div class="t-cap pm-hint">留空 = 自动查找视频旁的 Fonts\\ 或 Font\\；应用预设时会覆盖当前任务的对应输入</div>';
  h += '</div>';
  /* ---- 输出 / 命名（桌面双列） ---- */
  h += '<div class="pm-sec"><div class="pm-sec-title">输出 / 命名</div><div class="pm-out-grid">';
  h += '<div class="field pm-field"><label for="pm_f_out_name_tmpl">命名模板</label><input id="pm_f_out_name_tmpl" type="text" value="' + esc(d.out_name_tmpl || '') + '" placeholder="留空 = 沿用源文件名" autocomplete="off"></div>';
  h += '<div class="field pm-field"><label for="pm_f_out_dir">输出目录</label><input id="pm_f_out_dir" type="text" value="' + esc(d.out_dir || '') + '" placeholder="留空 = 替换源视频" autocomplete="off">' +
    '<button type="button" class="btn icon-btn" id="pm_f_out_dir_btn" aria-label="浏览输出目录">' + ic('folderOpen') + '</button></div>';
  h += '<div class="field pm-field"><label for="pm_f_title">MKV 标题</label><input id="pm_f_title" type="text" value="' + esc(d.title || '') + '" placeholder="留空则不写入标题" autocomplete="off"></div>';
  h += '<div class="field pm-field"><label for="pm_f_chapters">章节文件</label><input id="pm_f_chapters" type="text" value="' + esc(d.chapters || '') + '" placeholder="可选：导入章节文件" autocomplete="off">' +
    '<button type="button" class="btn icon-btn" id="pm_f_chapters_btn" aria-label="浏览章节文件">' + ic('folderOpen') + '</button></div>';
  h += '<div class="field pm-field"><label for="pm_f_postcmd">后处理命令</label><input id="pm_f_postcmd" type="text" value="' + esc(d.postcmd || '') + '" placeholder="封装成功后执行；{out}={成品路径} {src}={源视频} {ep}={集数}；空 = 用全局默认" autocomplete="off"></div>';
  h += '</div><div class="t-cap pm-hint">应用预设会覆盖当前任务的输出位置（留空 = 不改变）</div></div>';
  /* ---- 其他（横向复选） ---- */
  h += '<div class="pm-sec"><div class="pm-sec-title">其他</div><div class="pm-other">' +
    '<label class="check"><input id="pm_f_backup" type="checkbox"' + (d.backup !== false ? ' checked' : '') + '> 备份原件</label>' +
    '<label class="check"><input id="pm_f_force" type="checkbox"' + (d.force ? ' checked' : '') + '> 强制封装</label>' +
    '<label class="check"><input id="pm_f_use_sys_fonts" type="checkbox"' + (d.use_sys_fonts ? ' checked' : '') + '> 包含系统已装字体</label></div></div>';
  box.innerHTML = h;
  $('pmName').value = st.isNew ? '' : st.orig;
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
  /* 默认轨三态 segmented（动态生成，单独绑定；主页面 single.js 的 .seg 绑定只覆盖静态 DOM） */
  box.querySelectorAll('.pm-track-card .seg').forEach(seg => {
    seg.addEventListener('click', function (e) {
      const b = e.target.closest('.seg-btn');
      if (!b) return;
      const hidden = seg.parentElement.querySelector('input[type=hidden]');
      if (!hidden || hidden.value === b.dataset.v) return;
      hidden.value = b.dataset.v;
      seg.querySelectorAll('.seg-btn').forEach(x => x.classList.toggle('active', x === b));
      pmMarkDirty();
    });
  });
  /* 路径字段浏览按钮：复用 browser.js（openBrowser），选择后回填并标脏 */
  const bindBrowse = (btnId, inputId, filter, slot) => {
    const b = $(btnId);
    if (b) b.onclick = () => openBrowser(v => { $(inputId).value = v; pmMarkDirty(); }, filter, $(inputId).value, slot);
  };
  bindBrowse('pm_f_fonts_dir_btn', 'pm_f_fonts_dir', 'dir', 'fonts');
  bindBrowse('pm_f_out_dir_btn', 'pm_f_out_dir', 'dir', 'out');
  bindBrowse('pm_f_chapters_btn', 'pm_f_chapters', 'any', 'chapters');
  pmEditorDirty = false;   // 刚渲染的编辑器视为干净
  pmRenderEdHead();
  pmRenderFooter();
}
/* Footer 四态（一次最多一个 Primary）：
 * 新建 → [取消] [创建预设] [创建并应用*]；
 * 已有未保存 → [取消] [保存修改] [保存并应用*]（保存并应用已覆盖"应用"语义，不再显示应用按钮）；
 * 正在查看当前任务预设且未修改 → [取消] + "当前任务正在使用"轻提示（应用无意义，不显示）；
 * 查看其他预设未修改 → [取消] [应用到当前任务*]。左侧恒为删除（Ghost danger，仅已有预设）。 */
function pmRenderFooter() {
  const st = pmState.editing;
  const left = $('pmFootLeft'), acts = $('pmFootActions');
  if (!left || !acts) return;
  if (!st) { left.innerHTML = ''; acts.innerHTML = ''; return; }
  left.innerHTML = st.isNew ? '' :
    '<button type="button" class="btn ghost danger" id="pmDeleteBtn">' + ic('trash') + '<span>删除预设</span></button>';
  let h = '<button type="button" class="btn" id="pmCancelBtn">取消</button>';
  if (st.isNew) {
    h += '<button type="button" class="btn" id="pmSaveBtn">' + ic('plus') + '<span>创建预设</span></button>' +
      '<button type="button" class="btn primary" id="pmSaveApplyBtn">' + ic('play') + '<span>创建并应用</span></button>';
  } else if (pmEditorDirty) {
    h += '<button type="button" class="btn" id="pmSaveBtn">' + ic('check') + '<span>保存修改</span></button>' +
      '<button type="button" class="btn primary" id="pmSaveApplyBtn">' + ic('play') + '<span>保存并应用</span></button>';
  } else if (presetSession.currentId === st.orig) {
    h += '<span class="pm-cur-hint">' + ic('checkCircle') + '<span>当前任务正在使用</span></span>';
  } else {
    h += '<button type="button" class="btn primary" id="pmApplyBtn">' + ic('check') + '<span>应用到当前任务</span></button>';
  }
  acts.innerHTML = h;
  $('pmCancelBtn').onclick = closePresetManager;
  if ($('pmSaveBtn')) $('pmSaveBtn').onclick = () => pmSave(false);
  if ($('pmSaveApplyBtn')) $('pmSaveApplyBtn').onclick = () => pmSave(true);
  const apply = $('pmApplyBtn');
  if (apply) apply.onclick = () => {
    if (applyPresetToCurrentTask(st.orig)) {
      pmNoteOk('✓ 已应用到当前任务（' + st.orig + '）');
      pmRenderList(); pmRenderFooter();   // 当前任务徽章移动 + Footer 切到"正在使用"态
    }
  };
  const del = $('pmDeleteBtn');
  if (del) del.onclick = pmDelete;
}
async function pmSave(thenApply) {
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
    postcmd: $('pm_f_postcmd').value.trim(),
    backup: $('pm_f_backup').checked, force: $('pm_f_force').checked,
    use_sys_fonts: $('pm_f_use_sys_fonts').checked,
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
    // 会话引用跟随重命名（高级选项选择器 + 当前任务来源）
    if (st.orig !== name) {
      if (presetSession.currentId === st.orig) presetSession.currentId = PRESETS[name] ? name : null;
      if ($('preset_sel').value === st.orig) $('preset_sel').value = PRESETS[name] ? name : '';
    }
    pmEditorDirty = false;
    pmState.editing = { orig: name, isNew: false };
    if (thenApply) {
      refreshPresetSel();   // 先刷新两个下拉的 option（含批量 b_preset_sel）：新建/重命名的预设此时还没进下拉，先应用的话 .value 赋值会因 option 缺失而静默失败
      applyPresetToCurrentTask(name);   // 保存 → 用保存后的结果应用 → 刷新 + 快照（显式两步，不隐式覆盖）
      pmRenderList(); pmRenderEditor();
      pmNoteOk('✓ 已保存并应用到当前任务（' + name + '）');
    } else {
      refreshPresetSel(); updatePresetHint(); rememberPreset();
      pmRenderList(); pmRenderEditor();
      pmNoteOk('✓ 已保存（' + name + '）');
    }
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
    if (presetSession.currentId === name) {   // 删除的是当前任务来源预设：任务转为自定义配置（参数不动）
      presetSession.currentId = null;
      presetSession.snapshot = null;
    }
    refreshPresetSel(); updatePresetHint(); rememberPreset();
    pmEditorDirty = false;
    pmState.editing = Object.keys(PRESETS).length ? { orig: Object.keys(PRESETS)[0], isNew: false } : null;
    pmRenderList(); pmRenderEditor();
    pmNoteOk('✓ 已删除（' + name + '）');
  } catch (ex) { pmNoteErr('删除失败：' + ex); }
}
function pmNoteOk(t) { const el = $('pmNote'); el.textContent = t; el.style.color = 'var(--success)'; }
function pmNoteErr(t) { const el = $('pmNote'); el.textContent = t; el.style.color = 'var(--danger)'; }

/* ==================== 初始化（由 init.js bootstrap 统一调用，仅执行一次） ==================== */
function initPresets() {
$('preset_sel').onchange = function () {
  if (this.value && PRESETS[this.value]) applyPresetToCurrentTask(this.value);   // 唯一应用入口
  else detachCurrentPreset();   // 选回「选择预设…」= 解除预设（参数保留）
};
['sc_name', 'tc_name', 'fonts_dir', 'out_dir', 'out_name_tmpl', 'title', 'postcmd'].forEach(id => $(id).addEventListener('input', updatePresetHint));
['sc_forced', 'tc_forced', 'backup', 'force', 'fonts_mode', 'use_sys_fonts'].forEach(id => $(id).addEventListener('change', updatePresetHint));
$('pmClose').onclick = closePresetManager;
/* 新建预设按钮（静态侧栏底部，不随编辑器滚动/重渲染） */
$('pmNewBtn').onclick = function () {
  if (!pmGuardUnsaved()) return;
  pmState.editing = { orig: null, isNew: true, mode: 'blank', base: pmClone(PM_BLANK) };
  $('pmNote').textContent = '';
  pmRenderList(); pmRenderEditor();
};
/* 编辑器任何输入都标记未保存（切换列表项触发保护 + 头部/.Footer 状态切换；名称输入同步头部标题） */
$('pmEditor').addEventListener('input', function (e) {
  pmMarkDirty();
  if (e.target && e.target.id === 'pmName') pmRenderEdHead();
}, true);
$('pmEditor').addEventListener('change', pmMarkDirty, true);
}
