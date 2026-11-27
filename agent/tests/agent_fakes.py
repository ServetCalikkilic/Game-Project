"""Test doubles: an in-process tool client backed by the REAL mcp-server tool functions."""
import _agent_paths  # noqa: F401
import tools as server_tools
from retriever import KnowledgeRetriever
from session_store import SessionStore

from agent.session import AgentSession

NOW = 2_000_000.0


class Clock:
    def __init__(self, t=NOW):
        self.t = t

    def __call__(self):
        return self.t


def make_snapshot(**overrides):
    snap = {
        "phase": "UnderAttack", "captured_at": NOW,
        "resources": {"wood": 420, "food": 180, "gold": 250},
        "population": {"used": 14, "cap": 20},
        "technology": {"age": 1, "researched": [], "researching": []},
        "own_army": [{"type": "Worker", "count": 8, "zone": "FarmArea"}, {"type": "Spearman", "count": 3, "zone": "BaseCenter"}],
        "own_buildings": [{"type": "Citadel", "count": 1}, {"type": "Barracks", "count": 1}, {"type": "ArcheryYard", "count": 1}],
        "visible_enemies": [{"type": "Raider", "count": 4, "zone": "NorthGate"}],
        "recent_events": [{"kind": "UNDER_ATTACK", "zone": "NorthGate", "seconds_ago": 5}],
    }
    snap.update(overrides)
    return snap


_PROPS = {
    "get_player_resources": {}, "get_army_status": {}, "get_visible_enemies": {},
    "get_technology_status": {"tech_id": {}}, "get_recent_events": {"seconds": {}},
    "estimate_plan_cost": {"actions": {}}, "search_game_knowledge": {"query": {}, "top_k": {}},
}
_RETRIEVER = KnowledgeRetriever.load(index_path="does-not-exist.json")


class LocalToolClient:
    """Calls the real tool implementations in-process. Extras for fault injection."""

    def __init__(self, snapshot=None, clock=None, with_session=True, extra_tools=(), fail=(), override=None):
        self.clock = clock or Clock()
        self.store = SessionStore(clock=self.clock)
        self.session = AgentSession("m-test", "p-test")
        self.sid = None
        if with_session:
            ctx = self.store.create_session(self.session.match_id, self.session.player_id)
            self.sid = ctx.session_id
            self.store.ingest_snapshot(self.sid, snapshot or make_snapshot())
        self.extra_tools = list(extra_tools)
        self.fail = set(fail)
        self.override = override or {}
        self.calls = []  # (name, arguments) actually executed

    async def list_tools(self):
        tools = [{"name": n, "description": n, "input_schema": {"type": "object", "properties": p}} for n, p in _PROPS.items()]
        tools += [{"name": n, "description": n, "input_schema": {"type": "object", "properties": {}}} for n in self.extra_tools]
        return tools

    async def call_tool(self, name, arguments):
        self.calls.append((name, dict(arguments)))
        if name in self.fail:
            raise RuntimeError("boom")
        if name in self.override:
            return self.override[name]
        s, sid = self.store, self.sid
        if name == "search_game_knowledge":
            return server_tools.search_game_knowledge(s, _RETRIEVER, **arguments)
        return getattr(server_tools, name)(s, sid, **arguments)
