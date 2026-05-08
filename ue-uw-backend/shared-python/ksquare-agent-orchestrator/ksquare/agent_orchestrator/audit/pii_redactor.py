from __future__ import annotations

import re


EMAIL_RE = re.compile(r"([a-zA-Z0-9_.+-]+@[a-zA-Z0-9-]+\.[a-zA-Z0-9-.]+)")


def redact_pii(text: str) -> str:
    if not text:
        return text
    return EMAIL_RE.sub("REDACTED", text)
