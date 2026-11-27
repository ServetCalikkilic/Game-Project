"""Agent limits and allowlists. No secrets here; API keys come from the environment only."""
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SCHEMA_PATH = ROOT / "schemas" / "action_plan.schema.json"
MCP_SERVER_PATH = ROOT / "mcp-server" / "server.py"

MAX_AGENT_STEPS = 8          # LLM turns (tool rounds + final answer + validation retries)
MAX_TOOL_CALLS = 12          # total tool calls per question
MAX_CALLS_PER_STEP = 4       # parallel calls the model may issue in one turn
MAX_PLAN_RETRIES = 2         # re-asks after an invalid final answer
LLM_TIMEOUT_S = 60.0
TOOL_TIMEOUT_S = 15.0
MAX_QUESTION_CHARS = 500
MAX_TOOL_RESULT_CHARS = 6000

# Only these MCP tools are ever shown to / callable by the model.
ALLOWED_TOOLS = frozenset({
    "get_player_resources", "get_army_status", "get_visible_enemies", "get_technology_status",
    "get_recent_events", "estimate_plan_cost", "search_game_knowledge",
})
# Tools that read the live match state (used to judge whether a plan is grounded).
LIVE_STATE_TOOLS = frozenset({
    "get_player_resources", "get_army_status", "get_visible_enemies", "get_technology_status",
    "get_recent_events",
})
# Arguments the model may never pass; identity comes from the session only.
IDENTITY_KEYS = frozenset({"player_id", "match_id", "session_id", "user_id", "opponent", "team", "owner"})

CONFIDENCE_CAP_STALE = 0.5
CONFIDENCE_CAP_TOOL_FAILURE = 0.4
CONFIDENCE_CAP_NO_LIVE_STATE = 0.3
