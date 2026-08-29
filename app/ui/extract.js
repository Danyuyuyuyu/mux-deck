$('btnXVideo').onclick = () => openBrowser(v => $('x_video').value = v, 'video', $('x_video').value, 'video');
$('btnXOut').onclick = () => openBrowser(v => $('x_out').value = v, 'dir', $('x_out').value, 'out');
/* ==================== 字幕提取（独立工具） ==================== */
let xtTracks = [], xJob = null, xTimer = null;
$('x_video').addEventListener('change', function () {
  xtTracks = [];
  $('xList').innerHTML = '';
  $('xRes').innerHTML = '';
  $('btnXExtract').disabled = true;
});
$('btnXSubs').onclick = async () => {
  const v = $('x_video').value.trim() || $('video').value.trim();
  if (!v) { alert('请填写视频路径'); return; }
  const d = await api('/api/probe?path=' + encodeURIComponent(v));
  const box = $('xList');
  if (d.error) { box.innerHTML = '<div class="chip err" style="margin-top:8px">' + ic('xCircle') + '<span>' + esc(d.error) + '</span></div>'; return; }
  xtTracks = d.tracks.filter(t => t.type === 'subtitles').map(t => ({
    id: t.id, lang: t.lang, name: t.name, codec: t.codec, sel: true,
    ext: /pgs/i.test(t.codec || '') ? 'sup' : (/srt/i.test(t.codec || '') ? 'srt' : 'ass')
  }));
  if (!xtTracks.length) { box.innerHTML = '<div class="t-sec" style="margin-top:8px;">该视频没有字幕轨道</div>'; return; }
  $('btnXExtract').disabled = false;
  let h = '<div class="table-wrap" style="margin:8px 0;"><table style="min-width:520px;"><tr><th style="width:34px"></th><th>ID</th><th>语言</th><th>名称</th><th>格式</th></tr>';
  xtTracks.forEach((t, i) => {
    h += '<tr><td><input type="checkbox" checked onchange="xtSel(' + i + ',this.checked)"></td><td class="mono">' + t.id + '</td><td class="mono">' + esc(t.lang) + '</td><td>' + esc(t.name || '') + '</td><td class="mono">' + esc(t.ext) + '</td></tr>';
  });
  h += '</table></div><div class="t-sec">勾选要提取的字幕轨；PGS 图形字幕导出为 .sup，文本字幕导出为 .ass / .srt</div>';
  box.innerHTML = h;
};
function xtSel(i, on) { if (xtTracks[i]) xtTracks[i].sel = on; }
$('btnXExtract').onclick = async () => {
  if (xJob) return;
  const v = $('x_video').value.trim() || $('video').value.trim();
  if (!v) { alert('请填写视频路径'); return; }
  const picked = xtTracks.filter(t => t.sel);
  if (!picked.length) { alert('请先「查看字幕轨」并勾选要提取的轨道'); return; }
  const r = await api('/api/extract', { method:'POST', headers:{'Content-Type':'application/json'},
    body: JSON.stringify({ video: v, tracks: picked.map(t => ({ id: t.id, ext: t.ext, lang: t.lang || '' })), out_dir: $('x_out').value.trim() }) });
  if (r.error) { setStatus('错误：' + r.error, 'err'); return; }
  xJob = r.job; $('btnXExtract').disabled = true; $('xRes').textContent = '';
  showLogTab('xt'); setLog('xt', '');
  $('xBarWrap').style.display = ''; $('xBar').style.width = '0%';
  let xFail = 0;
  xTimer = setInterval(async () => {
    try {
      const st = await api('/api/job?id=' + xJob);
      xFail = 0;
      setLog('xt', st.log);
      if (st.progress != null) $('xBar').style.width = st.progress + '%';
      if (st.status === 'done') {
        clearInterval(xTimer); xTimer = null; xJob = null; $('btnXExtract').disabled = false;
        $('xBar').style.width = '100%';
        $('xRes').innerHTML = '<div class="chip ok" style="margin-top:8px">' + ic('checkCircle') + '<span>提取完成：' + esc(st.result || '') + '</span></div>';
        beep();
      } else if (st.status === 'error') {
        clearInterval(xTimer); xTimer = null; xJob = null; $('btnXExtract').disabled = false;
        $('xBarWrap').style.display = 'none';
        $('xRes').innerHTML = '<div class="chip err" style="margin-top:8px">' + ic('xCircle') + '<span>提取失败（退出码 ' + (st.exit ?? '?') + '），请查看日志</span></div>';
      } else if (st.status === 'killed') {
        clearInterval(xTimer); xTimer = null; xJob = null; $('btnXExtract').disabled = false;
        $('xBarWrap').style.display = 'none';
        $('xRes').innerHTML = '<div class="chip info" style="margin-top:8px">' + ic('info') + '<span>提取已停止</span></div>';
      }
    } catch (ex) {
      if (++xFail >= 5) {
        clearInterval(xTimer); xTimer = null; xJob = null; $('btnXExtract').disabled = false;
        $('xBarWrap').style.display = 'none';
        $('xRes').textContent = '连接丢失，请刷新';
        setStatus('连接丢失，请刷新', 'err');
      }
    }
  }, 1200);
};
