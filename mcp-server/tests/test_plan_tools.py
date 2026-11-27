import unittest

import _paths  # noqa: F401
import tools
from helpers import make_snapshot, make_store


def train(unit, count, **kw):
    return {"action_type": "TRAIN_UNIT", "unit_type": unit, "count": count, "reason": "test", **kw}


class EstimatePlanCostTests(unittest.TestCase):
    def setUp(self):
        self.store, self.sid, _ = make_store()

    def est(self, actions):
        return tools.estimate_plan_cost(self.store, self.sid, actions)

    def test_valid_plan_cost(self):
        r = self.est([train("Spearman", 3), {"action_type": "MOVE_GROUP", "target_zone": "NorthGate", "reason": "x"}])
        self.assertTrue(r["ok"])
        self.assertEqual(r["total_cost"], {"wood": 75, "food": 150, "gold": 0})
        self.assertTrue(r["affordable"] and r["feasible"])
        self.assertEqual(r["population"], {"used": 14, "needed": 3, "limit": 20})

    def test_shortfall(self):
        r = self.est([train("Spearman", 5)])  # 250 food needed, 180 available
        self.assertFalse(r["affordable"])
        self.assertEqual(r["shortfall"], {"food": 70})

    def test_invalid_counts(self):
        for bad in (-1, 0, 21, 1000, 2.5, "3", True, None):
            with self.subTest(count=bad):
                r = self.est([train("Spearman", bad)])
                self.assertEqual(r["error"], "invalid_plan")

    def test_unknown_identifiers(self):
        cases = [
            train("Dragon", 1),
            {"action_type": "BUILD", "building_type": "Citadel", "reason": "x"},      # not buildable
            {"action_type": "BUILD", "building_type": "DeathStar", "reason": "x"},
            {"action_type": "RESEARCH", "tech_id": "tech_nuke_v1", "reason": "x"},
            {"action_type": "MOVE_GROUP", "target_zone": "EnemyBase", "reason": "x"},
            {"action_type": "LAUNCH_NUKE", "reason": "x"},
            {"reason": "no action type"},
        ]
        for a in cases:
            with self.subTest(a=a):
                self.assertEqual(self.est([a])["error"], "invalid_plan")

    def test_structure_errors(self):
        self.assertEqual(self.est([])["error"], "invalid_plan")
        self.assertEqual(self.est("TRAIN")["error"], "invalid_plan")
        self.assertEqual(self.est(["x"])["error"], "invalid_plan")
        self.assertEqual(self.est([train("Spearman", 1)] * 9)["error"], "invalid_plan")  # max 8
        self.assertEqual(self.est([train("Spearman", 1, player_id="opponent")])["error"], "invalid_plan")
        self.assertEqual(self.est([{"action_type": "TRAIN_UNIT", "unit_type": "Spearman", "reason": "x"}])["error"], "invalid_plan")
        self.assertEqual(self.est([train("Spearman", 1, tech_id="tech_age2_iron_age_v1")])["error"], "invalid_plan")

    def test_age_gating_and_missing_building(self):
        r = self.est([train("Guardian", 1), train("Raider", 1)])
        self.assertIn("actions[0]: Guardian requires Age 2", r["issues"])
        self.assertIn("actions[1]: Raider requires Stable", r["issues"])
        self.assertFalse(r["feasible"])

    def test_planned_build_satisfies_later_requirement(self):
        r = self.est([{"action_type": "BUILD", "building_type": "Stable", "reason": "x"}, train("Raider", 1)])
        self.assertEqual(r["issues"], [])
        self.assertEqual(r["total_cost"], {"wood": 175, "food": 70, "gold": 90})

    def test_population_limit(self):
        r = self.est([train("Spearman", 7)])  # 14 + 7 > 20
        self.assertTrue(any("population" in i for i in r["issues"]))

    def test_research_issues(self):
        r = self.est([{"action_type": "RESEARCH", "tech_id": "tech_sharpened_tools_v1", "reason": "x"}])
        self.assertIn("actions[0]: Sharpened Tools is already researched", r["issues"])
        r = self.est([{"action_type": "RESEARCH", "tech_id": "tech_forged_blades_v1", "reason": "x"}])
        self.assertTrue(any("tech_age2_iron_age_v1" in i for i in r["issues"]))

    def test_no_session_is_reported(self):
        r = tools.estimate_plan_cost(self.store, None, [train("Spearman", 1)])
        self.assertEqual(r["error"], "no_active_session")


if __name__ == "__main__":
    unittest.main()
