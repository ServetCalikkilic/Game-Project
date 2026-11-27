import copy

import _paths  # noqa: F401
from session_store import SessionStore

NOW = 1_000_000.0


def make_snapshot(**overrides) -> dict:
    snap = {
        "phase": "UnderAttack",
        "captured_at": NOW,
        "resources": {"wood": 420, "food": 180, "gold": 250},
        "population": {"used": 14, "cap": 20},
        "technology": {"age": 1, "researched": ["tech_sharpened_tools_v1"], "researching": []},
        "own_army": [
            {"type": "Worker", "count": 8, "zone": "FarmArea"},
            {"type": "Spearman", "count": 3, "zone": "BaseCenter"},
        ],
        "own_buildings": [
            {"type": "Citadel", "count": 1},
            {"type": "Barracks", "count": 1},
            {"type": "ArcheryYard", "count": 1},
        ],
        "visible_enemies": [{"type": "Raider", "count": 4, "zone": "NorthGate"}],
        "recent_events": [
            {"kind": "UNDER_ATTACK", "zone": "NorthGate", "seconds_ago": 5},
            {"kind": "UNIT_LOST", "unit_type": "Worker", "count": 2, "seconds_ago": 90},
        ],
    }
    snap.update(copy.deepcopy(overrides))
    return snap


class Clock:
    def __init__(self, t=NOW):
        self.t = t

    def __call__(self):
        return self.t


def make_store(snapshot=None, match="m1", player="p1"):
    """Return (store, session_id, clock) with a fresh snapshot ingested."""
    clock = Clock()
    store = SessionStore(clock=clock)
    ctx = store.create_session(match, player)
    store.ingest_snapshot(ctx.session_id, snapshot if snapshot is not None else make_snapshot())
    return store, ctx.session_id, clock
