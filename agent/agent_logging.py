"""Structured, secret-free logging of tool calls and security events."""
from __future__ import annotations

import json
import time
from pathlib import Path
from typing import Any

from .security import redact
from .session import AgentSession


class AgentLogger:
    """Collects entries in memory; optionally appends JSON lines to a file (use logs/, which is gitignored).

    Tool entries: step, tool, args_summary, status, duration_ms, error. Tool *results* are not logged.
    """

    def __init__(self, session: AgentSession, path: str | Path | None = None):
        self.session = session
        self.path = Path(path) if path else None
        self.tool_calls: list[dict] = []
        self.security_events: list[dict] = []
        self.events: list[dict] = []

    def _emit(self, kind: str, entry: dict) -> None:
        entry = {"kind": kind, "t": round(time.time(), 3), **self.session.log_context(), **entry}
        if self.path:
            self.path.parent.mkdir(parents=True, exist_ok=True)
            with self.path.open("a", encoding="utf-8") as f:
                f.write(json.dumps(entry, ensure_ascii=False) + "\n")

    def tool_call(self, step: int, tool: str, args: Any, status: str, duration_ms: float, error: str | None = None) -> None:
        try:
            summary = json.dumps(args, ensure_ascii=False, sort_keys=True, default=str)
        except (TypeError, ValueError):
            summary = "<unserializable>"
        entry = {
            "step": step, "tool": tool,
            "args_summary": redact(summary)[:160],
            "status": status, "duration_ms": round(duration_ms, 2),
            "error": redact(error)[:200] if error else None,
        }
        self.tool_calls.append(entry)
        self._emit("tool_call", entry)

    def security(self, event: str, **details: Any) -> None:
        entry = {"event": event, **{k: v for k, v in details.items()}}
        self.security_events.append(entry)
        self._emit("security", entry)

    def info(self, event: str, **details: Any) -> None:
        entry = {"event": event, **details}
        self.events.append(entry)
        self._emit("info", entry)
