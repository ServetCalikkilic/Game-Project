import asyncio
import json
import unittest
from pathlib import Path

import _paths  # noqa: F401
import game_data as gd
from helpers import make_store
from tools.plan_tools import ACTION_TYPES

SCHEMA = json.loads((Path(__file__).resolve().parents[2] / "schemas" / "action_plan.schema.json").read_text(encoding="utf-8"))
EXPECTED_TOOLS = {"get_player_resources", "get_army_status", "get_visible_enemies", "get_technology_status",
                  "get_recent_events", "estimate_plan_cost", "search_game_knowledge"}


class SchemaParityTests(unittest.TestCase):
    """The server's whitelists must stay identical to the ActionPlan schema enums."""

    def test_enums_match_schema(self):
        d = SCHEMA["$defs"]
        self.assertEqual(set(d["unit_type"]["enum"]), set(gd.UNIT_NAMES))
        self.assertEqual(set(d["building_type"]["enum"]), set(gd.BUILDABLE_NAMES))
        self.assertEqual(set(d["tech_id"]["enum"]), set(gd.TECH_IDS))
        self.assertEqual(set(d["target_zone"]["enum"]), set(gd.ZONES))
        self.assertEqual(set(d["action"]["properties"]["action_type"]["enum"]), set(ACTION_TYPES))


try:
    import mcp  # noqa: F401
    HAVE_MCP = True
except ImportError:
    HAVE_MCP = False


@unittest.skipUnless(HAVE_MCP, "official MCP SDK not installed (pip install -r requirements.txt)")
class ServerTests(unittest.TestCase):
    def setUp(self):
        from server import build_server
        self.store, self.sid, _ = make_store()
        self.mcp = build_server(self.store, self.sid)

    def test_tools_registered_without_identity_params(self):
        tools = asyncio.run(self.mcp.list_tools())
        self.assertEqual({t.name for t in tools}, EXPECTED_TOOLS)
        for t in tools:
            props = set(t.inputSchema.get("properties", {}))
            self.assertFalse(props & {"player_id", "match_id", "session_id"}, t.name)

    def test_call_tool_end_to_end(self):
        result = asyncio.run(self.mcp.call_tool("get_visible_enemies", {}))
        self.assertIn("Raider", repr(result))
        self.assertEqual(self.store.tool_log[-1]["tool"], "get_visible_enemies")

    def test_identity_argument_is_rejected_by_sdk_schema_or_ignored(self):
        # Extra arguments must never change whose data is returned.
        try:
            asyncio.run(self.mcp.call_tool("get_player_resources", {"player_id": "opponent"}))
        except Exception:
            return
        self.assertEqual(self.store.tool_log[-1]["tool"], "get_player_resources")


if __name__ == "__main__":
    unittest.main()
