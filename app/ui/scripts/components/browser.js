/* 文件浏览器：BR / openBrowser / showBrowser 及 mb* 事件绑定。openBrowser 供各业务模块调用，须保持全局。 */

const BR = { setter: null, filter: 'any', path: 'D:\\Video' };

/* ==================== 文件浏览器 ==================== */
const CFG = { scanRoot: 'D:\\Video' };
function openBrowser(setter, filter, startPath, slot, dirSetter) {
  BR.setter = setter; BR.filter = filter; BR.slot = slot || filter || 'generic'; BR.dirSetter = dirSetter || null;
  BR.path = startPath || localStorage.getItem('muxui_ld_' + BR.slot) || localStorage.getItem('muxui_lastdir') || CFG.scanRoot;
  $('browserModal').style.display = 'block';
  showBrowser();
}
$('mbClose').onclick = () => $('browserModal').style.display = 'none';
$('mbUp').onclick = () => { BR.path = BR.path.replace(/\\+$/, '').replace(/[^\\/]+$/, '') || ''; showBrowser(); }; // 到盘根后再向上进入盘符列表
$('mbGo').onclick = () => { BR.path = $('mbPathInput').value.trim() || BR.path; showBrowser(); };
$('mbPathInput').onkeydown = e => { if (e.key === 'Enter') $('mbGo').click(); };
$('mbUseDir').onclick = () => {
  const fn = BR.dirSetter || BR.setter;
  fn(BR.path.replace(/\\+$/, ''));
  $('browserModal').style.display = 'none';
};
async function showBrowser() {
  let d;
  try {
    d = await api('/api/list?path=' + encodeURIComponent(BR.path));
  } catch (ex) {
    $('mbPath').textContent = '加载失败';
    $('mbHint').textContent = '连接失败：' + ex + '（请检查服务是否运行）';
    return;
  }
  $('mbPath').textContent = d.path || '（选择驱动器）';
  $('mbPathInput').value = d.path;
  const body = $('mbBody'); body.innerHTML = '';
  const ext = FILTERS[BR.filter];
  $('mbUseDir').style.display = ((BR.filter === 'dir' || BR.dirSetter) && d.path) ? '' : 'none';
  $('mbUseDir').innerHTML = (BR.dirSetter && BR.filter !== 'dir')
    ? ic('folderOutput') + '<span>添加此目录全部视频</span>'
    : ic('check') + '<span>使用此目录</span>';
  $('mbHint').textContent = d.error ? ('错误: ' + d.error) : '';
  if (d.path && !d.error) { try { localStorage.setItem('muxui_lastdir', d.path); localStorage.setItem('muxui_ld_' + (BR.slot || 'generic'), d.path); } catch (e) {} } // 记住上次浏览目录（分槽位）
  const itemCls = (n, dir) => '<span class="it">' + ic(dir ? 'folder' : 'fileText') + '<span>' + esc(n) + '</span></span>';
  if (d.drives) d.drives.forEach(dr => {
    const b = document.createElement('button'); b.className = 'mb-item dir';
    b.innerHTML = itemCls(dr, false) + ic('chevronDown');
    b.onclick = () => { BR.path = dr; showBrowser(); };
    body.appendChild(b);
  });
  d.dirs.forEach(n => {
    const b = document.createElement('button'); b.className = 'mb-item dir';
    b.innerHTML = itemCls(n, true);
    b.onclick = () => { BR.path = (d.path ? d.path.replace(/\\+$/, '') + '\\' : '') + n; showBrowser(); };
    body.appendChild(b);
  });
  d.files.forEach(f => {
    const [name, sz] = f;
    if (ext && !ext.includes(name.slice(name.lastIndexOf('.')).toLowerCase())) return;
    const b = document.createElement('button'); b.className = 'mb-item';
    b.innerHTML = itemCls(name, false) + (sz >= 0 ? '<span class="sz">' + (sz / 1048576).toFixed(1) + ' MB</span>' : '');
    b.onclick = () => { BR.setter((d.path ? d.path.replace(/\\+$/, '') + '\\' : '') + name); $('browserModal').style.display = 'none'; };
    body.appendChild(b);
  });
}
