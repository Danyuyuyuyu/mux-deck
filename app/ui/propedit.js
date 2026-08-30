/* ==================== 快速修补（mkvpropedit 原地改旗标/标题/章节） ====================
 * 依赖：api / ic / esc / openBrowser / setStatus / switchMode（app.js）；
 *       轨道探测复用 /api/probe；应用走 POST /api/propedit（原地修改，秒级）。
 * 逻辑：读取轨道后为每条音轨/字幕轨生成编辑行（名称/语言/default/forced），
 *       仅收集用户改动过的轨道提交；后端按 track:<a|s|v>序号 组装 mkvpropedit 参数。 */
const peEdits = {};   // trackId -> {name, language, default, forced}（仅记录改动）

function peEditsClear() { Object.keys(peEdits).forEach(k => delete peEdits[k]); }

/* ==================== 初始化（由 init.js bootstrap 统一调用，仅执行一次） ==================== */
function initPropedit() {
$('btnPeVideo').onclick = () => openBrowser(v => { $('pe_video').value = v; peEditsClear(); $('peList').innerHTML = ''; }, 'video', $('pe_video').value, 'video');
$('btnPeChapters').onclick = () => openBrowser(v => $('pe_chapters').value = v, 'any', $('pe_chapters').value, 'chapters');
$('btnPeProbe').onclick = async () => {
  const v = $('pe_video').value.trim();
  if (!v) { alert('请先填写视频路径'); return; }
  peEditsClear();
  const d = await api('/api/probe?path=' + encodeURIComponent(v));
  if ($('pe_video').value.trim() !== v) return;
  const box = $('peList');
  if (d.error) { box.innerHTML = '<div class="chip err" style="margin-top:8px">' + ic('xCircle') + '<span>' + esc(d.error) + '</span></div>'; return; }
  const ordinals = { audio: 0, subtitles: 0, video: 0 };
  const rows = [];
  (d.tracks || []).forEach(t => {
    ordinals[t.type] = (ordinals[t.type] || 0) + 1;
    if (t.type !== 'audio' && t.type !== 'subtitles') return;
    const k = t.type, n = ordinals[t.type], id = t.id;
    const p = t.properties || {};
    rows.push(
      '<tr data-tid="' + id + '" data-kind="' + k + '" data-idx="' + n + '">' +
      '<td class="mono">' + id + '</td><td>' + t.type + '</td>' +
      '<td><input class="ipt pe-name" style="width:110px" value="' + esc(p.track_name || '') + '" placeholder="名称"></td>' +
      '<td><input class="ipt pe-lang" style="width:90px" value="' + esc(p.language_ietf || p.language || '') + '" placeholder="语言"></td>' +
      '<td><input type="checkbox" class="pe-def"' + (p.default_track ? ' checked' : '') + '></td>' +
      '<td><input type="checkbox" class="pe-forced"' + (p.forced_track ? ' checked' : '') + '></td>' +
      '<td><span class="chip sm info pe-state">未改</span></td></tr>');
  });
  if (!rows.length) { box.innerHTML = '<div class="chip warn" style="margin-top:8px">' + ic('alertTriangle') + '<span>没有可编辑的音轨/字幕轨</span></div>'; return; }
  box.innerHTML = '<div class="table-wrap" style="margin-top:12px;"><table style="min-width:720px;">' +
    '<tr><th>ID</th><th>类型</th><th>名称</th><th>语言</th><th>默认</th><th>强制</th><th>状态</th></tr>' + rows.join('') + '</table></div>' +
    '<div class="t-sec" style="margin-top:6px">改动任意字段即标记该轨；「应用修改」只提交被标记的轨道。</div>';
  box.querySelectorAll('tr[data-tid]').forEach(tr => {
    const tid = tr.dataset.tid;
    const mark = () => {
      const st = tr.querySelector('.pe-state');
      st.textContent = '已改'; st.className = 'chip sm ok pe-state';
      peEdits[tid] = {
        kind: tr.dataset.kind, index: parseInt(tr.dataset.idx, 10),
        name: tr.querySelector('.pe-name').value,
        language: tr.querySelector('.pe-lang').value,
        default: tr.querySelector('.pe-def').checked,
        forced: tr.querySelector('.pe-forced').checked,
      };
    };
    ['pe-name', 'pe-lang'].forEach(cls => tr.querySelector('.' + cls).addEventListener('input', mark));
    ['pe-def', 'pe-forced'].forEach(cls => tr.querySelector('.' + cls).addEventListener('change', mark));
  });
};
$('btnPeApply').onclick = async () => {
  const video = $('pe_video').value.trim();
  if (!video) { alert('请先填写视频路径'); return; }
  const body = { video, tracks: Object.values(peEdits) };
  if ($('pe_title').value.trim()) body.title = $('pe_title').value.trim();
  if ($('pe_chapters').value.trim()) body.chapters = $('pe_chapters').value.trim();
  $('btnPeApply').disabled = true;
  $('peRes').innerHTML = '<div class="chip run" style="margin-top:12px">' + ic('loader', 'spin') + '<span>应用中…</span></div>';
  try {
    const r = await api('/api/propedit', { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify(body) });
    if (r.error) {
      $('peRes').innerHTML = '<div class="chip err" style="margin-top:12px">' + ic('xCircle') + '<span>' + esc(r.error) + '</span></div>' + (r.log ? '<pre class="log-pre">' + esc(r.log) + '</pre>' : '');
      return;
    }
    $('peRes').innerHTML = '<div class="chip ok" style="margin-top:12px">' + ic('checkCircle') + '<span>修改已应用（原地生效，未重封装）</span></div>';
    peEditsClear();
    document.querySelectorAll('#peList tr[data-tid] .pe-state').forEach(s => { s.textContent = '未改'; s.className = 'chip sm info pe-state'; });
  } catch (ex) {
    $('peRes').innerHTML = '<div class="chip err" style="margin-top:12px">' + ic('xCircle') + '<span>连接失败：' + esc(ex) + '</span></div>';
  } finally {
    $('btnPeApply').disabled = false;
  }
};
}
