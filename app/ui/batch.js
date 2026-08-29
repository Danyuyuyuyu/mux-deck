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
        '<span class="sub-badge sc">SC</span>' + '<input id="b_s_' + i + '" type="text" value="' + esc(it.sc) + '" placeholder="可选" autocomplete="off">' +
        '<span class="sub-badge tc">TC</span>' + '<input id="b_t_' + i + '" type="text" value="' + esc(it.tc) + '" placeholder="可选" autocomplete="off">' +
      '</div>';
    list.appendChild(div);
    var vin = $('b_v_' + i);
    vin.addEventListener('change', async function () {
      lastBatchResult = null;
      var v = vin.value.trim();
      batchItems[i].video = v;
      if (!v) return;
      var m = await api('/api/match_subs?path=' + encodeURIComponent(v));
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
$('btnBatchAdd').onclick = () => { batchItems.push({video:'', sc:'', tc:''}); renderBatch(); };
$('btnBatchClear').onclick = () => {
  if (bJob) { setStatus('批量任务进行中，不能清除列表', 'err'); return; }
  if (!batchItems.length) return;
  if (!confirm('确定清空批量列表（共 ' + batchItems.length + ' 项）？')) return;
  batchItems.splice(0, batchItems.length);
  renderBatch();
  refreshBatchSticky();
  setStatus('批量列表已清空', 'ok');
};

$('btnMatchAll').onclick = async () => {
  if (!batchItems.length) { alert('批量列表为空'); return; }
  setStatus('正在按集数匹配字幕…', 'run');
  try {
    let hit = 0, miss = 0, noVideo = 0, hitSc = 0, hitTc = 0;
    await Promise.all(batchItems.map(async function (it) {
      if (!it.video) { noVideo++; return; }
      const m = await api('/api/match_subs?path=' + encodeURIComponent(it.video));
      if (m.sc) hitSc++;
      if (m.tc) hitTc++;
      const matched = !!(m.sc || m.tc);
      if (m.sc && !it.sc) it.sc = m.sc;
      if (m.tc && !it.tc) it.tc = m.tc;
      if (matched) hit++; else miss++;
    }));
    if (hit === 0) {
      setStatus(miss === 0 ? '没有可匹配的项（视频路径均为空）' : '按集数未匹配到任何字幕（0/' + (hit + miss) + ' 命中）', 'err');
    } else {
      setStatus('批量字幕匹配完成：命中 ' + hit + ' 项（简 ' + hitSc + ' / 繁 ' + hitTc + '）' + (miss ? '，未命中 ' + miss + ' 项' : '') + (noVideo ? '，' + noVideo + ' 项无视频路径' : ''), 'ok');
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
    const m = await api('/api/match_subs?path=' + encodeURIComponent(it.video));
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
  const body = { items, fonts_dir: $('b_fonts').value.trim(),
                 out_dir: $('b_out').value.trim(), force: $('b_force').checked, no_backup: !$('b_backup').checked,
                 sc_default: $('b_sc_default').value, tc_default: $('b_tc_default').value,
                 sc_forced: $('b_sc_forced').checked, tc_forced: $('b_tc_forced').checked };
  const r = await api('/api/batch', { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify(body) });
  if (r.error) { $('batchState').textContent = '错误：' + r.error; return; }
  bJob = r.job; $('btnBatchStart').disabled = false; $('btnBatchStart').innerHTML = ic('square') + '<span>停止批量</span>'; $('btnBatchStart').classList.add('danger'); $('btnBatchStart').classList.remove('primary');
  showLogTab('batch'); setLog('batch', '');
  $('batchBar').style.width = '0%';
  bPoll();
};
let bJob = null, bTimer = null;
async function bPoll() {
  let failCount = 0;
  bTimer = setInterval(async () => {
    try {
      const s = await api('/api/job?id=' + bJob);
      failCount = 0;
      if (s.total) $('batchBar').style.width = Math.round(s.current / s.total * 100) + '%';
      $('batchState').textContent = s.total ? ('第 ' + s.current + ' / ' + s.total + ' 个：' + (s.current_video || '') + (s.progress != null ? ('  当前项 ' + s.progress + '%') : '')) : '';
      const bnote = $('batchStickyNote');
      bnote.className = 'sticky-note run';
      bnote.firstElementChild.innerHTML = ic('loader', 'spin');
      const bl = s.log || '';
      let bstage;
      if (s.progress != null) bstage = '（' + s.progress + '%）';
      else if (/Subset tool|subsetting|assfonts|AFS:/i.test(bl)) bstage = '（子集化中）';
      else bstage = '';
      bnote.querySelector('.sticky-txt').textContent = s.total ? ('批量封装中：第 ' + s.current + ' / ' + s.total + ' 个' + bstage) : '批量封装中…';
      setLog('batch', s.log);
      const resBox = $('batchResults');
      if (s.results && s.results.length) {
        resBox.innerHTML = '<div class="table-wrap" style="margin-top:8px;"><table style="min-width:560px;"><tr><th>#</th><th>输出文件</th><th>结果</th><th style="width:80px"></th></tr>' + s.results.map((r, i) =>
          '<tr><td>' + (i + 1) + '</td><td class="mono" style="word-break:break-all">' + esc(r.output || r.video) + '</td><td>' + (r.ok ? '<span class="chip sm ok">' + ic('check') + '成功</span>' : '<span class="chip sm err">' + ic('xCircle') + '失败' + (r.reason ? '：' + esc(r.reason) : ' (exit ' + r.exit + ')') + '</span>') + '</td><td><button class="btn small" data-open-dir="' + encodeURIComponent(r.output || r.video) + '">打开</button></td></tr>').join('') + '</table></div>';
      }
      if (s.status === 'done' || s.status === 'error' || s.status === 'killed') {
        clearInterval(bTimer); bTimer = null; bJob = null;
        $('btnBatchStart').disabled = false; $('btnBatchStart').innerHTML = ic('play') + '<span>开始批量封装</span>'; $('btnBatchStart').classList.add('primary'); $('btnBatchStart').classList.remove('danger');
        $('batchState').textContent = s.status === 'done' ? '全部完成' : (s.status === 'killed' ? '已停止' : '完成，但有失败项');
        $('batchProgress').style.display = 'none';
        $('batchStickyBarWrap').style.display = 'none';
        setStatus(s.status === 'done' ? '批量封装完成' : (s.status === 'killed' ? '批量已停止' : '批量封装结束（有失败项）'), s.status === 'done' ? 'ok' : 'err');
        lastBatchResult = s.status === 'done'
          ? { cls: 'ok', icon: 'checkCircle', text: '批量封装完成 · 全部成功' }
          : s.status === 'killed'
            ? { cls: 'info', icon: 'info', text: '批量已停止' }
            : { cls: 'err', icon: 'xCircle', text: '批量封装结束（有失败项）' };
        if (s.status === 'done') beep();
        refreshBatchSticky();
      }
    } catch (ex) {
      failCount++;
      if (failCount >= 5) {
        clearInterval(bTimer); bTimer = null; bJob = null;
        $('btnBatchStart').disabled = false; $('btnBatchStart').innerHTML = ic('play') + '<span>开始批量封装</span>'; $('btnBatchStart').classList.add('primary'); $('btnBatchStart').classList.remove('danger');
        $('batchState').textContent = '连接丢失，请刷新';
        $('batchProgress').style.display = 'none';
        $('batchStickyBarWrap').style.display = 'none';
        setStatus('连接丢失，请刷新', 'err');
        lastBatchResult = { cls: 'err', icon: 'xCircle', text: '连接丢失，请刷新' };
        refreshBatchSticky();
      }
    }
  }, 1200);
}
$('btnBFonts').onclick = () => openBrowser(v => $('b_fonts').value = v, 'dir', $('b_fonts').value, 'fonts');
$('btnBOut').onclick = () => openBrowser(v => $('b_out').value = v, 'dir', $('b_out').value, 'out');
