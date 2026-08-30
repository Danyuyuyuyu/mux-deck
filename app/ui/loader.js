/* ==================== App Shell 加载器（loader.js） ====================
 * index.html 只保留外壳；业务界面拆为 pages/（single / batch / subtitle-tools）与
 * partials/（console / modals）。启动时一次性并行加载全部片段并挂载（无 lazy loading，
 * 切换 mode 仅显示/隐藏，不重新 fetch、不丢输入状态），随后按原加载顺序注入执行业务
 * 脚本（app → identify → task → batch → extract → preview → propedit → env → init）——
 * 既有 JS 的顶层绑定代码因此运行在完整 DOM 之上，且只执行一次（无重复绑定）。
 * 片段/脚本加载失败时在 pageRoot 给出明确错误与重新加载入口，不静默白屏。 */
(function () {
  var FRAGMENTS = [
    { url: './pages/single.html', target: 'pageRoot' },
    { url: './pages/batch.html', target: 'pageRoot' },
    { url: './pages/subtitle-tools.html', target: 'pageRoot' },
    { url: './partials/console.html', target: 'consoleRoot' },
    { url: './partials/modals.html', target: 'modalRoot' }
  ];
  var SCRIPTS = ['app.js', 'identify.js', 'task.js', 'batch.js', 'extract.js', 'preview.js', 'propedit.js', 'env.js', 'init.js'];

  function fetchText(url) {
    return fetch(url).then(function (r) {
      if (!r.ok) throw new Error(url + ' → HTTP ' + r.status);
      return r.text();
    });
  }

  function mountFragment(f) {
    return fetchText(f.url).then(function (text) {
      var root = document.getElementById(f.target);
      if (!root) throw new Error('挂载点 #' + f.target + ' 不存在');
      var tpl = document.createElement('template');
      tpl.innerHTML = text;
      root.appendChild(tpl.content);
    });
  }

  function showLoadError(message) {
    var root = document.getElementById('pageRoot');
    if (!root) return;
    root.innerHTML =
      '<div class="panel" style="padding:28px;">' +
      '<h2 class="t-section">页面加载失败</h2>' +
      '<p class="t-sec">' + String(message).replace(/&/g, '&amp;').replace(/</g, '&lt;') + '</p>' +
      '<p class="t-sec">请确认后端服务已启动（start_mux_ui.bat），然后重试。</p>' +
      '<button type="button" class="btn" onclick="location.reload()">重新加载</button>' +
      '</div>';
  }

  function loadScripts() {
    return Promise.all(SCRIPTS.map(function (name) {
      return fetchText('./' + name).catch(function (ex) {
        ex.message = '脚本 ' + name + ' 加载失败：' + ex.message;
        throw ex;
      });
    })).then(function (sources) {
      // 内联 script 同步执行；按数组顺序逐个插入即为原 <script src> 的执行顺序
      sources.forEach(function (src) {
        var s = document.createElement('script');
        s.textContent = src;
        document.body.appendChild(s);
      });
    });
  }

  var loading = document.getElementById('pageLoading');
  Promise.all(FRAGMENTS.map(mountFragment))
    .then(function () {
      if (loading) loading.remove();
      return loadScripts();
    })
    .catch(function (ex) {
      if (ex && ex.message && ex.message.indexOf('脚本') === 0) console.error('[mux-ui]', ex.message);
      else console.error('[mux-ui] 片段加载失败：', ex);
      showLoadError(ex && ex.message ? ex.message : String(ex));
    });
})();
