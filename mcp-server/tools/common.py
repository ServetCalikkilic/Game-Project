"""Shared helpers for MCP tools: uniform error shape, logging, freshness metadata."""
from __future__ import annotations

import time
from typing import Any, Callable

from session_store import MAX_SNAPSHOT_AGE_S, SessionError, SessionStore


def run_tool(store: SessionStore, tool: str, args: dict, fn: Callable[[], dict]) -> dict:
    """Run a tool body; never raises. Returns {"ok": False, "error": code, ...} on failure.

    Logs tool name, (non-secret) arguments, status and duration. Session ids are never logged.
    """
    start = time.perf_counter()
    try:
        result = fn()
        status, error = "ok" if result.get("ok", True) else "rejected", result.get("error")
    except SessionError as exc:
        result = {"ok": False, "error": str(exc.args[0])}
        status, error = "error", result["error"]
    except ValueError as exc:
        result = {"ok": False, "error": "invalid_input", "detail": str(exc)}
        status, error = "rejected", "invalid_input"
    store.log_tool(tool, _loggable(args), status, (time.perf_counter() - start) * 1000, error)
    return result


def _loggable(args: dict) -> dict:
    out: dict[str, Any] = {}
    for k, v in args.items():
        s = repr(v)
        out[k] = s if len(s) <= 200 else s[:200] + "..."
    return out


def freshness(store: SessionStore, snap: dict) -> dict:
    age = store.snapshot_age(snap)
    meta: dict[str, Any] = {"snapshot_age_s": round(age, 1), "stale": age > MAX_SNAPSHOT_AGE_S}
    if meta["stale"]:
        meta["warning"] = "Snapshot is stale; state may be outdated. Report limited confidence."
    return meta


def owned_buildings(snap: dict) -> set[str]:
    owned = {b["type"] for b in snap["own_buildings"] if b["count"] > 0}
    owned.add("Citadel")  # every player starts with one; losing it ends the match
    return owned
