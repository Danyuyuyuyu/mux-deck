/* ==================== 初始化 ==================== */
$('btnCfgScan').onclick = () => openBrowser(v => { $('cfg_scan').value = v; }, 'dir', $('cfg_scan').value, 'cfg');
$('btnCfgSave').onclick = async () => {
  const p = $('cfg_scan').value.trim();
  try {
    const r = await api('/api/config', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ scan_root: p }) });
    if (r.error) { setStatus('工作目录保存失败：' + r.error, 'err'); return; }
    CFG.scanRoot = r.scan_root || p;
    updateGlobalSummary();
    setStatus('工作目录已保存：' + CFG.scanRoot + '（索引将自动重建）', 'ok');
  } catch (ex) { setStatus('工作目录保存失败：' + ex, 'err'); }
};
$('cfg_tool').onchange = async () => {
  try {
    const r = await api('/api/config', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ subset_tool: $('cfg_tool').value }) });
    if (r.error) { setStatus('子集工具保存失败：' + r.error, 'err'); return; }
    setStatus('子集化工具已切换：' + (r.subset_tool || $('cfg_tool').value), 'ok');
  } catch (ex) { setStatus('子集工具保存失败：' + ex, 'err'); }
};
Promise.all([
  api('/api/env_check').catch(() => null),
  api('/api/config').catch(() => null),
  api('/api/presets').catch(() => null),
]).then(([env, c, ps]) => {
  if (env && env.items) envRender(env);
  if (c && c.scan_root) { CFG.scanRoot = c.scan_root; $('cfg_scan').value = c.scan_root; }
  if (c && c.subset_tool) { $('cfg_tool').value = c.subset_tool; }
  updateGlobalSummary();
  if (ps && ps.presets) { PRESETS = ps.presets; refreshPresetSel(); }
  const needSetup = c && (!c.configured || !c.valid);
  const envBroken = env && env.overall === 'broken';
  if (envBroken) {
    openEnv();  // 环境缺失优先引导安装；关闭后再处理工作目录（closeEnv 内部兜底）
  } else {
    if (env && env.overall === 'partial') setStatus('部分组件缺失（子集工具 / AFS 后端）· 设置 → 环境检测', 'run');
    if (needSetup) showSetup();
  }
});
renderVideoCard();
refreshSticky();
refreshBatchSticky();
syncSubStatus();
$('video').value = localStorage.getItem('muxui_video') || $('video').value; // no-op guard
api('/api/version').then(v => { if (v.ok) setStatus('服务已就绪 · 可直接把文件拖进窗口', 'ok'); }).catch(() => setStatus('无法连接服务器', 'err'));
setInterval(() => {
  const s = $('status');
  if (s && (s.className === 'err' || s.className === 'run')) { // 仅当显示连接异常/进行中时重探
    api('/api/version').then(v => { if (v.ok) setStatus('服务已就绪 · 可直接把文件拖进窗口', 'ok'); }).catch(() => {});
  }
}, 15000);
