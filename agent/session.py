"""Authenticated session context held by the gateway/agent, never by the model."""
from __future__ import annotations

from dataclasses import dataclass


@dataclass(frozen=True)
class AgentSession:
    """Identity is fixed when the gateway creates the session. The LLM can neither read
    nor change it: it is passed to the MCP server process via its environment and is
    only attached to log entries."""

    match_id: str
    player_id: str
    snapshot_file: str | None = None  # dev bootstrap; production gateway ingests snapshots directly

    def __post_init__(self):
        for name in ("match_id", "player_id"):
            v = getattr(self, name)
            if not isinstance(v, str) or not v or len(v) > 64:
                raise ValueError(f"{name} must be a non-empty string (<=64 chars)")

    def server_env(self) -> dict[str, str]:
        env = {"RTS_MATCH_ID": self.match_id, "RTS_PLAYER_ID": self.player_id}
        if self.snapshot_file:
            env["RTS_SNAPSHOT_FILE"] = self.snapshot_file
        return env

    def log_context(self) -> dict[str, str]:
        return {"match_id": self.match_id, "player_id": self.player_id}
