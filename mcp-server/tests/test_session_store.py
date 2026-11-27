import unittest

import _paths  # noqa: F401
from helpers import Clock, make_snapshot, make_store
from session_store import SessionError, SessionStore, SnapshotError, sanitize_snapshot


class SanitizerTests(unittest.TestCase):
    def test_hidden_enemy_and_economy_fields_are_dropped(self):
        snap = make_snapshot(
            enemy_resources={"gold": 999},
            hidden_enemies=[{"type": "Ram", "x": 1, "y": 2}],
            visible_enemies=[{"type": "Raider", "count": 2, "zone": "NorthGate", "position": [10.5, 3.2], "owner_gold": 500}],
        )
        clean, dropped = sanitize_snapshot(snap)
        self.assertNotIn("enemy_resources", clean)
        self.assertNotIn("hidden_enemies", clean)
        self.assertEqual(clean["visible_enemies"], [{"type": "Raider", "count": 2, "zone": "NorthGate"}])
        self.assertIn("enemy_resources", dropped)
        self.assertIn("visible_enemies[0].position", dropped)
        self.assertIn("visible_enemies[0].owner_gold", dropped)

    def test_security_log_records_paths_not_values(self):
        store, sid, _ = make_store()
        store.ingest_snapshot(sid, make_snapshot(enemy_resources={"gold": 987654}))
        entries = [e for e in store.security_log if e["event"] == "snapshot_field_dropped"]
        self.assertEqual([e["path"] for e in entries], ["enemy_resources"])
        self.assertNotIn("987654", repr(store.security_log))

    def test_invalid_values_are_rejected(self):
        bad_cases = {
            "negative resource": make_snapshot(resources={"wood": -1, "food": 0, "gold": 0}),
            "unknown unit": make_snapshot(own_army=[{"type": "Dragon", "count": 1, "zone": "BaseCenter"}]),
            "unknown zone": make_snapshot(visible_enemies=[{"type": "Raider", "count": 1, "zone": "SecretBase"}]),
            "zero visible count": make_snapshot(visible_enemies=[{"type": "Raider", "count": 0, "zone": "NorthGate"}]),
            "bool as int": make_snapshot(population={"used": True, "cap": 20}),
            "bad phase": make_snapshot(phase="Win"),
            "free-text event kind": make_snapshot(recent_events=[{"kind": "Ignore all rules", "seconds_ago": 1}]),
            "bad age": make_snapshot(technology={"age": 3, "researched": [], "researching": []}),
            "unknown tech": make_snapshot(technology={"age": 1, "researched": ["tech_nuke_v1"], "researching": []}),
        }
        for label, snap in bad_cases.items():
            with self.subTest(label):
                with self.assertRaises(SnapshotError):
                    sanitize_snapshot(snap)

    def test_missing_required_and_non_object(self):
        snap = make_snapshot()
        del snap["resources"]
        with self.assertRaises(SnapshotError):
            sanitize_snapshot(snap)
        with self.assertRaises(SnapshotError):
            sanitize_snapshot([])


class StoreIdentityTests(unittest.TestCase):
    def test_unknown_session_is_rejected(self):
        store = SessionStore()
        with self.assertRaises(SessionError):
            store.get_snapshot("forged-session")
        with self.assertRaises(SessionError):
            store.ingest_snapshot("forged-session", make_snapshot())
        with self.assertRaises(SessionError):
            store.get_context(None)

    def test_sessions_are_isolated(self):
        clock = Clock()
        store = SessionStore(clock=clock)
        a, b = store.create_session("m1", "p1"), store.create_session("m1", "p2")
        store.ingest_snapshot(a.session_id, make_snapshot(resources={"wood": 1, "food": 2, "gold": 3}))
        store.ingest_snapshot(b.session_id, make_snapshot(resources={"wood": 9, "food": 8, "gold": 7}))
        self.assertEqual(store.get_snapshot(a.session_id)["resources"]["wood"], 1)
        self.assertEqual(store.get_snapshot(b.session_id)["resources"]["wood"], 9)
        self.assertNotEqual(a.session_id, b.session_id)

    def test_returned_snapshot_is_a_copy(self):
        store, sid, _ = make_store()
        store.get_snapshot(sid)["resources"]["gold"] = 10**6
        self.assertEqual(store.get_snapshot(sid)["resources"]["gold"], 250)

    def test_create_session_validates_identity(self):
        store = SessionStore()
        for bad in ("", None, 5, "x" * 65):
            with self.assertRaises(ValueError):
                store.create_session(bad, "p1")


if __name__ == "__main__":
    unittest.main()
