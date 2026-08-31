/* ==================== 批量封装 ==================== */
const batchItems = [];  // {video, sc, tc, chapters}
const batchSel = new Set();          // 选中行下标（紧凑列表多选）
let batchRunInfo = null;             // 运行中：{ current, total, results[] }，用于逐行状态

/* 队列持久化：批量列表与公共选项存 localStorage（断点续跑：页面刷新/服务重启后自动恢复） */
function saveBatchQueue() {
  try {
    localStorage.setItem('muxui_batch_queue', JSON.stringify({
      items: batchItems,
      b_fonts_dir: $('b_fonts_dir').value, b_out_dir: $('b_out_dir').value, b_out_name_tmpl: $('b_out_name_tmpl').value,
      b_force: $('b_force').checked, b_backup: $('b_backup').checked, b_skip: $('b_skip').checked,
      b_sc_default: $('b_sc_default').value, b_tc_default: $('b_tc_default').value,
      b_sc_forced: $('b_sc_forced').checked, b_tc_forced: $('b_tc_forced').checked,
    }));
  } catch (e) {}
}

/* ---- 单行状态：由 video/sc/tc/chapters 推导（等待/处理中由 batchRunInfo 覆盖） ---- */
function batchRowStatus(i) {
  const it = batchItems[i];
  if (!it) return { cls: 'idle', icon: 'circle', text: '—' };
  if (batchRunInfo && batchRunInfo.results && batchRunInfo.results.length) {
    if (i < batchRunInfo.results.length) {
      const r = batchRunInfo.results[i];
      if (r.skipped) return { cls: 'ok', icon: 'check', text: '已跳过' };
      if (r.ok) return { cls: 'ok', icon: 'check', text: '已完成' };
      return { cls: 'err', icon: 'xCircle', text: '封装失败' };
    }
    return { cls: 'wait', icon: 'circle', text: '等待中' };
  }
  if (batchRunInfo && batchRunInfo.current && batchRunInfo.current - 1 === i) {
    return { cls: 'run', icon: 'loader', text: '封装中…' };
  }
  if (batchRunInfo && batchRunInfo.current) {
    return { cls: 'wait', icon: 'circle', text: '等待中' };
  }
  if (!it.video) return { cls: 'idle', icon: 'circle', text: '待设置' };
  if (!it.sc && !it.tc) return { cls: 'err', icon: 'circle', text: '缺少字幕' };
  if (!it.tc) return { cls: 'warn', icon: 'alertTriangle', text: '缺少 TC' };
  if (!it.sc) return { cls: 'warn', icon: 'alertTriangle', text: '缺少 SC' };
  return { cls: 'ok', icon: 'check', text: '就绪' };
}

/* ---- 文件信息（无探测数据，仅从路径派生格式，不做假元数据） ---- */
function bqFileInfo(it) {
  if (!it || !it.video) return { nameBase: '', nameExt: '', path: '', fmt: '' };
  const v = it.video;
  const slash = v.lastIndexOf('/') > v.lastIndexOf('\\') ? v.lastIndexOf('/') : v.lastIndexOf('\\');
  const base = slash >= 0 ? v.slice(slash + 1) : v;   // 兼容正斜杠 / 反斜杠
  const dot = base.lastIndexOf('.');
  const fmt = (dot > 0 ? base.slice(dot + 1) : '').toUpperCase();
  return {
    nameBase: dot > 0 ? base.slice(0, dot) : base,   // 文件名主体（不含后缀，用于省略号截断）
    nameExt: dot > 0 ? base.slice(dot) : '',         // 含点后缀（如 .mkv），恒保留展示
    path: slash >= 0 ? v.slice(0, slash + 1) : '',   // 目录部分（含末尾分隔符）
    fmt: fmt
  };
}

function bqSubBadge(kind, label, val) {
  const present = !!val;
  return '<span class="bq-badge ' + kind + (present ? ' ok' : ' miss') + '">' +
    '<span class="bq-badge-ic">' + ic(present ? 'check' : 'x') + '</span>' + label +
    '</span>';
}

function bqRowHtml(i) {
  const it = batchItems[i];
  const sel = batchSel.has(i);
  const open = !!it.__open;
  const fi = bqFileInfo(it);
  const st = batchRowStatus(i);
  const vidBase = (it.video || '').split(/[\\/]/).pop() || '';
  const path = fi.path;   // 目录部分（bqFileInfo 已兼容正斜杠/反斜杠）
  const stIcon = st.icon === 'loader' ? ic('loader', 'spin') : st.icon === 'check' ? ic('check') : st.icon === 'xCircle' ? ic('xCircle') : st.icon === 'alertTriangle' ? ic('alertTriangle') : '<span class="bq-dot"></span>';
  return '<div class="bq-row' + (sel ? ' selected' : '') + (open ? ' open' : '') + '" data-i="' + i + '">' +
    '<div class="bq-main">' +
      '<span class="bq-col-check"><input type="checkbox" class="bq-rowcheck" data-i="' + i + '"' + (sel ? ' checked' : '') + ' aria-label="选择任务 ' + (i + 1) + '"></span>' +
      '<span class="bq-col-idx">' + (i + 1) + '</span>' +
      '<div class="bq-file">' +
        '<div class="bq-file-top"><span class="bq-file-name" title="' + esc(vidBase || '') + '"><span class="bq-fn-main">' + esc(fi.nameBase || vidBase || '（空任务）') + '</span>' + (fi.nameExt ? '<span class="bq-fn-ext">' + esc(fi.nameExt) + '</span>' : '') + '</span>' + (fi.fmt ? '<span class="bq-fmt">' + fi.fmt + '</span>' : '') + '</div>' +
        (path ? '<div class="bq-file-path" title="' + esc(it.video) + '">' + esc(path) + '</div>' : '<div class="bq-file-path muted">未设置视频文件</div>') +
      '</div>' +
      '<div class="bq-sub">' +
        bqSubBadge('sc', 'SC 简体', it.sc) +
        bqSubBadge('tc', 'TC 繁体', it.tc) +
        bqSubBadge('ch', '章节', it.chapters) +
      '</div>' +
      '<div class="bq-status"><span class="bq-status-badge ' + st.cls + '">' + stIcon + '<span>' + st.text + '</span></span></div>' +
      '<div class="bq-ops">' +
        '<button type="button" class="btn small ghost bq-detail-toggle" data-i="' + i + '">' + ic('chevronDown') + '<span>详情</span></button>' +
        '<div class="bq-rowmore" data-i="' + i + '">' +
          '<button type="button" class="btn icon-btn ghost bq-more-btn" data-i="' + i + '" aria-haspopup="true" aria-expanded="false" aria-label="更多操作">' + ic('moreHorizontal') + '</button>' +
          '<div class="bq-more-menu bq-rowmenu" data-i="' + i + '" role="menu">' +
            '<button type="button" class="bq-menu-item" data-act="rematch" data-i="' + i + '">' + ic('refreshCw') + '重新匹配</button>' +
            '<button type="button" class="bq-menu-item" data-act="toTop" data-i="' + i + '">' + ic('arrowUp') + '移动到顶部</button>' +
            '<button type="button" class="bq-menu-item" data-act="toBottom" data-i="' + i + '">' + ic('arrowUpRight') + '移动到底部</button>' +
            '<button type="button" class="bq-menu-item" data-act="dup" data-i="' + i + '">' + ic('copy') + '复制任务</button>' +
            '<div class="bq-menu-sep"></div>' +
            '<button type="button" class="bq-menu-item danger" data-act="del" data-i="' + i + '">' + ic('trash') + '删除任务</button>' +
          '</div>' +
        '</div>' +
      '</div>' +
    '</div>' +
    '<div class="bq-detail" style="' + (open ? '' : 'display:none;') + '">' +
      '<div class="bq-detail-grid">' +
        '<div class="bq-detail-field bq-detail-video">' +
          '<span class="bq-detail-label">视频文件</span>' +
          '<div class="bq-detail-control"><input id="b_v_' + i + '" type="text" value="' + esc(it.video) + '" placeholder="视频路径（填完自动匹配字幕）" autocomplete="off">' +
            '<button type="button" class="btn small" onclick="batchBrowse(' + i + ',\'video\')">' + ic('folderOpen') + '浏览</button>' +
          '</div>' +
        '</div>' +
        '<div class="bq-detail-field">' +
          '<span class="bq-detail-label"><span class="bq-badge sc">SC</span>简体中文</span>' +
          '<div class="bq-detail-control"><input id="b_s_' + i + '" type="text" value="' + esc(it.sc) + '" placeholder="可选" autocomplete="off">' +
            '<button type="button" class="btn small" title="浏览简体字幕" onclick="batchBrowse(' + i + ',\'sc\')">' + ic('folderOpen') + '更换</button>' +
            '<button type="button" class="btn small ghost" title="移除简体字幕" onclick="batchDelSub(' + i + ',\'sc\')">' + ic('trash') + '删除</button>' +
          '</div>' +
        '</div>' +
        '<div class="bq-detail-field">' +
          '<span class="bq-detail-label"><span class="bq-badge tc">TC</span>繁体中文</span>' +
          '<div class="bq-detail-control"><input id="b_t_' + i + '" type="text" value="' + esc(it.tc) + '" placeholder="可选" autocomplete="off">' +
            '<button type="button" class="btn small" title="浏览繁体字幕" onclick="batchBrowse(' + i + ',\'tc\')">' + ic('folderOpen') + '更换</button>' +
            '<button type="button" class="btn small ghost" title="移除繁体字幕" onclick="batchDelSub(' + i + ',\'tc\')">' + ic('trash') + '删除</button>' +
          '</div>' +
        '</div>' +
        '<div class="bq-detail-field">' +
          '<span class="bq-detail-label"><span class="bq-badge ch">章</span>章节文件</span>' +
          '<div class="bq-detail-control"><input id="b_c_' + i + '" type="text" value="' + esc(it.chapters || '') + '" placeholder="章节文件（自动匹配视频旁同名 txt/xml，可留空）" autocomplete="off">' +
            '<button type="button" class="btn small" title="浏览章节文件" onclick="batchBrowse(' + i + ',\'chapters\')">' + ic('folderOpen') + '更换</button>' +
            '<button type="button" class="btn small ghost" title="移除章节" onclick="batchDelSub(' + i + ',\'chapters\')">' + ic('trash') + '删除</button>' +
          '</div>' +
        '</div>' +
      '</div>' +
    '</div>' +
  '</div>';
}

function renderBatch() {
  lastBatchResult = null;   // 列表变更（增/删/清空）：清除上次批量结果
  const list = $('batchList'); list.innerHTML = '';
  batchItems.forEach((it, i) => {
    const div = document.createElement('div');
    div.innerHTML = bqRowHtml(i);
    list.appendChild(div.firstElementChild);
  });
  wireBatchRowEvents();
  refreshBatchSticky();
  refreshBatchCount();
  saveBatchQueue();
}

/* 行内事件：复选框 / 详情展开 / 更多菜单 / 各输入 change */
function wireBatchRowEvents() {
  const list = $('batchList');
  list.querySelectorAll('.bq-rowcheck').forEach(cb => {
    cb.onchange = () => {
      const i = +cb.dataset.i;
      if (cb.checked) batchSel.add(i); else batchSel.delete(i);
      updateBatchSelUI();
    };
  });
  list.querySelectorAll('.bq-detail-toggle').forEach(btn => {
    btn.onclick = () => toggleBatchDetail(+btn.dataset.i);
  });
  list.querySelectorAll('.bq-more-btn').forEach(btn => {
    btn.onclick = (e) => { e.stopPropagation(); toggleRowMenu(+btn.dataset.i, btn); };
  });
  list.querySelectorAll('.bq-rowmenu .bq-menu-item').forEach(item => {
    item.onclick = (e) => { e.stopPropagation(); rowMenuAction(item.dataset.act, +item.dataset.i); };
  });
  batchItems.forEach((it, i) => {
    const vin = $('b_v_' + i);
    if (vin) vin.addEventListener('change', async function () {
      lastBatchResult = null;
      var v = vin.value.trim();
      batchItems[i].video = v;
      if (!v) { saveBatchQueue(); refreshBatchSticky(); return; }
      var m = await identify(v);
      if (m.sc && !$('b_s_' + i).value) $('b_s_' + i).value = m.sc;
      if (m.tc && !$('b_t_' + i).value) $('b_t_' + i).value = m.tc;
      if (m.chapters && !$('b_c_' + i).value) $('b_c_' + i).value = m.chapters;
      batchItems[i].sc = $('b_s_' + i).value; batchItems[i].tc = $('b_t_' + i).value;
      batchItems[i].chapters = $('b_c_' + i).value;
      saveBatchQueue();
      refreshBatchSticky();
      refreshRowOnly(i);
    });
    ['sc', 'tc', 'chapters'].forEach(kind => {
      const inp = $({sc: 'b_s_', tc: 'b_t_', chapters: 'b_c_'}[kind] + i);
      if (inp) inp.addEventListener('input', function () {
        batchItems[i][kind] = inp.value.trim();
        lastBatchResult = null;
        refreshRowOnly(i);
      });
    });
  });
}

/* 仅刷新某行（避免全量重渲染丢焦点）；输入已由 input 事件同步数据，这里重绘徽章/状态与 file 摘要 */
function refreshRowOnly(i) {
  const row = document.querySelector('.bq-row[data-i="' + i + '"]');
  if (!row) return;
  const it = batchItems[i];
  // 更新文件摘要与徽章/状态（只替换非输入区，保留 detail 输入）
  const main = row.querySelector('.bq-main');
  const tmp = document.createElement('div');
  tmp.innerHTML = bqRowHtml(i);
  const newMain = tmp.querySelector('.bq-main');
  main.innerHTML = newMain.innerHTML;
  // 重新绑定行内事件（check/detail/more）
  row.querySelectorAll('.bq-rowcheck').forEach(cb => cb.onchange = () => { const ii = +cb.dataset.i; if (cb.checked) batchSel.add(ii); else batchSel.delete(ii); updateBatchSelUI(); });
  row.querySelectorAll('.bq-detail-toggle').forEach(btn => btn.onclick = () => toggleBatchDetail(+btn.dataset.i));
  row.querySelectorAll('.bq-more-btn').forEach(btn => btn.onclick = (e) => { e.stopPropagation(); toggleRowMenu(+btn.dataset.i, btn); });
  row.querySelectorAll('.bq-rowmenu .bq-menu-item').forEach(item => item.onclick = (e) => { e.stopPropagation(); rowMenuAction(item.dataset.act, +item.dataset.i); });
}

function toggleBatchDetail(i) {
  const row = document.querySelector('.bq-row[data-i="' + i + '"]');
  if (!row) return;
  const detail = row.querySelector('.bq-detail');
  const isOpen = row.classList.contains('open');
  row.classList.toggle('open', !isOpen);
  if (detail) detail.style.display = isOpen ? 'none' : '';
  batchItems[i].__open = !isOpen;
}
function toggleRowMenu(i, btn) {
  const menu = document.querySelector('.bq-rowmenu[data-i="' + i + '"]');
  if (!menu) return;
  const open = menu.classList.contains('open');
  closeAllRowMenus();
  if (!open) { menu.classList.add('open'); if (btn) btn.setAttribute('aria-expanded', 'true'); }
}
function closeAllRowMenus() {
  document.querySelectorAll('.bq-rowmenu.open').forEach(m => {
    m.classList.remove('open');
    const i = m.dataset.i;
    const b = document.querySelector('.bq-more-btn[data-i="' + i + '"]');
    if (b) b.setAttribute('aria-expanded', 'false');
  });
}
async function rowMenuAction(act, i) {
  const it = batchItems[i];
  if (!it) return;
  if (act === 'del') { batchSel.delete(i); batchItems.splice(i, 1); reindexSel(); renderBatch(); return; }
  if (act === 'dup') { batchItems.splice(i + 1, 0, Object.assign({}, it, { __open: false })); reindexSel(); renderBatch(); return; }
  if (act === 'toTop') { batchItems.splice(0, 0, batchItems.splice(i, 1)[0]); reindexSel(); renderBatch(); return; }
  if (act === 'toBottom') { batchItems.push(batchItems.splice(i, 1)[0]); reindexSel(); renderBatch(); return; }
  if (act === 'rematch') { await matchOne(i); }
}
function reindexSel() {
  const arr = [...batchSel].sort((a, b) => a - b);
  batchSel.clear();
  arr.forEach(offset => { if (offset < batchItems.length) batchSel.add(offset); });
}
async function matchOne(i) {
  const it = batchItems[i];
  if (!it || !it.video) { setStatus('该任务没有视频路径，无法匹配', 'err'); return; }
  setStatus('正在匹配字幕…', 'run');
  try {
    const id = await identify(it.video);
    if (id.sc && !it.sc) it.sc = id.sc;
    if (id.tc && !it.tc) it.tc = id.tc;
    if (id.chapters && !it.chapters) it.chapters = id.chapters;
    if (id.fontsDir && !$('b_fonts_dir').value.trim()) $('b_fonts_dir').value = id.fontsDir;
    renderBatch();
    setStatus((id.sc || id.tc) ? '已匹配：' + (it.video || '').split(/[\\/]/).pop() : '未匹配到字幕', id.sc || id.tc ? 'ok' : 'err');
  } catch (ex) { setStatus('匹配失败：' + ex, 'err'); }
}
async function rematchSelected() {
  const ids = [...batchSel].filter(i => batchItems[i] && batchItems[i].video);
  if (!ids.length) { setStatus('选中的任务没有视频路径', 'err'); return; }
  setStatus('正在重新匹配 ' + ids.length + ' 个任务…', 'run');
  let hit = 0;
  for (const i of ids) {
    const id = await identify(batchItems[i].video);
    if (id.sc && !batchItems[i].sc) batchItems[i].sc = id.sc;
    if (id.tc && !batchItems[i].tc) batchItems[i].tc = id.tc;
    if (id.chapters && !batchItems[i].chapters) batchItems[i].chapters = id.chapters;
    if (id.sc || id.tc) hit++;
  }
  renderBatch();
  setStatus('已重新匹配，命中 ' + hit + '/' + ids.length, 'ok');
}

function updateBatchSelUI() {
  const bar = $('batchSelBar');
  const countEl = $('batchSelCount');
  const n = batchSel.size;
  const selectAll = $('bqSelectAll');
  if (selectAll) {
    selectAll.checked = n > 0 && n === batchItems.length;
    selectAll.indeterminate = n > 0 && n < batchItems.length;
  }
  if (n === 0) { if (bar) bar.style.display = 'none'; }
  else {
    if (countEl) countEl.textContent = '已选择 ' + n + ' 个任务';
    if (bar) bar.style.display = '';
  }
  document.querySelectorAll('.bq-row').forEach(r => {
    const i = +r.dataset.i;
    r.classList.toggle('selected', batchSel.has(i));
    const cb = r.querySelector('.bq-rowcheck');
    if (cb) cb.checked = batchSel.has(i);
  });
  refreshBatchSticky();
}
function refreshBatchCount() {
  const el = $('batchCount');
  if (!el) return;
  const filled = batchItems.filter(it => (it.video || '').trim()).length;
  const ready = batchItems.filter(it => (it.video || '').trim() && it.sc && it.tc).length;
  const needs = filled - ready;
  if (!batchItems.length) el.textContent = '共 0 个任务';
  else el.textContent = '共 ' + batchItems.length + ' 个任务（就绪 ' + ready + ' 个' + (needs ? '，需处理 ' + needs + ' 个' : '') + '）';
}

function batchBrowse(i, kind) {
  const id = kind === 'video' ? 'b_v_' + i : kind === 'sc' ? 'b_s_' + i : kind === 'tc' ? 'b_t_' + i : 'b_c_' + i;
  openBrowser(v => { $(id).value = v; if (kind === 'video') { batchItems[i].video = v; fireChange($('b_v_' + i)); } else { batchItems[i][kind] = v; refreshRowOnly(i); saveBatchQueue(); } },
    kind === 'video' ? 'video' : kind === 'chapters' ? 'any' : 'sub', $(id).value, kind === 'video' ? 'video' : kind);
}
function batchDel(i) { batchSel.delete(i); batchItems.splice(i, 1); reindexSel(); renderBatch(); }   // renderBatch 内含 saveBatchQueue
/* 移除行内字幕/章节（输入框与数据同步清空，sticky 即时刷新） */
function batchDelSub(i, kind) {
  const id = kind === 'video' ? 'b_v_' + i : kind === 'sc' ? 'b_s_' + i : kind === 'tc' ? 'b_t_' + i : 'b_c_' + i;
  const inp = $(id);
  if (!inp) return;
  inp.value = '';
  batchItems[i][kind] = '';
  lastBatchResult = null;
  refreshRowOnly(i);
  refreshBatchSticky();
  saveBatchQueue();
}

/* 添加文件：浏览器选视频 → 统一识别（字幕 + 字体目录，逻辑见 identify.js） */
/* 添加整个文件夹：列出目录内全部视频 → 逐个统一识别 */
const BATCH_VIDEO_RE = /\.(mkv|mp4|m2ts|ts|avi|mov|webm|flv|wmv|m4v)$/i;
async function addVideosFromDir(dir) {
  if (!dir) return;
  let d;
  try {
    d = await api('/api/list?path=' + encodeURIComponent(dir));
  } catch (ex) { setStatus('读取目录失败：' + ex, 'err'); return; }
  const vids = (d.files || []).filter(f => BATCH_VIDEO_RE.test(f[0])).map(f => dir + '\\' + f[0]);
  if (!vids.length) { setStatus('该目录下没有视频文件（MKV/MP4 等）', 'err'); return; }
  setStatus('正在识别 ' + vids.length + ' 个视频的字幕与字体目录…', 'run');
  try {
    const ids = await Promise.all(vids.map(v => identify(v)));
    let added = 0;
    vids.forEach((v, i) => {
      if (batchItems.some(it => it.video && it.video.toLowerCase() === v.toLowerCase())) return;
      addBatchVideo(v, [], ids[i]);
      added++;
    });
    const fd = ids.map(x => x.fontsDir).find(Boolean);
    if (fd && !$('b_fonts_dir').value.trim()) $('b_fonts_dir').value = fd;   // 字体目录：取首个识别到的
    renderBatch();
    setStatus(added
      ? ('已添加 ' + added + ' 个视频' + (added < vids.length ? '（跳过 ' + (vids.length - added) + ' 个重复）' : ''))
      : '所选视频都已在列表中', 'ok');
  } catch (ex) {
    setStatus('添加失败：' + ex, 'err');
  }
}


function addBatchVideo(video, subPool, matched) {
  const dir = video.slice(0, video.lastIndexOf('\\') + 1);
  const stem = video.slice(video.lastIndexOf('\\') + 1).replace(/\.[^.]+$/, '');
  let sc = '', tc = '';
  const stems = [stem, stem.replace(/\s*[-_ ]\d+\s*$/, '')];
  const scSfx = ['.sc.ass', '.chs.ass', '.jpsc.ass', '.SC.ass', '.CHS.ass', '.JPSC.ass', '.zh-hans.ass', '.zh-cn.ass'];
  const tcSfx = ['.tc.ass', '.cht.ass', '.jptc.ass', '.TC.ass', '.CHT.ass', '.JPTC.ass', '.zh-hant.ass', '.zh-tw.ass'];
  for (const st of stems) {
    for (const sfx of scSfx) { if (!sc && subPool.some(p => p.toLowerCase() === (dir + st + sfx).toLowerCase())) sc = dir + st + sfx; }
    for (const sfx of tcSfx) { if (!tc && subPool.some(p => p.toLowerCase() === (dir + st + sfx).toLowerCase())) tc = dir + st + sfx; }
  }
  if (!sc && matched && matched.sc) sc = matched.sc;
  if (!tc && matched && matched.tc) tc = matched.tc;
  batchItems.push({video, sc, tc, chapters: (matched && matched.chapters) || '', __open: false});
}
let bJob = null;
/* 失败单集重跑：用列表中对应项（含已匹配字幕）重组为单项队列并直接开始 */
function rerunFailed(i) {
  if (bJob) { setStatus('批量任务进行中，不能重跑', 'err'); return; }
  const it = batchItems[i];
  if (!it || !it.video) { alert('找不到对应列表项（列表可能已被修改）'); return; }
  batchItems.splice(0, batchItems.length);
  batchItems.push({ video: it.video, sc: it.sc || '', tc: it.tc || '', chapters: it.chapters || '', __open: false });
  renderBatch();
  setStatus('已重组为单项队列并开始重跑：' + it.video.split(/[\\/]/).pop(), 'ok');
  $('btnBatchStart').click();
}
/* 预设套用 → 批量公共字段联动（由 presets.js applyPreset 调用；映射集中在此，batch 域自持） */
function applyPresetToBatchCommon(d) {
  const bm = { fonts_mode: 'b_fonts_mode', out_name_tmpl: 'b_out_name_tmpl', title: 'b_title',
               sc_default: 'b_sc_default', tc_default: 'b_tc_default', sc_forced: 'b_sc_forced', tc_forced: 'b_tc_forced',
               use_sys_fonts: 'b_use_sys_fonts', postcmd: 'b_postcmd' };
  Object.keys(bm).forEach(k => { if (d[k] !== undefined && d[k] !== '' && $(bm[k])) { if (bm[k].endsWith('_forced') || k === 'use_sys_fonts') $(bm[k]).checked = !!d[k]; else $(bm[k]).value = d[k]; } });
  if (d.fonts_dir && $('b_fonts_dir')) $('b_fonts_dir').value = d.fonts_dir;
  if (d.out_dir && $('b_out_dir')) $('b_out_dir').value = d.out_dir;
}
/* 页面加载恢复上次批量队列（断点续跑） */
/* ===== 底部批量状态条：总体进度 / 当前文件 / 耗时与剩余（纯展示，不改任务逻辑） ===== */
let bStickyStartTs = 0;   // 本轮批量开始时刻
function bStickyOverall(s) {   // 总体进度 = (已完成文件数 + 当前文件内部进度) / 总数，区别于单文件进度
  if (!s.total) return null;
  const cur = (s.progress != null ? s.progress : 0) / 100;
  return Math.min(100, Math.round((s.current - 1 + cur) / s.total * 100));
}
function bStickyUpdate(s) {
  const ov = bStickyOverall(s);
  $('bStickyPct').textContent = ov != null ? ov + '%' : '--';
  if (ov != null) $('batchStickyBar').style.width = ov + '%';
  if (s.total) $('bStickyCountNum').textContent = Math.min(Math.max(s.current || 1, 1), s.total) + ' / ' + s.total;
  if (s.current_video) {
    $('bStickyCurName').textContent = truncMid(s.current_video.split(/[\\/]/).pop(), 34);   // 写入即截中段；34 字符在让出状态区宽度后必然放下，CSS ellipsis 不再接管（尾部扩展名可见）
    $('bStickyCur').style.display = '';
  }
  const elapsed = Date.now() - bStickyStartTs;
  $('bStickyElapsed').textContent = fmtDur(elapsed);
  if (ov != null && ov > 0) $('bStickyEta').textContent = fmtDur(elapsed / ov * (100 - ov));
  else $('bStickyEta').textContent = '计算中…';
}
function bStickyFreeze() {   // 终态：耗时定格、剩余清空、当前文件与高光退场
  $('bStickyProgress').classList.remove('run');
  $('bStickyCur').style.display = 'none';
  $('bStickyEta').textContent = '--:--:--';
}
/* 清空全部任务 + 复位选项（原 btnBatchClear 语义，含确认） */
function clearBatchAll() {
  if (bJob) { setStatus('批量任务进行中，不能重置', 'err'); return; }
  const dirty = batchItems.length || $('batchResults').innerHTML || $('b_fonts_dir').value.trim() || $('b_out_dir').value.trim() ||
    $('b_force').checked || !$('b_backup').checked || $('b_skip').checked || $('b_sc_default').value || $('b_tc_default').value ||
    $('b_sc_forced').checked || $('b_tc_forced').checked || ($('b_use_sys_fonts') && $('b_use_sys_fonts').checked) ||
    ($('b_postcmd') && $('b_postcmd').value.trim());
  if (!dirty) return;
  if (!confirm('确定重置批量封装的全部设置？\n将清空批量列表、结果展示与字体目录/输出目录等选项（已生成的输出文件保留在磁盘，不会被删除）')) return;
  batchItems.splice(0, batchItems.length);
  batchSel.clear();
  renderBatch();
  $('batchResults').innerHTML = '';
  $('batchState').textContent = '';
  $('b_fonts_dir').value = '';       // 字体目录（自动识别项，下次批量自动重填）
  $('b_out_dir').value = '';         // 输出目录
  $('b_force').checked = false;  // 强制封装
  $('b_backup').checked = true;  // 备份原件（默认勾选）
  $('b_skip').checked = false;   // 跳过已存在输出
  $('b_sc_default').value = ''; $('b_tc_default').value = '';   // 字幕旗标
  $('b_sc_forced').checked = false; $('b_tc_forced').checked = false;
  if ($('b_use_sys_fonts')) $('b_use_sys_fonts').checked = false;   // 包含系统已装字体
  if ($('b_postcmd')) $('b_postcmd').value = '';   // 后处理命令
  if ($('b_out_name_tmpl')) $('b_out_name_tmpl').value = '';   // 命名模板
  if ($('b_title')) $('b_title').value = '';   // 标题
  // 底部批量状态条一并复位（与启动时初态一致）
  $('bStickyPct').textContent = '--';
  $('batchStickyBar').style.width = '0%';
  $('bStickyCountNum').textContent = '0 / 0';
  $('bStickyElapsed').textContent = '--:--:--';
  $('bStickyEta').textContent = '--:--:--';
  $('bStickyCur').style.display = 'none';
  updateBatchSelUI();
  try { localStorage.removeItem('muxui_batch_queue'); } catch (e) {}
  refreshBatchSticky();
  setStatus('已重置批量封装设置（输出文件保留在磁盘）', 'ok');
}

/* ==================== 初始化（由 init.js bootstrap 统一调用，仅执行一次） ==================== */
function initBatch() {
$('btnBatchAddRow').onclick = () => { batchItems.push({video:'', sc:'', tc:'', __open:true}); renderBatch(); };
$('btnBatchClear').onclick = clearBatchAll;
$('btnBatchRematchAll').onclick = async () => {
  if (!batchItems.length) { alert('批量列表为空'); return; }
  setStatus('正在按集数匹配字幕…', 'run');
  try {
    let hit = 0, miss = 0, noVideo = 0, hitSc = 0, hitTc = 0, firstId = null;
    await Promise.all(batchItems.map(async function (it) {
      if (!it.video) { noVideo++; return; }
      const id = await identify(it.video);   // 统一识别：字幕 + 字体目录 + 章节
      if (!firstId) firstId = id;
      if (id.sc) hitSc++;
      if (id.tc) hitTc++;
      if (id.chapters && !it.chapters) it.chapters = id.chapters;
      const matched = !!(id.sc || id.tc);
      if (id.sc && !it.sc) it.sc = id.sc;
      if (id.tc && !it.tc) it.tc = id.tc;
      if (matched) hit++; else miss++;
    }));
    const fontFound = !!(firstId && firstId.fontsDir && !$('b_fonts_dir').value.trim() && ($('b_fonts_dir').value = firstId.fontsDir, true));   // 自动匹配字体目录
    if (hit === 0) {
      setStatus(miss === 0 ? '没有可匹配的项（视频路径均为空）' : '按集数未匹配到任何字幕（0/' + (hit + miss) + ' 命中）' + (fontFound ? ' · 已自动识别字体目录' : ''), 'err');
    } else {
      setStatus('批量字幕匹配完成：命中 ' + hit + ' 项（简 ' + hitSc + ' / 繁 ' + hitTc + '）' + (miss ? '，未命中 ' + miss + ' 项' : '') + (noVideo ? '，' + noVideo + ' 项无视频路径' : '') + (fontFound ? ' · 已自动识别字体目录' : ''), 'ok');
    }
  } catch (ex) {
    setStatus('批量匹配失败：' + ex, 'err');
  } finally {
    renderBatch();
    refreshBatchSticky();
  }
};
$('btnBatchClearSubs').onclick = () => {
  if (!batchItems.length) return;
  batchItems.forEach(it => { it.sc = ''; it.tc = ''; });
  renderBatch();
  setStatus('已清除所有任务的字幕匹配（视频保留）', 'ok');
};
$('btnBatchFiles').onclick = () => openBrowser(async v => {
  if (!v) return;
  if (!BATCH_VIDEO_RE.test(v)) { setStatus('请选择视频文件（MKV/MP4/M2TS 等）', 'err'); return; }
  if (batchItems.some(it => it.video && it.video.toLowerCase() === v.toLowerCase())) { setStatus('该视频已在列表中：' + v, 'err'); return; }
  setStatus('正在识别字幕与字体目录…', 'run');
  try {
    const id = await identify(v);   // 统一识别：字幕 + 字体目录
    addBatchVideo(v, [], id);   // addBatchVideo 内置 matched.sc/tc 填充
    if (id.fontsDir && !$('b_fonts_dir').value.trim()) $('b_fonts_dir').value = id.fontsDir;
    renderBatch();
    setStatus('已添加：' + v.split(/[\\/]/).pop() + (id.sc || id.tc ? ' · 已自动匹配字幕' : ''), 'ok');
  } catch (ex) {
    setStatus('添加失败：' + ex, 'err');
  }
}, 'video', '', 'batch', addVideosFromDir);
$('btnBatchDir').onclick = () => openBrowser(v => { if (v) addVideosFromDir(v); }, 'dir', '', 'batch');
/* 更多菜单开合 */
$('btnBatchMore').onclick = (e) => {
  e.stopPropagation();
  const open = $('bqMore').classList.toggle('open');
  $('btnBatchMore').setAttribute('aria-expanded', open ? 'true' : 'false');
};
document.addEventListener('click', function (e) {
  const wrap = $('bqMore');
  if (wrap && !wrap.contains(e.target)) { wrap.classList.remove('open'); $('btnBatchMore').setAttribute('aria-expanded', 'false'); }
  closeAllRowMenus();
});
/* 全选 */
$('bqSelectAll').onchange = function () {
  const all = batchItems.map((_, i) => i);
  if (this.checked) { batchItems.forEach((_, i) => batchSel.add(i)); }
  else { batchSel.clear(); }
  updateBatchSelUI();
};
/* 选中操作栏 */
$('btnSelDelete').onclick = () => {
  if (!batchSel.size) return;
  if (!confirm('删除选中的 ' + batchSel.size + ' 个任务？')) return;
  const toDel = [...batchSel].sort((a, b) => b - a);
  toDel.forEach(i => batchItems.splice(i, 1));
  batchSel.clear();
  renderBatch();
};
$('btnSelRematch').onclick = rematchSelected;
$('btnSelClear').onclick = () => { batchSel.clear(); updateBatchSelUI(); };
/* 高级设置折叠 */
$('btnBatchAdv').onclick = function () {
  const body = $('batchAdvBody');
  const open = body.style.display !== 'none';
  body.style.display = open ? 'none' : '';
  this.classList.toggle('open', !open);
  this.setAttribute('aria-expanded', open ? 'false' : 'true');
  this.querySelector('.bq-adv-label').textContent = open ? '展开高级设置' : '收起高级设置';
};
/* 预设选择（批量设置区）：复用唯一应用入口 applyPresetToCurrentTask */
$('b_preset_sel').onchange = function () {
  if (this.value && window.PRESETS && window.PRESETS[this.value]) window.applyPresetToCurrentTask(this.value);
  else if (this.value === '') { /* 选回「选择预设…」= 解除预设 */ if (window.detachCurrentPreset) window.detachCurrentPreset(); }
};
$('btnBatchPresetManage').onclick = () => { if (window.openPresetManager) window.openPresetManager(); };
/* 检查问题：定位到第一个缺失字幕/无视频的任务 */
$('btnBatchCheck').onclick = () => {
  if (!batchItems.length) return;
  const idx = batchItems.findIndex(it => !it.video || (!it.sc && !it.tc) || !it.tc || !it.sc);
  const row = idx >= 0 ? document.querySelector('.bq-row[data-i="' + idx + '"]') : null;
  if (row) { row.scrollIntoView({ behavior: 'smooth', block: 'center' }); row.classList.add('flash'); setTimeout(() => row.classList.remove('flash'), 1200); }
  else setStatus('所有任务都已就绪', 'ok');
};
$('btnMatchAll').onclick = $('btnBatchRematchAll').onclick;
$('btnBFonts').onclick = () => openBrowser(v => $('b_fonts_dir').value = v, 'dir', $('b_fonts_dir').value, 'fonts');
$('btnBOut').onclick = () => openBrowser(v => $('b_out_dir').value = v, 'dir', $('b_out_dir').value, 'out');

/* ---- 批量任务主体（按钮启动逻辑，保留原语义） ---- */
const _btnStart = $('btnBatchStart');
_btnStart.onclick = async () => {
  if (bJob) {
    setStatus('正在停止…', 'run');
    await api('/api/stop', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ id: bJob }) });
    return;
  }
  batchItems.forEach((it, i) => { it.video = $('b_v_' + i).value.trim(); it.sc = $('b_s_' + i).value.trim(); it.tc = $('b_t_' + i).value.trim(); it.chapters = ($('b_c_' + i) ? $('b_c_' + i).value.trim() : ''); });
  let items = batchItems.filter(it => it.video);
  if (!items.length) { alert('批量列表为空'); return; }
  let autoMatched = 0;
  await Promise.all(items.map(async (it) => {
    if (it.sc || it.tc) return;
    const m = await identify(it.video);   // 统一识别入口（见 identify.js）
    if (m.sc) { it.sc = m.sc; autoMatched++; }
    if (m.tc) { it.tc = m.tc; autoMatched++; }
  }));
  if (autoMatched) { renderBatch(); setStatus('已自动匹配 ' + autoMatched + ' 个字幕', 'ok'); }
  const noSub = items.filter(it => !it.sc && !it.tc);
  const noSc = items.filter(it => !it.sc && it.tc);
  const noTc = items.filter(it => it.sc && !it.tc);
  if (noSub.length || noSc.length || noTc.length) {
    const listOf = arr => '第 ' + arr.slice(0, 8).map(it => batchItems.indexOf(it) + 1).join('、') + ' 项' + (arr.length > 8 ? '（共 ' + arr.length + ' 项）' : '');
    const lines = [];
    if (noSub.length) lines.push('· ' + noSub.length + ' 项无任何字幕（将保留源字幕与源字体）：' + listOf(noSub));
    if (noSc.length) lines.push('· ' + noSc.length + ' 项缺简体（仅繁体）：' + listOf(noSc));
    if (noTc.length) lines.push('· ' + noTc.length + ' 项缺繁体（仅简体）：' + listOf(noTc));
    if (!confirm('批量封装前的字幕匹配情况：\n' + lines.join('\n') + '\n仍要开始批量封装？')) {
      const parts = [];
      if (noSub.length) parts.push(noSub.length + ' 项无字幕');
      if (noSc.length) parts.push(noSc.length + ' 项缺简');
      if (noTc.length) parts.push(noTc.length + ' 项缺繁');
      setStatus('已取消批量封装：' + parts.join('，') + '，请检查匹配或手动填写', 'err');
      renderBatch();
      return;
    }
  }
  const body = Object.assign({ items }, buildMuxCommon('b_'));   // 公共参数（字体/输出/备份/旗标，与单个封装同一份逻辑，见 task.js）
  const r = await api('/api/batch', { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify(body) });
  if (r.error) { $('batchState').textContent = '错误：' + r.error; return; }
  bJob = r.job;
  bStickyStartTs = Date.now();
  $('bStickyProgress').classList.add('run');
  $('batchStickyBar').style.width = '0%';
  $('bStickyPct').textContent = '--';
  $('bStickyCountNum').textContent = '0 / ' + items.length;
  $('bStickyCur').style.display = 'none';
  $('bStickyElapsed').textContent = '00:00:00';
  $('bStickyEta').textContent = '计算中…';
  batchRunInfo = { current: 0, total: items.length, results: [] };
  refreshRowStatuses();
  setRunButton(_btnStart, true, '停止批量', '开始批量封装');
  showLogTab('batch'); setLog('batch', '');
  const bfin = (s, lastR, stateText, statusMsg, statusCls) => {
    bJob = null;
    batchRunInfo = null;
    refreshRowStatuses();
    setRunButton(_btnStart, false, '停止批量', '开始批量封装');
    const qs = s && s.qc_summary;
    if (qs && qs.total) stateText += ' · QC 通过 ' + qs.ok + '/' + qs.total + (qs.warn ? '，预警 ' + qs.warn : '') + (qs.fail ? '，失败 ' + qs.fail : '');
    $('batchState').textContent = stateText;
    bStickyFreeze();
    setStatus(statusMsg, statusCls);
    // 终态：用结果表填充文件计数/百分比，并按 成功/部分失败/全部失败 生成状态文案
    const res = (s && s.results) || [];
    let last = lastR;
    if (res.length) {
      const okN = res.filter(r => r.ok).length, failN = res.length - okN;
      const pct = okN === res.length ? 100 : Math.round(okN / res.length * 100);
      $('bStickyCountNum').textContent = res.length + ' / ' + res.length;
      $('bStickyPct').textContent = pct + '%';
      $('batchStickyBar').style.width = pct + '%';
      if (s && (s.status === 'done' || s.status === 'error')) {
        last = okN === res.length ? { cls: 'ok', icon: 'checkCircle', text: '批量封装完成 · ' + res.length + ' 个文件' }
          : okN === 0 ? { cls: 'err', icon: 'xCircle', text: '批量封装失败' }
          : { cls: 'err', icon: 'alertTriangle', text: '已完成 ' + okN + ' 个，失败 ' + failN + ' 个' };
      }
    }
    lastBatchResult = last;
    refreshBatchSticky();
  };
  startTaskPolling({
    job: bJob, interval: 1200,
    onAny: s => {
      $('batchState').textContent = s.total ? ('第 ' + s.current + ' / ' + s.total + ' 个：' + (s.current_video || '') + (s.progress != null ? ('  当前项 ' + s.progress + '%') : '')) : '';
      setLog('batch', s.log);
      if (s.current && s.total) {
        batchRunInfo = { current: s.current, total: s.total, results: s.results || batchRunInfo ? batchRunInfo.results : [] };
        if (s.results && s.results.length) batchRunInfo.results = s.results;
        refreshRowStatuses();
      }
      const resBox = $('batchResults');
      if (s.results && s.results.length) {
        resBox.innerHTML = '<div class="table-wrap" style="margin-top:8px;"><table style="min-width:560px;"><tr><th>#</th><th>输出文件</th><th>结果</th><th style="width:130px"></th></tr>' + s.results.map((r, i) =>
          '<tr><td>' + (i + 1) + '</td><td class="mono" style="word-break:break-all">' + esc(r.output || r.video) + '</td><td>' + (r.skipped ? '<span class="chip sm info">' + ic('info') + '已存在，跳过</span>' : r.ok ? '<span class="chip sm ok">' + ic('check') + '成功</span>' : '<span class="chip sm err">' + ic('xCircle') + '失败' + (r.reason ? '：' + esc(r.reason) : ' (exit ' + r.exit + ')') + '</span>') + (r.qc ? '<span class="chip sm ' + (r.qc.status === 'ok' ? 'ok' : r.qc.status === 'warn' ? 'warn' : 'err') + '" title="' + esc(((r.qc.warn || []).concat(r.qc.hard || [])).join('\n')) + '">QC' + (r.qc.status === 'ok' ? '通过' : r.qc.status === 'warn' ? '预警' + (r.qc.warn || []).length : '失败') + '</span>' : '') + '</td><td><button class="btn small" data-open-dir="' + encodeURIComponent(r.output || r.video) + '">打开</button>' + (r.cmd ? ' <button class="btn small" data-cmd="' + b64e(r.cmd) + '" title="查看本次封装的 mkvmerge 命令">' + ic('terminal') + '命令</button>' : '') + (r.ok ? '' : ' <button class="btn small" onclick="rerunFailed(' + i + ')">重跑</button>') + '</td></tr>').join('') + '</table></div>';
      }
    },
    onTick: s => {
      setStickyRun($('batchStickyNote'), '正在批量封装');   // 当前文件/计数/进度在各自区块展示
      bStickyUpdate(s);
    },
    onDone: s => { beep(); bfin(s, { cls: 'ok', icon: 'checkCircle', text: '批量封装完成' }, '全部完成', '批量封装完成', 'ok'); },
    onError: s => bfin(s, { cls: 'err', icon: 'xCircle', text: '批量封装结束（有失败项）' }, '完成，但有失败项', '批量封装结束（有失败项）', 'err'),
    onKilled: s => bfin(s, { cls: 'info', icon: 'info', text: '批量已停止' }, '已停止', '批量已停止', 'err'),
    onLost: () => bfin(null, { cls: 'err', icon: 'xCircle', text: '连接丢失，请刷新' }, '连接丢失，请刷新', '连接丢失，请刷新', 'err')
  });
};

/* 运行中：仅刷新各行状态徽章（保留 detail 输入，避免丢焦点） */
function refreshRowStatuses() {
  batchItems.forEach((it, i) => {
    const row = document.querySelector('.bq-row[data-i="' + i + '"]');
    if (!row) return;
    const st = batchRowStatus(i);
    const cell = row.querySelector('.bq-status');
    if (!cell) return;
    const stIcon = st.icon === 'loader' ? ic('loader', 'spin') : st.icon === 'check' ? ic('check') : st.icon === 'xCircle' ? ic('xCircle') : st.icon === 'alertTriangle' ? ic('alertTriangle') : '<span class="bq-dot"></span>';
    cell.innerHTML = '<span class="bq-status-badge ' + st.cls + '">' + stIcon + '<span>' + st.text + '</span></span>';
  });
  refreshBatchCount();
}

(function restoreBatchQueue() {
  try {
    const q = JSON.parse(localStorage.getItem('muxui_batch_queue') || 'null');
    if (!q) return;
    (q.items || []).forEach(function (it) {
      if (it && (it.video || it.sc || it.tc)) batchItems.push({ video: it.video || '', sc: it.sc || '', tc: it.tc || '', chapters: it.chapters || '', __open: false });
    });
    if (!batchItems.length) return;
    if (q.b_fonts) $('b_fonts_dir').value = q.b_fonts;         // 兼容旧键名
    if (q.b_fonts_dir) $('b_fonts_dir').value = q.b_fonts_dir;
    if (q.b_out) $('b_out_dir').value = q.b_out;               // 兼容旧键名
    if (q.b_out_dir) $('b_out_dir').value = q.b_out_dir;
    if (q.b_out_name_tmpl) $('b_out_name_tmpl').value = q.b_out_name_tmpl;
    $('b_force').checked = !!q.b_force;
    $('b_backup').checked = q.b_backup !== false;
    $('b_skip').checked = !!q.b_skip;
    if (q.b_sc_default) $('b_sc_default').value = q.b_sc_default;
    if (q.b_tc_default) $('b_tc_default').value = q.b_tc_default;
    $('b_sc_forced').checked = !!q.b_sc_forced;
    $('b_tc_forced').checked = !!q.b_tc_forced;
    renderBatch();
    setStatus('已恢复上次的批量列表（' + batchItems.length + ' 项）与选项，可续跑', 'ok');
  } catch (e) {}
})();
}
