/* 应用级 glue（跨 feature 协调，勿再塞业务）：
 * 1) 批量 sticky 刷新（lastBatchResult/refreshBatchSticky，被 switchMode 与 batch 调用）
 * 2) 顶部状态镜像（MutationObserver → sticky note）
 * 3) 拖放识别分发（单视频→单个封装，多视频→批量）
 * 应用级初始化在 init.js；DOM 等待与脚本注入在 loader.js。 */

let lastBatchResult = null;   // 最近一次批量任务结果，列表变更后清除
function getBatchTaskStatus() { return { running: !!bJob, result: lastBatchResult }; }
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
    const ready = batchItems.filter(function (it) { return (it.video || '').trim() && it.sc && it.tc; }).length;
    const needs = filled - ready;
    note.className = needs ? 'sticky-note warn' : 'sticky-note ok';
    note.firstElementChild.innerHTML = ic(needs ? 'alertTriangle' : 'checkCircle');
    txt.textContent = '已准备 ' + filled + ' 个任务' + (needs ? ' · 需处理 ' + needs + ' 个' : '');
    btn.disabled = false;
  }
}

/* 运行中：把顶部状态镜像到粘性操作条 + 同步进度条 */

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

/* ==================== 初始化（由 init.js bootstrap 统一调用，仅执行一次） ==================== */
function initAppGlue() {
(function () {
  new MutationObserver(function () {
    const s = $('status');
    updateConsoleStatus();   // 控制台折叠条状态与顶部状态联动（含运行中实时文案）
    const t = s.textContent.trim();
    const stSingle = getSingleTaskStatus(), stBatch = getBatchTaskStatus();
    if ((stSingle.running || stBatch.running) && t && t.indexOf('服务已就绪') !== 0 && t.indexOf('连接中') !== 0) {
      const note = stSingle.running ? $('stickyNote') : $('batchStickyNote');
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
window.addEventListener('dragenter', e => { e.preventDefault(); dragDepth++; $('dropOverlay').style.display = 'block'; });
window.addEventListener('dragover', e => e.preventDefault());
window.addEventListener('dragleave', e => { e.preventDefault(); dragDepth--; if (dragDepth <= 0) { dragDepth = 0; $('dropOverlay').style.display = 'none'; } });
window.addEventListener('drop', async e => {
  e.preventDefault(); dragDepth = 0; $('dropOverlay').style.display = 'none';
  if (isModalOpen('browserModal')) { setStatus('文件浏览器已打开，请先关闭再拖放', 'err'); return; }
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
    clearSingleResult(); refreshSticky();
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
    clearSingleResult(); refreshSticky();
    setStatus('已填充字幕' + (plainSub ? ' · 无简/繁标识的字幕已按简体处理' : ''), 'ok');
  }
});
}
