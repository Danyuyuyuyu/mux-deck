/* 单个封装主交互：视频卡/probe/轨道选择/字幕卡(SC·TC)/编码检查/内容体检/字体体检/自动匹配/重置/
 * 输出预览/高级折叠/折叠分区摘要/sticky 状态与时间/提交任务（singleState.job + btnStart + startMuxTask）。 */

/* ===== 底部状态条时间信息（耗时 / 预计剩余；纯展示，不影响任务逻辑） ===== */
let stickyStartTs = 0;   // 本轮任务开始时刻

function stickyTimesRunning(progress) {
  const elapsed = Date.now() - stickyStartTs;
  $('stickyElapsed').textContent = fmtDur(elapsed);
  // 线性外推：按已耗时长度的进度占比推算剩余（progress 为空=阶段未报进度，先显示计算中）
  if (progress != null && progress > 0) $('stickyEta').textContent = fmtDur(elapsed / progress * (100 - progress));
  else $('stickyEta').textContent = '计算中…';
}
function stickyTimesFreeze() { $('stickyEta').textContent = '--:--:--'; }   // 终态：耗时定格，剩余不再显示

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
function wireVideo() {
  const inp = $('video');
  inp.onchange = async function () {
    singleState.lastResult = null;   // 输入变更：清除上次任务结果，恢复静态状态
    hideTaskSummary();
    const v = inp.value.trim();
    const replaced = v && v !== singleState.lastVideo;
    if (replaced) {
      // 视频已更换：旧字幕属于旧视频，清空防止重新匹配时张冠李戴（预设已选时轨道名保留预设值）
      $('sc_sub').value = ''; $('tc_sub').value = '';
      $('sc_enc').textContent = ''; $('tc_enc').textContent = '';
      if (!presetTrackNameLocked()) {
        $('sc_name').value = 'SC'; $('tc_name').value = 'TC';
      }
      subCheckUi.sc = null; subCheckUi.tc = null;   // 换视频后旧体检结果作废
      subCheckSig.sc = ''; subCheckSig.tc = '';
      fontSig = '';
      hidePreflightIssues();
      syncSubStatus();
      setStatus('视频已更换，字幕已清空；正在自动识别字幕与字体目录…', 'run');
    }
    singleState.lastVideo = v;
    trackSel.audio.clear(); trackSel.sub.clear();
    trackSel.allAudio = []; trackSel.allSub = []; trackSel.keepAtt = false;
    $('probeBox').innerHTML = '';
    renderVideoCard();
    if (!v) singleState.probeCache = null;
    else if (!singleState.probeCache || singleState.probeCache.video !== v) autoProbe(v);   // 自动探测媒体信息（紧凑摘要行 + {res} 预览）
    refreshOutPreview();
    if (!replaced) hidePreflightIssues();
    refreshSticky();
    if (replaced) {
      // 新视频自动识别（与批量添加文件同一入口，见 identify.js）；识别期间视频又被更换则丢弃结果
      const id = await identify(v);
      if ($('video').value.trim() !== v) return;   // 读当前 DOM（renderVideoCard 已重建节点，闭包里的 inp 已失效）
      applyIdentify($('sc_sub'), $('tc_sub'), $('fonts_dir'), id, $('chapters'));   // 已有值不覆盖
      autoTrackName('sc_sub', 'sc_name', 'sc');
      autoTrackName('tc_sub', 'tc_name', 'tc');
      syncSubStatus();
      singleState.lastResult = null; refreshSticky();
      const hits = [id.sc && '简体字幕', id.tc && '繁体字幕', id.fontsDir && '字体目录', id.chapters && '章节'].filter(Boolean);
      if (hits.length) setStatus('已自动识别：' + hits.join('、'), 'ok');
      else setStatus('未自动识别到字幕与字体目录，可手动填写或点「自动匹配字幕」重试', 'info');
    }
  };
  inp.onkeydown = function (e) { if (e.key === 'Enter') inp.blur(); };
}

/* ==================== 状态刷新（粘性操作条） ==================== */
function refreshSticky() {
  updateConsoleStatus();
  const note = $('stickyNote'), txt = note.querySelector('.sticky-txt');
  const btn = $('btnStart');
  if (singleState.job) return;
  if (singleState.lastResult) {
    note.className = 'sticky-note ' + singleState.lastResult.cls;
    note.firstElementChild.innerHTML = ic(singleState.lastResult.icon);
    txt.textContent = singleState.lastResult.text;
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
  if (wasFilled !== !!sub) setSubCardOpen(kind, true);
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

/* ==================== 重置单个封装 ==================== */

/* ==================== 自动匹配字幕（单个） ==================== */

/* ==================== 查看轨道 / 视频媒体信息自动探测 ==================== */
function renderVideoTrackInfo() {
  const el = $('videoTrackInfo');
  if (!el) return;
  const d = singleState.probeCache && singleState.probeCache.data;
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
  singleState.probeCache = { video: v, data: d };
  renderVideoTrackInfo();
  refreshOutPreview();   // {res} 高度就绪后输出预览自动补全
  return d;
}
async function autoProbe(v) {
  if (!v) return;
  const el = $('videoTrackInfo');
  if (el) el.innerHTML = ic('loader', 'spin') + '<span> 正在读取媒体信息…</span>';
  try { await fetchProbe(v); } catch (ex) { singleState.probeCache = null; renderVideoTrackInfo(); }
}
const trackSel = { audio: new Set(), sub: new Set(), keepAtt: false, allAudio: [], allSub: [] };
function toggleSel(id, kind) { const set = kind === 'audio' ? trackSel.audio : trackSel.sub; if (set.has(id)) set.delete(id); else set.add(id); }
function toggleAtt(v) { trackSel.keepAtt = v; }

/* ==================== 字幕编码检查 ==================== */

/* ==================== 字幕内容体检（时间轴/CPS/行宽/样式，纯文本分析） ==================== */
const SUBCHECK_TYPE = { overlap: '时间重叠', empty: '空台词', bad_time: '时间错误', bad_style: '坏样式', cps: 'CPS 超速', long_line: '单行过长' };
const subCheckUi = { sc: null, tc: null };   // 各轨体检摘要（写入字幕卡摘要行，结果明细仍在 subCheckBox）
const subCheckSig = { sc: '', tc: '' };      // 体检时的字幕路径：路径变更即结果过期（preflight 降级为 info）
function setSubCheckUi(kind, cls, icon, text) { subCheckUi[kind] = { cls, icon, text }; subCheckSig[kind] = $(kind + '_sub').value.trim(); renderSubCard(kind); }

/* ==================== 字体体检 ==================== */
let fontState = { status: 'idle', missing: 0 };   // 体检状态（字体折叠摘要 + 字幕卡摘要行引用）
let fontSig = '';                                 // 体检时的 [sc, tc, fonts_dir]：任一变更即结果过期
function markFontChecked() { fontSig = JSON.stringify([$('sc_sub').value.trim(), $('tc_sub').value.trim(), $('fonts_dir').value.trim()]); }
function refreshFontSummaryUI() {
  updateFontsSummary();
  ['sc', 'tc'].forEach(renderSubCard);
}
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
  singleState.job = r.job;
  stickyStartTs = Date.now();
  $('stickyProgress').classList.add('run');   // 进度条流动高光
  $('stickyBar').style.width = '0%';
  $('stickyPct').textContent = '--';
  $('stickyElapsed').textContent = '00:00:00';
  $('stickyEta').textContent = '计算中…';
  setRunButton($('btnStart'), true, '停止封装', '开始封装');
  showLogTab('mux'); setLog('mux', '');
  const fin = (s, lastR, statusMsg, statusCls, resultHtml) => {
    singleState.job = null;
    setRunButton($('btnStart'), false, '停止封装', '开始封装');
    $('stickyProgress').classList.remove('run');
    stickyTimesFreeze();   // 耗时定格、剩余清空（进度条与百分比一致定格，下次启动时归零）
    setStatus(statusMsg, statusCls);
    singleState.lastResult = lastR;
    if (resultHtml) $('result').innerHTML = resultHtml;
    refreshSticky();
  };
  startTaskPolling({
    job: singleState.job, interval: 1200,
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
/* 预设已选择时轨道名以预设为准：自动识别/匹配/拖放/手输均不改写轨道名（解除预设即恢复自动识别） */
function presetTrackNameLocked() {
  return !!(presetSession.currentId && PRESETS[presetSession.currentId]);
}
function autoTrackName(subField, nameField, kind) {
  if (presetTrackNameLocked()) return;
  var tok = pickNameToken($(subField).value, kind);
  if (tok) $(nameField).value = tok;
}
/* 手动填字幕（手输 change/input、浏览按钮）后同步粘性操作栏与字幕状态 */
function onManualSub(subField, nameField, kind) {
  singleState.lastResult = null;
  autoTrackName(subField, nameField, kind);
  const row = $(kind + 'FileInputRow');
  if (row && $(subField).value.trim()) row.style.display = 'none';   // 路径已填：回到文件信息展示
  syncSubStatus();
  refreshSticky();
}
/* 字幕选择：摘要区「选择字幕」（主操作）与编辑区「浏览」共用同一入口 */
function browseSub(kind) {
  openBrowser(v => { $(kind + '_sub').value = v; fireChange($(kind + '_sub')); }, 'sub', $(kind + '_sub').value, 'sub');
}
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
/* Header 折叠：点击/键盘均可；Header 内按钮与 ⋯ 菜单不触发折叠 */
/* 文件区展示模式优先；「编辑文件路径」（⋯ 菜单）按需展开输入行 */
function toggleManualPath(kind) {
  const row = $(kind + 'FileInputRow');
  if (!row) return;
  const show = row.style.display === 'none';
  row.style.display = show ? '' : 'none';
  if (show) { setSubCardOpen(kind, true); $(kind + '_sub').focus(); }
}
/* 默认轨三态 segmented control：写回隐藏域（'' 自动 / '1' 是 / '0' 否，语义与原 select 一致） */
function syncSegControls() {
  ['sc', 'tc'].forEach(function (kind) {
    const seg = $(kind + '_default_seg');
    if (!seg) return;
    const val = $(kind + '_default').value;
    seg.querySelectorAll('.seg-btn').forEach(b => b.classList.toggle('active', b.dataset.v === val));
  });
}

/* ==================== 输出预览（服务端 /api/out_preview 复用 mux_cli.resolve_out_name，与实际封装同一套规则） ==================== */
let outPreviewTimer = null;
function scheduleOutPreview() {
  clearTimeout(outPreviewTimer);
  outPreviewTimer = setTimeout(refreshOutPreview, 300);
}
function videoHeight() { return (singleState.probeCache && singleState.probeCache.data && singleState.probeCache.data.video_height) || 0; }
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

/* ==================== 高级选项折叠 ==================== */

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

/* ==================== 模块状态（single 域私有；跨模块一律走下方 getter） ==================== */
const singleState = { job: null, lastResult: null, lastVideo: '', probeCache: null };

/* ---- 跨模块轻量读取接口（console / preflight 使用；不暴露内部可变引用） ---- */
function getSingleTaskStatus() {
  return { running: !!singleState.job, result: singleState.lastResult };
}
function clearSingleResult() { singleState.lastResult = null; }
function fontCheckFresh() {
  return (fontState.status === 'ok' || fontState.status === 'warn' || fontState.status === 'error') &&
    fontSig === JSON.stringify([$('sc_sub').value.trim(), $('tc_sub').value.trim(), $('fonts_dir').value.trim()]);
}
function getSingleValidationState() {
  const video = $('video').value.trim();
  const fresh = kind => !!subCheckUi[kind] && subCheckSig[kind] === $(kind + '_sub').value.trim();
  return {
    video,
    sc: $('sc_sub').value.trim(), tc: $('tc_sub').value.trim(),
    outDir: $('out_dir').value.trim(), template: $('out_name_tmpl').value.trim(), title: $('title').value.trim(),
    height: videoHeight(),
    probe: (singleState.probeCache && singleState.probeCache.video === video) ? singleState.probeCache.data : null,
    enc: { sc: ($('sc_enc').textContent || '').trim(), tc: ($('tc_enc').textContent || '').trim() },
    checks: {
      sc: subCheckUi.sc ? { cls: subCheckUi.sc.cls, text: subCheckUi.sc.text, fresh: fresh('sc') } : null,
      tc: subCheckUi.tc ? { cls: subCheckUi.tc.cls, text: subCheckUi.tc.text, fresh: fresh('tc') } : null,
    },
    fonts: { status: fontState.status, missing: fontState.missing, fresh: fontCheckFresh() },
  };
}

/* ==================== 字幕检查（统一入口：编码 → 内容 → 字体） ====================
 * 三个底层检查各自独立可用（原按钮保留，调用同一函数，零逻辑复制）；
 * runSubtitleCheck 串行编排并汇总，全部复用既有状态（编码徽章 / subCheckUi / fontState），
 * 不建第二套检查状态；单项失败相互隔离，不改变任何 preflight 判定规则。 */
async function runEncodingCheck(quiet) {
  const sc = $('sc_sub').value.trim(), tc = $('tc_sub').value.trim();
  if (!sc && !tc) { if (!quiet) alert('请先填写字幕路径'); return null; }
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
    const badge = k => ($(k + '_enc').textContent || '').trim();
    const bad = ['sc', 'tc'].filter(k => { const x = badge(k); return x && (x.indexOf('错误') === 0 || x.indexOf('歧义') >= 0); }).length;
    return { ok: bad === 0, warn: bad };
  } catch (ex) {
    const msg = '连接失败：' + ex;
    if (sc) $('sc_enc').textContent = msg;
    if (tc) $('tc_enc').textContent = msg;
    syncSubStatus();   // 编码摘要行随错误文案一并刷新
    return { ok: false, warn: 0, err: 1 };
  } finally {
    $('btnPrepSubs').disabled = false;
  }
}
async function runContentCheck(quiet) {
  const subs = [['sc', $('sc_sub').value.trim()], ['tc', $('tc_sub').value.trim()]].filter(x => x[1]);
  if (!subs.length) { if (!quiet) alert('请先填写字幕路径'); return null; }
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
  const warn = ['sc', 'tc'].filter(k => subCheckUi[k] && subCheckUi[k].cls === 'warn').length;
  const err = ['sc', 'tc'].filter(k => subCheckUi[k] && subCheckUi[k].cls === 'err').length;
  return { ok: warn + err === 0, warn, err };
}
async function runFontCheck(quiet) {
  const subs = [$('sc_sub').value.trim(), $('tc_sub').value.trim()].filter(Boolean);
  const fonts_dir = $('fonts_dir').value.trim();
  if (!subs.length) { if (!quiet) alert('请先填写字幕路径'); return null; }
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
  return { ok: fontState.status === 'ok', status: fontState.status, missing: fontState.missing };
}
async function runSubtitleCheck() {
  const btn = $('btnSubtitleCheck');
  const hasSub = $('sc_sub').value.trim() || $('tc_sub').value.trim();
  if (!hasSub) { setStatus('请先选择简/繁字幕，再运行字幕检查', 'err'); return; }
  if (btn.disabled) return;   // 检查进行中，防重复触发
  btn.disabled = true;
  const oldHtml = btn.innerHTML;
  btn.innerHTML = ic('loader', 'spin') + '<span>检查中…</span>';
  let warns = 0;
  try {
    setStatus('字幕检查：正在检查编码…', 'run');
    const enc = await runEncodingCheck(true) || { ok: true, warn: 0 };
    setStatus('字幕检查：正在检查字幕内容…', 'run');
    const content = await runContentCheck(true) || { ok: true, warn: 0, err: 0 };
    setStatus('字幕检查：正在检查字体…', 'run');
    const fonts = await runFontCheck(true) || { ok: true, status: 'idle', missing: 0 };
    // 汇总：复用编码徽章 / subCheckUi / fontState，不建第二套检查状态
    const seg = [];
    ['sc', 'tc'].forEach(kind => {
      if (!$(kind + '_sub').value.trim()) return;
      const cell = [];
      const encTxt = ($(kind + '_enc').textContent || '').trim();
      cell.push('编码' + (encTxt ? (encTxt.indexOf('错误') === 0 ? '✗' : encTxt.indexOf('歧义') >= 0 ? '⚠' : '✓') : '—'));
      const st = subCheckUi[kind];
      cell.push('内容' + (st ? (st.cls === 'on' ? '✓' : st.cls === 'err' ? '✗' : '⚠') : '—'));
      if (fontState.status !== 'idle') cell.push('字体' + (fontState.status === 'ok' ? '✓' : fontState.status === 'warn' ? '⚠' + fontState.missing : '✗'));
      seg.push(kind.toUpperCase() + ' ' + cell.join(' '));
    });
    if (enc && enc.warn) warns += enc.warn;
    if (content) warns += content.warn + content.err;
    if (fonts && fonts.status === 'warn') warns += 1;
    else if (fonts && fonts.status === 'error') warns += 1;
    setStatus('字幕检查完成：' + seg.join(' · ') + (warns ? '（' + warns + ' 项建议处理）' : '（可以封装）'), warns ? 'warn' : 'ok');
  } catch (ex) {
    setStatus('字幕检查失败：' + ex, 'err');
  } finally {
    btn.disabled = false;
    btn.innerHTML = oldHtml;
  }
}

/* ==================== 预设状态条（主流程顶部；状态数据来自 presets.js 的 getCurrentPresetInfo） ====================
 * 只负责呈现：◆ X · 已应用 / ◆ X · 已修改 / 自定义配置，以及 更改/选择预设/解除预设 按钮态。
 * 预设 CRUD 与应用逻辑在 presets.js；本函数不做任何预设数据判断以外的业务。 */
function renderPresetStatus() {
  const box = $('presetStatusBar');
  if (!box) return;
  const info = getCurrentPresetInfo();
  const txt = $('presetStatusText');
  const has = !!info.id;
  txt.textContent = has ? ('◆ ' + info.id + (info.dirty ? ' · 已修改' : ' · 已应用')) : '自定义配置';
  txt.className = 'preset-status-text' + (has && info.dirty ? ' preset-dirty' : '');
  box.classList.toggle('has-preset', has);
  $('btnPresetChange').style.display = has ? '' : 'none';
  $('btnPresetDetach').style.display = has ? '' : 'none';
  $('btnPresetPick').style.display = has ? 'none' : '';
}

/* ==================== 初始化（由 init.js bootstrap 统一调用，仅执行一次） ==================== */
function initSingle() {
$('btnSingleReset').onclick = () => {
  if (singleState.job) { setStatus('封装任务进行中，不能重置', 'err'); return; }
  if (!confirm('确定重置单个封装的全部设置？')) return;
  pickVideoPath(''); // 清视频并联动：轨道选择/探测结果清空、卡片重渲染、粘性条刷新
  $('sc_sub').value = ''; $('tc_sub').value = '';
  $('sc_name').value = 'SC'; $('tc_name').value = 'TC';
  $('sc_enc').textContent = ''; $('tc_enc').textContent = '';
  $('fonts_dir').value = '';
  $('chapters').value = '';
  $('out_name_tmpl').value = '';
  $('title').value = '';
  $('postcmd').value = '';
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
  // 预设已选择：重置 = 先清回默认、再重新套用该预设（回到预设基线），选择器保留
  const pName = $('preset_sel').value;
  if (PRESETS[pName]) {
    applyPreset(PRESETS[pName]);
    updatePresetHint();
    setStatus('已重置并套用预设「' + pName + '」', 'ok');
  } else {
    setStatus('已重置单个封装设置', 'ok');
  }
};
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
    singleState.lastResult = null; refreshSticky();   // 字幕已填充：同步底部操作栏状态
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
$('btnPrepSubs').onclick = () => runEncodingCheck();
$('btnSubCheck').onclick = () => runContentCheck();
$('btnCheckFonts').onclick = () => runFontCheck();
$('btnSubtitleCheck').onclick = () => runSubtitleCheck();   // 统一入口：编码→内容→字体
$('btnPresetChange').onclick = () => openPresetManager();   // 更改：管理器默认选中当前任务预设
$('btnPresetPick').onclick = () => openPresetManager();     // 选择预设：自定义配置态入口
$('btnPresetDetach').onclick = () => detachCurrentPreset(); // 解除预设：参数保留，转为自定义配置
renderPresetStatus();   // 初始呈现（无预设 = 自定义配置）
$('btnStart').onclick = async () => {
  if (singleState.job) {
    setStatus('正在停止…', 'run');
    await api('/api/stop', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ id: singleState.job }) });
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
$('sc_sub').addEventListener('change', function () { onManualSub('sc_sub', 'sc_name', 'sc'); });
$('tc_sub').addEventListener('change', function () { onManualSub('tc_sub', 'tc_name', 'tc'); });
$('sc_sub').addEventListener('input', function () { singleState.lastResult = null; syncSubStatus(); refreshSticky(); });
$('tc_sub').addEventListener('input', function () { singleState.lastResult = null; syncSubStatus(); refreshSticky(); });
$('sc_name').addEventListener('input', function () { });
$('btnSc').onclick = () => browseSub('sc');
$('btnTc').onclick = () => browseSub('tc');
$('btnScPick').onclick = () => browseSub('sc');
$('btnTcPick').onclick = () => browseSub('tc');
$('btnScClear').onclick = () => { toggleMoreMenu('sc', false); clearSub('sc'); };
$('btnTcClear').onclick = () => { toggleMoreMenu('tc', false); clearSub('tc'); };
['sc', 'tc'].forEach(function (kind) {
  const head = $(kind + 'Card').querySelector('.sub-head');
  head.addEventListener('click', function (e) { if (e.target.closest('button') || e.target.closest('.more-wrap')) return; toggleSubCard(kind); });
  head.addEventListener('keydown', function (e) {
    if ((e.key === 'Enter' || e.key === ' ') && !e.target.closest('button') && !e.target.closest('.more-wrap')) { e.preventDefault(); toggleSubCard(kind); }
  });
});
$('btnScManual').onclick = () => { toggleMoreMenu('sc', false); toggleManualPath('sc'); };
$('btnTcManual').onclick = () => { toggleMoreMenu('tc', false); toggleManualPath('tc'); };
$('btnScMore').onclick = function (e) { e.stopPropagation(); toggleMoreMenu('sc'); };
$('btnTcMore').onclick = function (e) { e.stopPropagation(); toggleMoreMenu('tc'); };
document.addEventListener('click', function (e) {
  if (!e.target.closest('.more-wrap')) { toggleMoreMenu('sc', false); toggleMoreMenu('tc', false); }
});
document.querySelectorAll('.seg').forEach(function (seg) {
  seg.addEventListener('click', function (e) {
    const b = e.target.closest('.seg-btn');
    if (!b) return;
    const kind = seg.id === 'sc_default_seg' ? 'sc' : 'tc';
    $(kind + '_default').value = b.dataset.v;
    syncSegControls();
    syncDefaultBadge();   // 摘要徽章即时反映
    updatePresetHint();
  });
});
$('sc_forced').addEventListener('change', syncDefaultBadge);
$('tc_forced').addEventListener('change', syncDefaultBadge);
$('btnFonts').onclick = () => openBrowser(v => { $('fonts_dir').value = v; updateFontsSummary(); }, 'dir', $('fonts_dir').value, 'fonts');
$('btnAudio').onclick = () => openBrowser(v => { $('audio').value = v; updateAudioSummary(); }, 'audio', $('audio').value, 'audio');
$('btnOut').onclick = () => openBrowser(v => { $('out_dir').value = v; scheduleOutPreview(); }, 'dir', $('out_dir').value, 'out');
$('btnChapters').onclick = () => openBrowser(v => $('chapters').value = v, 'any', $('chapters').value, 'chapters');
$('out_dir').addEventListener('input', scheduleOutPreview);
$('out_dir').addEventListener('change', scheduleOutPreview);
$('out_name_tmpl').addEventListener('input', scheduleOutPreview);
$('out_name_tmpl').addEventListener('change', scheduleOutPreview);
$('title').addEventListener('input', scheduleOutPreview);   // {title} 占位符：标题变化实时反映到输出预览
(function () {
  const toggle = $('advToggle'), body = $('advBody');
  toggle.onclick = function () {
    const open = body.classList.toggle('show');
    toggle.classList.toggle('open', open);
    toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
  };
})();
$('fonts_dir').addEventListener('input', updateFontsSummary);
$('fonts_dir').addEventListener('change', updateFontsSummary);
$('audio').addEventListener('input', updateAudioSummary);
$('audio').addEventListener('change', updateAudioSummary);
}
