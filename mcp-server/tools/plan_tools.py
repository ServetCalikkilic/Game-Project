"""estimate_plan_cost: deterministic cost/feasibility estimate for a proposed plan.

This is advisory. The authoritative check is GameActionValidator in Unity, which
runs again after the player approves. Input validation mirrors
schemas/action_plan.schema.json (a test checks the enum parity).
"""
from __future__ import annotations

import game_data as gd
from session_store import SessionStore

from .common import freshness, owned_buildings, run_tool
from .state_tools import prerequisite_met

MAX_ACTIONS = 8
ACTION_TYPES = ("TRAIN_UNIT", "BUILD", "MOVE_GROUP", "RESEARCH")
_ALLOWED_KEYS = {"action_type", "unit_type", "building_type", "tech_id", "count", "target_zone", "reason"}
# Per action type: required fields, and the payload fields that must NOT be present.
_REQUIRED = {"TRAIN_UNIT": {"unit_type", "count"}, "BUILD": {"building_type"},
             "MOVE_GROUP": {"target_zone"}, "RESEARCH": {"tech_id"}}
_FORBIDDEN = {
    "TRAIN_UNIT": {"building_type", "tech_id", "target_zone"},
    "BUILD": {"unit_type", "tech_id", "count"},
    "MOVE_GROUP": {"building_type", "tech_id", "count"},
    "RESEARCH": {"unit_type", "building_type", "count", "target_zone"},
}


def validate_actions(actions) -> tuple[list[dict], list[str]]:
    """Return (clean_actions, errors). Never trusts types; whitelists every identifier."""
    if not isinstance(actions, list) or not actions:
        return [], ["actions must be a non-empty list"]
    if len(actions) > MAX_ACTIONS:
        return [], [f"too many actions (max {MAX_ACTIONS})"]
    clean, errors = [], []
    for i, a in enumerate(actions):
        p = f"actions[{i}]"
        if not isinstance(a, dict):
            errors.append(f"{p}: must be an object")
            continue
        extra = set(a) - _ALLOWED_KEYS
        if extra:
            errors.append(f"{p}: unknown field(s) {sorted(extra)}")
        at = a.get("action_type")
        if at not in ACTION_TYPES:
            errors.append(f"{p}: unknown action_type")
            continue
        for f in sorted(_REQUIRED[at] - set(a)):
            errors.append(f"{p}: {at} requires '{f}'")
        for f in sorted(_FORBIDDEN[at] & set(a)):
            errors.append(f"{p}: '{f}' not allowed for {at}")
        if "unit_type" in a and a["unit_type"] not in gd.UNIT_NAMES:
            errors.append(f"{p}: unknown unit_type")
        if "building_type" in a and a["building_type"] not in gd.BUILDABLE_NAMES:
            errors.append(f"{p}: unknown or non-buildable building_type")
        if "tech_id" in a and a["tech_id"] not in gd.TECH_IDS:
            errors.append(f"{p}: unknown tech_id")
        if "target_zone" in a and a["target_zone"] not in gd.ZONES:
            errors.append(f"{p}: unknown target_zone")
        if "count" in a and (isinstance(a["count"], bool) or not isinstance(a["count"], int) or not 1 <= a["count"] <= 20):
            errors.append(f"{p}: count must be an integer between 1 and 20")
        if "reason" in a and (not isinstance(a["reason"], str) or len(a["reason"]) > 250):
            errors.append(f"{p}: reason must be a string up to 250 chars")
        clean.append(a)
    return ([] if errors else clean), errors


def estimate_plan_cost(store: SessionStore, session_id: str, actions: list) -> dict:
    def body():
        clean, errors = validate_actions(actions)
        if errors:
            return {"ok": False, "error": "invalid_plan", "details": errors}
        snap = store.get_snapshot(session_id)
        res, tech = snap["resources"], snap["technology"]
        age = tech["age"]
        owned = owned_buildings(snap)
        total = {r: 0 for r in gd.RESOURCES}
        pop_needed, issues, per_action = 0, [], []

        for i, a in enumerate(clean):
            cost: dict = {}
            at = a["action_type"]
            if at == "TRAIN_UNIT":
                u = gd.UNITS[a["unit_type"]]
                cost = {r: v * a["count"] for r, v in u["cost"].items()}
                pop_needed += u["population"] * a["count"]
                if u["required_age"] > age:
                    issues.append(f"actions[{i}]: {u['name']} requires Age {u['required_age']}")
                if u["trained_at"] not in owned:
                    issues.append(f"actions[{i}]: {u['name']} requires {u['trained_at']}")
            elif at == "BUILD":
                b = gd.BUILDINGS[a["building_type"]]
                cost = dict(b["cost"])
                if b["required_age"] > age:
                    issues.append(f"actions[{i}]: {b['name']} requires Age {b['required_age']}")
                owned.add(b["name"])  # later actions in the same plan may rely on it
            elif at == "RESEARCH":
                t = gd.TECHS[a["tech_id"]]
                cost = dict(t["cost"])
                done = a["tech_id"] in tech["researched"] or (a["tech_id"] == "tech_age2_iron_age_v1" and age >= 2)
                if done:
                    issues.append(f"actions[{i}]: {t['name']} is already researched")
                if a["tech_id"] in tech["researching"]:
                    issues.append(f"actions[{i}]: {t['name']} is already being researched")
                for p in t["prerequisites"]:
                    if not prerequisite_met(p, {**snap, "own_buildings": [{"type": n, "count": 1} for n in owned]}):
                        issues.append(f"actions[{i}]: missing prerequisite '{p}'")
                if t["researched_at"] not in owned:
                    issues.append(f"actions[{i}]: requires {t['researched_at']}")
            for r, v in cost.items():
                total[r] += v
            per_action.append({"index": i, "action_type": at, "cost": cost})

        shortfall = {r: total[r] - res[r] for r in gd.RESOURCES if total[r] > res[r]}
        pop = snap["population"]
        limit = min(pop["cap"], gd.POP_HARD_CAP[age])
        if pop["used"] + pop_needed > limit:
            issues.append(f"population {pop['used']}+{pop_needed} exceeds limit {limit}")
        return {"ok": True, "total_cost": total, "available": res, "affordable": not shortfall,
                "shortfall": shortfall,
                "population": {"used": pop["used"], "needed": pop_needed, "limit": limit},
                "issues": issues, "feasible": not shortfall and not issues,
                "per_action": per_action, **freshness(store, snap)}
    return run_tool(store, "estimate_plan_cost", {"n_actions": len(actions) if isinstance(actions, list) else None}, body)
