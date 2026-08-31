/* ==================== 识别服务（identify） ====================
 * 字幕与字体目录的统一识别入口：单个封装与批量封装共用同一逻辑。
 * 未来扩充识别范围（如音轨、封面等）只需在此函数内扩展返回字段，调用点无需改动。
 * identify(video) -> Promise<{sc, tc, scLang, tcLang, fontsDir, chapters}>（失败项返回空串，不抛错） */

/* 文件名语言标签别名表（与后端 tracks.lang_of 同一张表；token 以 [._\- ] 切分，忽略大小写）。
 * 相邻两 token 以 '-' 连接的组合也参与查表（'zh'+'hans' → 'zh-hans'）。 */
const LANG_ALIAS = {
  'zh-hans': 'zh-Hans', 'zh-cn': 'zh-Hans', 'zh-chs': 'zh-Hans', 'chs': 'zh-Hans', 'sc': 'zh-Hans',
  'jpsc': 'zh-Hans', '简体': 'zh-Hans', '简': 'zh-Hans', 'gb': 'zh-Hans',
  'zh-hant': 'zh-Hant', 'zh-tw': 'zh-Hant', 'zh-hk': 'zh-Hant', 'zh-mo': 'zh-Hant', 'cht': 'zh-Hant',
  'tc': 'zh-Hant', 'jptc': 'zh-Hant', '繁体': 'zh-Hant', '繁': 'zh-Hant', 'big5': 'zh-Hant',
  'zh': 'zh', 'chi': 'zh', 'zho': 'zh',
};
/* 文件名 → 语言标签；简繁标签优先（先扫 SC/TC 别名含相邻组合，再扫裸 zh），认不出返回 '' */
function langFromName(filename) {
  const base = String(filename || '').split(/[\\/]/).pop() || '';
  const toks = base.toLowerCase().split(/[._\- ]+/).filter(Boolean);
  for (let i = 0; i < toks.length; i++) {
    if (i + 1 < toks.length) {
      const p = LANG_ALIAS[toks[i] + '-' + toks[i + 1]];
      if (p && p !== 'zh') return p;
    }
    const s = LANG_ALIAS[toks[i]];
    if (s && s !== 'zh') return s;
  }
  for (let i = 0; i < toks.length; i++) {
    if (LANG_ALIAS[toks[i]] === 'zh') return 'zh';
  }
  return '';
}

async function identify(video) {
  const [m, fd, ch] = await Promise.all([
    api('/api/match_subs?path=' + encodeURIComponent(video)).catch(() => ({})),
    api('/api/detect_fonts_dir?path=' + encodeURIComponent(video)).catch(() => ({})),
    api('/api/detect_chapters?path=' + encodeURIComponent(video)).catch(() => ({})),
  ]);
  return { sc: m.sc || '', tc: m.tc || '', scLang: m.sc_lang || '', tcLang: m.tc_lang || '',
           fontsDir: fd.fonts_dir || '', chapters: ch.chapters || '' };
}
/* 便捷：把识别结果填入输入框（已有值不覆盖）；语言回填带「自动」标记（data-auto） */
function applyIdentify(inputSc, inputTc, inputFonts, id, inputChapters, inputScLang, inputTcLang) {
  let changed = false;
  if (id.sc && inputSc && !inputSc.value.trim()) { inputSc.value = id.sc; changed = true; }
  if (id.tc && inputTc && !inputTc.value.trim()) { inputTc.value = id.tc; changed = true; }
  if (id.fontsDir && inputFonts && !inputFonts.value.trim()) { inputFonts.value = id.fontsDir; changed = true; }
  if (id.chapters && inputChapters && !inputChapters.value.trim()) { inputChapters.value = id.chapters; changed = true; }
  if (id.scLang && inputScLang && !inputScLang.value.trim()) { inputScLang.value = id.scLang; inputScLang.dataset.auto = '1'; changed = true; }
  if (id.tcLang && inputTcLang && !inputTcLang.value.trim()) { inputTcLang.value = id.tcLang; inputTcLang.dataset.auto = '1'; changed = true; }
  return changed;
}
