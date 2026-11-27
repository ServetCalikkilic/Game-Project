"""Agent loop: LLM <-> MCP tools -> schema-valid ActionPlan (a proposal only).

Flow per question:
  sanitize question -> list MCP tools (allowlist) -> loop up to MAX_AGENT_STEPS:
      LLM turn -> tool calls (policy-checked, logged, results treated as data)
               -> or final answer -> parse -> schema + leak validation -> retry or accept
  accepted plan -> data-quality pass (stale/failed tools lower confidence + limitations)

The runner never executes game actions. Unity shows the plan, the player approves, and
GameActionValidator re-checks it before any Photon command is issued.
"""
from __future__ import annotations

import asyncio
import copy
import json
import time
from dataclasses import asdict, dataclass, field
from typing import Any

from . import config as cfg
from .agent_logging import AgentLogger
from .llm import LLMClient, LLMResponse, ToolCall
from .plan_validation import extract_json, validate_plan
from .prompts import build_system_prompt
from .security import check_output_text, check_tool_args, detect_injection, sanitize_question, sanitize_tool_result
from .session import AgentSession
from .tool_client import ToolClient

_DATA_FAILURE_IGNORED = {"invalid_input", "invalid_plan"}  # the model's bad arguments, not missing game data


@dataclass
class AgentResult:
    ok: bool
    plan: dict | None
    errors: list[str] = field(default_factory=list)
    steps: int = 0
    retries: int = 0
    used_fallback: bool = False
    answer_text: str | None = None
    tool_calls: list[dict] = field(default_factory=list)
    security_events: list[dict] = field(default_factory=list)

    def to_dict(self) -> dict:
        return asdict(self)


def fallback_plan(reason: str) -> dict:
    """Safe, schema-valid 'do nothing' plan shown when the agent could not produce a valid answer."""
    return {"summary": "No safe plan could be produced; no action is proposed.", "confidence": 0.0,
            "limitations": reason[:250], "actions": []}


class _Quality:
    """Tracks what the agent actually learned, to ground confidence and limitations."""

    def __init__(self):
        self.stale = False
        self.failed: set[str] = set()
        self.live_ok: set[str] = set()
        self.retrieved_ids: set[str] = set()


class AgentRunner:
    def __init__(self, llm: LLMClient, tool_client: ToolClient, session: AgentSession,
                 logger: AgentLogger | None = None, max_steps: int = cfg.MAX_AGENT_STEPS,
                 use_jsonschema: bool = True):
        self.llm, self.tools, self.session = llm, tool_client, session
        self.log = logger or AgentLogger(session)
        self.max_steps = max_steps
        self.use_jsonschema = use_jsonschema

    # ------------------------------------------------------------------ public
    async def run(self, question: str) -> AgentResult:
        t0 = time.perf_counter()
        try:
            q = sanitize_question(question)
        except ValueError as exc:
            return self._fail(["invalid question: " + str(exc)], 0, 0, None)

        labels = detect_injection(q)
        if labels:
            self.log.security("prompt_injection_attempt", labels=labels, question_len=len(q))

        try:
            tool_defs, schemas = await self._load_tools()
        except Exception as exc:  # noqa: BLE001 - transport failure must not crash the game
            self.log.info("tool_listing_failed", error=type(exc).__name__)
            return self._fail([f"tool listing failed: {type(exc).__name__}"], 0, 0, None)

        quality = _Quality()
        messages: list[dict] = [{"role": "system", "content": build_system_prompt()},
                                {"role": "user", "content": q}]
        retries, calls_made = 0, [0]
        last_text: str | None = None

        for step in range(1, self.max_steps + 1):
            try:
                resp: LLMResponse = await asyncio.wait_for(self.llm.generate(messages, tool_defs), cfg.LLM_TIMEOUT_S)
            except Exception as exc:  # noqa: BLE001
                self.log.info("llm_error", error=type(exc).__name__, step=step)
                return self._fail([f"llm error: {type(exc).__name__}"], step, retries, last_text)

            if resp.tool_calls:
                messages.append({"role": "assistant", "text": resp.text, "tool_calls": resp.tool_calls})
                for i, call in enumerate(resp.tool_calls):
                    if i >= cfg.MAX_CALLS_PER_STEP:
                        content = json.dumps({"ok": False, "error": "too_many_calls_in_one_step"})
                    else:
                        content = await self._execute(call, step, schemas, quality, calls_made)
                    messages.append({"role": "tool", "tool_call_id": call.id, "name": call.name, "content": content})
                continue

            last_text = resp.text
            plan, errors = self._parse_and_validate(resp.text)
            if not errors:
                plan = self._finalize(plan, quality)
                self.log.info("plan_accepted", steps=step, retries=retries,
                              latency_ms=round((time.perf_counter() - t0) * 1000, 1))
                return AgentResult(True, plan, [], step, retries, False, last_text,
                                   list(self.log.tool_calls), list(self.log.security_events))
            retries += 1
            self.log.info("plan_rejected", errors=errors[:5], attempt=retries)
            if retries > cfg.MAX_PLAN_RETRIES:
                return self._fail(errors, step, retries, last_text)
            messages.append({"role": "assistant", "text": resp.text, "tool_calls": []})
            messages.append({"role": "user", "content":
                             "Your answer was invalid: " + "; ".join(errors)[:600] +
                             ". Reply with the corrected JSON object only."})

        return self._fail(["max_steps_exceeded"], self.max_steps, retries, last_text)

    # ------------------------------------------------------------------ tools
    async def _load_tools(self) -> tuple[list[dict], dict[str, dict]]:
        raw = await asyncio.wait_for(self.tools.list_tools(), cfg.TOOL_TIMEOUT_S)
        allowed = [t for t in raw if t["name"] in cfg.ALLOWED_TOOLS]
        hidden = sorted({t["name"] for t in raw} - cfg.ALLOWED_TOOLS)
        if hidden:
            self.log.info("tools_hidden_from_model", tools=hidden)
        return allowed, {t["name"]: t.get("input_schema") or {} for t in allowed}

    async def _execute(self, call: ToolCall, step: int, schemas: dict, q: _Quality, counter: list[int]) -> str:
        name, args = call.name, call.arguments
        t0 = time.perf_counter()

        def reject(error: str, event: str | None = None, **details: Any) -> str:
            if event:
                self.log.security(event, tool=name, **details)
            self.log.tool_call(step, name, args, "rejected", (time.perf_counter() - t0) * 1000, error)
            return json.dumps({"ok": False, "error": error})

        if name not in schemas:
            return reject("unknown_or_disallowed_tool", "disallowed_tool_call")
        if counter[0] >= cfg.MAX_TOOL_CALLS:
            return reject("tool_call_budget_exhausted")
        problem = check_tool_args(name, args, schemas[name])
        if problem:
            ident = problem.startswith("identity")
            return reject("invalid_arguments: " + problem, "identity_argument_blocked" if ident else None)
        counter[0] += 1

        try:
            result = await asyncio.wait_for(self.tools.call_tool(name, args), cfg.TOOL_TIMEOUT_S)
        except Exception as exc:  # noqa: BLE001
            q.failed.add(name)
            self.log.tool_call(step, name, args, "error", (time.perf_counter() - t0) * 1000, type(exc).__name__)
            return json.dumps({"ok": False, "error": "tool_failed"})

        ok = result.get("ok", True) is not False
        error = result.get("error")
        status = "ok" if ok else "rejected"
        if ok:
            if name in cfg.LIVE_STATE_TOOLS:
                q.live_ok.add(name)
            if result.get("stale"):
                q.stale = True
            for hit in result.get("results", []) if name == "search_game_knowledge" else []:
                if isinstance(hit, dict) and isinstance(hit.get("source_id"), str):
                    q.retrieved_ids.add(hit["source_id"])
        elif error not in _DATA_FAILURE_IGNORED:
            q.failed.add(name)

        safe, flagged = sanitize_tool_result(result)
        if flagged:
            self.log.security("injection_in_tool_output", tool=name, flagged_strings=flagged)
        self.log.tool_call(step, name, args, status, (time.perf_counter() - t0) * 1000, error)
        content = json.dumps(safe, ensure_ascii=False)
        if len(content) > cfg.MAX_TOOL_RESULT_CHARS:
            content = content[: cfg.MAX_TOOL_RESULT_CHARS] + "...[truncated]"
        return content

    # ------------------------------------------------------------------ output
    def _parse_and_validate(self, text: str | None) -> tuple[dict | None, list[str]]:
        plan, err = extract_json(text or "")
        if err:
            return None, [err]
        errors = validate_plan(plan, self.use_jsonschema)
        if not errors:
            leaks = check_output_text(plan)
            if leaks:
                self.log.security("output_leak_blocked", labels=leaks)
                errors = [f"free text contains forbidden content ({', '.join(leaks)})"]
        return (plan if not errors else None), errors

    def _finalize(self, plan: dict, q: _Quality) -> dict:
        """Ground the plan: drop unretrieved evidence ids, reflect stale/missing data in confidence."""
        plan = copy.deepcopy(plan)
        if "evidence" in plan:
            kept = [e for e in plan["evidence"] if e in q.retrieved_ids]
            if len(kept) != len(plan["evidence"]):
                self.log.info("evidence_dropped", dropped=len(plan["evidence"]) - len(kept))
            plan["evidence"] = kept
            if not kept:
                del plan["evidence"]

        notes, cap = [], 1.0
        if q.stale:
            notes.append("Game snapshot is stale.")
            cap = min(cap, cfg.CONFIDENCE_CAP_STALE)
        if q.failed:
            notes.append("Some tools failed or had no data: " + ", ".join(sorted(q.failed)) + ".")
            cap = min(cap, cfg.CONFIDENCE_CAP_TOOL_FAILURE)
        if plan["actions"] and not q.live_ok:
            notes.append("No live game state was read.")
            cap = min(cap, cfg.CONFIDENCE_CAP_NO_LIVE_STATE)
        if notes:
            ours = " ".join(notes)
            theirs = plan.get("limitations", "")
            plan["limitations"] = (ours + (" " + theirs if theirs else ""))[:250]
        plan["confidence"] = round(min(plan["confidence"], cap), 4)
        return plan

    def _fail(self, errors: list[str], steps: int, retries: int, text: str | None) -> AgentResult:
        self.log.info("agent_failed", errors=errors[:5])
        plan = fallback_plan("; ".join(errors))
        return AgentResult(False, plan, errors, steps, retries, True, text,
                           list(self.log.tool_calls), list(self.log.security_events))
