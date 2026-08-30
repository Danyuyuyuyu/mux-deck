// 字幕封装助手 UI 冒烟测试（第一版：bootstrap / 导航 / 设置 / 预设管理 / 高级选项 / 控制台 / Modal / 前端错误捕获）
// 不跑真实封装、不增删用户预设数据。
const { test, expect } = require('@playwright/test');

test.describe('mux-ui smoke', () => {
  let pageErrors;
  let consoleErrors;

  test.beforeEach(async ({ page }) => {
    pageErrors = [];
    consoleErrors = [];
    page.on('pageerror', e => pageErrors.push(String(e && e.message || e)));
    page.on('console', m => { if (m.type() === 'error') consoleErrors.push(m.text()); });
    await page.goto('/');
    await page.waitForFunction('window.MUXUI_READY === true', null, { timeout: 15000 });   // 等 loader 挂载 + bootstrap 完成
  });

  test.afterEach(async () => {
    // 冒烟测试最重要的价值：任何 ReferenceError / TypeError / null DOM 访问都直接失败
    expect(pageErrors, '页面 JS 异常: ' + pageErrors.join(' | ')).toEqual([]);
    expect(consoleErrors, 'console.error: ' + consoleErrors.join(' | ')).toEqual([]);
  });

  test('页面加载：App Shell 挂载 fragments，默认 single 激活，关键 DOM 齐备', async ({ page }) => {
    await expect(page.locator('#mode-single')).toBeVisible();
    await expect(page.locator('#mode-batch')).toBeAttached();
    await expect(page.locator('#consolePanel')).toBeAttached();
    await expect(page.locator('#presetModal')).toBeAttached();
    await expect(page.locator('#offlineBar')).toBeAttached();
    await expect(page.locator('.mode-nav .mode-tab')).toHaveCount(3);   // 单个 / 批量 / 字幕工具▾
    await expect(page.locator('#mode-tab.active, .mode-tab.active')).toHaveCount(1);
    await expect(page.locator('.mode-tab.active')).toHaveAttribute('data-mode', 'single');
    await expect(page.locator('body')).toHaveClass(/single-active/);
    // fragments 确实加载（业务元素存在，而非空壳）
    await expect(page.locator('#videoCard')).toBeAttached();
    await expect(page.locator('#stickySingle')).toBeAttached();
    await expect(page.locator('#browserModal')).toBeAttached();
  });

  test('导航：批量封装切换 active', async ({ page }) => {
    await page.click('.mode-tab[data-mode="batch"]');
    await expect(page.locator('#mode-batch')).toBeVisible();
    await expect(page.locator('#mode-single')).toBeHidden();
    await expect(page.locator('.mode-tab.active')).toHaveAttribute('data-mode', 'batch');
    await expect(page.locator('body')).toHaveClass(/batch-active/);
  });

  test('导航：字幕工具下拉 → 字幕预览，一级入口与下拉项同时激活', async ({ page }) => {
    await page.click('#toolsTab');
    await expect(page.locator('#toolsDrop')).toHaveClass(/open/);
    await page.click('.drop-item[data-mode="preview"]');
    await expect(page.locator('#mode-preview')).toBeVisible();
    await expect(page.locator('.mode-tab.active')).toHaveAttribute('data-mode', 'tools');   // 一级入口高亮
    await expect(page.locator('.drop-item.active')).toHaveAttribute('data-mode', 'preview'); // 下拉项高亮
  });

  test('设置弹层：三入口齐备（封装预设 / 全局设置 / 环境检测）', async ({ page }) => {
    await page.click('#btnSettings');
    await expect(page.locator('#settingsPop')).toHaveClass(/open/);
    await expect(page.locator('#popPresetBtn')).toBeVisible();
    await expect(page.locator('#popGlobalBtn')).toBeVisible();
    await expect(page.locator('#popEnvBtn')).toBeVisible();
  });

  test('封装预设管理窗口：从设置打开、关闭（不改动预设数据）', async ({ page }) => {
    await page.click('#btnSettings');
    await page.click('#popPresetBtn');
    await expect(page.locator('#settingsPop')).not.toHaveClass(/open/);   // 设置弹层先关闭
    await expect(page.locator('#presetModal')).toBeVisible();
    await expect(page.locator('#pmList')).toBeVisible();
    await expect(page.locator('body')).toHaveClass(/modal-open/);          // 页面滚动锁生效
    // Preset Manager 结构：X 图标关闭 / 编辑器头部 / SC·TC 轨道卡 / segmented 默认轨 / 固定 Footer
    await expect(page.locator('#pmClose')).toHaveAttribute('aria-label', '关闭封装预设');
    await expect(page.locator('#pmEdHead')).toContainText('正在编辑');
    await expect(page.locator('.pm-track-card .sub-badge.sc')).toBeVisible();
    await expect(page.locator('.pm-track-card .sub-badge.tc')).toBeVisible();
    await expect(page.locator('#pm_f_sc_default_seg .seg-btn', { hasText: '自动' })).toBeVisible();
    await expect(page.locator('#pmNewBtn')).toBeVisible();                 // 新建按钮固定侧栏底部
    await expect(page.locator('#pmCancelBtn')).toBeVisible();              // Footer 恒有取消
    await expect(page.locator('#pmFootActions .btn.primary')).toHaveCount(1);   // 一次最多一个 Primary
    // 叠层：管理器内开文件浏览器 → 浏览器必须在上层（modal.js 栈深 z-index）
    await page.evaluate(() => openBrowser(() => {}, 'any', '', 'pmstack'));
    await expect(page.locator('#browserModal')).toBeVisible();
    const zi = await page.evaluate(() => ({
      browser: document.getElementById('browserModal').style.zIndex,
      preset: getComputedStyle(document.getElementById('presetModal')).zIndex,
    }));
    expect(parseInt(zi.browser, 10)).toBeGreaterThan(parseInt(zi.preset, 10));
    await page.evaluate(() => closeModal('browserModal'));
    await page.click('#pmClose');
    await expect(page.locator('#presetModal')).toBeHidden();
    await expect(page.locator('body')).not.toHaveClass(/modal-open/);
  });

  test('主页面预设状态条：初始自定义配置，按钮态正确', async ({ page }) => {
    await expect(page.locator('#presetStatusBar')).toBeVisible();
    await expect(page.locator('#presetStatusText')).toHaveText('自定义配置');
    await expect(page.locator('#btnPresetPick')).toBeVisible();
    await expect(page.locator('#btnPresetChange')).toBeHidden();
    await expect(page.locator('#btnPresetDetach')).toBeHidden();
  });

  test('Modal 公共组件：Esc 关闭 + 焦点恢复（验证任务2 机制）', async ({ page }) => {
    await page.evaluate(() => {
      document.getElementById('btnSettings').focus();
      openModal('globalModal');   // open/close/isModalOpen 统一入口
    });
    await expect(page.locator('#globalModal')).toBeVisible();
    await page.click('#cfg_scan');   // 焦点进入弹窗内部：closeModal 必须先移出焦点再标 aria-hidden，否则 Chrome 报 Blocked aria-hidden（afterEach 捕获）
    await page.keyboard.press('Escape');
    await expect(page.locator('#globalModal')).toBeHidden();
    const focusId = await page.evaluate(() => document.activeElement && document.activeElement.id);
    expect(focusId).toBe('btnSettings');   // 焦点恢复到触发元素
  });

  test('高级选项：展开后预设选择器与关键任务字段齐备', async ({ page }) => {
    await page.click('#advToggle');
    await expect(page.locator('#advBody')).toHaveClass(/show/);
    await expect(page.locator('#preset_sel')).toBeAttached();
    await expect(page.locator('#backup')).toBeAttached();
    await expect(page.locator('#fonts_mode')).toBeAttached();
    await expect(page.locator('#force')).toBeAttached();
    await expect(page.locator('#btnSubtitleCheck')).toBeAttached();   // 统一字幕检查入口
  });

  test('控制台：默认收起 → 展开有日志 tabs → 再收起', async ({ page }) => {
    await expect(page.locator('#consolePanel')).toHaveClass(/collapsed/);
    await page.click('#consoleCollapsed');
    await expect(page.locator('#consolePanel')).not.toHaveClass(/collapsed/);
    await expect(page.locator('.ltab')).toHaveCount(4);   // 封装/批量/提取/历史
    await page.click('#btnLogFold');
    await expect(page.locator('#consolePanel')).toHaveClass(/collapsed/);
  });

  test('页面切换不丢输入状态、不重新挂载 fragments', async ({ page }) => {
    await page.evaluate(() => { document.getElementById('out_name_tmpl').value = '[G] {ep}'; });
    const before = await page.evaluate(() => document.querySelectorAll('#mode-single .panel').length);
    await page.click('.mode-tab[data-mode="batch"]');
    await page.click('.mode-tab[data-mode="single"]');
    await expect(page.locator('#out_name_tmpl')).toHaveValue('[G] {ep}');   // 切换不丢状态
    const after = await page.evaluate(() => document.querySelectorAll('#mode-single .panel').length);
    expect(after).toBe(before);   // DOM 未重建
  });
});
