import inspect
import unittest

import _paths  # noqa: F401
import tools
from helpers import make_snapshot, make_store
from session_store import MAX_SNAPSHOT_AGE_S, SessionStore


class StateToolTests(unittest.TestCase):
    def test_resources(self):
        store, sid, _ = make_store()
        r = tools.get_player_resources(store, sid)
        self.assertTrue(r["ok"])
        self.assertEqual(r["resources"], {"wood": 420, "food": 180, "gold": 250})
        self.assertFalse(r["stale"])

    def test_army_totals(self):
        store, sid, _ = make_store()
        r = tools.get_army_status(store, sid)
        self.assertEqual(r["totals"], {"Worker": 8, "Spearman": 3})

    def test_visible_enemies_only_contains_safe_fields(self):
        store, sid, _ = make_store(make_snapshot(visible_enemies=[
            {"type": "Raider", "count": 4, "zone": "NorthGate", "position": [1, 2], "hidden_gold": 5}]))
        r = tools.get_visible_enemies(store, sid)
        self.assertEqual(r["enemies"], [{"type": "Raider", "count": 4, "zone": "NorthGate"}])
        self.assertNotIn("position", repr(r))
        self.assertNotIn("hidden_gold", repr(r))

    def test_stale_snapshot_is_flagged(self):
        store, sid, clock = make_store()
        clock.t += MAX_SNAPSHOT_AGE_S + 5
        r = tools.get_visible_enemies(store, sid)
        self.assertTrue(r["stale"])
        self.assertIn("warning", r)

    def test_no_session_and_no_snapshot(self):
        store = SessionStore()
        self.assertEqual(tools.get_player_resources(store, None), {"ok": False, "error": "no_active_session"})
        self.assertEqual(tools.get_army_status(store, "forged")["error"], "no_active_session")
        ctx = store.create_session("m", "p")
        self.assertEqual(tools.get_visible_enemies(store, ctx.session_id)["error"], "no_snapshot")

    def test_recent_events_window_and_validation(self):
        store, sid, _ = make_store()
        r = tools.get_recent_events(store, sid, 30)
        self.assertEqual([e["kind"] for e in r["events"]], ["UNDER_ATTACK"])  # 90 s old event excluded
        self.assertEqual(len(tools.get_recent_events(store, sid, 120)["events"]), 2)
        for bad in (0, -5, 121, "30", 2.5, True, None):
            with self.subTest(bad=bad):
                self.assertEqual(tools.get_recent_events(store, sid, bad)["error"], "invalid_input")

    def test_technology_status(self):
        store, sid, _ = make_store()
        r = tools.get_technology_status(store, sid, "tech_age2_iron_age_v1")
        t = r["techs"][0]
        self.assertFalse(t["researched"])
        self.assertEqual(t["shortfall"], {"food": 120})  # needs 300 food, has 180
        self.assertEqual(t["prerequisites_missing"], [])  # Barracks + ArcheryYard owned
        self.assertFalse(t["affordable"])
        # Age II tech is blocked until Iron Age is done
        fb = tools.get_technology_status(store, sid, "tech_forged_blades_v1")["techs"][0]
        self.assertIn("tech_age2_iron_age_v1", fb["prerequisites_missing"])
        self.assertEqual(len(tools.get_technology_status(store, sid)["techs"]), 5)

    def test_technology_status_missing_building(self):
        snap = make_snapshot(own_buildings=[{"type": "Citadel", "count": 1}])
        store, sid, _ = make_store(snap)
        t = tools.get_technology_status(store, sid, "tech_longbow_training_v1")["techs"][0]
        self.assertIn("ArcheryYard built", t["prerequisites_missing"])

    def test_technology_status_unknown_tech(self):
        store, sid, _ = make_store()
        for bad in ("tech_nuke_v1", "", 5, ["x"]):
            with self.subTest(bad=bad):
                self.assertEqual(tools.get_technology_status(store, sid, bad)["error"], "invalid_input")

    def test_tool_log_records_calls_without_session_id(self):
        store, sid, _ = make_store()
        tools.get_player_resources(store, sid)
        tools.get_recent_events(store, sid, 999)
        self.assertEqual([(e["tool"], e["status"]) for e in store.tool_log],
                         [("get_player_resources", "ok"), ("get_recent_events", "rejected")])
        self.assertNotIn(sid, repr(store.tool_log))


class IdentityBoundaryTests(unittest.TestCase):
    def test_no_tool_accepts_identity_arguments(self):
        # session_id is a gateway-bound internal parameter; server.py never exposes it (see server test).
        forbidden = {"player_id", "match_id", "user_id", "opponent", "team"}
        for name in ("get_player_resources", "get_army_status", "get_visible_enemies", "get_recent_events",
                     "get_technology_status", "estimate_plan_cost", "search_game_knowledge"):
            params = set(inspect.signature(getattr(tools, name)).parameters)
            self.assertFalse(params & forbidden, f"{name} exposes identity parameter")

    def test_identity_kwarg_cannot_be_injected(self):
        store, sid, _ = make_store()
        with self.assertRaises(TypeError):
            tools.get_player_resources(store, sid, player_id="opponent")  # type: ignore[call-arg]


if __name__ == "__main__":
    unittest.main()
