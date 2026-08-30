/* 章节编辑器：OGM 明文编辑/从源视频提取/加载文件/保存回填。复用 api/openBrowser/setStatus。 */

/* ==================== 章节编辑器（OGM 明文，可从源视频提取/加载文件/保存回填） ==================== */
function chEditToText(chs) {
  return chs.map((c, i) => {
    const ts = String(c.time).replace(",", ".");
    const m = ts.match(/^(\d+):(\d{1,2}):(\d{2})\.(\d+)/);
    const norm = m ? (m[1].padStart(2, "0") + ":" + m[2].padStart(2, "0") + ":" + m[3] + "." + (m[4] + "000").slice(0, 3)) : ts;
    return "CHAPTER" + String(i + 1).padStart(2, "0") + "=" + norm + "\nCHAPTER" + String(i + 1).padStart(2, "0") + "NAME=" + (c.name || "");
  }).join("\n");
}
$('btnChEdit').onclick = () => {
  $('chEditNote').textContent = '';
  if ($('chapters').value.trim()) {
    $('btnChLoadFile').click();   // 已填章节文件 → 直接加载内容
    return;
  }
  $('chEditText').value = '';
  $('chEditModal').style.display = 'flex';
};
$('chEditClose').onclick = () => { $('chEditModal').style.display = 'none'; };
$('btnChFromVideo').onclick = async () => {
  const v = $('video').value.trim();
  if (!v) { $('chEditNote').textContent = '请先在主流程选择视频（从源视频提取章节需要）'; return; }
  $('chEditNote').textContent = '正在从源视频提取章节…';
  try {
    const r = await api('/api/chapters/extract', { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify({ video: v }) });
    if (r.error) { $('chEditNote').textContent = '提取失败：' + r.error; return; }
    if (!r.chapters.length) { $('chEditNote').textContent = r.note || '源视频没有章节'; return; }
    $('chEditText').value = chEditToText(r.chapters);
    $('chEditNote').textContent = '已提取 ' + r.chapters.length + ' 章';
  } catch (ex) { $('chEditNote').textContent = '提取失败：' + ex; }
};
$('btnChLoadFile').onclick = () => {
  const p = $('chapters').value.trim();
  if (!p) {
    openBrowser(v2 => { if (v2) { $('chapters').value = v2; $('btnChLoadFile').click(); } }, 'any', p, 'chapters');
    return;
  }
  api('/api/chapters/parse', { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify({ path: p }) })
    .then(r => {
      if (r.error) { $('chEditNote').textContent = '加载失败：' + r.error; return; }
      $('chEditText').value = chEditToText(r.chapters);
      $('chEditNote').textContent = '已加载 ' + r.chapters.length + ' 章';
      $('chEditModal').style.display = 'flex';
    })
    .catch(ex => { $('chEditNote').textContent = '加载失败：' + ex; });
};
$('btnChSave').onclick = async () => {
  const txt = $('chEditText').value.trim();
  if (!txt) { $('chEditNote').textContent = '章节内容为空'; return; }
  const chs = [];
  let cur = null;
  for (const ln of txt.split(/\r?\n/)) {
    const l = ln.trim();
    if (!l) continue;
    let m = l.match(/^CHAPTER(\d+)=(.+)$/i);
    if (m) { if (cur) chs.push(cur); cur = { time: m[2].trim(), name: "" }; continue; }
    m = l.match(/^CHAPTER\d+NAME=(.+)$/i);
    if (m && cur) { cur.name = m[1].trim(); continue; }
    $('chEditNote').textContent = '无法识别的行：' + l.slice(0, 40);
    return;
  }
  if (cur) chs.push(cur);
  if (!chs.length) { $('chEditNote').textContent = '未解析到任何章节'; return; }
  try {
    const r = await api('/api/chapters/save', { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify({ chapters: chs }) });
    if (r.error) { $('chEditNote').textContent = '保存失败：' + r.error; return; }
    $('chapters').value = r.path;
    $('chEditModal').style.display = 'none';
    setStatus('章节已保存（' + r.count + ' 章）并填入章节文件框', 'ok');
  } catch (ex) { $('chEditNote').textContent = '保存失败：' + ex; }
};
