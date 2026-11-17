"""Keeps src/ReaRadioDj.Contracts/Localization/strings.tsv up to date.

Collects every text that is translated at run time:
  - fixed texts in the studio views (.axaml): Content, Text, Header, ToolTip.Tip, Watermark, PlaceholderText, Title,
    and the text inside <TextBlock>, <Run> and <ToolTip.Tip>;
  - Tr.T("...") and Tr.F("...") in the C# code of studio, engine and core;
  - texts marked in the web pages (data-tr attributes and tr("...") in scripts).
New texts are added with empty translations; existing translations are kept. Texts that are no longer used are
removed with --prune. Run from the repository root:  python scripts/strings.py [--prune] [--missing en|de|es]
"""
import html
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
TSV = ROOT / "src" / "ReaRadioDj.Contracts" / "Localization" / "strings.tsv"
LANGS = ["nl", "en", "de", "es"]

ATTR = re.compile(r'\s(?:Content|Text|Header|ToolTip\.Tip|Watermark|PlaceholderText|Title|EmptyText)="([^"]*)"')
ELEMENT = re.compile(r"<(TextBlock|Run|ToolTip\.Tip|SelectableTextBlock)(?:\s[^>]*)?>([^<]+)</\1>")
# A C# string literal (regular or verbatim) as the first argument of Tr.T / Tr.F.
CALL = re.compile(r'\bTr\.[TF]\(\s*(@?)"((?:[^"\\]|\\.|"")*)"')
WEB = re.compile(r'(?:data-tr="([^"]*)"|\btr\(\s*"((?:[^"\\]|\\.)*)"|\btr\(\s*\'((?:[^\'\\]|\\.)*)\')')


def escape(text: str) -> str:
    return text.replace("\\", "\\\\").replace("\t", "\\t").replace("\r", "\\r").replace("\n", "\\n")


def unescape(cell: str) -> str:
    out, i = [], 0
    while i < len(cell):
        c = cell[i]
        if c == "\\" and i + 1 < len(cell):
            n = cell[i + 1]
            out.append({"n": "\n", "t": "\t", "r": "\r"}.get(n, n))
            i += 2
        else:
            out.append(c)
            i += 1
    return "".join(out)


def cs_literal(verbatim: str, body: str) -> str:
    if verbatim:
        return body.replace('""', '"')
    return re.sub(r'\\(u[0-9a-fA-F]{4}|.)', lambda m: {
        "n": "\n", "t": "\t", "r": "\r", '"': '"', "\\": "\\", "'": "'", "0": "\0"}.get(m.group(1), chr(int(m.group(1)[1:], 16)) if m.group(1).startswith("u") else m.group(1)), body)


def is_text(s: str, code: bool = False) -> bool:
    """A translatable text: has letters; in a view, a {Binding ...} or other markup extension is not a text."""
    s = s.strip()
    return len(s) > 0 and (code or not s.startswith("{")) and re.search(r"[A-Za-zÀ-ÿ]{2}", s) is not None


def same_edges(key: str, text: str) -> str:
    """A translation keeps the spaces at the start and end of its key (as in "Fout: "), which are easy to lose."""
    if not text:
        return text
    lead = key[: len(key) - len(key.lstrip())]
    trail = key[len(key.rstrip()):]
    return lead + text.strip() + trail


def collect() -> dict[str, str]:
    found: dict[str, str] = {}

    def add(text: str, where: str, code: bool = False):
        if is_text(text, code):
            found.setdefault(text, where)

    for p in sorted((ROOT / "src" / "ReaRadioDj.Studio").rglob("*.axaml")):
        src = p.read_text(encoding="utf-8")
        rel = p.relative_to(ROOT).as_posix()
        for m in ATTR.finditer(src):
            add(html.unescape(m.group(1)), rel)
        for m in ELEMENT.finditer(src):
            add(html.unescape(m.group(2).strip()), rel)
    for project in ["ReaRadioDj.Studio", "ReaRadioDj.Server", "ReaRadioDj.Core", "ReaRadioDj.Contracts", "ReaRadioDj.Cmd"]:
        for p in sorted((ROOT / "src" / project).rglob("*.cs")):
            if "obj" in p.parts or "bin" in p.parts:
                continue
            src = p.read_text(encoding="utf-8")
            rel = p.relative_to(ROOT).as_posix()
            for m in CALL.finditer(src):
                add(cs_literal(m.group(1), m.group(2)), rel, code=True)
    web = ROOT / "src" / "ReaRadioDj.Server" / "wwwroot"
    for p in sorted(list(web.rglob("*.html")) + list(web.rglob("*.js"))):
        src = p.read_text(encoding="utf-8")
        rel = p.relative_to(ROOT).as_posix()
        for m in WEB.finditer(src):
            add(html.unescape(m.group(1)) if m.group(1) is not None else cs_literal("", m.group(2) or m.group(3) or ""), rel)
    return found


def load() -> dict[str, list[str]]:
    rows: dict[str, list[str]] = {}
    if TSV.exists():
        lines = TSV.read_text(encoding="utf-8").splitlines()
        for line in lines[1:]:
            cells = line.split("\t")
            if not cells or not cells[0]:
                continue
            cells += [""] * (len(LANGS) - len(cells))
            rows[unescape(cells[0])] = [unescape(c) for c in cells[1:len(LANGS)]]
    return rows


def save(rows: dict[str, list[str]]):
    out = ["\t".join(LANGS)]
    for key in sorted(rows, key=lambda k: (k.lower(), k)):
        out.append("\t".join([escape(key)] + [escape(same_edges(key, c)) for c in rows[key]]))
    TSV.write_text("\n".join(out) + "\n", encoding="utf-8", newline="\n")


def main():
    prune = "--prune" in sys.argv
    found = collect()
    rows = load()
    added = 0
    for key in found:
        if key not in rows:
            rows[key] = ["", "", ""]
            added += 1
    removed = 0
    if prune:
        for key in [k for k in rows if k not in found]:
            del rows[key]
            removed += 1
    save(rows)
    print(f"{len(rows)} texts, {added} new, {removed} removed")
    for i, lang in enumerate(LANGS[1:]):
        print(f"  {lang}: {sum(1 for r in rows.values() if not r[i])} without translation")
    if "--missing" in sys.argv:
        lang = sys.argv[sys.argv.index("--missing") + 1]
        i = LANGS.index(lang) - 1
        for key, r in rows.items():
            if not r[i]:
                print(f"{escape(key)}\t{found.get(key, '?')}")


if __name__ == "__main__":
    main()
