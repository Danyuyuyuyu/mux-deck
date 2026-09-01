/* 封装前检查：getPreflightResult / 阻断条 / 确认弹窗 / 修复动作分发。运行时读取 single.js 的共享状态
 * （job/subCheckUi/fontState/probeCache 等）——classic script 共享全局，勿引入状态层。 */

/* ==================== 封装前检查（preflight） ====================
 * 汇总既有状态与轻量检查，返回 { blocking, warnings, info }；不重跑昂贵检查：
 * 字幕内容/字体体检仅在结果未过期时计为 warning，过期或未跑降级为 info 引导用户手动运行。
 * single 域状态一律经 getSingleValidationState() 读取，不直接触其内部变量。 */
async function getPreflightResult() {
  const s = getSingleValidationState();
  const video = s.video;
  const sc = s.sc, tc = s.tc;
  const outDir = s.outDir;
  const items = [];
  const add = (type, code, title, description, source, action) => items.push({ type, code, title, description, source, action: action || '' });
  let fs = {};
  try {
    fs = await api('/api/preflight_fs', { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify({ video: video, sc: sc, tc: tc, out_dir: outDir }) }) || {};
  } catch (ex) { fs = {}; }
  let out = null;
  try {
    out = await api('/api/out_preview', { method:'POST', headers:{'Content-Type':'application/json'}, body: JSON.stringify({ video: video, template: $('out_name_tmpl').value.trim(), title: s.title, out_dir: outDir, height: s.height }) });
  } catch (ex) { out = null; }

  /* 视频 */
  if (!video) add('error', 'no_video', '尚未选择视频', '选择视频后才能开始封装。', 'video', 'pick_video');
  else if (fs.video_ok === false) add('error', 'video_missing', '无法读取视频文件', '文件可能已移动、被删除或无访问权限。', 'video', 'pick_video');
  else if (s.probe && s.probe.error)
    add('warning', 'media_info', '无法读取完整媒体信息', '仍可尝试封装，但轨道信息可能不完整。', 'video', '');
  /* 字幕文件与状态 */
  if (sc && fs.sc_ok === false) add('error', 'sub_missing_sc', '简体字幕文件不存在', '文件可能已被移动或删除，请重新选择。', 'subtitle_sc', 'pick_sub_sc');
  if (tc && fs.tc_ok === false) add('error', 'sub_missing_tc', '繁体字幕文件不存在', '文件可能已被移动或删除，请重新选择。', 'subtitle_tc', 'pick_sub_tc');
  if (!sc && !tc) add('warning', 'no_subtitle', '未提供字幕', '将保留源字幕与源字体（无新字幕时不做字体子集化）。', 'subtitle', '');
  [['sc', '简体'], ['tc', '繁体']].forEach(function (pair) {
    const kind = pair[0], label = pair[1];
    if (!$(kind + '_sub').value.trim()) return;
    const enc = s.enc[kind];
    if (enc.indexOf('错误') === 0) add('warning', 'enc_' + kind, label + '字幕编码检查未通过', enc, 'subtitle_' + kind, '');
    else if (enc.indexOf('歧义') >= 0) add('warning', 'enc_ambig_' + kind, label + '字幕编码存在歧义', enc, 'subtitle_' + kind, '');
    const st = s.checks[kind];
    if (st) {
      if (st.cls === 'warn') add('warning', 'subcheck_' + kind, label + '字幕内容体检有预警', st.text + '（校对参考，不影响封装）', 'subtitle_' + kind, 'view_subcheck');
      else if (st.cls === 'err') add('warning', 'subcheck_err_' + kind, label + '字幕内容体检失败', '未能完成内容分析，可重新运行体检。', 'subtitle_' + kind, '');
      else if (st.fresh === false) add('info', 'subcheck_stale_' + kind, label + '字幕内容已变更', '先前的体检结果已过期，可在字幕区重新运行。', 'subtitle_' + kind, '');
    } else add('info', 'subcheck_none_' + kind, label + '字幕尚未内容体检', '可在字幕区运行「内容体检」。', 'subtitle_' + kind, '');
  });
  /* 字体 */
  if (sc || tc) {
    if (s.fonts.status === 'warn') add('warning', 'missing_fonts', '缺少 ' + s.fonts.missing + ' 个字体', '字幕可以继续封装，但播放效果可能异常。', 'fonts', 'view_fonts');
    else if (s.fonts.status === 'error') add('warning', 'font_check_failed', '字体体检失败', '未能确认字体是否齐全，可重试体检。', 'fonts', '');
    else if (!s.fonts.fresh) add('info', 'fonts_not_checked', '字体尚未检查', '可在「字体设置」中运行字体体检。', 'fonts', '');
  }
  /* 输出 */
  if (outDir && fs.out_dir_ok === false) add('error', 'out_dir_missing', '输出目录不存在', '请检查输出目录路径。', 'output', 'pick_out');
  else if (outDir && fs.out_dir_writable === false) add('error', 'out_dir_unwritable', '输出目录不可写', '没有写入权限或路径无效。', 'output', 'pick_out');
  if (out && out.error) add('warning', 'out_unknown', '输出路径预览失败', out.error, 'output', '');
  else if (out && out.full) {
    if (out.unresolved_res) add('info', 'res_unresolved', '{res} 暂无法解析', '读取视频信息后自动补全。', 'output', '');
    if (out.exists) add('warning', 'out_exists', '目标文件已存在', out.full + '（继续封装将覆盖该文件）', 'output', '');
    if (out.replace) {
      const backupOn = $('backup').checked;
      add('warning', 'replace_source', '将替换原视频', '输出目录为空：原文件将' + (backupOn ? '备份到 __mux_tmp_manual 后替换。' : '被直接替换。'), 'output', '');
      if (!backupOn) add('warning', 'no_backup_replace', '原文件可能无法恢复', '替换原视频且未启用备份。', 'output', 'enable_backup');
    }
  }
  /* 工具 / 环境 */
  if (ENV.overall === 'broken') add('error', 'env_broken', '封装组件缺失', '必需组件不可用，请打开环境检测安装。', 'env', 'open_env');
  else if (ENV.overall === 'partial' && (sc || tc) && $('fonts_mode').value === 'subset')
    add('warning', 'tool_partial', '子集化组件部分缺失', '可选组件不可用，子集化可能回退或跳过。', 'env', 'open_env');
  if ($('force').checked) add('warning', 'force_enabled', '已启用强制封装', '源视频已有字体附件时将强制重建附件。', 'task', '');
  return {
    blocking: items.filter(function (i) { return i.type === 'error'; }),
    warnings: items.filter(function (i) { return i.type === 'warning'; }),
    info: items.filter(function (i) { return i.type === 'info'; })
  };
}

/* --- 渲染：条目 / 阻断条 / 确认弹窗 --- */
const PF_ACTION_LABEL = { pick_video: '重新选择', pick_sub_sc: '更换字幕', pick_sub_tc: '更换字幕', pick_out: '重新选择',
  view_fonts: '前往字体设置', view_subcheck: '查看体检结果', open_env: '前往设置', enable_backup: '启用备份' };
function pfItemHtml(it, withAction) {
  const icn = it.type === 'error' ? 'xCircle' : it.type === 'warning' ? 'alertTriangle' : 'info';
  let h = '<div class="pf-item ' + it.type + '"><span class="pf-ic">' + ic(icn) + '</span><div class="pf-main"><div class="pf-title">' + esc(it.title) + '</div>'
    + (it.description ? '<div class="pf-desc">' + esc(it.description) + '</div>' : '') + '</div>';
  if (withAction && it.action) h += '<button type="button" class="btn small" data-pf-action="' + esc(it.action) + '">' + esc(PF_ACTION_LABEL[it.action] || '处理') + '</button>';
  return h + '</div>';
}
function hidePreflightIssues() {
  const el = $('preflightBox');
  if (el) { el.style.display = 'none'; el.innerHTML = ''; }
}
function showPreflightIssues(pf) {
  const el = $('preflightBox');
  if (!el) return;
  let h = '<div class="pf-strip-head">' + ic('xCircle') + '<span>还有 ' + pf.blocking.length + ' 项需要处理，已阻止开始封装</span></div>'
    + pf.blocking.map(function (it) { return pfItemHtml(it, true); }).join('');
  if (pf.warnings.length) h += '<div class="pf-strip-note">另有 ' + pf.warnings.length + ' 项提醒，处理阻断项后开始时会再确认。</div>';
  el.innerHTML = h;
  el.style.display = '';
}
function openPreflightModal(pf) {
  $('pfHead').textContent = '发现 ' + pf.warnings.length + ' 个需要确认的项目';
  $('pfWarnList').innerHTML = pf.warnings.map(function (it) { return pfItemHtml(it, true); }).join('');
  const danger = pf.warnings.some(function (w) { return w.code === 'no_backup_replace'; });
  $('pfDanger').style.display = danger ? '' : 'none';
  $('pfDanger').innerHTML = ic('alertTriangle') + '<span>将覆盖原视频且未启用备份，原文件可能无法恢复。</span>';
  const hasInfo = pf.info.length > 0;
  $('pfToggleAll').style.display = hasInfo ? '' : 'none';
  $('pfInfoSec').style.display = 'none';
  $('pfInfoList').innerHTML = pf.info.map(function (it) { return pfItemHtml(it, false); }).join('');
  openModal('pfModal', { closeOnBackdrop: false, closeOnEscape: false });   // 确认弹窗：不允许误触关闭
}
/* 修复动作统一分发（就近跳转，不堆 Alert） */

/* ==================== 初始化（由 init.js bootstrap 统一调用，仅执行一次） ==================== */
function initPreflight() {
$('pfCancel').onclick = function () { closeModal('pfModal'); };
$('pfClose').onclick = function () { closeModal('pfModal'); };
$('pfProceed').onclick = function () { closeModal('pfModal'); startMuxTask(); };
$('pfToggleAll').onclick = function () {
  const sec = $('pfInfoSec');
  sec.style.display = sec.style.display === 'none' ? '' : 'none';
};
document.addEventListener('click', function (e) {
  const el = e.target.closest('[data-pf-action]');
  if (!el) return;
  const a = el.dataset.pfAction;
  if (a === 'pick_video') openBrowser(pickVideoPath, 'video', $('video').value, 'video');
  else if (a === 'pick_sub_sc') browseSub('sc');
  else if (a === 'pick_sub_tc') browseSub('tc');
  else if (a === 'pick_out') openBrowser(function (v) { $('out_dir').value = v; scheduleOutPreview(); }, 'dir', $('out_dir').value, 'out');
  else if (a === 'view_fonts') { toggleCollapse('fontsSec', true); $('fontsSec').scrollIntoView({ behavior: 'smooth', block: 'center' }); }
  else if (a === 'view_subcheck') $('subCheckBox').scrollIntoView({ behavior: 'smooth', block: 'center' });
  else if (a === 'open_env') openEnv();
  else if (a === 'enable_backup') { $('backup').checked = true; }
});
}
