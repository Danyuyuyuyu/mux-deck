/* 模式切换：TOOL_MODES / switchMode（.mode 显隐 + 导航 active）/ 字幕工具下拉开合。
 * mode 名称不变（single/batch/preview/extract/propedit）；switchMode 供 inline onclick 调用，须保持全局。 */

/* ==================== 模式切换 ==================== */
/* 字幕工具三模式（preview/extract/propedit）共用一级入口「字幕工具」：工具组内任一模式
 * 高亮一级 tab，同时高亮下拉中对应 item；mode 名称本身不变。 */
const TOOL_MODES = ['preview', 'extract', 'propedit'];
function switchMode(mode) {
  document.querySelectorAll('.mode').forEach(function (m) { m.classList.toggle('active', m.id === 'mode-' + mode); });
  const isTool = TOOL_MODES.indexOf(mode) >= 0;
  document.querySelectorAll('.mode-tab').forEach(function (b) { b.classList.toggle('active', b.dataset.mode === (isTool ? 'tools' : mode)); });
  document.querySelectorAll('.drop-item').forEach(function (b) { b.classList.toggle('active', b.dataset.mode === mode); });
  document.body.classList.toggle('single-active', mode === 'single');   // 单封装固定状态条：给页尾让位
  document.body.classList.toggle('batch-active', mode === 'batch');     // 批量固定状态条同理
  refreshSticky();
  refreshBatchSticky();
}
/* 字幕工具下拉：点击一级入口开合、点选后收起、点击外部收起 */
(function () {
  const wrap = document.getElementById('toolsDrop');
  const tab = document.getElementById('toolsTab');
  if (!wrap || !tab) return;
  function setToolsDrop(open) {
    wrap.classList.toggle('open', open);
    tab.setAttribute('aria-expanded', open ? 'true' : 'false');
  }
  tab.addEventListener('click', function (e) { e.stopPropagation(); setToolsDrop(!wrap.classList.contains('open')); });
  wrap.querySelectorAll('.drop-item').forEach(function (b) { b.addEventListener('click', function () { setToolsDrop(false); }); });
  document.addEventListener('click', function (e) { if (!wrap.contains(e.target)) setToolsDrop(false); });
})();
