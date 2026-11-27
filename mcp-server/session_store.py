"""Safe session / snapshot store for the AI gateway and MCP tools.

Security properties:
  * Identity (match_id, player_id) is bound to a session_id when the *gateway*
    creates the session. MCP tools never accept identity arguments; the model
    cannot switch players or matches.
  * Snapshots are rebuilt from an allowlist. Any field not on the allowlist
    (enemy positions, enemy economy, hidden units, free text...) is dropped and
    only its *path* is written to the security log, never its value.
  * Values are type/range checked; enemies and events use fixed enums, so no
    free text from the game can reach the model through the state tools.
  * Ingestion (`ingest_snapshot`) is a gateway API, NOT an MCP tool.
"""
from __future__ import annotations

import copy
import secrets
import time
from dataclasses import dataclass, field
from typing import Any, Callable

import game_data as gd

MAX_SNAPSHOT_AGE_S = 60.0
MAX_LIST = 64
MAX_EVENTS = 50
MAX_COUNT = 500
MAX_RESOURCE = 1_000_000


class SnapshotError(ValueError):
    """The snapshot violates the schema (wrong type/range/enum)."""


class SessionError(KeyError):
    """Unknown or missing session."""


@dataclass(frozen=True)
class SessionContext:
    session_id: str = field(repr=False)
    match_id: str
    player_id: str


# ---------- sanitizer ----------

def _pick(obj: Any, allowed: tuple[str, ...], path: str, dropped: list[str]) -> dict:
    if not isinstance(obj, dict):
        raise SnapshotError(f"{path}: expected object")
    for key in obj:
        if key not in allowed:
            dropped.append(f"{path}.{key}" if path else str(key))
    return {k: obj[k] for k in allowed if k in obj}


def _int(value: Any, path: str, lo: int, hi: int) -> int:
    if isinstance(value, bool) or not isinstance(value, int) or not lo <= value <= hi:
        raise SnapshotError(f"{path}: expected integer in [{lo}, {hi}]")
    return value


def _enum(value: Any, path: str, allowed: tuple[str, ...]) -> str:
    if not isinstance(value, str) or value not in allowed:
        raise SnapshotError(f"{path}: value not in allowed set")
    return value


def _list(value: Any, path: str, limit: int) -> list:
    if not isinstance(value, list) or len(value) > limit:
        raise SnapshotError(f"{path}: expected list with at most {limit} items")
    return value


def sanitize_snapshot(raw: Any) -> tuple[dict, list[str]]:
    """Return (clean_snapshot, dropped_field_paths). Raises SnapshotError on bad values."""
    dropped: list[str] = []
    top = _pick(raw, ("phase", "resources", "population", "own_army", "own_buildings",
                      "technology", "recent_events", "visible_enemies", "captured_at"), "", dropped)
    for required in ("phase", "resources", "population", "technology", "captured_at"):
        if required not in top:
            raise SnapshotError(f"missing required field: {required}")

    clean: dict[str, Any] = {"phase": _enum(top["phase"], "phase", gd.PHASES)}

    res = _pick(top["resources"], gd.RESOURCES, "resources", dropped)
    clean["resources"] = {r: _int(res.get(r, 0), f"resources.{r}", 0, MAX_RESOURCE) for r in gd.RESOURCES}

    pop = _pick(top["population"], ("used", "cap"), "population", dropped)
    clean["population"] = {k: _int(pop.get(k), f"population.{k}", 0, 200) for k in ("used", "cap")}

    tech = _pick(top["technology"], ("age", "researched", "researching"), "technology", dropped)
    clean["technology"] = {
        "age": _int(tech.get("age"), "technology.age", 1, 2),
        "researched": [_enum(t, "technology.researched", gd.TECH_IDS) for t in _list(tech.get("researched", []), "technology.researched", MAX_LIST)],
        "researching": [_enum(t, "technology.researching", gd.TECH_IDS) for t in _list(tech.get("researching", []), "technology.researching", MAX_LIST)],
    }

    clean["own_army"] = []
    for i, e in enumerate(_list(top.get("own_army", []), "own_army", MAX_LIST)):
        e = _pick(e, ("type", "count", "zone"), f"own_army[{i}]", dropped)
        clean["own_army"].append({
            "type": _enum(e.get("type"), f"own_army[{i}].type", gd.UNIT_NAMES),
            "count": _int(e.get("count"), f"own_army[{i}].count", 0, MAX_COUNT),
            "zone": _enum(e.get("zone"), f"own_army[{i}].zone", gd.ZONES),
        })

    clean["own_buildings"] = []
    for i, e in enumerate(_list(top.get("own_buildings", []), "own_buildings", MAX_LIST)):
        e = _pick(e, ("type", "count"), f"own_buildings[{i}]", dropped)
        clean["own_buildings"].append({
            "type": _enum(e.get("type"), f"own_buildings[{i}].type", gd.BUILDING_NAMES),
            "count": _int(e.get("count"), f"own_buildings[{i}].count", 0, MAX_COUNT),
        })

    # Only enemies the player currently sees; coarse zone, no coordinates.
    clean["visible_enemies"] = []
    for i, e in enumerate(_list(top.get("visible_enemies", []), "visible_enemies", MAX_LIST)):
        e = _pick(e, ("type", "count", "zone"), f"visible_enemies[{i}]", dropped)
        clean["visible_enemies"].append({
            "type": _enum(e.get("type"), f"visible_enemies[{i}].type", gd.UNIT_NAMES),
            "count": _int(e.get("count"), f"visible_enemies[{i}].count", 1, MAX_COUNT),
            "zone": _enum(e.get("zone"), f"visible_enemies[{i}].zone", gd.ZONES),
        })

    clean["recent_events"] = []
    for i, e in enumerate(_list(top.get("recent_events", []), "recent_events", MAX_EVENTS)):
        e = _pick(e, ("kind", "seconds_ago", "zone", "unit_type", "count"), f"recent_events[{i}]", dropped)
        ev: dict[str, Any] = {
            "kind": _enum(e.get("kind"), f"recent_events[{i}].kind", gd.EVENT_KINDS),
            "seconds_ago": _int(e.get("seconds_ago"), f"recent_events[{i}].seconds_ago", 0, 3600),
        }
        if "zone" in e:
            ev["zone"] = _enum(e["zone"], f"recent_events[{i}].zone", gd.ZONES)
        if "unit_type" in e:
            ev["unit_type"] = _enum(e["unit_type"], f"recent_events[{i}].unit_type", gd.UNIT_NAMES)
        if "count" in e:
            ev["count"] = _int(e["count"], f"recent_events[{i}].count", 1, MAX_COUNT)
        clean["recent_events"].append(ev)

    ca = top["captured_at"]
    if isinstance(ca, bool) or not isinstance(ca, (int, float)) or ca < 0:
        raise SnapshotError("captured_at: expected non-negative epoch seconds")
    clean["captured_at"] = float(ca)
    return clean, dropped


# ---------- store ----------

class SessionStore:
    """In-memory store. One entry per (match, player) session created by the gateway."""

    def __init__(self, clock: Callable[[], float] = time.time):
        self._clock = clock
        self._sessions: dict[str, SessionContext] = {}
        self._snapshots: dict[str, dict] = {}
        self.tool_log: list[dict] = []
        self.security_log: list[dict] = []

    def now(self) -> float:
        return self._clock()

    def create_session(self, match_id: str, player_id: str) -> SessionContext:
        for name, v in (("match_id", match_id), ("player_id", player_id)):
            if not isinstance(v, str) or not v or len(v) > 64:
                raise ValueError(f"{name} must be a non-empty string (<=64 chars)")
        ctx = SessionContext(secrets.token_urlsafe(24), match_id, player_id)
        self._sessions[ctx.session_id] = ctx
        return ctx

    def get_context(self, session_id: str | None) -> SessionContext:
        ctx = self._sessions.get(session_id or "")
        if ctx is None:
            raise SessionError("no_active_session")
        return ctx

    def ingest_snapshot(self, session_id: str, raw: Any) -> list[str]:
        """Gateway-only. Sanitizes and stores the player's own filtered snapshot."""
        ctx = self.get_context(session_id)
        clean, dropped = sanitize_snapshot(raw)
        for path in dropped:
            self.log_security("snapshot_field_dropped", ctx, path=path)
        self._snapshots[session_id] = clean
        return dropped

    def get_snapshot(self, session_id: str | None) -> dict:
        self.get_context(session_id)
        snap = self._snapshots.get(session_id or "")
        if snap is None:
            raise SessionError("no_snapshot")
        return copy.deepcopy(snap)

    def snapshot_age(self, snap: dict) -> float:
        return max(0.0, self.now() - snap["captured_at"])

    # ---- logging (never records secrets or session ids) ----
    def log_tool(self, tool: str, args: dict, status: str, duration_ms: float, error: str | None = None) -> None:
        self.tool_log.append({"tool": tool, "args": args, "status": status,
                              "duration_ms": round(duration_ms, 2), "error": error})

    def log_security(self, event: str, ctx: SessionContext | None = None, **details: Any) -> None:
        entry = {"event": event, "t": self.now(), **details}
        if ctx is not None:
            entry["match_id"], entry["player_id"] = ctx.match_id, ctx.player_id
        self.security_log.append(entry)
