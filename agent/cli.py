"""CLI demo: python -m agent.cli "question" [--snapshot FILE] [--match-id M] [--player-id P] [--log FILE]

Starts the MCP server for one session, runs the agent with the offline rule-based LLM
(no API key needed) and prints the tool log, security events and the ActionPlan JSON.
"""
from __future__ import annotations

import argparse
import asyncio
import json
import sys

from .agent_logging import AgentLogger
from .agent_runner import AgentRunner
from .config import ROOT
from .offline_llm import OfflineLLM
from .session import AgentSession
from .tool_client import McpStdioToolClient


async def _main(args) -> int:
    session = AgentSession(args.match_id, args.player_id, str(args.snapshot))
    logger = AgentLogger(session, args.log)
    async with McpStdioToolClient(session) as client:
        result = await AgentRunner(OfflineLLM(), client, session, logger).run(args.question)
    print(json.dumps({"ok": result.ok, "steps": result.steps, "retries": result.retries,
                      "tool_calls": result.tool_calls, "security_events": result.security_events,
                      "errors": result.errors, "plan": result.plan}, indent=2, ensure_ascii=False))
    return 0 if result.ok else 1


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("question")
    p.add_argument("--snapshot", default=str(ROOT / "mcp-server" / "sample_snapshot.json"))
    p.add_argument("--match-id", default="dev-match")
    p.add_argument("--player-id", default="dev-player")
    p.add_argument("--log", default=None, help="JSONL log file (e.g. logs/agent.jsonl)")
    sys.exit(asyncio.run(_main(p.parse_args())))


if __name__ == "__main__":
    main()
