#!/usr/bin/env python3
"""Regenerate public/index.html from the shared board page.

The board page lives once, in .claude/skills/ai-discuss/board.html (it is also published as a claude.ai
artifact). This copy is the same page wrapped in a normal HTML document, with one extra tag,
<meta name="board-api" content="/api/board">, which makes it read from this project's API instead of the
claude.ai database. Run it after every edit to board.html:

    python3 sync.py            # rewrite public/index.html
    python3 sync.py --check    # exit 1 if public/index.html is out of date
"""
import pathlib
import sys

HERE = pathlib.Path(__file__).resolve().parent
SRC = HERE.parent / ".claude" / "skills" / "ai-discuss" / "board.html"
OUT = HERE / "public" / "index.html"

VIEWPORT_META = '<meta name="claude:viewport-fit-sides" content="cover">'
SKELETON_HEAD = (
    '<!doctype html>\n<html lang="ar">\n<head>\n<meta charset="utf-8">\n'
    '<meta name="viewport" content="width=device-width,initial-scale=1,viewport-fit=cover">\n'
    "<style>:root{color-scheme:dark;padding:env(safe-area-inset-top,0px) 0 env(safe-area-inset-bottom,0px)}"
    "body{margin:0;font:14px system-ui,sans-serif}img{max-width:100%}[hidden]{display:none!important}</style>\n"
)


def build() -> str:
    src = SRC.read_text(encoding="utf-8")
    cut = src.index('<div id="app"')  # everything before it is head material (title, meta, fonts, style)
    head, body = src[:cut], src[cut:]
    if VIEWPORT_META not in head:
        sys.exit("board.html no longer has the expected viewport meta; update sync.py")
    head = head.replace(VIEWPORT_META, VIEWPORT_META + '\n<meta name="board-api" content="/api/board">')
    return SKELETON_HEAD + head + "</head>\n<body>\n" + body.rstrip() + "\n</body>\n</html>\n"


if __name__ == "__main__":
    html = build()
    if "--check" in sys.argv:
        current = OUT.read_text(encoding="utf-8") if OUT.exists() else ""
        if current != html:
            sys.exit("public/index.html is out of date: run `python3 sync.py`")
        print("public/index.html is up to date")
    else:
        OUT.parent.mkdir(parents=True, exist_ok=True)
        OUT.write_text(html, encoding="utf-8")
        print(f"wrote {OUT.relative_to(HERE.parent)} ({len(html)} bytes)")
