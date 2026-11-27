import copy
import unittest

import _agent_paths  # noqa: F401
from agent.plan_validation import extract_json, schema_backend, validate_plan

VALID = {
    "summary": "Counter the north cavalry.", "confidence": 0.84, "evidence": ["unit_spearman_v1"],
    "actions": [
        {"action_type": "TRAIN_UNIT", "unit_type": "Spearman", "count": 4, "reason": "Cavalry-heavy enemy."},
        {"action_type": "MOVE_GROUP", "target_zone": "NorthGate", "reason": "Concentrate defenders."},
        {"action_type": "BUILD", "building_type": "House", "reason": "Need population."},
        {"action_type": "RESEARCH", "tech_id": "tech_age2_iron_age_v1", "reason": "Advance."},
    ],
}


def mutate(fn):
    plan = copy.deepcopy(VALID)
    fn(plan)
    return plan


INVALID = {
    "unknown action type": mutate(lambda p: p["actions"][0].update(action_type="LAUNCH_NUKE")),
    "negative count": mutate(lambda p: p["actions"][0].update(count=-1)),
    "count too large": mutate(lambda p: p["actions"][0].update(count=21)),
    "float count": mutate(lambda p: p["actions"][0].update(count=2.5)),
    "bool count": mutate(lambda p: p["actions"][0].update(count=True)),
    "unknown unit": mutate(lambda p: p["actions"][0].update(unit_type="Dragon")),
    "unknown tech": mutate(lambda p: p["actions"][3].update(tech_id="tech_nuke_v1")),
    "unknown zone": mutate(lambda p: p["actions"][1].update(target_zone="EnemyBase")),
    "citadel not buildable": mutate(lambda p: p["actions"][2].update(building_type="Citadel")),
    "missing count": mutate(lambda p: p["actions"][0].pop("count")),
    "forbidden field for type": mutate(lambda p: p["actions"][1].update(count=3)),
    "extra action field": mutate(lambda p: p["actions"][0].update(player_id="opponent")),
    "extra top field": mutate(lambda p: p.update(debug=True)),
    "missing reason": mutate(lambda p: p["actions"][0].pop("reason")),
    "reason too long": mutate(lambda p: p["actions"][0].update(reason="x" * 251)),
    "missing summary": mutate(lambda p: p.pop("summary")),
    "summary too long": mutate(lambda p: p.update(summary="x" * 401)),
    "confidence > 1": mutate(lambda p: p.update(confidence=1.5)),
    "confidence string": mutate(lambda p: p.update(confidence="high")),
    "too many actions": mutate(lambda p: p.update(actions=[p["actions"][0]] * 9)),
    "bad evidence id": mutate(lambda p: p.update(evidence=["not an id"])),
    "duplicate evidence": mutate(lambda p: p.update(evidence=["unit_spearman_v1", "unit_spearman_v1"])),
    "limitations too long": mutate(lambda p: p.update(limitations="x" * 251)),
    "actions not a list": mutate(lambda p: p.update(actions="train")),
}


class PlanValidationTests(unittest.TestCase):
    def test_valid_plan_builtin_and_default(self):
        self.assertEqual(validate_plan(VALID, use_jsonschema=False), [])
        self.assertEqual(validate_plan(VALID), [])

    def test_empty_actions_is_valid(self):
        self.assertEqual(validate_plan({"summary": "Info only.", "confidence": 0.7, "actions": []}), [])

    def test_invalid_corpus_builtin(self):
        for label, plan in INVALID.items():
            with self.subTest(label):
                self.assertTrue(validate_plan(plan, use_jsonschema=False), label)

    def test_non_object_plans(self):
        for bad in (None, [], "plan", 5):
            self.assertTrue(validate_plan(bad, use_jsonschema=False))

    def test_builtin_matches_jsonschema(self):
        if schema_backend() != "jsonschema":
            self.skipTest("jsonschema not installed")
        for label, plan in {"valid": VALID, **INVALID}.items():
            with self.subTest(label):
                self.assertEqual(bool(validate_plan(plan, use_jsonschema=False)),
                                 bool(validate_plan(plan, use_jsonschema=True)), label)


class ExtractJsonTests(unittest.TestCase):
    def test_variants(self):
        self.assertEqual(extract_json('{"a": 1}'), ({"a": 1}, None))
        self.assertEqual(extract_json('Here:\n```json\n{"a": 1}\n```')[0], {"a": 1})
        self.assertEqual(extract_json('Plan => {"a": 1} done')[0], {"a": 1})

    def test_errors(self):
        for bad in ("", "   ", None, "no json here", "[1, 2]", "{broken"):
            obj, err = extract_json(bad)
            self.assertIsNone(obj)
            self.assertTrue(err)


if __name__ == "__main__":
    unittest.main()
