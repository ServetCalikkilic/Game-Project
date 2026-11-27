"""MCP tool client: connects to the RTS MCP server over stdio and calls tools."""
from __future__ import annotations

import json
import sys
from typing import Any, Protocol

from .config import MCP_SERVER_PATH
from .session import AgentSession


class ToolError(RuntimeError):
    """A tool call failed at the transport/protocol level."""


class ToolClient(Protocol):
    async def list_tools(self) -> list[dict]: ...
    async def call_tool(self, name: str, arguments: dict) -> dict: ...


class McpStdioToolClient:
    """Spawns `mcp-server/server.py` for ONE session (identity passed via env, not by the model).

    Usage:
        async with McpStdioToolClient(session) as client:
            tools = await client.list_tools()
    """

    def __init__(self, session: AgentSession, server_path=MCP_SERVER_PATH, python: str | None = None):
        self.session = session
        self.server_path = str(server_path)
        self.python = python or sys.executable
        self._stack = None
        self._mcp = None

    async def __aenter__(self):
        from contextlib import AsyncExitStack

        from mcp import ClientSession, StdioServerParameters
        from mcp.client.stdio import stdio_client

        self._stack = AsyncExitStack()
        # Only RTS_* identity variables are passed on purpose; API keys never reach the server process.
        params = StdioServerParameters(command=self.python, args=[self.server_path], env=self.session.server_env())
        read, write = await self._stack.enter_async_context(stdio_client(params))
        self._mcp = await self._stack.enter_async_context(ClientSession(read, write))
        await self._mcp.initialize()
        return self

    async def __aexit__(self, *exc):
        if self._stack:
            await self._stack.aclose()
        self._stack = self._mcp = None

    async def list_tools(self) -> list[dict]:
        res = await self._mcp.list_tools()
        return [{"name": t.name, "description": t.description or "", "input_schema": t.inputSchema} for t in res.tools]

    async def call_tool(self, name: str, arguments: dict) -> dict:
        res = await self._mcp.call_tool(name, arguments)
        text = next((c.text for c in res.content if getattr(c, "text", None) is not None), None)
        if res.isError:
            raise ToolError((text or "tool error")[:200])
        try:
            data: Any = json.loads(text) if text is not None else None
        except json.JSONDecodeError as exc:
            raise ToolError("tool returned non-JSON output") from exc
        if not isinstance(data, dict):
            raise ToolError("tool returned unexpected output shape")
        return data
