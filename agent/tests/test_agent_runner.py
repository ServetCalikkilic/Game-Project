import asyncio
import json
import unittest

import _agent_paths  # noqa: F401
from agent_fakes import Clock, LocalToolClient, make_snapshot

from agent import config as cfg
from agent.agent_logging import AgentLogger
from agent.agent_runner import AgentRunner
from agent.llm import LLMResponse, ScriptedLLM, ToolCall
from agent.offline_llm import OfflineLLM
from agent.plan_validation import validate_plan

GOOD_PLAN = {"summary": "Train spearmen against the raiders.", "confidence": 0.9,
             "actions": [{"action_type": "TRAIN_UNIT", "unit_type": "Spearman", "count": 3, "reason": "Counter cavalry."}]}


def tc(name, args=None, i=[0]):
    i[0] += 1
    return ToolCall(f"id{i[0]}", name, args or {})


def final(plan):
    return LLMResponse(text=json.dumps(plan))


def run(llm, client, question="What should I do?", **kw):
    logger = AgentLogger(client.session)
    runner = AgentRunner(llm, client, client.session, logger, **kw)
    return asyncio.run(runner.run(question)), logger


def tool_names(result):
    return [c["tool"] for c in result.tool_calls]


class MultiToolTests(unittest.TestCase):
    def test_threat_question_calls_multiple_tools_dynamically(self):
        client = LocalToolClient()
        res, _ = run(OfflineLLM(), client, "I am being attacked by cavalry from the north. Can I defend?")
        self.assertTrue(res.ok, res.errors)
        names = set(tool_names(res))
        self.assertTrue({"get_visible_enemies", "get_army_status", "get_player_resources",
                         "search_game_knowledge", "estimate_plan_cost"} <= names)
        self.assertNotIn("get_technology_status", names)  # not relevant to the question
        train = next(a for a in res.plan["actions"] if a["action_type"] == "TRAIN_UNIT")
        self.assertEqual(train["unit_type"], "Spearman")
        self.assertEqual(train["count"], 3)  # 4 raiders -> 200 food needed, only 180 -> scaled to 3
        self.assertEqual(validate_plan(res.plan), [])

    def test_explicit_train_question_uses_resources_enemies_knowledge_and_cost(self):
        res, _ = run(OfflineLLM(), LocalToolClient(), "Can I train 5 Spearmen now, and would they help against the north gate attackers?")
        self.assertTrue(res.ok, res.errors)
        self.assertTrue({"get_player_resources", "get_visible_enemies", "search_game_knowledge", "estimate_plan_cost"} <= set(tool_names(res)))

    def test_tool_selection_depends_on_question(self):
        a, _ = run(OfflineLLM(), LocalToolClient(), "Should I research the next age upgrade?")
        b, _ = run(OfflineLLM(), LocalToolClient(), "I am being attacked from the north!")
        self.assertIn("get_technology_status", tool_names(a))
        self.assertNotIn("get_technology_status", tool_names(b))
        self.assertIn("get_visible_enemies", tool_names(b))

    def test_scripted_parallel_calls_are_all_logged(self):
        client = LocalToolClient()
        llm = ScriptedLLM([LLMResponse(tool_calls=[tc("get_player_resources"), tc("get_army_status"), tc("get_visible_enemies")]),
                           final(GOOD_PLAN)])
        res, logger = run(llm, client)
        self.assertTrue(res.ok)
        self.assertEqual(tool_names(res), ["get_player_resources", "get_army_status", "get_visible_enemies"])
        for entry in res.tool_calls:
            self.assertEqual(set(entry), {"step", "tool", "args_summary", "status", "duration_ms", "error"} | {"step"})
            self.assertEqual(entry["status"], "ok")


class KnowledgeOnlyTests(unittest.TestCase):
    def test_knowledge_question_calls_only_rag(self):
        client = LocalToolClient()
        res, _ = run(OfflineLLM(), client, "What does Iron Age do?")
        self.assertTrue(res.ok, res.errors)
        self.assertEqual(tool_names(res), ["search_game_knowledge"])
        self.assertEqual([c[0] for c in client.calls], ["search_game_knowledge"])
        self.assertIn("tech_age2_iron_age_v1", res.plan["evidence"])
        self.assertEqual(res.plan["actions"], [])
        self.assertGreaterEqual(res.plan["confidence"], 0.7)  # no live state needed -> no cap


class PlanValidityTests(unittest.TestCase):
    def test_invalid_then_valid_retries(self):
        bad = dict(GOOD_PLAN, actions=[{"action_type": "TRAIN_UNIT", "unit_type": "Spearman", "count": -4, "reason": "x"}])
        llm = ScriptedLLM([LLMResponse(tool_calls=[tc("get_player_resources")]), final(bad), final(GOOD_PLAN)])
        res, logger = run(llm, LocalToolClient())
        self.assertTrue(res.ok)
        self.assertEqual(res.retries, 1)
        self.assertTrue(any("invalid" in str(m.get("content", "")) for m in llm.calls[-1] if m["role"] == "user"))
        self.assertTrue(any(e["event"] == "plan_rejected" for e in logger.events))

    def test_always_invalid_returns_safe_fallback(self):
        for label, text in {"unknown action": json.dumps(dict(GOOD_PLAN, actions=[{"action_type": "NUKE", "reason": "x"}])),
                            "not json": "Sure! Train some spearmen.", "empty": ""}.items():
            with self.subTest(label):
                llm = ScriptedLLM([LLMResponse(text=text)] * 3)
                res, _ = run(llm, LocalToolClient())
                self.assertFalse(res.ok)
                self.assertTrue(res.used_fallback)
                self.assertEqual(res.plan["actions"], [])
                self.assertEqual(res.plan["confidence"], 0.0)
                self.assertEqual(validate_plan(res.plan), [])  # fallback itself is schema-valid
                self.assertEqual(res.retries, cfg.MAX_PLAN_RETRIES + 1)

    def test_unretrieved_evidence_is_dropped(self):
        plan = dict(GOOD_PLAN, evidence=["unit_spearman_v1", "unit_ram_v1"])
        found = {"ok": True, "results": [{"source_id": "unit_spearman_v1", "score": 1.0, "text": "Spearman"}]}
        llm = ScriptedLLM([LLMResponse(tool_calls=[tc("search_game_knowledge", {"query": "spearman counter", "top_k": 1})]), final(plan)])
        res, _ = run(llm, LocalToolClient(override={"search_game_knowledge": found}))
        self.assertTrue(res.ok)
        self.assertEqual(res.plan["evidence"], ["unit_spearman_v1"])

    def test_max_steps_limit(self):
        llm = ScriptedLLM([LLMResponse(tool_calls=[tc("get_player_resources")])] * 10)
        res, _ = run(llm, LocalToolClient(), max_steps=3)
        self.assertFalse(res.ok)
        self.assertEqual(res.errors, ["max_steps_exceeded"])
        self.assertEqual(res.steps, 3)
        self.assertEqual(len(llm.calls), 3)

    def test_tool_call_budget(self):
        llm = ScriptedLLM([LLMResponse(tool_calls=[tc("get_player_resources")] * 4)] * 5 + [final(GOOD_PLAN)])
        client = LocalToolClient()
        res, _ = run(llm, client, max_steps=8)
        self.assertEqual(len(client.calls), cfg.MAX_TOOL_CALLS)
        self.assertIn("rejected", [c["status"] for c in res.tool_calls])

    def test_llm_failure_is_contained(self):
        class Boom:
            async def generate(self, m, t):
                raise ConnectionError("down")
        res, _ = run(Boom(), LocalToolClient())
        self.assertFalse(res.ok)
        self.assertTrue(res.used_fallback)


class StaleAndMissingDataTests(unittest.TestCase):
    def test_stale_snapshot_lowers_confidence_and_sets_limitations(self):
        clock = Clock()
        client = LocalToolClient(clock=clock)
        clock.t += 300  # snapshot is 5 minutes old
        llm = ScriptedLLM([LLMResponse(tool_calls=[tc("get_visible_enemies")]), final(GOOD_PLAN)])
        res, _ = run(llm, client)
        self.assertTrue(res.ok)
        self.assertLessEqual(res.plan["confidence"], cfg.CONFIDENCE_CAP_STALE)
        self.assertIn("stale", res.plan["limitations"].lower())
        self.assertEqual(validate_plan(res.plan), [])

    def test_failed_tool_lowers_confidence(self):
        client = LocalToolClient(fail={"get_army_status"})
        llm = ScriptedLLM([LLMResponse(tool_calls=[tc("get_army_status"), tc("get_player_resources")]), final(GOOD_PLAN)])
        res, _ = run(llm, client)
        self.assertTrue(res.ok)
        self.assertLessEqual(res.plan["confidence"], cfg.CONFIDENCE_CAP_TOOL_FAILURE)
        self.assertIn("get_army_status", res.plan["limitations"])
        self.assertEqual([c["status"] for c in res.tool_calls], ["error", "ok"])
        self.assertEqual(res.tool_calls[0]["error"], "RuntimeError")

    def test_no_session_data_caps_confidence(self):
        client = LocalToolClient(with_session=False)
        llm = ScriptedLLM([LLMResponse(tool_calls=[tc("get_player_resources")]), final(GOOD_PLAN)])
        res, _ = run(llm, client)
        self.assertTrue(res.ok)
        self.assertLessEqual(res.plan["confidence"], cfg.CONFIDENCE_CAP_NO_LIVE_STATE)
        self.assertIn("No live game state", res.plan["limitations"])

    def test_plan_with_actions_but_no_tools_is_capped(self):
        res, _ = run(ScriptedLLM([final(GOOD_PLAN)]), LocalToolClient())
        self.assertLessEqual(res.plan["confidence"], cfg.CONFIDENCE_CAP_NO_LIVE_STATE)

    def test_limitations_stay_within_schema_limit(self):
        plan = dict(GOOD_PLAN, limitations="y" * 250)
        res, _ = run(ScriptedLLM([final(plan)]), LocalToolClient(fail=set()))
        self.assertLessEqual(len(res.plan["limitations"]), 250)
        self.assertEqual(validate_plan(res.plan), [])


class SessionIdentityTests(unittest.TestCase):
    def test_identity_is_in_logs_not_in_model_visible_messages(self):
        client = LocalToolClient()
        llm = ScriptedLLM([LLMResponse(tool_calls=[tc("get_player_resources")]), final(GOOD_PLAN)])
        _, logger = run(llm, client)
        self.assertEqual(logger.tool_calls[0]["tool"], "get_player_resources")
        blob = json.dumps(llm.calls, default=lambda o: o.__dict__)
        self.assertNotIn("m-test", blob)
        self.assertNotIn("p-test", blob)

    def test_log_file_has_identity_and_no_secrets(self):
        import tempfile
        from pathlib import Path
        client = LocalToolClient()
        with tempfile.TemporaryDirectory() as d:
            path = Path(d) / "agent.jsonl"
            logger = AgentLogger(client.session, path)
            llm = ScriptedLLM([LLMResponse(tool_calls=[tc("search_game_knowledge", {"query": "api_key=sk-ABCDEFGHIJKLMNOP123 spearman"})]),
                               final(GOOD_PLAN)])
            asyncio.run(AgentRunner(llm, client, client.session, logger).run("spearman?"))
            text = path.read_text(encoding="utf-8")
        self.assertNotIn("sk-ABCDEFGHIJKLMNOP123", text)
        self.assertIn("[REDACTED]", text)
        self.assertIn('"match_id": "m-test"', text)


if __name__ == "__main__":
    unittest.main()
