/* ==================== 环境检测与安装（初始启动引导） ==================== */
const ENV = { overall: 'unknown', missing: [], installing: false };
function envRow(i) {
  const st = i.status === 'ok' ? 'ok' : 'missing';
  const ico = i.status === 'ok' ? 'checkCircle' : 'xCircle';
  const label = i.status === 'ok' ? '就绪' : '缺失';
  const ess = i.essential ? '（必需）' : '';
  const path = i.path
    ? '<div class="env-path">' + esc(i.path) + '</div>'
    : '<div class="env-path">未找到</div>';
  return '<div class="env-row ' + st + '"><span class="env-ic" data-ic="' + ico + '"></span>' +
    '<div class="env-main"><div class="env-name">' + esc(i.name) + ess + '<span class="t-cap">' + esc(i.hint || '') + '</span></div>' + path + '</div>' +
    '<span class="chip sm ' + st + '">' + label + '</span></div>';
}
function envRender(d) {
  ENV.overall = d.overall; ENV.missing = d.missing || [];
  const list = $('envList');
  list.innerHTML = (d.items || []).map(envRow).join('');
  list.querySelectorAll('[data-ic]').forEach(el => { el.innerHTML = ic(el.dataset.ic); });
  const note = $('envNote');
  if (d.overall === 'ready') {
    note.style.color = 'var(--ok-line)';
    note.textContent = '全部组件就绪：可正常封装、提取与预览。';
  } else if (d.overall === 'partial') {
    note.style.color = 'var(--warn-line)';
    note.textContent = '核心工具已就绪，仅缺可选组件（子集工具 / AFS 后端）——功能可用，建议补齐。';
  } else {
    note.style.color = 'var(--err-line)';
    note.textContent = '存在必需组件缺失，请点击「一键安装缺失组件」下载补齐；完成后自动重新检测。';
  }
  $('btnEnvInstall').disabled = ENV.installing || !ENV.missing.length;
}
async function envLoad() {
  try {
    const d = await api('/api/env_check');
    if (d && d.items) envRender(d);
  } catch (ex) { /* 断线时由 offlineBar 提示 */ }
}
function openEnv() {
  openModal('envModal');
  $('envInstallBox').style.display = 'none';
  $('envLog').textContent = '';
  envLoad();
}
function closeEnv() {
  closeModal('envModal');
  // 环境引导关闭后，若工作目录还没设置过，接着引导设置工作目录
  api('/api/config').then(c => { if (c && (!c.configured || !c.valid)) showSetup(); }).catch(() => {});
}

/* ==================== 初始化（由 init.js bootstrap 统一调用，仅执行一次） ==================== */
function initEnv() {
$('btnEnvInstall').onclick = async () => {
  if (ENV.installing) return;
  ENV.installing = true;
  const btn = $('btnEnvInstall');
  btn.disabled = true;
  const box = $('envInstallBox'), log = $('envLog');
  box.style.display = 'block';
  log.textContent = '准备安装：' + (ENV.missing.join('、') || '无') + ' …';
  log.scrollTop = log.scrollHeight;
  try {
    const r = await api('/api/env_install', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ proxy: $('envProxy').value.trim(), items: ENV.missing }) });
    if (r.error) { log.textContent += '\n' + r.error; return; }
    if (!r.id) { log.textContent += '\n' + (r.note || '没有需要安装的组件'); await envLoad(); return; }
    const id = r.id;
    await new Promise((resolve) => {
      const timer = setInterval(async () => {
        try {
          const s = await api('/api/env_install?id=' + encodeURIComponent(id));
          if (!s || s.error) { clearInterval(timer); log.textContent += '\n' + ((s && s.error) || '查询失败'); resolve(); return; }
          log.textContent = s.log || '';
          log.scrollTop = log.scrollHeight;
          if (s.done) {
            clearInterval(timer);
            log.textContent += '\n' + (s.ok
              ? '\n全部安装完成，已自动重新检测。'
              : '\n部分组件安装失败（' + (s.fail || []).join('、') + '），可调整代理后重试。');
            resolve();
          }
        } catch (ex) {
          clearInterval(timer);
          log.textContent += '\n查询失败：' + ex;
          resolve();
        }
      }, 1000);
    });
  } catch (ex) {
    log.textContent += '\n安装请求失败：' + ex;
  } finally {
    ENV.installing = false;
    btn.disabled = false;
    await envLoad();
  }
};
$('btnEnvRefresh').onclick = envLoad;
$('envClose').onclick = closeEnv;
}
