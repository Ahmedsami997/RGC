#!/usr/bin/env python3
"""Send events of an /ai-discuss session to the self-hosted live board.

    python3 board_post.py <<'JSON'
    {"sid": "s-20261018-101500", "session": {"idea": "...", "status": "running"}, "events": [{"kind": "phase", "label": "..."}]}
    JSON

    python3 board_post.py --ping     # checks the URL and the token without writing anything

The board URL comes from board.json (http.url); the secret comes from the environment variable
AI_DISCUSS_TOKEN and is never printed. Prints "ok <total events>" on success, otherwise "error: ..." and exits 1.
Standard library only.
"""
import json
import os
import pathlib
import sys
import time
import urllib.error
import urllib.request

CONFIG = pathlib.Path(__file__).resolve().parent / "board.json"


def main() -> int:
    token = os.environ.get("AI_DISCUSS_TOKEN", "")
    if not token:
        print("error: AI_DISCUSS_TOKEN is not set")
        return 1
    try:
        base = json.loads(CONFIG.read_text(encoding="utf-8"))["http"]["url"].rstrip("/")
    except Exception as exc:  # missing file, bad JSON, no http.url
        print(f"error: board.json has no http.url ({exc})")
        return 1
    payload = {"ping": True} if "--ping" in sys.argv else json.loads(sys.stdin.read())
    data = json.dumps(payload, ensure_ascii=False).encode("utf-8")
    last = "unknown"
    for attempt in range(2):  # one retry for a network blip or a 5xx
        req = urllib.request.Request(
            base + "/api/ingest",
            data=data,
            method="POST",
            headers={"Authorization": "Bearer " + token, "Content-Type": "application/json"},
        )
        try:
            with urllib.request.urlopen(req, timeout=20) as resp:
                out = json.loads(resp.read().decode("utf-8") or "{}")
            print("ok " + str(out.get("total", "pong" if out.get("pong") else "")))
            return 0
        except urllib.error.HTTPError as exc:
            last = f"HTTP {exc.code} {exc.read().decode('utf-8', 'replace')[:200]}"
            if exc.code < 500:  # 4xx will not get better by retrying
                break
        except Exception as exc:
            last = f"{type(exc).__name__}: {exc}"
        time.sleep(1.5)
    print("error: " + last)
    return 1


if __name__ == "__main__":
    sys.exit(main())
