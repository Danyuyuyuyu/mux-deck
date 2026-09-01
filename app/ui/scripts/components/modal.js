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
  /* 叠层 z-index：仅当盖在别的 modal 之上（栈深>1）时按栈深抬升（100 为 .modal 基准层），
   * 保证后开的 modal 在上层（如预设管理器里开文件浏览器）；单独打开不动各 modal 现有 z-index。 */
  if (MODAL_STACK.length > 1) el.style.zIndex = String(100 + (MODAL_STACK.length - 1) * 10);
  el.setAttribute('aria-hidden', 'false');
  if (MODAL_STACK.length === 1) document.body.classList.add('modal-open');   // 页面滚动锁：消除页面+Modal 双滚动
}

function closeModal(id) {
  const el = typeof id === 'string' ? document.getElementById(id) : id;
  if (!el) return;
  const idx = MODAL_STACK.findIndex(m => m.el === el);
  const m = idx >= 0 ? MODAL_STACK[idx] : null;
  /* beforeClose：关闭前守卫（返回 false 中止关闭）——如预设管理器有未保存修改时确认丢弃。
   * Esc / backdrop / 业务语义关闭函数（closeXxx）统一经过这里，守卫一处生效。 */
  if (m && typeof m.options.beforeClose === 'function' && m.options.beforeClose() === false) return;
  if (idx >= 0) MODAL_STACK.splice(idx, 1);   // 未入栈：m=null，只做隐藏容错
  /* 焦点必须先移出弹窗、再标 aria-hidden（反序会触发 Chrome
   * "Blocked aria-hidden … descendant retained focus" 告警）：先恢复触发元素焦点，
   * 恢复目标不可聚焦/断连时焦点仍滞留弹窗内，则 blur 退到 body。 */
  const t = m ? (m.options.returnFocus || m.trigger) : null;
  if (t && t.isConnected) t.focus();
  if (el.contains(document.activeElement) && document.activeElement.blur) document.activeElement.blur();
  el.style.display = 'none';
  el.setAttribute('aria-hidden', 'true');
  if (!MODAL_STACK.length) document.body.classList.remove('modal-open');
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
