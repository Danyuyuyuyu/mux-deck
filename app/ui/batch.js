/* ==================== 批量封装 ==================== */
const batchItems = [];  // {video, sc, tc, chapters}
/* 队列持久化：批量列表与公共选项存 localStorage（断点续跑：页面刷新/服务重启后自动恢复） */
function saveBatchQueue() {
  try {
    localStorage.setItem('muxui_batch_queue', JSON.stringify({
      items: batchItems,
      b_fonts: $('b_fonts').value, b_out: $('b_out').value, b_out_name_tmpl: $('b_out_name_tmpl').value,
      b_force: $('b_force').checked, b_backup: $('b_backup').checked, b_skip: $('b_skip').checked,
      b_sc_default: $('b_sc_default').value, b_tc_default: $('b_tc_default').value,
      b_sc_forced: $('b_sc_forced').checked, b_tc_forced: $('b_tc_forced').checked,
    }));
  } catch (e) {}
}
function renderBatch() {
  lastBatchResult = null;   // 列表变更（增/删/清空）：清除上次批量结果
  const list = $('batchList'); list.innerHTML = '';
  batchItems.forEach((it, i) => {
    const div = document.createElement('div'); div.className = 'b-item';
    div.innerHTML =
      '<div class="b-head">' +
        '<span class="b-idx">' + (i + 1) + '</span>' +
        '<input id="b_v_' + i + '" type="text" value="' + esc(it.video) + '" placeholder="视频路径（填完自动匹配字幕）" autocomplete="off">' +
        '<span class="b-btns">' +
          '<button type="button" class="btn small" onclick="batchBrowse(' + i + ',\'video\')">' + ic('folderOpen') + '浏览</button>' +
          '<button type="button" class="btn small ghost" onclick="batchDel(' + i + ')">' + ic('trash') + '移除</button>' +
        '</span>' +
      '</div>' +
      '<div class="b-grid">' +
        '<span class="sub-badge sc">SC</span>' +
        '<div class="b-sub"><input id="b_s_' + i + '" type="text" value="' + esc(it.sc) + '" placeholder="可选" autocomplete="off">' +
          '<button type="button" class="btn icon-btn" title="浏览简体字幕" aria-label="浏览简体字幕" onclick="batchBrowse(' + i + ',\'sc\')">' + ic('folderOpen') + '</button>' +
          '<button type="button" class="btn icon-btn ghost" title="移除简体字幕" aria-label="移除简体字幕" onclick="batchDelSub(' + i + ',\'sc\')">' + ic('trash') + '</button>' +
        '</div>' +
        '<span class="sub-badge tc">TC</span>' +
        '<div class="b-sub"><input id="b_t_' + i + '" type="text" value="' + esc(it.tc) + '" placeholder="可选" autocomplete="off">' +
          '<button type="button" class="btn icon-btn" title="浏览繁体字幕" aria-label="浏览繁体字幕" onclick="batchBrowse(' + i + ',\'tc\')">' + ic('folderOpen') + '</button>' +
          '<button type="button" class="btn icon-btn ghost" title="移除繁体字幕" aria-label="移除繁体字幕" onclick="batchDelSub(' + i + ',\'tc\')">' + ic('trash') + '</button>' +
        '</div>' +
        '<span class="sub-badge ch">章</span>' +
        '<div class="b-sub"><input id="b_c_' + i + '" type="text" value="' + esc(it.chapters || '') + '" placeholder="章节文件（自动匹配视频旁同名 txt/xml，可留空）" autocomplete="off">' +
          '<button type="button" class="btn icon-btn" title="浏览章节文件" aria-label="浏览章节文件" onclick="batchBrowse(' + i + ',\'chapters\')">' + ic('folderOpen') + '</button>' +
          '<button type="button" class="btn icon-btn ghost" title="移除章节" aria-label="移除章节" onclick="batchDelSub(' + i + ',\'chapters\')">' + ic('trash') + '</button>' +
        '</div>' +
      '</div>';
    list.appendChild(div);
    var vin = $('b_v_' + i);
    vin.addEventListener('change', async function () {
      lastBatchResult = null;
      var v = vin.value.trim();
      batchItems[i].video = v;
      if (!v) { saveBatchQueue(); return; }
      var m = await identify(v);
      if (m.sc && !$('b_s_' + i).value) $('b_s_' + i).value = m.sc;
      if (m.tc && !$('b_t_' + i).value) $('b_t_' + i).value = m.tc;
      if (m.chapters && !$('b_c_' + i).value) $('b_c_' + i).value = m.chapters;
      batchItems[i].sc = $('b_s_' + i).value; batchItems[i].tc = $('b_t_' + i).value;
      batchItems[i].chapters = $('b_c_' + i).value;
      saveBatchQueue();
      refreshBatchSticky();
    });
  });
  refreshBatchSticky();
  saveBatchQueue();
}
function batchBrowse(i, kind) {
  const id = kind === 'video' ? 'b_v_' + i : kind === 'sc' ? 'b_s_' + i : kind === 'tc' ? 'b_t_' + i : 'b_c_' + i;
  openBrowser(v => { $(id).value = v; if (kind === 'video') { batchItems[i].video = v; fireChange($('b_v_' + i)); } else { batchItems[i][kind] = v; saveBatchQueue(); } },
    kind === 'video' ? 'video' : kind === 'chapters' ? 'any' : 'sub', $(id).value, kind === 'video' ? 'video' : kind);
}
function batchDel(i) { batchItems.splice(i, 1); renderBatch(); }   // renderBatch 内含 saveBatchQueue
/* 移除行内字幕/章节（输入框与数据同步清空，sticky 即时刷新） */
function batchDelSub(i, kind) {
  const id = kind === 'video' ? 'b_v_' + i : kind === 'sc' ? 'b_s_' + i : kind === 'tc' ? 'b_t_' + i : 'b_c_' + i;
  const inp = $(id);
  if (!inp) return;
  inp.value = '';
  batchItems[i][kind] = '';
  lastBatchResult = null;
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
    if (fd && !$('b_fonts').value.trim()) $('b_fonts').value = fd;   // 字体目录：取首个识别到的
    renderBatch();
    setStatus(added
      ? ('已添加 ' + added + ' 个视频' + (added < vids.length ? '（跳过 ' + (vids.length - added) + ' 个重复）' : ''))
      : '所选视频都已在列表中', 'ok');
  } catch (ex) {
    setStatus('添加失败：' + ex, 'err');
  }
}
const _lastBv = $('b_v_' + Math.max(0, batchItems.length - 1));


function addBatchVideo(video, subPool, matched) {
  const dir = video.slice(0, video.lastIndexOf('\\') + 1);
  const stem = video.slice(video.lastIndexOf('\\') + 1).replace(/\.[^.]+$/, '');
  let sc = '', tc = '';
  const stems = [stem, stem.replace(/\s*[-_ ]\d+\s*$/, '')];
  const scSfx = ['.sc.ass', '.chs.ass', '.jpsc.ass', '.SC.ass', '.CHS.ass', '.JPSC.ass'];
  const tcSfx = ['.tc.ass', '.cht.ass', '.jptc.ass', '.TC.ass', '.CHT.ass', '.JPTC.ass'];
  for (const st of stems) {
    for (const sfx of scSfx) { if (!sc && subPool.some(p => p.toLowerCase() === (dir + st + sfx).toLowerCase())) sc = dir + st + sfx; }
    for (const sfx of tcSfx) { if (!tc && subPool.some(p => p.toLowerCase() === (dir + st + sfx).toLowerCase())) tc = dir + st + sfx; }
  }
  if (!sc && matched && matched.sc) sc = matched.sc;
  if (!tc && matched && matched.tc) tc = matched.tc;
  batchItems.push({video, sc, tc, chapters: (matched && matched.chapters) || ''});
}
let bJob = null;
/* 失败单集重跑：用列表中对应项（含已匹配字幕）重组为单项队列并直接开始 */
function rerunFailed(i) {
  if (bJob) { setStatus('批量任务进行中，不能重跑', 'err'); return; }
  const it = batchItems[i];
  if (!it || !it.video) { alert('找不到对应列表项（列表可能已被修改）'); return; }
  batchItems.splice(0, batchItems.length);
  batchItems.push({ video: it.video, sc: it.sc || '', tc: it.tc || '', chapters: it.chapters || '' });
  renderBatch();
  setStatus('已重组为单项队列并开始重跑：' + it.video.split(/[\\/]/).pop(), 'ok');
  $('btnBatchStart').click();
}
/* 预设套用 → 批量公共字段联动（由 presets.js applyPreset 调用；映射集中在此，batch 域自持） */
function applyPresetToBatchCommon(d) {
  const bm = { fonts_mode: 'b_fonts_mode', out_name_tmpl: 'b_out_name_tmpl', title: 'b_title',
               sc_default: 'b_sc_default', tc_default: 'b_tc_default', sc_forced: 'b_sc_forced', tc_forced: 'b_tc_forced' };
  Object.keys(bm).forEach(k => { if (d[k] !== undefined && d[k] !== '' && $(bm[k])) { if (bm[k].endsWith('_forced')) $(bm[k]).checked = !!d[k]; else $(bm[k]).value = d[k]; } });
  if (d.fonts_dir && $('b_fonts')) $('b_fonts').value = d.fonts_dir;
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

/* ==================== 初始化（由 init.js bootstrap 统一调用，仅执行一次） ==================== */
function initBatch() {
$('btnBatchAdd').onclick = () => { batchItems.push({video:'', sc:'', tc:''}); renderBatch(); };
$('btnBatchFiles').onclick = () => openBrowser(async v => {
  if (!v) return;
  if (!BATCH_VIDEO_RE.test(v)) { setStatus('请选择视频文件（MKV/MP4/M2TS 等）', 'err'); return; }
  if (batchItems.some(it => it.video && it.video.toLowerCase() === v.toLowerCase())) { setStatus('该视频已在列表中：' + v, 'err'); return; }
  setStatus('正在识别字幕与字体目录…', 'run');
  try {
    const id = await identify(v);   // 统一识别：字幕 + 字体目录
    addBatchVideo(v, [], id);   // addBatchVideo 内置 matched.sc/tc 填充
    if (id.fontsDir && !$('b_fonts').value.trim()) $('b_fonts').value = id.fontsDir;
    renderBatch();
    setStatus('已添加：' + v.split(/[\\/]/).pop() + (id.sc || id.tc ? ' · 已自动匹配字幕' : ''), 'ok');
  } catch (ex) {
    setStatus('添加失败：' + ex, 'err');
  }
}, 'video', _lastBv ? _lastBv.value : '', 'batch', addVideosFromDir);
$('btnBatchClear').onclick = () => {
  if (bJob) { setStatus('批量任务进行中，不能重置', 'err'); return; }
  const dirty = batchItems.length || $('batchResults').innerHTML || $('b_fonts').value.trim() || $('b_out').value.trim() ||
    $('b_force').checked || !$('b_backup').checked || $('b_skip').checked || $('b_sc_default').value || $('b_tc_default').value ||
    $('b_sc_forced').checked || $('b_tc_forced').checked;
  if (!dirty) return;
  if (!confirm('确定重置批量封装的全部设置？\n将清空批量列表、结果展示与字体目录/输出目录等选项（已生成的输出文件保留在磁盘，不会被删除）')) return;
  batchItems.splice(0, batchItems.length);
  renderBatch();
  $('batchResults').innerHTML = '';
  $('batchState').textContent = '';
  $('b_fonts').value = '';       // 字体目录（自动识别项，下次批量自动重填）
  $('b_out').value = '';         // 输出目录
  $('b_force').checked = false;  // 强制封装
  $('b_backup').checked = true;  // 备份原件（默认勾选）
  $('b_skip').checked = false;   // 跳过已存在输出
  $('b_sc_default').value = ''; $('b_tc_default').value = '';   // 字幕旗标
  $('b_sc_forced').checked = false; $('b_tc_forced').checked = false;
  // 底部批量状态条一并复位（与启动时初态一致）
  $('bStickyPct').textContent = '--';
  $('batchStickyBar').style.width = '0%';
  $('bStickyCountNum').textContent = '0 / 0';
  $('bStickyElapsed').textContent = '--:--:--';
  $('bStickyEta').textContent = '--:--:--';
  $('bStickyCur').style.display = 'none';
  try { localStorage.removeItem('muxui_batch_queue'); } catch (e) {}
  refreshBatchSticky();
  setStatus('已重置批量封装设置（输出文件保留在磁盘）', 'ok');
};
$('btnMatchAll').onclick = async () => {
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
    const fontFound = !!(firstId && firstId.fontsDir && !$('b_fonts').value.trim() && ($('b_fonts').value = firstId.fontsDir, true));   // 自动匹配字体目录
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
$('btnBatchStart').onclick = async () => {
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
  setRunButton($('btnBatchStart'), true, '停止批量', '开始批量封装');
  showLogTab('batch'); setLog('batch', '');
  const bfin = (s, lastR, stateText, statusMsg, statusCls) => {
    bJob = null;
    setRunButton($('btnBatchStart'), false, '停止批量', '开始批量封装');
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
(function restoreBatchQueue() {
  try {
    const q = JSON.parse(localStorage.getItem('muxui_batch_queue') || 'null');
    if (!q) return;
    (q.items || []).forEach(function (it) {
      if (it && (it.video || it.sc || it.tc)) batchItems.push({ video: it.video || '', sc: it.sc || '', tc: it.tc || '', chapters: it.chapters || '' });
    });
    if (!batchItems.length) return;
    if (q.b_fonts) $('b_fonts').value = q.b_fonts;
    if (q.b_out) $('b_out').value = q.b_out;
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
$('btnBFonts').onclick = () => openBrowser(v => $('b_fonts').value = v, 'dir', $('b_fonts').value, 'fonts');
$('btnBOut').onclick = () => openBrowser(v => $('b_out').value = v, 'dir', $('b_out').value, 'out');
}
