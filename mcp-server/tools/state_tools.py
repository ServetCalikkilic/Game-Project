"""Read-only tools over the current player's own, visibility-filtered snapshot.

None of these accept player_id / match_id: identity comes from the session.
"""
from __future__ import annotations

import game_data as gd
from session_store import SessionStore

from .common import freshness, owned_buildings, run_tool


def get_player_resources(store: SessionStore, session_id: str) -> dict:
    def body():
        snap = store.get_snapshot(session_id)
        return {"ok": True, "resources": snap["resources"], "population": snap["population"],
                **freshness(store, snap)}
    return run_tool(store, "get_player_resources", {}, body)


def get_army_status(store: SessionStore, session_id: str) -> dict:
    def body():
        snap = store.get_snapshot(session_id)
        totals: dict[str, int] = {}
        for e in snap["own_army"]:
            totals[e["type"]] = totals.get(e["type"], 0) + e["count"]
        return {"ok": True, "units_by_zone": snap["own_army"], "totals": totals,
                "buildings": snap["own_buildings"], "phase": snap["phase"], **freshness(store, snap)}
    return run_tool(store, "get_army_status", {}, body)


def get_visible_enemies(store: SessionStore, session_id: str) -> dict:
    def body():
        snap = store.get_snapshot(session_id)
        return {"ok": True, "enemies": snap["visible_enemies"],
                "note": "Only enemies currently visible to this player. An empty list means none "
                        "are visible, not that none exist.", **freshness(store, snap)}
    return run_tool(store, "get_visible_enemies", {}, body)


def get_recent_events(store: SessionStore, session_id: str, seconds: int = 30) -> dict:
    def body():
        if isinstance(seconds, bool) or not isinstance(seconds, int) or not 1 <= seconds <= 120:
            raise ValueError("seconds must be an integer between 1 and 120")
        snap = store.get_snapshot(session_id)
        events = [e for e in snap["recent_events"] if e["seconds_ago"] <= seconds]
        return {"ok": True, "window_s": seconds, "events": events, **freshness(store, snap)}
    return run_tool(store, "get_recent_events", {"seconds": seconds}, body)


def prerequisite_met(item: str, snap: dict) -> bool:
    """Evaluate one prerequisite string from technologies.json; unknown forms count as unmet."""
    researched = set(snap["technology"]["researched"])
    if item.startswith("tech_"):
        return item in researched or (item == "tech_age2_iron_age_v1" and snap["technology"]["age"] >= 2)
    if item.endswith(" built"):
        names = [n.strip().replace(" ", "") for n in item[: -len(" built")].split(" or ")]
        owned = owned_buildings(snap)
        return any(n in owned for n in names)
    return False


def tech_status(tech_id: str, snap: dict) -> dict:
    tech = gd.TECHS[tech_id]
    t = snap["technology"]
    res = snap["resources"]
    done = tech_id in t["researched"] or (tech_id == "tech_age2_iron_age_v1" and t["age"] >= 2)
    missing = [p for p in tech["prerequisites"] if not prerequisite_met(p, snap)]
    if tech["researched_at"] not in owned_buildings(snap):
        missing.append(f"{tech['researched_at']} built")
    shortfall = {r: tech["cost"].get(r, 0) - res[r] for r in gd.RESOURCES if tech["cost"].get(r, 0) > res[r]}
    return {"tech_id": tech_id, "name": tech["name"], "researched": done,
            "researching": tech_id in t["researching"], "cost": tech["cost"],
            "prerequisites_missing": missing, "affordable": not shortfall, "shortfall": shortfall}


def get_technology_status(store: SessionStore, session_id: str, tech_id: str | None = None) -> dict:
    def body():
        if tech_id is not None and (not isinstance(tech_id, str) or tech_id not in gd.TECHS):
            raise ValueError("unknown tech_id")
        snap = store.get_snapshot(session_id)
        ids = [tech_id] if tech_id else list(gd.TECH_IDS)
        return {"ok": True, "age": snap["technology"]["age"], "researched": snap["technology"]["researched"],
                "researching": snap["technology"]["researching"],
                "techs": [tech_status(i, snap) for i in ids], **freshness(store, snap)}
    return run_tool(store, "get_technology_status", {"tech_id": tech_id}, body)
