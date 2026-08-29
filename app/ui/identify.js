/* ==================== 识别服务（identify） ====================
 * 字幕与字体目录的统一识别入口：单个封装与批量封装共用同一逻辑。
 * 未来扩充识别范围（如音轨、封面等）只需在此函数内扩展返回字段，调用点无需改动。
 * identify(video) -> Promise<{sc, tc, fontsDir}>（失败项返回空串，不抛错） */
async function identify(video) {
  const [m, fd] = await Promise.all([
    api('/api/match_subs?path=' + encodeURIComponent(video)).catch(() => ({})),
    api('/api/detect_fonts_dir?path=' + encodeURIComponent(video)).catch(() => ({})),
  ]);
  return { sc: m.sc || '', tc: m.tc || '', fontsDir: fd.fonts_dir || '' };
}
/* 便捷：把识别结果填入输入框（已有值不覆盖） */
function applyIdentify(inputSc, inputTc, inputFonts, id) {
  let changed = false;
  if (id.sc && inputSc && !inputSc.value.trim()) { inputSc.value = id.sc; changed = true; }
  if (id.tc && inputTc && !inputTc.value.trim()) { inputTc.value = id.tc; changed = true; }
  if (id.fontsDir && inputFonts && !inputFonts.value.trim()) { inputFonts.value = id.fontsDir; changed = true; }
  return changed;
}
