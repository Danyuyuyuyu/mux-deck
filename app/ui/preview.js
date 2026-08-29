/* ==================== 字幕预览 ==================== */
$('btnPreview').onclick = async () => {
  const video = $('pv_video').value.trim() || $('video').value.trim();
  const sel = $('pv_subsel').value;
  let sub = String(sel).indexOf('track:') === 0 ? sel : $('pv_sub').value.trim();
  const fonts_dir = $('pv_fonts').value.trim() || $('fonts_dir').value.trim();
  const t = parseFloat($('pv_time').value || '0');
  if (isNaN(t) || t < 0) { alert('请输入有效的时间点（非负数字）'); return; }
  if (!video) { alert('请选择视频文件'); return; }
  if (!sub) { alert('请选择字幕文件'); return; }
  $('btnPreview').disabled = true;
  $('previewBox').innerHTML = '<div class="chip run" style="margin-top:12px">' + ic('loader', 'spin') + '<span>渲染中…</span></div>';
  try {
    const r = await api('/api/preview', { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify({video, sub, fonts_dir, time: t}) });
    const box = $('previewBox');
    if (r.ok) {
      box.innerHTML = '<img id="previewImg" src="' + r.url + '" alt="字幕预览帧">';
    } else {
      box.innerHTML = '<div class="chip err" style="margin-top:12px">' + ic('xCircle') + '<span>渲染失败：' + esc(r.error || '') + '</span></div>' + (r.log ? '<pre class="log-pre">' + esc(r.log) + '</pre>' : '');
    }
  } catch (ex) {
    $('previewBox').innerHTML = '<div class="chip err" style="margin-top:12px">' + ic('xCircle') + '<span>连接失败：' + esc(ex) + '</span></div>';
  } finally {
    $('btnPreview').disabled = false;
  }
};

/* 连拍：job 模式——逐帧渲染进度（复用任务轮询骨架），可中途停止 */
let pvJob = null;
function pvGridReset() {
  pvJob = null;
  setRunButton($('btnPreviewGrid'), false, '停止渲染', '连拍 8 帧');
}
$('btnPreviewGrid').onclick = async () => {
  if (pvJob) {
    setStatus('正在停止渲染…', 'run');
    await api('/api/stop', { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify({ id: pvJob }) });
    return;
  }
  const video = $('pv_video').value.trim() || $('video').value.trim();
  const sel = $('pv_subsel').value;
  let sub = String(sel).indexOf('track:') === 0 ? sel : $('pv_sub').value.trim();
  const fonts_dir = $('pv_fonts').value.trim() || $('fonts_dir').value.trim();
  if (!video) { alert('请选择视频文件'); return; }
  if (!sub) { alert('请选择字幕文件'); return; }
  $('btnPreviewGrid').disabled = true;
  $('previewBox').innerHTML = '<div class="chip run" style="margin-top:12px">' + ic('loader', 'spin') + '<span id="pvGridMsg">渲染中…</span></div>';
  try {
    const r = await api('/api/preview', { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify({video, sub, fonts_dir, mode: 'grid'}) });
    if (r.error || !r.job) {
      pvGridReset();
      $('previewBox').innerHTML = '<div class="chip err" style="margin-top:12px">' + ic('xCircle') + '<span>连拍失败：' + esc(r.error || '未知错误') + '</span></div>';
      return;
    }
    pvJob = r.job;
    setRunButton($('btnPreviewGrid'), true, '停止渲染', '连拍 8 帧');
    const fin = html => { pvGridReset(); $('previewBox').innerHTML = html; };
    startTaskPolling({
      job: pvJob, interval: 800,
      onTick: s => {
        const msg = $('pvGridMsg');
        if (!msg) return;
        const step = (s.current && s.total) ? ('（第 ' + Math.min(s.current, s.total) + ' / ' + s.total + ' 步' + (s.current_video ? ' · ' + s.current_video : '') + '）') : '';
        msg.textContent = s.progress != null ? ('渲染中 ' + s.progress + '%' + step) : '渲染中…';
      },
      onDone: s => fin('<img id="previewImg" src="' + esc(s.result || '') + '" alt="连拍网格">'),
      onError: s => fin('<div class="chip err" style="margin-top:12px">' + ic('xCircle') + '<span>连拍失败：' + esc(s.reason || ('退出码 ' + (s.exit != null ? s.exit : '?'))) + '</span></div>'),
      onKilled: () => fin('<div class="chip info" style="margin-top:12px">' + ic('info') + '<span>连拍已停止</span></div>'),
      onLost: () => fin('<div class="chip err" style="margin-top:12px">' + ic('xCircle') + '<span>连接丢失，请刷新页面后重试</span></div>')
    });
  } catch (ex) {
    pvGridReset();
    $('previewBox').innerHTML = '<div class="chip err" style="margin-top:12px">' + ic('xCircle') + '<span>连接失败：' + esc(ex) + '</span></div>';
  }
};

$('btnPreviewSub').onclick = async () => {
  const sel = $('pv_subsel').value;
  const isTrack = String(sel).indexOf('track:') === 0;
  let sub = isTrack ? sel : $('pv_sub').value.trim();
  const xv = isTrack ? ($('pv_video').value.trim() || $('video').value.trim()) : '';
  const fonts_dir = $('pv_fonts').value.trim() || $('fonts_dir').value.trim();
  const t = parseFloat($('pv_time').value || '0');
  if (isNaN(t) || t < 0) { alert('请输入有效的时间点（非负数字）'); return; }
  if (!sub) { alert('请选择字幕文件'); return; }
  if (isTrack && !xv) { alert('内封轨道预览需要视频路径'); return; }
  $('btnPreviewSub').disabled = true;
  $('previewBox').innerHTML = '<div class="chip run" style="margin-top:12px">' + ic('loader', 'spin') + '<span>渲染中…</span></div>';
  try {
    const r = await api('/api/preview', { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify({ video: xv, sub, fonts_dir, time: t, mode: 'subtitle' }) });
    const box = $('previewBox');
    if (r.ok) {
      box.innerHTML = '<img id="previewImg" src="' + r.url + '" alt="字幕预览帧">';
    } else {
      box.innerHTML = '<div class="chip err" style="margin-top:12px">' + ic('xCircle') + '<span>渲染失败：' + esc(r.error || '') + '</span></div>' + (r.log ? '<pre class="log-pre">' + esc(r.log) + '</pre>' : '');
    }
  } catch (ex) {
    $('previewBox').innerHTML = '<div class="chip err" style="margin-top:12px">' + ic('xCircle') + '<span>连接失败：' + esc(ex) + '</span></div>';
  } finally {
    $('btnPreviewSub').disabled = false;
  }
};

$('btnPvVideo').onclick = () => openBrowser(v => $('pv_video').value = v, 'video', $('pv_video').value, 'video');
$('btnPvSub').onclick = () => openBrowser(v => $('pv_sub').value = v, 'sub', $('pv_sub').value, 'sub');
function pvClearTracks() {
  [...$('pv_subsel').options].forEach(o => { if (String(o.value).indexOf('track:') === 0) o.remove(); });
  pvSyncSubInput();
}
/* 外部字幕输入框/浏览按钮仅在下拉=自定义路径时显示（选内封轨后隐藏） */
function pvSyncSubInput() {
  const ext = $('pv_subsel').value === 'custom';
  $('pv_sub').style.display = ext ? '' : 'none';
  $('btnPvSub').style.display = ext ? '' : 'none';
}
$('pv_video').addEventListener('change', pvClearTracks);
$('btnPvTracks').onclick = async () => {
  const v = $('pv_video').value.trim() || $('video').value.trim();
  if (!v) { alert('请先填写视频路径（或主流程视频）'); return; }
  pvClearTracks();
  try {
    const d = await api('/api/probe?path=' + encodeURIComponent(v));
    if (($('pv_video').value.trim() || $('video').value.trim()) !== v) return; // 视频已变更，丢弃过期结果
    if (d.error) { setStatus('内封轨道探测失败：' + d.error, 'err'); return; }
    const subs = (d.tracks || []).filter(t => t.type === 'subtitles');
    if (!subs.length) { setStatus('该视频没有内封字幕轨', 'err'); return; }
    const sel = $('pv_subsel');
    let first = null;
    subs.forEach(t => {
      const ext = /pgs/i.test(t.codec || '') ? 'sup' : (/srt/i.test(t.codec || '') ? 'srt' : 'ass');
      const o = document.createElement('option');
      o.value = 'track:' + t.id + ':' + ext;
      o.textContent = '#' + t.id + (t.lang ? ' ' + t.lang : '') + (t.name ? ' ' + t.name : '') + (ext === 'sup' ? '（内封·PGS 不可渲染）' : '（内封）');
      sel.appendChild(o);
      if (!first && ext !== 'sup') first = o.value;
    });
    if (first) { sel.value = first; pvSyncSubInput(); }   // 程序赋值不触发 change，显隐需手动同步
    setStatus('已读取 ' + subs.length + ' 条内封字幕轨，可在「字幕」下拉中选择', 'ok');
  } catch (ex) { setStatus('内封轨道探测失败：' + ex, 'err'); }
};
$('btnPvFonts').onclick = () => openBrowser(v => $('pv_fonts').value = v, 'dir', $('pv_fonts').value, 'fonts');
$('pv_subsel').onchange = pvSyncSubInput;
