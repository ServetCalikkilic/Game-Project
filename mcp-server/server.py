"""RTS Strategic Commander MCP server (official MCP Python SDK, FastMCP API).

Run (stdio):   python mcp-server/server.py
Inspector:     mcp dev mcp-server/server.py      (or: npx @modelcontextprotocol/inspector python mcp-server/server.py)

Identity model: one server process = one authenticated session. The AI Gateway
creates the session (match_id + player_id) and feeds filtered snapshots through
`SessionStore.ingest_snapshot`. Tools take NO identity arguments, and there is no
tool that ingests state, so the model can neither switch players nor inject data.

Dev bootstrap (no gateway): set
    RTS_MATCH_ID, RTS_PLAYER_ID     identity for the session
    RTS_SNAPSHOT_FILE               path to a JSON AiSafeSnapshot (re-read when it changes)
"""
from __future__ import annotations

import json
import os
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
sys.path.insert(0, str(HERE.parent / "rag"))

from mcp.server.fastmcp import FastMCP  # noqa: E402
from retriever import KnowledgeRetriever  # noqa: E402

import tools  # noqa: E402
from session_store import SessionStore  # noqa: E402


def build_server(store: SessionStore, session_id: str | None, retriever=None, refresh=None) -> FastMCP:
    """Create the MCP server bound to a fixed session. `refresh` is an optional dev hook."""
    retriever = retriever or KnowledgeRetriever.load()
    mcp = FastMCP("RTS Strategic Commander")

    def sid() -> str | None:
        if refresh:
            refresh()
        return session_id

    @mcp.tool()
    def get_player_resources() -> dict:
        """Current player's wood/food/gold and population (used/cap). Own data only."""
        return tools.get_player_resources(store, sid())

    @mcp.tool()
    def get_army_status() -> dict:
        """Current player's own units (by type, count, zone) and own buildings."""
        return tools.get_army_status(store, sid())

    @mcp.tool()
    def get_visible_enemies() -> dict:
        """Enemies currently visible under fog of war (type, count, coarse zone). Hidden enemies are never returned."""
        return tools.get_visible_enemies(store, sid())

    @mcp.tool()
    def get_technology_status(tech_id: str | None = None) -> dict:
        """Age and technology status: researched, prerequisites, cost, affordability. Optional tech_id (e.g. tech_age2_iron_age_v1)."""
        return tools.get_technology_status(store, sid(), tech_id)

    @mcp.tool()
    def get_recent_events(seconds: int = 30) -> dict:
        """Safe, structured recent events of the current player (1-120 s window)."""
        return tools.get_recent_events(store, sid(), seconds)

    @mcp.tool()
    def estimate_plan_cost(actions: list[dict]) -> dict:
        """Estimate cost, affordability, population and prerequisite issues of up to 8 proposed actions
        (TRAIN_UNIT/BUILD/MOVE_GROUP/RESEARCH, same fields as the ActionPlan schema). Advisory only."""
        return tools.estimate_plan_cost(store, sid(), actions)

    @mcp.tool()
    def search_game_knowledge(query: str, top_k: int = 3) -> dict:
        """RAG search over unit, building, technology and rule records. Returns source_id + text (top_k 1-5)."""
        return tools.search_game_knowledge(store, retriever, query, top_k)

    return mcp


def _dev_session() -> tuple[SessionStore, str | None, object]:
    """Bootstrap a session from environment variables (development / Inspector use)."""
    store = SessionStore()
    match_id, player_id = os.environ.get("RTS_MATCH_ID"), os.environ.get("RTS_PLAYER_ID")
    snap_file = os.environ.get("RTS_SNAPSHOT_FILE")
    if not (match_id and player_id):
        print("RTS_MATCH_ID / RTS_PLAYER_ID not set: tools will report no_active_session", file=sys.stderr)
        return store, None, None
    ctx = store.create_session(match_id, player_id)
    state = {"mtime": None}

    def refresh():
        if not snap_file:
            return
        try:
            m = os.path.getmtime(snap_file)
            if m != state["mtime"]:
                store.ingest_snapshot(ctx.session_id, json.loads(Path(snap_file).read_text(encoding="utf-8")))
                state["mtime"] = m
        except (OSError, ValueError) as exc:
            print(f"snapshot refresh failed: {type(exc).__name__}", file=sys.stderr)

    refresh()
    return store, ctx.session_id, refresh


if __name__ == "__main__":
    _store, _sid, _refresh = _dev_session()
    build_server(_store, _sid, refresh=_refresh).run()
