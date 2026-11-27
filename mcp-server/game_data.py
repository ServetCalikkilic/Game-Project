"""Static game data (costs, prerequisites) read from rag/knowledge/*.json.

The JSON files are the single source of truth; nothing is hard-coded here except
the enums that the ActionPlan schema also fixes (zones, phases, event kinds).
A test checks parity with schemas/action_plan.schema.json.
"""
from __future__ import annotations

import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
KNOWLEDGE_DIR = ROOT / "rag" / "knowledge"

ZONES = ("BaseCenter", "NorthGate", "SouthGate", "EastGate", "WestGate", "FarmArea", "Forest", "GoldMine")
PHASES = ("EarlyEconomy", "BuildUp", "UnderAttack", "Skirmish", "LateGame")
EVENT_KINDS = ("UNDER_ATTACK", "UNIT_LOST", "BUILDING_LOST", "UNIT_TRAINED", "BUILDING_COMPLETED", "TECH_RESEARCHED")
RESOURCES = ("wood", "food", "gold")


def _load(name: str) -> list[dict]:
    return json.loads((KNOWLEDGE_DIR / name).read_text(encoding="utf-8"))


UNITS = {r["name"]: r for r in _load("units.json")}
BUILDINGS = {r["name"]: r for r in _load("buildings.json")}
TECHS = {r["id"]: r for r in _load("technologies.json")}

UNIT_NAMES = tuple(UNITS)
BUILDING_NAMES = tuple(BUILDINGS)  # includes Citadel (owned, not buildable)
BUILDABLE_NAMES = tuple(n for n in BUILDINGS if BUILDINGS[n]["cost"])  # Citadel has no cost
TECH_IDS = tuple(TECHS)

# Hard population cap per age (documented in rules: rule_population_v1).
POP_HARD_CAP = {1: 40, 2: 60}
