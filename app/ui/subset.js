/* ==================== 独立子集化（ASS 字体子集化输出到指定目录，不封装视频） ====================
 * 依赖：$ / esc / ic / api / setStatus / beep（utils/console）；openBrowser（browser.js）。
 * 后端：POST /api/subset_run（job 化）→ GET /api/subset_status 轮询（1s）→ POST /api/subset_stop。
 * AFS 产物 = 修正后同名 ASS + 子集字体；assfonts 回退仅子集字体（结果区注明差异）。
 * switchMode 切走只做显隐，轮询继续，不中断、不重新初始化。 */
let subsetState = { job: null, timer: null, tool: '' };

function ssBaseName(p) { return (p || '').split('\\').pop().split('/').pop(); }

function ssRenderResults(st) {
  const box = $('ssRes');
  const rows = (st.results || []).map(r => {
    let h = '<div class="chip ' + (r.ok ? 'ok' : 'err') + '" style="margin-top:8px">' +
      ic(r.ok ? 'checkCircle' : 'xCircle') + '<span>' + esc(ssBaseName(r.sub)) +
      (r.ok ? '' : '：' + esc(r.error || '处理失败')) + '</span></div>';
    if (r.missing && r.missing.length) {
      h += '<div class="t-sec" style="margin:2px 0 0 26px">缺字体：' + esc(r.missing.join('、')) + '</div>';
    }
    if (r.out_dir) {
      h += '<div class="t-sec" style="margin:2px 0 0 26px">产物目录：' + esc(r.out_dir) + '</div>';
    }
    return h;
  }).join('');
  const note = subsetState.tool === 'assfonts'
    ? '<div class="chip warn" style="margin-top:8px">' + ic('alertTriangle') +
      '<span>assfonts 回退：仅子集字体，无修正后 ASS</span></div>'
    : '';
  box.innerHTML = note + rows;
}

function ssFinal(cls, iconName, text) {
  $('ssRes').insertAdjacentHTML('afterbegin',
    '<div class="chip ' + cls + '" style="margin-top:8px">' + ic(iconName) + '<span>' + text + '</span></div>');
}

function ssReset() {
  clearInterval(subsetState.timer); subsetState.timer = null; subsetState.job = null;
  $('btnSSRun').disabled = false; $('btnSSStop').style.display = 'none';
}

/* ==================== 初始化（由 init.js bootstrap 统一调用，仅执行一次） ==================== */
function initSubset() {
$('btnSSBrowse').onclick = () => openBrowser(v => {
  const ta = $('ss_subs');
  const cur = ta.value.replace(/\s+$/, '');
  ta.value = cur ? cur + '\n' + v : v;   // 浏览器选择器单选：选一个追加一行
}, 'sub', '', 'ss_subs');
$('btnSSFonts').onclick = () => openBrowser(v => $('ss_fonts').value = v, 'dir', $('ss_fonts').value, 'ss_fonts');
$('btnSSOut').onclick = () => openBrowser(v => $('ss_out').value = v, 'dir', $('ss_out').value, 'ss_out');
$('btnSSRun').onclick = async () => {
  if (subsetState.job) return;
  const subs = $('ss_subs').value.split('\n').map(s => s.trim()).filter(Boolean);
  if (!subs.length) { alert('请先填写字幕文件路径（每行一个）'); return; }
  const fonts_dir = $('ss_fonts').value.trim();
  if (!fonts_dir) { alert('请填写字体目录'); return; }
  const out_dir = $('ss_out').value.trim();
  if (!out_dir) { alert('请填写输出目录'); return; }
  if (out_dir.toLowerCase() === fonts_dir.toLowerCase()) { alert('输出目录不能与字体目录相同（避免污染字体库）'); return; }
  let r;
  try {
    r = await api('/api/subset_run', { method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ subs, fonts_dir, out_dir, use_sys_fonts: $('ss_use_sys').checked }) });
  } catch (ex) { setStatus('连接失败：' + ex, 'err'); return; }
  if (r.error) { setStatus('错误：' + r.error, 'err'); return; }
  subsetState.job = r.job; subsetState.tool = r.tool || '';
  $('btnSSRun').disabled = true; $('btnSSStop').style.display = '';
  $('ssRes').textContent = '';
  $('ssBarWrap').style.display = ''; $('ssBar').style.width = '0%';
  let sFail = 0;
  subsetState.timer = setInterval(async () => {
    try {
      const st = await api('/api/subset_status?id=' + subsetState.job);
      sFail = 0;
      ssRenderResults(st);
      if (st.total) $('ssBar').style.width = Math.round((st.done || 0) / st.total * 100) + '%';
      if (st.error) {   // 任务不存在等协议级错误：终止轮询
        ssReset(); $('ssBarWrap').style.display = 'none';
        ssFinal('err', 'xCircle', '查询任务状态失败：' + esc(st.error));
        return;
      }
      if (st.status === 'done' || st.status === 'error' || st.status === 'killed') {
        const done = st.status === 'done', killed = st.status === 'killed';
        ssReset();
        if (done) {
          $('ssBar').style.width = '100%';
          ssFinal('ok', 'checkCircle', '子集化完成：' + esc(st.result || ''));
          beep();
        } else if (killed) {
          $('ssBarWrap').style.display = 'none';
          ssFinal('info', 'info', '子集化已停止');
        } else {
          $('ssBarWrap').style.display = 'none';
          ssFinal('err', 'xCircle', '子集化失败（' + (st.failed || 0) + '/' + (st.total || 0) + ' 个字幕未成功），请查看日志');
        }
      }
    } catch (ex) {
      if (++sFail >= 5) {
        ssReset(); $('ssBarWrap').style.display = 'none';
        $('ssRes').textContent = '连接丢失，请刷新';
        setStatus('连接丢失，请刷新', 'err');
      }
    }
  }, 1000);
};
$('btnSSStop').onclick = async () => {
  if (!subsetState.job) return;
  try {
    await api('/api/subset_stop', { method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ id: subsetState.job }) });
  } catch (ex) { setStatus('停止失败：' + ex, 'err'); }
};
}
