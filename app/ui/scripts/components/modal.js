/* ==================== Modal 公共控制层 ====================
 * 统一"怎么打开/关闭"：display 模式、Esc / backdrop 关闭、触发焦点记录与恢复、
 * aria-hidden 同步、栈式管理（Esc 只关最上层，允许 browserModal 这类合法叠层）。
 * 业务层保留 openXxx / closeXxx 函数决定"为什么打开"，本层只负责机制。
 * 不改变任何 modal 的 DOM / 样式 / 字段 / 确认逻辑。 */
const MODAL_STACK = [];

function isModalOpen(id) {
  const el = typeof id === 'string' ? document.getElementById(id) : id;
  return !!el && MODAL_STACK.some(m => m.el === el);
}

function openModal(id, options) {
  const el = typeof id === 'string' ? document.getElementById(id) : id;
  if (!el || isModalOpen(el)) return;
  options = options || {};
  MODAL_STACK.push({
    el,
    trigger: document.activeElement instanceof HTMLElement ? document.activeElement : null,
    options
  });
  el.style.display = options.display || 'flex';
  el.setAttribute('aria-hidden', 'false');
}

function closeModal(id) {
  const el = typeof id === 'string' ? document.getElementById(id) : id;
  if (!el) return;
  const idx = MODAL_STACK.findIndex(m => m.el === el);
  if (idx >= 0) {
    const m = MODAL_STACK.splice(idx, 1)[0];
    el.style.display = 'none';
    el.setAttribute('aria-hidden', 'true');
    const t = m.options.returnFocus || m.trigger;
    if (t && t.isConnected) t.focus();
  } else {
    el.style.display = 'none';   // 容错：未入栈的直接隐藏
    el.setAttribute('aria-hidden', 'true');
  }
}

function initModal() {
  document.addEventListener('keydown', function (e) {
    if (e.key !== 'Escape' || !MODAL_STACK.length) return;
    const top = MODAL_STACK[MODAL_STACK.length - 1];
    if (top.options.closeOnEscape === false) return;
    closeModal(top.el);
  });
  document.addEventListener('click', function (e) {
    if (!MODAL_STACK.length) return;
    const top = MODAL_STACK[MODAL_STACK.length - 1];
    if (top.options.closeOnBackdrop === false) return;
    if (e.target === top.el) closeModal(top.el);   // 仅点在遮罩自身（非内容）才关
  });
}
