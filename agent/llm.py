"""Provider-neutral LLM interface.

Neutral message format (adapters convert to/from their provider's format):
  {"role": "system"|"user", "content": str}
  {"role": "assistant", "text": str|None, "tool_calls": [ToolCall, ...]}
  {"role": "tool", "tool_call_id": str, "name": str, "content": str}
Tool definitions: {"name", "description", "input_schema"}.
"""
from __future__ import annotations

from dataclasses import dataclass, field
from typing import Protocol


@dataclass
class ToolCall:
    id: str
    name: str
    arguments: dict


@dataclass
class LLMResponse:
    text: str | None = None
    tool_calls: list[ToolCall] = field(default_factory=list)


class LLMClient(Protocol):
    async def generate(self, messages: list[dict], tools: list[dict]) -> LLMResponse: ...


class ScriptedLLM:
    """Deterministic LLM for tests: replays a list of responses (or callables(messages, tools))."""

    def __init__(self, script: list):
        self.script = list(script)
        self.calls: list[list[dict]] = []

    async def generate(self, messages: list[dict], tools: list[dict]) -> LLMResponse:
        self.calls.append([dict(m) for m in messages])
        if not self.script:
            raise RuntimeError("ScriptedLLM script exhausted")
        item = self.script.pop(0)
        return item(messages, tools) if callable(item) else item
