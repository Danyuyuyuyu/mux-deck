// Playwright 冒烟测试配置：
// - 默认自动拉起后端（py -3 app/server.py）；若 8765 已有服务在跑则直接复用（reuseExistingServer）
// - 指定 MUXUI_URL 环境变量可指向已运行的地址（此时不再自动拉起）
// - 浏览器下载慢/失败时可设置镜像：PLAYWRIGHT_DOWNLOAD_HOST=https://cdn.npmmirror.com/binaries/playwright
const path = require('path');

const BASE_URL = process.env.MUXUI_URL || 'http://127.0.0.1:8765';

module.exports = {
  testDir: __dirname,
  timeout: 30000,
  retries: 0,
  use: {
    baseURL: BASE_URL,
    viewport: { width: 1280, height: 900 },
  },
  webServer: process.env.MUXUI_URL ? undefined : {
    command: 'py -3 ' + path.join(__dirname, '..', '..', 'app', 'server.py'),
    url: BASE_URL + '/api/version',
    reuseExistingServer: true,
    timeout: 60000,
  },
  projects: [
    { name: 'chromium', use: { browserName: 'chromium' } },
  ],
};
