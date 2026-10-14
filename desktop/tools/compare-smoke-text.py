#!/usr/bin/env python3
"""比较 Smoke UI 套件源文件里的中文文案是否与基线一致。

对 `desktop/tests/MuxDeck.Smoke` 下的三个环境/组件 UI 套件源文件，提取所有含中文字符
（U+4E00–U+9FFF）的双引号字面量，按出现顺序去重，再比较基线与目标两个版本。用于确认
重构只调整装配与调用方式，不改变用户可见文案。

统计口径（唯一权威）：一个“文案”= 文件里“含至少一个中文字符的双引号字面量的去重值”。
历史报告里环境 16 / 安装 35 / 卸载 47 那组数字来自未入库的脚本，口径已失效，以本脚本为准。

用法（脚本自行定位仓库根，可在任意目录执行）：

    python desktop/tools/compare-smoke-text.py <base-ref> [<head-ref>] [--list]

- `<base-ref>`、`<head-ref>` 为任意 git 引用（分支、标签或提交哈希）。
- 省略 `<head-ref>` 时比较基线与当前工作区文件。
- `--list` 逐条打印基线的全部文案、缺失项与目标独有项。

退出码：基线文案在目标版本中有缺失时返回 1，否则返回 0。
"""

import argparse
import re
import subprocess
import sys
from pathlib import Path

FILES = ("EnvironmentUiChecks", "ComponentInstallationUiChecks", "ComponentUninstallUiChecks")
RELATIVE = "desktop/tests/MuxDeck.Smoke/{0}.cs"
LITERAL = re.compile(r'"((?:[^"\\]|\\.)*)"')
CJK = re.compile(r"[\u4e00-\u9fff]")


def repo_root() -> Path:
    result = subprocess.run(["git", "rev-parse", "--show-toplevel"], capture_output=True)
    if result.returncode != 0:
        sys.exit("找不到仓库根：git rev-parse 失败。请在本仓库 checkout 内运行。")
    return Path(result.stdout.decode("utf-8").strip())


def read_source(root: Path, ref: str | None, relative: str) -> str:
    if ref is None:
        return (root / relative).read_text(encoding="utf-8")
    result = subprocess.run(["git", "show", f"{ref}:{relative}"], cwd=root, capture_output=True)
    if result.returncode != 0:
        sys.exit(f"读取失败：git show {ref}:{relative}\n{result.stderr.decode('utf-8', 'replace').strip()}")
    return result.stdout.decode("utf-8")


def literals(text: str) -> list[str]:
    seen: list[str] = []
    for match in LITERAL.finditer(text):
        value = match.group(1)
        if CJK.search(value) and value not in seen:
            seen.append(value)
    return seen


def main() -> int:
    parser = argparse.ArgumentParser(description="比较 Smoke UI 套件的中文文案与基线是否一致。")
    parser.add_argument("base", help="基线 git 引用")
    parser.add_argument("head", nargs="?", default=None, help="目标 git 引用；省略时用工作区文件")
    parser.add_argument("--list", action="store_true", dest="show_list", help="逐条打印文案")
    args = parser.parse_args()

    root = repo_root()
    head_label = args.head or "工作区"
    total_base = total_head = total_missing = total_new = 0
    for name in FILES:
        relative = RELATIVE.format(name)
        base = literals(read_source(root, args.base, relative))
        head = literals(read_source(root, args.head, relative))
        head_set = set(head)
        missing = [item for item in base if item not in head_set]
        only_new = [item for item in head if item not in set(base)]
        total_base += len(base)
        total_head += len(head)
        total_missing += len(missing)
        total_new += len(only_new)
        print(f"{name}: base({args.base})={len(base)} head({head_label})={len(head)} "
              f"missing={len(missing)} new={len(only_new)}")
        if args.show_list:
            for index, item in enumerate(base, start=1):
                print(f"  {index:>2}. {item}")
            for item in missing:
                print(f"  MISSING: {item}")
            for item in only_new:
                print(f"  NEW: {item}")
    print(f"合计: base={total_base} head={total_head} missing={total_missing} new={total_new}")
    return 1 if total_missing else 0


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    raise SystemExit(main())
