/* ==================== 启动引导（bootstrap） ====================
 * 加载时序：loader.js 在 pages/partials 全部挂载后按序注入脚本（脚本内只定义函数与状态，
 * 不含 DOM 绑定）→ 本文件 bootstrap() 按依赖顺序调用各模块 initXxx()（仅此一次，页面切换
 * 只做显示/隐藏、绝不重新 init）→ startup() 执行启动期的异步装配（配置/预设/环境引导）。
 * 各 init 只做"事件绑定 + 初始 UI 同步"；业务函数仍由各模块顶层定义并保持全局可用。 */
function bootstrap() {
  initModal();
  initNavigation();
  initSettings();
  initBrowser();
  initConsole();
  initSingle();
  initPresets();
  initPreflight();
  initChapters();
  initBatch();
  initExtract();
  initPreview();
  initPropedit();
  initSubset();
  initEnv();
  initAppGlue();
  startup();
}

function startup() {
  Promise.all([
    api('/api/env_check').catch(() => null),
    api('/api/config').catch(() => null),
    api('/api/presets').catch(() => null),
  ]).then(([env, c, ps]) => {
    if (env && env.items) envRender(env);
    if (c && c.scan_root) { CFG.scanRoot = c.scan_root; $('cfg_scan').value = c.scan_root; }
    if (c && c.subset_tool) { $('cfg_tool').value = c.subset_tool; }
    if (c && typeof c.postcmd === 'string') {
      CFG.postcmd = c.postcmd;
      $('cfg_postcmd').value = CFG.postcmd;
      // 全局默认落任务输入：单/批量任务输入均为空且全局默认非空时填入（用户可见可改；程序赋值不触发事件）
      const sp = $('postcmd'), bp = $('b_postcmd');
      if (CFG.postcmd && sp && bp && !sp.value.trim() && !bp.value.trim()) { sp.value = CFG.postcmd; bp.value = CFG.postcmd; }
    }
    updateGlobalSummary();
    if (ps && ps.presets) { PRESETS = ps.presets; refreshPresetSel(); restoreRememberedPreset(); }   // 恢复上次选中的预设并自动套用（名称失效则回落）
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
}

bootstrap();
window.MUXUI_READY = true;   // 冒烟测试就绪信号：fragments 挂载 + 全部 init 执行完毕
