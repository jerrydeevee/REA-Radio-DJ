"""Keeps src/ReaRadioDj.Contracts/Localization/strings.tsv up to date.

Collects every text that is translated at run time:
  - fixed texts in the studio views (.axaml): Content, Text, Header, ToolTip.Tip, Watermark, PlaceholderText, Title,
    and the text inside <TextBlock>, <Run> and <ToolTip.Tip>;
  - Tr.T("...") and Tr.F("...") in the C# code of studio, engine and core;
  - texts marked in the web pages (data-tr attributes and tr("...") in scripts);
  - texts the engine sends to the studio: string literals in engine and core code, with "{expr}" as "{0}"; the studio
    translates them with Tr.E. Literals in comparisons, log calls and technical texts are skipped.
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
# Words that occur in Dutch texts but also in English or technical ones; on their own they do not make a literal Dutch.
ENGLISH = set("""the and for with from not new off out all stream log cue cues jingle jingles item items mix error deck decks
player overlay pfl mic live auto semi sync fade loop roll gain echo filter test start stop play pause next end edit open save
file name type music voice playlist playlists studio engine server client port host user admin token json api url http https
spotify voip sip rtp asio midi bass mackie ember livewire wav mp3 ogg flac opus lufs bpm dbfs kbps hook intro outro""".split())

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
        if p.suffix == ".html":
            # The fixed texts of the page, which tr.js translates when it loads: text between tags and the attributes
            # people read. Scripts and styles are left out; their texts go through tr().
            page = re.sub(r"<(script|style)\b[^>]*>.*?</\1>", " ", src, flags=re.S)
            page = re.sub(r"<!--.*?-->", " ", page, flags=re.S)
            for m in re.finditer(r">([^<>]+)<", page):
                add(html.unescape(m.group(1).strip()), rel)
            for m in re.finditer(r'\s(?:placeholder|title|aria-label)="([^"]*)"', page):
                add(html.unescape(m.group(1).strip()), rel)
        for m in WEB.finditer(src):
            add(html.unescape(m.group(1)) if m.group(1) is not None else cs_literal("", m.group(2) or m.group(3) or ""), rel)
    words = {w.lower() for k in found for w in re.findall(r"[A-Za-zÀ-ÿ]{3,}", k)} - ENGLISH
    for text, rel in engine_texts(words):
        add(text, rel, code=True)
    return found


ENGINE_SKIP = re.compile(r'(==|!=|\bcase\b|\bis\s|Equals\(|StartsWith\(|EndsWith\(|Contains\(|IndexOf\(|Replace\(|Split\(|'
                         r'Log\w*\(|Debug\.\w+\(|Trace\.\w+\(|Get\w*\(|\[\s*|nameof\(|"\s*=>\s*|Header\w*\(|Environment\.\w+\()\s*$')
TECHNICAL = re.compile(r'BASS_|\b(SELECT|INSERT|UPDATE|DELETE|CREATE|ALTER|PRAGMA)\s|https?://|^[\w.]+\(.*\)$|^/|\\|^[\w-]+:\w|^\w+=')
# AI prompts (their language is the presenter's own setting) and the engine's tray menu are not studio texts.
SKIP_FILES = {"ScriptWriters.cs", "NewsWriter.cs", "VoiceProfiles.cs", "TrayIcon.cs"}


def cs_literals(line: str):
    """(start, end, interpolated, body) for each regular or interpolated string literal on a line of C#."""
    i, n = 0, len(line)
    while i < n:
        c = line[i]
        if line.startswith("//", i) or line.startswith('"""', i) or line.startswith('$"""', i):
            return
        if c == "'":
            j = i + 1
            while j < n and line[j] != "'":
                j += 2 if line[j] == "\\" else 1
            i = j + 1
            continue
        if line.startswith('@"', i) or line.startswith('$@"', i) or line.startswith('@$"', i):
            j = line.index('"', i) + 1
            while j < n and not (line[j] == '"' and not line.startswith('""', j)):
                j += 2 if line.startswith('""', j) else 1
            i = j + 1
            continue
        if c == '"' or line.startswith('$"', i):
            interp, start = c == "$", i
            j, depth = i + (2 if interp else 1), 0
            while j < n:
                ch = line[j]
                if depth == 0:
                    if ch == "\\":
                        j += 2
                        continue
                    if ch == '"':
                        break
                    if interp and ch == "{":
                        if line.startswith("{{", j):
                            j += 2
                            continue
                        depth = 1
                    elif interp and line.startswith("}}", j):
                        j += 2
                        continue
                    j += 1
                else:
                    if ch in "\"'":
                        q = ch
                        j += 1
                        while j < n and line[j] != q:
                            j += 2 if line[j] == "\\" else 1
                        j += 1
                        continue
                    depth += ch in "{(["
                    depth -= ch in "})]"
                    j += 1
            yield start, j + 1, interp, line[start + (2 if interp else 1):j]
            i = j + 1
            continue
        i += 1


def template(body: str) -> str:
    """An interpolated literal as a format text: each {expression[,align][:format]} becomes {n[:format]}."""
    out, n, i = [], 0, 0
    while i < len(body):
        if body.startswith("{{", i) or body.startswith("}}", i):
            out.append(body[i:i + 2])
            i += 2
            continue
        if body[i] != "{":
            out.append(body[i])
            i += 1
            continue
        depth, j, fmt = 1, i + 1, None
        while j < len(body) and depth:
            ch = body[j]
            if ch in "\"'":
                j += 1
                while j < len(body) and body[j] != ch:
                    j += 2 if body[j] == "\\" else 1
            elif ch in "{([":
                depth += 1
            elif ch in "})]":
                depth -= 1
            elif ch == ":" and depth == 1 and fmt is None and "?" not in body[i:j]:
                fmt = j
            j += 1
        out.append("{%d%s}" % (n, body[fmt:j - 1] if fmt else ""))
        n += 1
        i = j
    return "".join(out)


def engine_texts(known_words: set[str]):
    """Dutch texts in engine and core code that can end up on a studio screen."""
    for project in ["ReaRadioDj.Server", "ReaRadioDj.Core"]:
        for p in sorted((ROOT / "src" / project).rglob("*.cs")):
            if "obj" in p.parts or "bin" in p.parts or p.name in SKIP_FILES:
                continue
            rel = p.relative_to(ROOT).as_posix()
            for line in p.read_text(encoding="utf-8").split("\n"):
                if line.strip().startswith(("//", "[", "#")):
                    continue
                # A line that is only a text in a list of the AI presenter (topics, phrases) is input for the AI.
                if "Presenter" in p.parts and re.fullmatch(r'\s*\$?"[^"]*",?\s*', line):
                    continue
                for s, e, interp, body in cs_literals(line):
                    if re.search(r"Tr\.[TFN]\(\s*$", line[:s]) or ENGINE_SKIP.search(line[:s]) or re.search(r"\bLog\w*\([^\"]*$", line[:s]) \
                            or re.match(r"\s*(==|!=|=>)", line[e:]) or (not interp and "{" in body):
                        continue
                    text = cs_literal("", template(body) if interp else body)
                    bare = re.sub(r"\{\d+[^}]*\}", " ", text)
                    words = re.findall(r"[A-Za-zÀ-ÿ]{3,}", bare)
                    if not any(w.lower() in known_words for w in words) or TECHNICAL.search(text):
                        continue
                    # One word without spaces is an id, a file name or a keyword, unless it is a capitalised name ("Uitzending").
                    if not re.search(r"\s", text.strip()) and not re.fullmatch(r"[A-ZÀ-Ý][a-zà-ÿ-]+", text.strip()):
                        continue
                    letters = re.sub(r"[^A-Za-zÀ-ÿ]", "", bare)
                    if sum(ch.isupper() for ch in letters) > len(letters) / 2 or text.count(";") >= 3 or "\r" in text or "\n" in text.strip():
                        continue
                    yield text, rel


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
