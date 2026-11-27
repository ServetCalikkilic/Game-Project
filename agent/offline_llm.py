"""OfflineLLM: a small rule-based stand-in for a real LLM.

It lets the whole pipeline (MCP tools, RAG, validation, approval payload) run end to end
without an API key, and gives a deterministic baseline for evaluation. It decides *which
tools to call from the question* and builds a plan only from the tool results it received.
It is NOT the production planner; plug a real provider in via the `LLMClient` protocol.
"""
from __future__ import annotations

import json
import re

from .llm import LLMResponse, ToolCall
from .security import detect_injection

# unit -> best counter unit (from rag/knowledge/rules.md: rule_counter_triangle_v1)
_COUNTER = {"Raider": "Spearman", "Archer": "Raider", "Spearman": "Archer", "Guardian": "Raider", "Ram": "Spearman"}
_UNITS = ("spearman", "archer", "raider", "guardian", "ram", "worker")
_THREAT = ("attack", "defend", "enemy", "enemies", "threat", "cavalry", "raider", "incoming", "north", "south", "east", "west")
_ECON = ("afford", "resource", "gold", "food", "wood", "train", "build", "cost", "spend", "economy")
_TECH = ("tech", "upgrade", "age", "research")


class OfflineLLM:
    def __init__(self):
        self._n = 0

    async def generate(self, messages: list[dict], tools: list[dict]) -> LLMResponse:
        q = next(m["content"] for m in messages if m["role"] == "user")
        ql = q.lower()
        results = {m["name"]: json.loads(m["content"]) for m in messages if m["role"] == "tool"
                   if m["content"].startswith("{") and m["content"].endswith("}")}
        called = {m["name"] for m in messages if m["role"] == "tool"}
        available = {t["name"] for t in tools}

        injection = bool(detect_injection(q))
        explicit = re.search(r"(\d+)\s+(spearm[ae]n|archers?|raiders?|guardians?|rams?|workers?)\b", ql)
        threat = any(w in ql for w in _THREAT)
        econ = any(w in ql for w in _ECON) or explicit is not None
        tech = any(w in ql for w in _TECH)
        info = re.match(r"\s*(what does|what is|what are|explain|how does|tell me about)", ql) is not None
        knowledge_only = info and not (threat or re.search(r"\b(my|can i|should i|now)\b", ql)) and not injection

        if injection:
            wanted = ["get_visible_enemies"]
        elif knowledge_only:
            wanted = ["search_game_knowledge"]
        else:
            wanted = []
            if threat:
                wanted += ["get_visible_enemies", "get_army_status"]
            if econ or threat:
                wanted.append("get_player_resources")
            if tech:
                wanted.append("get_technology_status")
            wanted.append("search_game_knowledge")
        wanted = [w for w in wanted if w in available]

        pending = [w for w in wanted if w not in called]
        if pending:
            return LLMResponse(tool_calls=self._calls(pending, q))

        if injection:
            return self._final("I can only reason from information currently visible to you; hidden enemy "
                               "positions, resources and army are not available to me, so I propose no action.",
                               0.6, [], results)

        if knowledge_only:
            return self._final(self._knowledge_summary(results), 0.8, [], results)

        draft = self._draft(q, explicit, results)
        if draft and "estimate_plan_cost" in available and "estimate_plan_cost" not in called:
            return LLMResponse(tool_calls=self._calls(["estimate_plan_cost"], q, {"actions": draft}))
        if not draft:
            enemies = results.get("get_visible_enemies", {}).get("enemies")
            msg = ("No enemies are currently visible; no action is needed right now."
                   if enemies == [] else "I could not determine a useful action from the available information.")
            return self._final(msg, 0.5, [], results)
        return self._finish_with_estimate(draft, results)

    # -- helpers --
    def _calls(self, names, q, override=None):
        out = []
        for w in names:
            self._n += 1
            out.append(ToolCall(f"call_{self._n}", w, override if override and w == "estimate_plan_cost" else self._args(w, q)))
        return out

    @staticmethod
    def _args(name: str, q: str) -> dict:
        return {"query": q[:200], "top_k": 3} if name == "search_game_knowledge" else {}

    @staticmethod
    def _knowledge_summary(results: dict) -> str:
        hits = results.get("search_game_knowledge", {}).get("results", [])
        if not hits:
            return "No matching game knowledge was found."
        m = re.search(r"description: (.+)|text: (.+)|\n([^\n]{40,})$", hits[0]["text"])
        body = next((g for g in (m.groups() if m else ()) if g), hits[0]["text"].split("\n", 1)[-1])
        return f"{hits[0]['source_id']}: {body}"[:400]

    @staticmethod
    def _draft(q: str, explicit, results: dict) -> list[dict]:
        actions: list[dict] = []
        enemies = results.get("get_visible_enemies", {}).get("enemies", [])
        if explicit:
            unit = next(u.capitalize() for u in _UNITS if explicit.group(2).startswith(u[:5]))
            actions.append({"action_type": "TRAIN_UNIT", "unit_type": unit,
                            "count": max(1, min(20, int(explicit.group(1)))), "reason": "Requested by the player."})
            return actions
        threats = [e for e in enemies if e["type"] in _COUNTER]
        if threats:
            main = max(threats, key=lambda e: e["count"])
            actions.append({"action_type": "TRAIN_UNIT", "unit_type": _COUNTER[main["type"]],
                            "count": min(20, main["count"]),
                            "reason": f"Visible {main['type']} force at {main['zone']}; counter from knowledge base."})
            actions.append({"action_type": "MOVE_GROUP", "target_zone": main["zone"],
                            "reason": "Concentrate defenders at the threatened zone."})
        return actions

    def _finish_with_estimate(self, draft: list[dict], results: dict) -> LLMResponse:
        est = results.get("estimate_plan_cost", {})
        actions = [dict(a) for a in draft]
        train = next((a for a in actions if a["action_type"] == "TRAIN_UNIT"), None)
        note = ""
        if est.get("ok") and train:
            total, avail = est["total_cost"], est["available"]
            ratios = [avail[r] / total[r] for r in total if total[r] > 0]
            scale = min(ratios + [1.0])
            blocked = any("requires" in i for i in est.get("issues", []))
            if blocked or int(train["count"] * scale) < 1:
                actions.remove(train)
                note = " Training is not possible now (cost or prerequisites)."
            elif scale < 1.0:
                train["count"] = int(train["count"] * scale)
                note = f" Reduced to {train['count']} units to stay affordable."
        elif not est.get("ok"):
            note = " Cost estimate unavailable."
        names = [f"{a.get('count', '')} {a.get('unit_type') or a.get('target_zone')}".strip() for a in actions]
        summary = ("Proposed: " + ", ".join(names) + "." if names else "No affordable action right now.") + note
        return self._final(summary, 0.8, actions, results)

    @staticmethod
    def _final(summary: str, confidence: float, actions: list[dict], results: dict) -> LLMResponse:
        ids = [h["source_id"] for h in results.get("search_game_knowledge", {}).get("results", [])][:3]
        plan = {"summary": summary[:400], "confidence": confidence, "actions": actions}
        if ids:
            plan["evidence"] = ids
        return LLMResponse(text=json.dumps(plan))
