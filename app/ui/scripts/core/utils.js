/* 公共基础：$ / esc / fireChange / FILTERS / api / setOffline / CFG / truncMid / setStatus / fmtDur。
 * 全应用（single/batch/工具页/设置/控制台）共用；保持 classic script 全局语义，勿挂 window 命名空间。 */

/* ==================== 基础工具 ==================== */
const $ = id => document.getElementById(id);
function esc(s) { return (s || '').replace(/&/g,'&amp;').replace(/"/g,'&quot;').replace(/</g,'&lt;').replace(/>/g,'&gt;'); }
function fireChange(el) { el.dispatchEvent(new Event('change', { bubbles: true })); }
const FILTERS = {
  video: ['.mkv','.mp4','.m2ts','.ts','.avi','.mov','.webm','.flv','.wmv','.m4v'],
  sub:   ['.ass','.ssa','.srt'],
  font:  ['.ttf','.otf','.ttc','.otc','.woff','.woff2'],
  audio: ['.mka','.flac','.aac','.m4a','.mp3','.opus','.ogg','.wav','.ac3','.dts','.eac3'],
  any: null, dir: null
};
const VEXT = new Set(FILTERS.video), SEXT = new Set(FILTERS.sub), FEXT = new Set(FILTERS.font);
function isScName(n) { return /(?:^|[._\- ])(?:sc|chs|jpsc)(?:[._\- ]|$)/i.test(n); }
function isTcName(n) { return /(?:^|[._\- ])(?:tc|cht|jptc)(?:[._\- ]|$)/i.test(n); }


async function api(url, opts) {
  try {
    const r = await fetch(url, opts);
    setOffline(false);
    return r.json();
  } catch (ex) {
    if (ex instanceof TypeError) setOffline(true); // fetch 网络层失败 = 服务不可达
    throw ex;
  }
}
/* ---------- 服务断线探测：横幅 + 每 3s 重试，恢复自动隐藏 ---------- */
let __offline = null;
function setOffline(bad) {
  if (__offline === bad) return;
  __offline = bad;
  const bar = document.getElementById('offlineBar');
  if (bar) bar.style.display = bad ? 'block' : 'none';
  if (bad && !setOffline._timer) {
    setOffline._timer = setInterval(async () => {
      try {
        const r = await fetch('/api/version');
        if (r.ok) { clearInterval(setOffline._timer); setOffline._timer = null; setOffline(false); }
      } catch (e) { /* 仍在断线 */ }
    }, 3000);
  }
}
/* 初始探测一次：打开页面时后端就没在跑也能立刻给出提示 */
fetch('/api/version').then(r => { if (r.ok) setOffline(false); }).catch(() => setOffline(true));
function truncMid(t, max) {
  t = String(t || '');
  var pi = t.search(/[A-Za-z]:\\/);
  if (pi >= 0) {
    var head = t.slice(0, pi);
    var path = t.slice(pi);
    return (head.length > 10 ? truncMid(head, 10) : head) + path;
  }
  if (t.length <= max) return t;
  const keep = Math.max(4, Math.floor((max - 1) / 2));
  return t.slice(0, keep) + '…' + t.slice(t.length - keep);
}
const STATUS_ICON = { ok:'checkCircle', err:'xCircle', run:'loader', '':'info' };
function setStatus(msg, cls) {
  const s = $('status');
  const k = cls || '';
  s.innerHTML = ic(STATUS_ICON[k] || 'info', k === 'run' ? 'spin' : '') + '<span>' + esc(truncMid(String(msg || ''), 36)) + '</span>';
  s.className = k;
  s.title = String(msg || '');
}
function setResult(msg) { $('result').textContent = msg; }


function fmtDur(ms) {
  const t = Math.max(0, Math.floor(ms / 1000));
  const p = n => String(n).padStart(2, '0');
  return p(Math.floor(t / 3600)) + ':' + p(Math.floor((t % 3600) / 60)) + ':' + p(t % 60);
}
