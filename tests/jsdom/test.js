/* jsdom 整页运行时验证：
 * 1) bug 修复：单个封装手动填字幕（input/change/浏览按钮）后 sticky 状态立即更新
 * 2) 新功能：单个封装选视频后自动识别字幕与字体目录（复用 identify，参考批量添加文件） */
const fs = require('fs');
const path = require('path');
const { JSDOM, VirtualConsole } = require('jsdom');

const UI = path.join(__dirname, '..', '..', 'app', 'ui');
let html = fs.readFileSync(path.join(UI, 'index.html'), 'utf8');
// 外部 script 内联组装（拆分后 index.html 只剩 loader.js；jsdom 不加载外部脚本资源）。
// pages/partials 片段与业务脚本改由下方 mockFetch 从磁盘按 URL 供给，
// 让 jsdom 走与线上一致的 loader 异步加载路径（并行挂载 → 按序注入脚本 → 初始化）。
html = html.replace(/<script src="([^"]+)"><\/script>/g, (m, src) =>
  '<script>\n' + fs.readFileSync(path.join(UI, src), 'utf8') + '\n</script>');

const results = [];
function check(name, cond, extra) {
  results.push({ name, ok: !!cond, extra: extra || '' });
  console.log((cond ? 'PASS' : 'FAIL') + '  ' + name + (cond ? '' : '   ' + extra));
}
const sleep = ms => new Promise(r => setTimeout(r, ms));
async function waitUntil(fn, timeout) {
  const t0 = Date.now();
  while (Date.now() - t0 < (timeout || 2000)) { try { if (fn()) return true; } catch (e) {} await sleep(20); }
  return false;
}

/* mock fetch：按 URL 前缀分发假数据 */
let matchSubsRet = { sc: 'D:\\Video\\EP01.sc.ass', tc: '' };
let fontsDirRet = { fonts_dir: 'D:\\Video\\Fonts' };
let chaptersRet = { chapters: '' };
let jobCalls = 0;   // /api/job 轮询计数：前 2 次运行中(68%)，之后 done
let batchJobCalls = 0;   // 批量任务轮询：前 2 次运行中(第 1/1 个, 68%)，之后 done 全成功
let pvGridJobCalls = 0;  // 连拍轮询：前 2 次运行中(40%, 第 3/9 步)，之后 done 出图
const presetStore = { };  // 预设存取 mock
function mockFetch(url, opts) {
  const u = url.split('?')[0];
  const q = (url.split('?')[1] || '');
  let data = {};
  if (u === '/api/version') data = { version: 'test' };
  else if (u === '/api/env_check') data = { overall: 'ready', items: [], missing: [] };
  else if (u === '/api/config') data = { configured: true, valid: true, scan_root: 'D:\\Video', subset_tool: 'afs' };
  else if (u === '/api/match_subs') { data = { video: q.replace('path=', ''), sc: matchSubsRet.sc, tc: matchSubsRet.tc }; }
  else if (u === '/api/detect_fonts_dir') { data = { path: q.replace('path=', ''), fonts_dir: fontsDirRet.fonts_dir }; }
  else if (u === '/api/detect_chapters') { data = { path: q.replace('path=', ''), chapters: chaptersRet.chapters }; }
  else if (u === '/api/list') data = { path: 'D:\\Video', dirs: [], files: [['EP01.sc.ass', 1024], ['EP01.chs.ass', 2048], ['EP01.mkv', 999]] };
  else if (u === '/api/probe') data = { tracks: [{ id: 1, type: 'video', codec: 'AVC' }, { id: 2, type: 'audio', codec: 'AAC' }, { id: 3, type: 'subtitles', codec: 'ASS', lang: 'zh' }], attachments: 0 };
  else if (u === '/api/chapters/extract') data = { chapters: [{ time: '00:00:00.000', name: 'OP' }] };
  else if (u === '/api/chapters/parse') data = { chapters: [{ time: '00:00:00.000', name: 'OP' }] };
  else if (u === '/api/chapters/save') data = { path: 'C:\\ch_edited.txt', count: 1 };
  else if (u === '/api/propedit') data = { ok: true, log: '', probe: {} };
  else if (u === '/api/sub_check') data = { ok: true, dialogue: 120, counts: { overlap: 1, empty: 0, bad_time: 0, bad_style: 0, cps: 2, long_line: 1 }, issues: [{ line: 12, type: 'cps', detail: 'CPS 18.0 超过 15（27 字 / 1.50s）' }], total_issues: 4, truncated: false, status: 'warn' };
  else if (u === '/api/history') data = { items: [] };
  else if (u === '/api/presets') {
    if (opts && opts.body) { try { const b = JSON.parse(opts.body); presetStore[b.name || '测试预设'] = b.data || {}; } catch (e) {} }
    data = { presets: presetStore };
  }
  else if (u === '/api/presets/delete') data = { ok: true, presets: presetStore };
  else if (u === '/api/mux') data = { job: 'jtest' };
  else if (u === '/api/batch') data = { job: 'bjtest' };
  else if (u === '/api/preview') data = { job: 'pjtest' };
  else if (u === '/api/job') {
    if (q.indexOf('id=pjtest') === 0) {
      pvGridJobCalls++;
      data = pvGridJobCalls <= 2
        ? { status: 'running', progress: 40, current: 3, total: 9, current_video: '渲染第 3/8 帧', log: '' }
        : { status: 'done', progress: 100, current: 9, total: 9, result: '/api/file?path=test.png', log: '' };
    } else if (q.indexOf('id=bjtest') === 0) {
      batchJobCalls++;
      data = batchJobCalls <= 2
        ? { status: 'running', total: 1, current: 1, progress: 68, current_video: 'D:\\Video\\EP01.mkv', log: 'Muxing' }
        : { status: 'done', total: 1, current: 1, log: '', qc_summary: { total: 1, ok: 1, warn: 0, fail: 0 }, results: [{ ok: true, output: 'D:\\Video\\EP01.out.mkv', cmd: 'mkvmerge -o out.mkv in.mkv sub.ass', qc: { status: 'ok', ok: ['通过（字幕轨 2 条）'] } }] };
    } else {
      jobCalls++;
      data = jobCalls <= 2 ? { status: 'running', progress: 68, log: 'Muxing' } : { status: 'done', progress: 100, log: '', result: 'D:\\Video\\out.mkv' };
    }
  }
  else if (!u.startsWith('/api/')) {
    // App Shell 片段与业务脚本：从磁盘按路径供给（loader.js 的 fetch 走这里）
    const rel = u.replace(/^\.?\//, '');
    try {
      const content = fs.readFileSync(path.join(UI, rel), 'utf8');
      return Promise.resolve({ ok: true, status: 200, text: async () => content });
    } catch (e) {
      return Promise.resolve({ ok: false, status: 404, text: async () => '' });
    }
  }
  else return Promise.resolve({ ok: true, status: 200, json: async () => ({}) });
  return Promise.resolve({ ok: true, status: 200, json: async () => data });
}

(async () => {
  const vc = new VirtualConsole();
  vc.on('jsdomError', e => console.log('[jsdomError]', e.message, (e.detail && e.detail.stack || '').split('\n').slice(0, 4).join(' | ')));
  vc.on('error', (...a) => console.log('[console.error]', ...a));
  const dom = new JSDOM(html, {
    url: 'http://127.0.0.1:8765/',
    runScripts: 'dangerously',
    pretendToBeVisual: true,
    virtualConsole: vc,
    beforeParse(window) {
      window.fetch = mockFetch;
      window.confirm = () => true;
      window.alert = m => console.log('[alert]', m);
      window.scrollTo = () => {};
      window.prompt = () => '测试预设';
    },
  });
  const { window } = dom;
  const $ = id => window.document.getElementById(id);
  await sleep(300); // 等 loader 并行挂载片段 + 按序注入脚本 + init.js 的 Promise.all/version 探测完成
  if (!$('stickyNote')) {
    console.log('DEBUG bodyLen', window.document.body.innerHTML.length, 'scripts', window.document.querySelectorAll('script').length);
    console.log('DEBUG html head:', html.slice(0, 200));
    console.log('DEBUG tail:', window.document.body.innerHTML.slice(-300));
    process.exit(3);
  }
  console.log('GUARD: n=', !!window.document.getElementById('stickyNote'), 'qs=', !!window.document.querySelector('#stickyNote'), 'count=', window.document.querySelectorAll('[id=stickyNote]').length);
  setTimeout(function probe() {
    console.log('LATER: n=', !!window.document.getElementById('stickyNote'), 'bodyLen', window.document.body.innerHTML.length);
  }, 0);

  const stickyTxt = () => { const n = $('stickyNote'); if (!n) { console.log('NULL! same window?', window === dom.window, 'docURL', window.document.URL, 'bodyLen', window.document.body.innerHTML.length); process.exit(4); } return n.querySelector('.sticky-txt').textContent; };
  const stickyCls = () => $('stickyNote').className;

  /* ---- 初始态 ---- */
  check('初始 sticky=尚未选择视频 且按钮禁用', stickyTxt() === '尚未选择视频' && $('btnStart').disabled && stickyCls().includes('info'), stickyTxt() + '/' + stickyCls());

  /* ---- 场景1：浏览选视频 → 自动识别字幕+字体目录 ---- */
  window.pickVideoPath('D:\\Video\\EP01.mkv');
  let ok = await waitUntil(() => $('sc_sub').value === 'D:\\Video\\EP01.sc.ass' && $('fonts_dir').value === 'D:\\Video\\Fonts');
  check('自动识别填充 sc_sub', ok, 'sc_sub=' + $('sc_sub').value);
  check('自动识别填充 fonts_dir', $('fonts_dir').value === 'D:\\Video\\Fonts', $('fonts_dir').value);
  check('视频卡片已识别（收缩为紧凑态）', $('videoCard').className === 'file-card compact');
  check('sticky=所有资源准备完成(ok) 且按钮可用', stickyTxt() === '所有资源准备完成' && stickyCls().includes('ok') && !$('btnStart').disabled, stickyTxt() + '/' + stickyCls());
  check('状态栏提示已自动识别', ($('status').textContent || '').includes('已自动识别'), $('status').textContent);
  check('scStatus 徽章=已识别', $('scStatus').className.includes('on'));

  /* ---- 场景2（bug 修复）：手输字幕 input 事件 → sticky 立即更新 ---- */
  const sc = $('sc_sub');
  sc.value = ''; sc.dispatchEvent(new window.Event('input', { bubbles: true }));
  check('清空字幕(手输中) sticky 立即=未提供字幕(warn)', stickyTxt().includes('未提供字幕') && stickyCls().includes('warn'), stickyTxt() + '/' + stickyCls());
  sc.value = 'D:\\Video\\EP01.chs.ass'; sc.dispatchEvent(new window.Event('input', { bubbles: true }));
  check('手输字幕 input 后 sticky 立即=所有资源准备完成(ok)', stickyTxt() === '所有资源准备完成' && stickyCls().includes('ok'), stickyTxt() + '/' + stickyCls());

  /* ---- 场景3（bug 修复）：blur(change) 事件 → sticky 更新 + 轨道名自动识别 ---- */
  sc.value = ''; sc.dispatchEvent(new window.Event('input', { bubbles: true }));
  sc.value = 'D:\\Video\\EP01.chs.ass'; sc.dispatchEvent(new window.Event('change', { bubbles: true }));
  check('change 后 sticky=准备完成 且 轨道名自动=CHS', stickyTxt() === '所有资源准备完成' && $('sc_name').value === 'CHS', stickyTxt() + '/name=' + $('sc_name').value);

  /* ---- 场景4（bug 修复）：字幕「浏览」按钮 → fireChange → sticky 更新 ---- */
  sc.value = ''; sc.dispatchEvent(new window.Event('input', { bubbles: true }));
  check('清空后 sticky=未提供字幕', stickyTxt().includes('未提供字幕'), stickyTxt());
  $('btnSc').onclick(new window.Event('click'));
  await waitUntil(() => $('browserModal').style.display === 'block' && $('mbBody').children.length);
  const fileBtn = [...$('mbBody').children].find(b => b.textContent.includes('EP01.sc.ass'));
  fileBtn.click();
  await sleep(30);
  check('浏览按钮选字幕 → sticky=准备完成 且值已填', stickyTxt() === '所有资源准备完成' && sc.value === 'D:\\Video\\EP01.sc.ass', stickyTxt() + '/' + sc.value);

  /* ---- 场景5：仅繁体视频 → tc 填充 + 默认轨徽章切换 ---- */
  matchSubsRet = { sc: '', tc: 'D:\\Video\\EP01.tc.ass' };
  window.pickVideoPath('D:\\Video\\EP02.mkv');
  ok = await waitUntil(() => $('tc_sub').value === 'D:\\Video\\EP01.tc.ass');
  check('仅繁体自动填充 tc_sub', ok, $('tc_sub').value);
  check('sc 槽位保持为空（上一视频的 sc 已随更换清空）', $('sc_sub').value === '', $('sc_sub').value);
  check('仅 TC 时 tcDefaultBadge=默认轨', $('tcDefaultBadge').textContent === '默认轨', $('tcDefaultBadge').textContent);
  check('sticky=准备完成(ok)', stickyTxt() === '所有资源准备完成' && stickyCls().includes('ok'), stickyTxt() + '/' + stickyCls());

  /* ---- 场景6：已有值不覆盖（fonts_dir 手动值保留） ---- */
  $('fonts_dir').value = 'D:\\MyFonts';
  matchSubsRet = { sc: 'D:\\Video\\EP03.sc.ass', tc: '' };
  window.pickVideoPath('D:\\Video\\EP03.mkv');
  ok = await waitUntil(() => $('sc_sub').value === 'D:\\Video\\EP03.sc.ass');
  check('新视频 sc 自动填充', ok, $('sc_sub').value);
  check('fonts_dir 已有值不被覆盖', $('fonts_dir').value === 'D:\\MyFonts', $('fonts_dir').value);

  /* ---- 场景7：识别全空 → 提示未识别到 ---- */
  matchSubsRet = { sc: '', tc: '' };
  fontsDirRet = { fonts_dir: '' };
  window.pickVideoPath('D:\\Video\\EP04.mkv');
  ok = await waitUntil(() => ($('status').textContent || '').includes('未自动识别到'));
  check('识别全空时提示未识别到', ok && stickyTxt().includes('未提供字幕'), $('status').textContent + '/' + stickyTxt());

  /* ---- 场景8：单个封装字幕「移除」按钮 ---- */
  matchSubsRet = { sc: 'D:\\Video\\EP01.sc.ass', tc: '' };
  fontsDirRet = { fonts_dir: 'D:\\Video\\Fonts' };
  window.pickVideoPath('D:\\Video\\EP05.mkv');
  ok = await waitUntil(() => $('sc_sub').value === 'D:\\Video\\EP01.sc.ass');
  check('场景8前置：新视频已自动填充 sc', ok, $('sc_sub').value);
  $('btnScClear').click();
  check('移除按钮清空 sc_sub', $('sc_sub').value === '', $('sc_sub').value);
  check('移除后轨道名复位 SC、编码徽章清除', $('sc_name').value === 'SC' && $('sc_enc').textContent === '', $('sc_name').value + '/' + $('sc_enc').textContent);
  check('移除后 scStatus=未设置 且 sticky=未提供字幕(warn)', $('scStatus').className.includes('off') && stickyTxt().includes('未提供字幕') && stickyCls().includes('warn'), stickyTxt() + '/' + stickyCls());
  check('移除后 tc 不受影响', !$('tc_sub').value, $('tc_sub').value);

  /* ---- 场景9：批量行内字幕浏览/移除按钮 ---- */
  window.eval('batchItems.length = 0');
  window.eval('batchItems.push({video: "D:/Video/EP01.mkv", sc: "D:/Video/EP01.sc.ass", tc: ""})');
  window.renderBatch();
  const row = $('b_s_0');
  check('批量行渲染 sc 输入框与按钮', !!row && row.value === 'D:/Video/EP01.sc.ass' && !!row.parentElement.querySelector('button[title="浏览简体字幕"]') && !!row.parentElement.querySelector('button[title="移除简体字幕"]'));
  // 浏览：mock /api/list 已返回 EP01.sc.ass
  row.parentElement.querySelector('button[title="浏览简体字幕"]').click();
  ok = await waitUntil(() => $('browserModal').style.display === 'block' && [...$('mbBody').children].some(b => b.textContent.includes('EP01.sc.ass')));
  const subBtn = [...$('mbBody').children].find(b => b.textContent.includes('EP01.sc.ass'));
  subBtn.click();
  await sleep(30);
  check('批量行内浏览可选字幕填入', $('b_s_0').value === 'D:\\Video\\EP01.sc.ass', $('b_s_0').value);
  // 移除
  row.parentElement.querySelector('button[title="移除简体字幕"]').click();
  check('批量行内移除清空输入框与数据', $('b_s_0').value === '' && window.eval('batchItems[0].sc') === '', $('b_s_0').value + '/' + window.eval('batchItems[0].sc'));
  check('批量移除后 sticky=已准备（视频仍在）', $('batchStickyNote').querySelector('.sticky-txt').textContent.includes('已准备 1'), $('batchStickyNote').textContent);

  /* ---- 场景10：底部状态条（进度/百分比/耗时/剩余/高光/按钮态全链路） ---- */
  check('状态条结构：进度块/百分比/耗时/剩余元素齐备', !!$('stickyProgress') && !!$('stickyPct') && !!$('stickyElapsed') && !!$('stickyEta') && !!$('stickyBarWrap') && !!$('stickyBar'));
  check('初始：pct=--、时间=--:--:--、无高光态', $('stickyPct').textContent === '--' && $('stickyElapsed').textContent === '--:--:--' && $('stickyEta').textContent === '--:--:--' && !$('stickyProgress').classList.contains('run'));
  check('single 模式 body 挂 single-active（页尾让位）', window.document.body.classList.contains('single-active'));
  window.switchMode('batch');
  check('切批量后 body 无 single-active', !window.document.body.classList.contains('single-active'));
  window.switchMode('single');
  check('切回单个后 body 恢复 single-active', window.document.body.classList.contains('single-active'));

  jobCalls = 0;
  window.document.querySelector('#btnStart').click();   // 提交任务（mock 返回 job=jtest）
  ok = await waitUntil(() => $('stickyProgress').classList.contains('run') && $('btnStart').textContent.includes('停止'));
  check('任务启动：run 高光态 + 按钮变停止', ok);
  ok = await waitUntil(() => $('stickyPct').textContent === '68%', 4000);
  check('轮询中：百分比=68%（与真实 progress 一致）', ok, $('stickyPct').textContent);
  check('进度条宽度镜像 singleBar', $('stickyBar').style.width === '68%', $('stickyBar').style.width);
  const et = $('stickyElapsed').textContent, ea = $('stickyEta').textContent;
  check('运行中：耗时格式 HH:MM:SS', /^\d{2}:\d{2}:\d{2}$/.test(et), et);
  check('运行中：有进度时剩余=线性外推时间', /^\d{2}:\d{2}:\d{2}$/.test(ea), ea);
  ok = await waitUntil(() => !$('stickyProgress').classList.contains('run'), 5000);
  check('终态：run 高光态移除', ok);
  check('完成：pct=100%、剩余归 --、按钮恢复开始封装', $('stickyPct').textContent === '100%' && $('stickyEta').textContent === '--:--:--' && $('btnStart').textContent.includes('开始封装'), $('stickyPct').textContent + '/' + $('stickyEta').textContent);
  check('完成：耗时定格保留', /^\d{2}:\d{2}:\d{2}$/.test($('stickyElapsed').textContent), $('stickyElapsed').textContent);

  /* ---- 场景11：批量底部状态条（当前文件/总体进度/计数/时间全链路） ---- */
  check('批量状态条结构：当前文件/进度/计数/时间元素齐备', !!$('bStickyCur') && !!$('bStickyCurName') && !!$('bStickyPct') && !!$('bStickyCountNum') && !!$('bStickyElapsed') && !!$('bStickyEta') && !!$('batchStickyBar'));
  check('批量初始：当前文件隐藏、pct=--、计数 0 / 0、时间 --', $('bStickyCur').style.display === 'none' && $('bStickyPct').textContent === '--' && $('bStickyCountNum').textContent === '0 / 0' && $('bStickyElapsed').textContent === '--:--:--');
  window.eval('batchItems.length = 0');
  window.eval('batchItems.push({video: "D:/Video/EP01.mkv", sc: "", tc: ""})');
  window.renderBatch();
  batchJobCalls = 0;
  $('btnBatchStart').click();
  ok = await waitUntil(() => $('bStickyProgress').classList.contains('run') && $('btnBatchStart').textContent.includes('停止批量'), 5000);
  check('批量启动：run 态 + 按钮变停止批量', ok);
  ok = await waitUntil(() => $('bStickyPct').textContent === '68%', 5000);
  check('批量总体进度=68%（(0+0.68)/1 复合计算）', ok, $('bStickyPct').textContent);
  check('批量文件计数=1 / 1', $('bStickyCountNum').textContent === '1 / 1', $('bStickyCountNum').textContent);
  check('批量当前文件显示短文件名', $('bStickyCur').style.display !== 'none' && $('bStickyCurName').textContent === 'EP01.mkv', $('bStickyCurName').textContent);
  check('批量状态文字=正在批量封装', $('batchStickyNote').querySelector('.sticky-txt').textContent === '正在批量封装', $('batchStickyNote').textContent);
  check('批量运行中耗时格式正确', /^\d{2}:\d{2}:\d{2}$/.test($('bStickyElapsed').textContent), $('bStickyElapsed').textContent);
  ok = await waitUntil(() => !$('bStickyProgress').classList.contains('run'), 8000);
  check('批量终态：run 态移除', ok);
  check('批量完成文案含批量封装完成 · 1 个文件', $('batchStickyNote').querySelector('.sticky-txt').textContent.includes('批量封装完成 · 1 个文件'), $('batchStickyNote').textContent);
  check('批量终态 pct=100%、计数 1 / 1、剩余归 --、当前文件隐藏', $('bStickyPct').textContent === '100%' && $('bStickyCountNum').textContent === '1 / 1' && $('bStickyEta').textContent === '--:--:--' && $('bStickyCur').style.display === 'none', $('bStickyPct').textContent + '/' + $('bStickyCountNum').textContent);
  check('批量按钮恢复开始批量封装', $('btnBatchStart').textContent.includes('开始批量封装'));

  /* ---- 场景12：预览读取内封轨道后隐藏外部字幕输入 ---- */
  $('pv_video').value = 'D:\\Video\\EP01.mkv';
  $('btnPvTracks').click();
  ok = await waitUntil(() => String($('pv_subsel').value).indexOf('track:') === 0);
  check('读取内封轨道后下拉自动切到内封轨', ok, $('pv_subsel').value);
  check('外部字幕输入框与浏览按钮隐藏', $('pv_sub').style.display === 'none' && $('btnPvSub').style.display === 'none', $('pv_sub').style.display + '/' + $('btnPvSub').style.display);
  $('pv_subsel').value = 'custom';
  $('pv_subsel').dispatchEvent(new window.Event('change', { bubbles: true }));
  check('切回自定义路径后输入框恢复显示', $('pv_sub').style.display === '' && $('btnPvSub').style.display === '');

  /* ---- 场景13：连拍 job 化——逐帧进度/停止按钮/完成出图 ---- */
  $('pv_video').value = 'D:\\Video\\EP01.mkv';
  $('pv_sub').value = 'D:\\Video\\EP01.ass';
  pvGridJobCalls = 0;
  $('btnPreviewGrid').click();
  ok = await waitUntil(() => $('btnPreviewGrid').textContent.includes('停止渲染'), 5000);
  check('连拍启动：按钮变停止渲染', ok);
  ok = await waitUntil(() => $('pvGridMsg') && $('pvGridMsg').textContent.indexOf('40%') >= 0, 5000);
  check('连拍进度：渲染中 40%（第 3 / 9 步）', ok, $('pvGridMsg') && $('pvGridMsg').textContent);
  ok = await waitUntil(() => !!$('previewImg'), 6000);
  check('连拍完成：注入网格图', ok && String($('previewImg').src).indexOf('test.png') >= 0, $('previewImg') && $('previewImg').src);
  check('连拍完成：按钮恢复连拍 8 帧', $('btnPreviewGrid').textContent.includes('连拍 8 帧') && !$('btnPreviewGrid').disabled);

  /* ---- 场景14：预设管理（管理器新建/套用） + 新参数（章节/模板/标题/collect）进提交体 ---- */
  check('预设控件存在（高级选项只保留选择器，管理入口在右上角）', !!$('preset_sel') && !$('btnPresetSave') && !$('btnPresetDel') && !!$('fonts_mode') && !!$('chapters') && !!$('out_name_tmpl') && !!$('title') && !!$('presetHint'));
  check('高级选项已移除全局配置区', !$('globalSummary') && !$('btnOpenSettings'));
  matchSubsRet = { sc: 'D:\\Video\\EP01.sc.ass', tc: '' };
  fontsDirRet = { fonts_dir: 'D:\\Video\\Fonts' };
  window.pickVideoPath('D:\\Video\\EP01.mkv');
  ok = await waitUntil(() => $('sc_sub').value === 'D:\\Video\\EP01.sc.ass');
  check('场景14前置：视频与字幕就绪', ok);
  let savedBody = null;
  window.fetch = function (url, opts) {
    if (String(url).split('?')[0] === '/api/mux' && opts && opts.body) { try { savedBody = JSON.parse(opts.body); } catch (e) {} }
    return mockFetch(url, opts);
  };
  const savePresetViaManager = async (name) => {
    window.openPresetManager();
    $('pmNewBtn').click();
    const radio = window.document.querySelector('input[name="pmNewMode"][value="task"]');
    radio.checked = true; radio.dispatchEvent(new window.Event('change', { bubbles: true }));   // 以当前任务配置为基底
    $('pmName').value = name;
    $('pmSaveBtn').click();
    await sleep(60);
    $('pmClose').click();
  };
  $('sc_name').value = '简中'; $('fonts_mode').value = 'collect';   // 先改字段再经管理器存预设
  await savePresetViaManager('测试预设');
  check('管理器新建后主页面下拉立即同步', [...$('preset_sel').options].some(o => o.value === '测试预设'));
  $('preset_sel').value = '测试预设';
  $('preset_sel').dispatchEvent(new window.Event('change', { bubbles: true }));
  check('套用预设：轨道名/字体模式生效', $('sc_name').value === '简中' && $('fonts_mode').value === 'collect', $('sc_name').value + '/' + $('fonts_mode').value);
  check('dirty 提示=测试预设 · 已应用', $('presetHint').textContent === '测试预设 · 已应用', $('presetHint').textContent);
  check('状态条=◆ 测试预设 · 已应用', $('presetStatusText').textContent === '◆ 测试预设 · 已应用', $('presetStatusText').textContent);
  check('状态条按钮态：更改/解除可见，选择隐藏', $('btnPresetChange').style.display !== 'none' && $('btnPresetDetach').style.display !== 'none' && $('btnPresetPick').style.display === 'none');
  $('sc_name').value = '简中2'; $('sc_name').dispatchEvent(new window.Event('input', { bubbles: true }));
  check('改字段后 dirty 提示=已修改', $('presetHint').textContent === '测试预设 · 已修改', $('presetHint').textContent);
  check('状态条=◆ 测试预设 · 已修改', $('presetStatusText').textContent === '◆ 测试预设 · 已修改', $('presetStatusText').textContent);
  /* 解除预设：转为自定义配置，当前任务参数保留；重新应用走唯一入口 */
  $('btnPresetDetach').click();
  check('解除预设：状态=自定义配置且参数保留', $('presetStatusText').textContent === '自定义配置' && $('sc_name').value === '简中2' && $('preset_sel').value === '', $('presetStatusText').textContent + '/' + $('sc_name').value);
  check('解除后按钮态：选择可见，更改/解除隐藏', $('btnPresetPick').style.display !== 'none' && $('btnPresetChange').style.display === 'none' && $('btnPresetDetach').style.display === 'none');
  window.applyPresetToCurrentTask('测试预设');
  check('重新应用：状态恢复且轨道名回到预设值', $('presetStatusText').textContent === '◆ 测试预设 · 已应用' && $('sc_name').value === '简中', $('presetStatusText').textContent + '/' + $('sc_name').value);
  $('sc_name').value = '简中'; $('sc_name').dispatchEvent(new window.Event('input', { bubbles: true }));
  $('chapters').value = 'C:\\c.txt'; $('out_name_tmpl').value = '[G] {ep}'; $('title').value = 'T1';
  window.pickVideoPath('D:\\Video\\EP01.mkv');   // 重触发（复用已有）
  await sleep(80);
  $('btnStart').click();
  await waitUntil(() => savedBody, 3000);
  check('提交体携带章节/命名模板/标题', savedBody && savedBody.chapters === 'C:\\c.txt' && savedBody.out_name === '[G] {ep}' && savedBody.title === 'T1', savedBody && JSON.stringify({ c: savedBody.chapters, n: savedBody.out_name, t: savedBody.title }));
  window.pickVideoPath('D:\\Video\\EP06.mkv');   // 预设已选：换视频触发自动识别
  ok = await waitUntil(() => $('sc_sub').value === 'D:\\Video\\EP01.sc.ass');
  check('预设已选：换视频后轨道名不被自动识别改写', ok && $('sc_name').value === '简中', $('sc_name').value);

  /* ---- 场景14b：管理器 Footer 四态 / 列表双状态分离 / segmented / 保存语义 ---- */
  await savePresetViaManager('预设B');   // 造第二条预设（以当前任务配置为基底）
  window.openPresetManager();
  check('管理器：头部显示 正在编辑：预设B', $('pmEdHead').textContent.indexOf('正在编辑') >= 0 && $('pmEdHead').textContent.indexOf('预设B') >= 0, $('pmEdHead').textContent);
  check('单独打开不设内联 z-index（样式表层级行为不变）', $('presetModal').style.zIndex === '', $('presetModal').style.zIndex);
  /* 叠层：管理器内开文件浏览器 → 浏览器必须在预设管理器上层（modal.js 栈深 z-index） */
  window.openBrowser(function () {}, 'any', '', 'pmstack');
  check('叠层：文件浏览器显示在预设管理器上层', parseInt($('browserModal').style.zIndex || '100', 10) > parseInt($('presetModal').style.zIndex || '100', 10), 'browser=' + $('browserModal').style.zIndex);
  window.closeModal('browserModal');
  /* 焦点管理：closeModal 必须先把焦点移出弹窗再标 aria-hidden（恢复触发元素/退 body） */
  $('pmName').focus();
  window.closePresetManager();   // 触发元素是打开时的 activeElement（body）：焦点落 body，不滞留弹窗
  check('closeModal：焦点移出弹窗不滞留（aria-hidden 前提）', !$('presetModal').contains(window.document.activeElement), window.document.activeElement && window.document.activeElement.id);
  $('btnSc').focus();
  window.openBrowser(function () {}, 'any', '', 'focus');   // 触发元素=btnSc
  window.document.querySelector('.mb-item').focus();        // 模拟点击后焦点在弹窗内
  window.closeModal('browserModal');
  check('closeModal：焦点恢复到触发元素', window.document.activeElement === $('btnSc'), window.document.activeElement && window.document.activeElement.id);
  check('管理器：已有预设名称回填输入框', $('pmName').value === '预设B', $('pmName').value);
  check('管理器：Header 为 X 图标按钮（aria-label）', $('pmClose').getAttribute('aria-label') === '关闭封装预设');
  check('Footer 状态A：查看非当前任务预设 → 只有应用按钮（无保存类）', !!$('pmApplyBtn') && !$('pmSaveBtn') && !$('pmSaveApplyBtn'));
  check('Footer 左侧删除按钮存在', !!$('pmDeleteBtn') && $('pmDeleteBtn').textContent.indexOf('删除预设') >= 0);
  $('pm_f_sc_name').value = 'B改';
  $('pm_f_sc_name').dispatchEvent(new window.Event('input', { bubbles: true }));
  check('Footer 状态B：未保存修改 → 保存修改/保存并应用（无应用按钮）', !!$('pmSaveBtn') && !!$('pmSaveApplyBtn') && !$('pmApplyBtn'));
  check('编辑头部出现未保存指示', $('pmEdHead').textContent.indexOf('未保存') >= 0, $('pmEdHead').textContent);
  const pmSegBtn = window.document.querySelector('#pm_f_sc_default_seg .seg-btn[data-v="1"]');
  pmSegBtn.click();
  check('默认轨 segmented 点选写入隐藏域并高亮', $('pm_f_sc_default').value === '1' && pmSegBtn.classList.contains('active'), $('pm_f_sc_default').value);
  window.document.querySelector('.pm-item[data-name="测试预设"]').click();   // 未保存切换保护（confirm mock=true → 放行）
  check('未保存切换保护放行后切换到测试预设', $('pmEdHead').textContent.indexOf('测试预设') >= 0, $('pmEdHead').textContent);
  check('Footer 状态C：查看当前任务预设未修改 → 无应用/保存按钮 + 正在使用提示', !$('pmApplyBtn') && !$('pmSaveBtn') && $('pmFootActions').textContent.indexOf('当前任务正在使用') >= 0, $('pmFootActions').textContent);
  check('列表：选中高亮与当前任务徽章同落测试预设', window.document.querySelector('.pm-item[data-name="测试预设"]').classList.contains('selected') && !!window.document.querySelector('.pm-item[data-name="测试预设"] .pm-cur'));
  window.document.querySelector('.pm-item[data-name="预设B"]').click();
  check('列表双状态可分离：选中在预设B、徽章在测试预设', window.document.querySelector('.pm-item[data-name="预设B"]').classList.contains('selected') && !!window.document.querySelector('.pm-item[data-name="测试预设"] .pm-cur') && !window.document.querySelector('.pm-item[data-name="预设B"] .pm-cur'));
  $('pmApplyBtn').click();   // 状态A 的应用按钮：当前任务切到 预设B
  check('应用按钮：当前任务切到预设B', $('presetStatusText').textContent.indexOf('预设B') >= 0, $('presetStatusText').textContent);
  check('应用后 Footer 切到状态C（正在使用）', !$('pmApplyBtn') && $('pmFootActions').textContent.indexOf('当前任务正在使用') >= 0);
  /* 保存语义：保存修改不自动改当前任务；保存并应用刷新当前任务 */
  $('pm_f_sc_name').value = '存后新名';
  $('pm_f_sc_name').dispatchEvent(new window.Event('input', { bubbles: true }));
  $('pmSaveBtn').click();
  await sleep(60);
  check('保存修改不自动改当前任务参数', $('sc_name').value === '简中', $('sc_name').value);
  check('保存后回到干净态（状态C）', !$('pmSaveBtn') && $('pmFootActions').textContent.indexOf('当前任务正在使用') >= 0);
  $('pm_f_sc_name').value = '存应新名';
  $('pm_f_sc_name').dispatchEvent(new window.Event('input', { bubbles: true }));
  $('pmSaveApplyBtn').click();
  await sleep(60);
  check('保存并应用刷新当前任务参数', $('sc_name').value === '存应新名', $('sc_name').value);
  /* 空名预设 UI fallback（仅显示层，不回写持久化名称） */
  $('pmCancelBtn').click();
  presetStore[''] = { sc_name: 'SC' };
  await window.loadPresets();
  window.openPresetManager();
  check('空名预设列表显示 未命名预设 1', window.document.querySelector('.pm-item[data-name=""]') && window.document.querySelector('.pm-item[data-name=""]').textContent.indexOf('未命名预设 1') >= 0);
  $('pmCancelBtn').click();
  delete presetStore[''];
  await window.loadPresets();
  window.applyPresetToCurrentTask('测试预设');   // 恢复场景14 结束态（来源/记忆/参数）
  check('恢复：预设来源与记忆回到测试预设', window.localStorage.getItem('muxui_preset') === '测试预设' && $('sc_name').value === '简中');

  /* ---- 场景15：批量章节自动匹配 + 预设套用批量字段 ---- */
  chaptersRet = { chapters: 'D:\\Video\\EP01.chapters.txt' };
  window.eval('batchItems.length = 0');
  window.eval('batchItems.push({video: "D:/Video/EP01.mkv", sc: "", tc: "", chapters: ""})');
  window.renderBatch();
  $('b_v_0').value = 'D:\\Video\\EP01.mkv';
  $('b_v_0').dispatchEvent(new window.Event('change', { bubbles: true }));
  await waitUntil(() => $('b_c_0') && $('b_c_0').value.indexOf('chapters.txt') >= 0);
  check('批量行视频变更自动匹配章节文件', $('b_c_0').value === 'D:\\Video\\EP01.chapters.txt', $('b_c_0').value);
  check('章节入批量数据（随队列持久化/提交体）', window.eval('batchItems[0].chapters') === 'D:\\Video\\EP01.chapters.txt');
  window.eval('batchDelSub(0, "chapters")');
  check('移除章节按钮清空输入与数据', $('b_c_0').value === '' && window.eval('batchItems[0].chapters') === '');
  $('sc_name').value = '简中'; $('fonts_mode').value = 'collect'; $('sc_default').value = '1'; $('b_out_name_tmpl').value = '';
  await savePresetViaManager('测试预设');   // 同名覆盖保存（与原「保存当前为预设」语义一致）
  await sleep(60);
  $('b_sc_default').value = ''; $('b_out_name_tmpl').value = '旧值'; $('b_fonts_mode').value = 'subset';
  $('preset_sel').value = '测试预设';
  $('preset_sel').dispatchEvent(new window.Event('change', { bubbles: true }));
  check('预设同时套用批量公共字段', $('b_fonts_mode').value === 'collect' && $('b_out_name_tmpl').value !== '旧值' && $('b_sc_default').value === '1', $('b_fonts_mode').value + '/' + $('b_out_name_tmpl').value + '/' + $('b_sc_default').value);

  /* ---- 场景15b：预设记忆（刷新恢复）与重置基线 ---- */
  check('选择预设即写入记忆', window.localStorage.getItem('muxui_preset') === '测试预设', String(window.localStorage.getItem('muxui_preset')));
  $('preset_sel').value = ''; $('sc_name').value = 'SC2'; $('sc_name').dispatchEvent(new window.Event('input', { bubbles: true }));
  window.restoreRememberedPreset();   // 模拟刷新后的恢复路径
  check('预设记忆恢复：选择器恢复并自动套用', $('preset_sel').value === '测试预设' && $('sc_name').value === '简中' && $('fonts_mode').value === 'collect', $('preset_sel').value + '/' + $('sc_name').value + '/' + $('fonts_mode').value);
  window.localStorage.setItem('muxui_preset', '已被删除的预设');
  $('preset_sel').value = '';
  window.restoreRememberedPreset();
  check('预设记忆：名称失效回落并清记忆', $('preset_sel').value === '' && window.localStorage.getItem('muxui_preset') === null, $('preset_sel').value + '/' + String(window.localStorage.getItem('muxui_preset')));
  // 重置基线：已选预设时重置 = 清回默认后重新套用，选择器保留
  ok = await waitUntil(() => !window.eval('job'), 6000);   // 等场景14的封装任务终态（job 清空）再重置
  $('preset_sel').value = '测试预设';
  $('sc_name').value = '改过';
  $('btnSingleReset').click();   // confirm 已 mock 为 true
  await sleep(30);
  check('重置=回到预设基线且选择保留', $('sc_name').value === '简中' && $('preset_sel').value === '测试预设', $('sc_name').value + '/' + $('preset_sel').value);
  window.pickVideoPath('D:\\Video\\EP01.mkv');   // 重置清了视频：补回，供场景19 章节提取使用

  /* ---- 场景16：mkvmerge 命令查看/复制 ---- */
  check('批量结果行带命令按钮', !!window.document.querySelector('[data-cmd]'), 'no data-cmd btn');
  const cmdBtn = window.document.querySelector('[data-cmd]');
  cmdBtn.click();
  ok = await waitUntil(() => $('cmdPop'));
  check('点击命令按钮弹出命令弹窗（base64 解码）', ok && $('cmdPop').textContent.includes('mkvmerge -o out.mkv in.mkv sub.ass'), $('cmdPop') && $('cmdPop').textContent.slice(0, 80));
  $('cmdClose').click();
  check('关闭命令弹窗', !$('cmdPop'));

  /* ---- 场景17：字幕内容体检 UI ---- */
  $('sc_sub').value = 'D:\\Video\\EP01.sc.ass';
  $('sc_sub').dispatchEvent(new window.Event('change', { bubbles: true }));
  $('btnSubCheck').click();
  ok = await waitUntil(() => $('subCheckBox') && $('subCheckBox').innerHTML.indexOf('第12行') >= 0);
  check('字幕体检渲染预警汇总与明细', ok && $('subCheckBox').innerHTML.indexOf('CPS 超速 2') >= 0 && $('subCheckBox').innerHTML.indexOf('时间重叠 1') >= 0, $('subCheckBox') && $('subCheckBox').innerHTML.slice(0, 150));

  /* ---- 场景17b：统一字幕检查（编码→内容→字体 串行编排） ---- */
  check('统一入口按钮存在', !!$('btnSubtitleCheck') && $('btnSubtitleCheck').textContent.includes('字幕检查'));
  $('btnSubtitleCheck').click();
  ok = await waitUntil(() => ($('status').textContent || '').indexOf('字幕检查完成') >= 0, 4000);
  check('统一检查完成：汇总含 SC 分段与编码/内容/字体', ok && ($('status').textContent || '').indexOf('SC') >= 0 && ($('status').textContent || '').indexOf('编码') >= 0 && ($('status').textContent || '').indexOf('内容') >= 0 && ($('status').textContent || '').indexOf('字体') >= 0, $('status').textContent);
  check('统一检查结束：按钮恢复可用与原标签', !$('btnSubtitleCheck').disabled && $('btnSubtitleCheck').textContent.includes('字幕检查'), $('btnSubtitleCheck').textContent);
  check('检查后 sticky 未受干扰', stickyTxt().length > 0);

  /* ---- 场景18：快速修补（读取轨道 → 改动标记 → 应用） ---- */
  window.switchMode('propedit');
  check('快速修补 tab 切换', window.document.getElementById('mode-propedit').classList.contains('active'));
  $('pe_video').value = 'D:\Video\EP01.mkv';
  $('btnPeProbe').click();
  ok = await waitUntil(() => $('peList') && $('peList').querySelector('tr[data-tid]'));
  check('快速修补读取轨道并生成编辑行', ok && window.document.querySelectorAll('#peList tr[data-tid]').length >= 2);
  const peRow = $('peList').querySelector('tr[data-tid]');
  peRow.querySelector('.pe-name').value = '改名';
  peRow.querySelector('.pe-name').dispatchEvent(new window.Event('input', { bubbles: true }));
  check('改动字段标记已改', peRow.querySelector('.pe-state').textContent === '已改');
  $('pe_title').value = 'T2';
  $('btnPeApply').click();
  await sleep(60);
  check('应用修补成功提示', $('peRes').innerHTML.indexOf('修改已应用') >= 0, $('peRes').innerHTML.slice(0, 100));

  /* ---- 场景19：QC 汇总/结果 chip + 章节编辑器 ---- */
  check('批量结果行 QC chip', $('batchResults').innerHTML.indexOf('QC通过') >= 0, $('batchResults').innerHTML.slice(0, 200));
  check('批量终态 QC 汇总', $('batchState').textContent.includes('QC 通过 1/1'), $('batchState').textContent);
  $('chapters').value = '';
  $('btnChEdit').click();
  check('空章节直接打开编辑器', $('chEditModal').style.display === 'flex');
  $('btnChFromVideo').click();
  ok = await waitUntil(() => $('chEditText').value.indexOf('CHAPTER01') >= 0);
  check('从源视频提取章节进编辑器', ok && $('chEditText').value.indexOf('OP') >= 0, $('chEditText').value.slice(0, 60));
  $('btnChSave').click();
  ok = await waitUntil(() => $('chapters').value.indexOf('ch_edited.txt') >= 0);
  check('保存章节生成文件并回填', ok && $('chEditModal').style.display === 'none', $('chapters').value);

  const failed = results.filter(r => !r.ok);
  console.log('\n=== ' + (results.length - failed.length) + '/' + results.length + ' PASS ===');
  window.close();
  process.exit(failed.length ? 1 : 0);
})().catch(e => { console.error('FATAL', e); process.exit(2); });
