/* ==================== 任务公共模块（task.js） ====================
 * 单个封装与批量封装共用的任务处理逻辑：
 *   setRunButton      按钮开始/停止切换（play↔square、primary↔danger）
 *   muxStage          从任务日志判断阶段（子集化中/封装中/处理中 + 进度）
 *   setStickyRun      sticky 操作栏运行态样式与文本
 *   startTaskPolling 任务轮询骨架（failCount 重试 + 终态分发）
 *   buildMuxCommon   封装公共参数构造（字体/输出/备份/旗标，单批量同一份）
 * 依赖：api / ic / setStatus（app.js），加载顺序 app → identify → task → batch */

/* 按钮运行态切换：running=true 显示「停止」样式，false 恢复「开始」 */
function setRunButton(btn, running, stopLabel, startLabel) {
  btn.disabled = false;
  btn.innerHTML = ic(running ? 'square' : 'play') + '<span>' + (running ? stopLabel : startLabel) + '</span>';
  btn.classList.toggle('danger', running);
  btn.classList.toggle('primary', !running);
}

/* 从任务状态/日志判断封装阶段 */
function muxStage(s) {
  const lg = s.log || '';
  const subsetting = /Subset tool|subsetting|assfonts|AFS:/i.test(lg);
  const muxing = /Muxing/i.test(lg);
  let label;
  if (s.progress != null) label = '封装中 ' + s.progress + '%';
  else if (muxing) label = '封装中…';
  else if (subsetting) label = '子集化中…';
  else label = '处理中…';
  return { progress: s.progress, subsetting, label };
}

/* sticky 操作栏运行态（note 需含 .sticky-txt 子元素） */
function setStickyRun(note, text) {
  note.className = 'sticky-note run';
  note.firstElementChild.innerHTML = ic('loader', 'spin');
  note.querySelector('.sticky-txt').textContent = text;
}

/* 任务轮询骨架：每 interval 轮询 /api/job，终态自动停止并分发；连续 5 次失败触发 onLost。
 * cfg: { job, interval, onAny(每轮含终态), onTick(运行中), onDone, onError, onKilled, onLost }
 * 返回 { stop } */
function startTaskPolling(cfg) {
  let failCount = 0;
  let timer = null;
  const stop = () => { if (timer) { clearInterval(timer); timer = null; } };
  timer = setInterval(async () => {
    let s;
    try {
      s = await api('/api/job?id=' + cfg.job);
      failCount = 0;
      if (cfg.onAny) cfg.onAny(s);
      if (s.status === 'done') { stop(); if (cfg.onDone) cfg.onDone(s); }
      else if (s.status === 'error') { stop(); if (cfg.onError) cfg.onError(s); }
      else if (s.status === 'killed') { stop(); if (cfg.onKilled) cfg.onKilled(s); }
      else if (cfg.onTick) cfg.onTick(s);
    } catch (ex) {
      if (++failCount >= 5) { stop(); if (cfg.onLost) cfg.onLost(); }
    }
  }, cfg.interval || 1200);
  return { stop };
}

/* 封装公共参数构造：prefix 为元素 id 前缀（单封装 ''，批量 'b_'）。
 * 取值：fonts_dir/out_dir/sc_default/tc_default 取 value，force/backup/sc_forced/tc_forced 取 checked */
function buildMuxCommon(prefix) {
  const el = id => $(prefix + id);
  const v = id => (el(id) ? String(el(id).value || '') : '');
  const c = id => !!(el(id) && el(id).checked);
  return {
    fonts_dir: v('fonts_dir').trim(),
    out_dir: v('out_dir').trim(),
    out_name: v('out_name_tmpl').trim(),
    title: v('title').trim(),
    fonts_mode: v('fonts_mode') || 'subset',
    force: c('force'),
    no_backup: !c('backup'),
    skip_existing: c('b_skip'),
    sc_default: v('sc_default') || '',
    tc_default: v('tc_default') || '',
    sc_forced: c('sc_forced'),
    tc_forced: c('tc_forced'),
  };
}
