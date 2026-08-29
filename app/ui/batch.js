/* ==================== 批量封装 ==================== */
const batchItems = [];  // {video, sc, tc}
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
      '</div>';
    list.appendChild(div);
    var vin = $('b_v_' + i);
    vin.addEventListener('change', async function () {
      lastBatchResult = null;
      var v = vin.value.trim();
      batchItems[i].video = v;
      if (!v) return;
      var m = await identify(v);
      if (m.sc && !$('b_s_' + i).value) $('b_s_' + i).value = m.sc;
      if (m.tc && !$('b_t_' + i).value) $('b_t_' + i).value = m.tc;
      batchItems[i].sc = $('b_s_' + i).value; batchItems[i].tc = $('b_t_' + i).value;
      refreshBatchSticky();
    });
  });
  refreshBatchSticky();
}
function batchBrowse(i, kind) {
  const id = kind === 'video' ? 'b_v_' + i : kind === 'sc' ? 'b_s_' + i : 'b_t_' + i;
  openBrowser(v => { $('b_' + kind.charAt(0) + '_' + i).value = v; if (kind === 'video') { batchItems[i].video = v; fireChange($('b_v_' + i)); } }, kind === 'video' ? 'video' : 'sub', $('b_' + kind.charAt(0) + '_' + i).value, kind === 'video' ? 'video' : 'sub');
}
function batchDel(i) { batchItems.splice(i, 1); renderBatch(); }
/* 移除行内字幕（输入框与数据同步清空，sticky 即时刷新） */
function batchDelSub(i, kind) {
  const inp = $('b_' + (kind === 'sc' ? 's' : 't') + '_' + i);
  if (!inp) return;
  inp.value = '';
  batchItems[i][kind] = '';
  lastBatchResult = null;
  refreshBatchSticky();
}
$('btnBatchAdd').onclick = () => { batchItems.push({video:'', sc:'', tc:''}); renderBatch(); };

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
    $('b_force').checked || !$('b_backup').checked || $('b_sc_default').value || $('b_tc_default').value ||
    $('b_sc_forced').checked || $('b_tc_forced').checked;
  if (!dirty) return;
  if (!confirm('确定重置批量封装的全部设置？\n将清空批量列表、结果展示与字体目录/输出目录等选项（已生成的输出文件保留在磁盘，不会被删除）')) return;
  batchItems.splice(0, batchItems.length);
  renderBatch();
  $('batchResults').innerHTML = '';
  $('batchState').textContent = '';
  $('batchBar').style.width = '0%';
  $('batchProgress').style.display = '';
  $('b_fonts').value = '';       // 字体目录（自动识别项，下次批量自动重填）
  $('b_out').value = '';         // 输出目录
  $('b_force').checked = false;  // 强制封装
  $('b_backup').checked = true;  // 备份原件（默认勾选）
  $('b_sc_default').value = ''; $('b_tc_default').value = '';   // 字幕旗标
  $('b_sc_forced').checked = false; $('b_tc_forced').checked = false;
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
      const id = await identify(it.video);   // 统一识别：字幕 + 字体目录
      if (!firstId) firstId = id;
      if (id.sc) hitSc++;
      if (id.tc) hitTc++;
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
  batchItems.push({video, sc, tc});
}
$('btnBatchStart').onclick = async () => {
  if (bJob) {
    setStatus('正在停止…', 'run');
    await api('/api/stop', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ id: bJob }) });
    return;
  }
  batchItems.forEach((it, i) => { it.video = $('b_v_' + i).value.trim(); it.sc = $('b_s_' + i).value.trim(); it.tc = $('b_t_' + i).value.trim(); });
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
  setRunButton($('btnBatchStart'), true, '停止批量', '开始批量封装');
  showLogTab('batch'); setLog('batch', '');
  $('batchBar').style.width = '0%';
  const bfin = (s, lastR, stateText, statusMsg, statusCls) => {
    bJob = null;
    setRunButton($('btnBatchStart'), false, '停止批量', '开始批量封装');
    $('batchState').textContent = stateText;
    $('batchProgress').style.display = 'none';
    $('batchStickyBarWrap').style.display = 'none';
    setStatus(statusMsg, statusCls);
    lastBatchResult = lastR;
    refreshBatchSticky();
  };
  startTaskPolling({
    job, interval: 1200,
    onAny: s => {
      if (s.total) $('batchBar').style.width = Math.round(s.current / s.total * 100) + '%';
      $('batchState').textContent = s.total ? ('第 ' + s.current + ' / ' + s.total + ' 个：' + (s.current_video || '') + (s.progress != null ? ('  当前项 ' + s.progress + '%') : '')) : '';
      setLog('batch', s.log);
      const resBox = $('batchResults');
      if (s.results && s.results.length) {
        resBox.innerHTML = '<div class="table-wrap" style="margin-top:8px;"><table style="min-width:560px;"><tr><th>#</th><th>输出文件</th><th>结果</th><th style="width:80px"></th></tr>' + s.results.map((r, i) =>
          '<tr><td>' + (i + 1) + '</td><td class="mono" style="word-break:break-all">' + esc(r.output || r.video) + '</td><td>' + (r.ok ? '<span class="chip sm ok">' + ic('check') + '成功</span>' : '<span class="chip sm err">' + ic('xCircle') + '失败' + (r.reason ? '：' + esc(r.reason) : ' (exit ' + r.exit + ')') + '</span>') + '</td><td><button class="btn small" data-open-dir="' + encodeURIComponent(r.output || r.video) + '">打开</button></td></tr>').join('') + '</table></div>';
      }
    },
    onTick: s => {
      const st = muxStage(s);
      const bstage = st.progress != null ? ('（' + st.progress + '%）') : st.subsetting ? '（子集化中）' : '';
      setStickyRun($('batchStickyNote'), s.total ? ('批量封装中：第 ' + s.current + ' / ' + s.total + ' 个' + bstage) : '批量封装中…');
    },
    onDone: s => { beep(); bfin(s, { cls: 'ok', icon: 'checkCircle', text: '批量封装完成 · 全部成功' }, '全部完成', '批量封装完成', 'ok'); },
    onError: s => bfin(s, { cls: 'err', icon: 'xCircle', text: '批量封装结束（有失败项）' }, '完成，但有失败项', '批量封装结束（有失败项）', 'err'),
    onKilled: s => bfin(s, { cls: 'info', icon: 'info', text: '批量已停止' }, '已停止', '批量已停止', 'err'),
    onLost: () => bfin(null, { cls: 'err', icon: 'xCircle', text: '连接丢失，请刷新' }, '连接丢失，请刷新', '连接丢失，请刷新', 'err')
  });
};
let bJob = null;
$('btnBFonts').onclick = () => openBrowser(v => $('b_fonts').value = v, 'dir', $('b_fonts').value, 'fonts');
$('btnBOut').onclick = () => openBrowser(v => $('b_out').value = v, 'dir', $('b_out').value, 'out');
