"""ActionPlan parsing and validation against schemas/action_plan.schema.json.

Uses `jsonschema` when installed; otherwise a built-in validator that mirrors the schema
rule by rule (enums are read from the schema file, so there is one source of truth).
`validate_plan(..., use_jsonschema=False)` forces the built-in path (used by parity tests).
"""
from __future__ import annotations

import json
import re
from functools import lru_cache
from typing import Any

from .config import SCHEMA_PATH

_EVIDENCE_RE = re.compile(r"^(unit|bld|tech|rule)_[a-z0-9_]+_v[0-9]+$")
_FENCE_RE = re.compile(r"```(?:json)?\s*(\{.*?\})\s*```", re.S)


@lru_cache(maxsize=1)
def load_schema() -> dict:
    return json.loads(SCHEMA_PATH.read_text(encoding="utf-8"))


def extract_json(text: str) -> tuple[Any, str | None]:
    """Pull a JSON object out of the model's final answer. Returns (obj, error)."""
    if not isinstance(text, str) or not text.strip():
        return None, "empty answer"
    candidates = [text.strip()]
    m = _FENCE_RE.search(text)
    if m:
        candidates.append(m.group(1))
    if "{" in text and "}" in text:
        candidates.append(text[text.index("{"): text.rindex("}") + 1])
    for c in candidates:
        try:
            obj = json.loads(c)
        except json.JSONDecodeError:
            continue
        if isinstance(obj, dict):
            return obj, None
        return None, "top-level JSON value must be an object"
    return None, "no valid JSON object found in answer"


# ---------- built-in validator (mirrors the schema) ----------

def _is_int(v: Any) -> bool:
    return (isinstance(v, int) and not isinstance(v, bool)) or (isinstance(v, float) and v.is_integer())


def _is_num(v: Any) -> bool:
    return isinstance(v, (int, float)) and not isinstance(v, bool)


def _str(v: Any, path: str, lo: int, hi: int, errors: list[str]) -> None:
    if not isinstance(v, str):
        errors.append(f"{path}: must be a string")
    elif not lo <= len(v) <= hi:
        errors.append(f"{path}: length must be {lo}..{hi}")


def _validate_builtin(plan: Any) -> list[str]:
    schema = load_schema()
    defs = schema["$defs"]
    errors: list[str] = []
    if not isinstance(plan, dict):
        return ["plan: must be an object"]
    allowed = set(schema["properties"])
    for k in sorted(set(plan) - allowed):
        errors.append(f"plan: unknown field '{k}'")
    for k in schema["required"]:
        if k not in plan:
            errors.append(f"plan: missing required field '{k}'")

    if "summary" in plan:
        _str(plan["summary"], "summary", 1, 400, errors)
    if "confidence" in plan and (not _is_num(plan["confidence"]) or not 0 <= plan["confidence"] <= 1):
        errors.append("confidence: must be a number between 0 and 1")
    if "limitations" in plan:
        _str(plan["limitations"], "limitations", 0, 250, errors)
    if "evidence" in plan:
        ev = plan["evidence"]
        if not isinstance(ev, list) or len(ev) > 10:
            errors.append("evidence: must be an array of at most 10 items")
        else:
            if len(set(map(repr, ev))) != len(ev):
                errors.append("evidence: items must be unique")
            for i, e in enumerate(ev):
                if not isinstance(e, str) or not _EVIDENCE_RE.match(e):
                    errors.append(f"evidence[{i}]: invalid source id")

    if "actions" in plan:
        actions = plan["actions"]
        if not isinstance(actions, list) or len(actions) > 8:
            errors.append("actions: must be an array of at most 8 items")
        else:
            for i, a in enumerate(actions):
                errors.extend(_validate_action(a, f"actions[{i}]", defs))
    return errors


def _validate_action(a: Any, p: str, defs: dict) -> list[str]:
    errors: list[str] = []
    if not isinstance(a, dict):
        return [f"{p}: must be an object"]
    props = defs["action"]["properties"]
    for k in sorted(set(a) - set(props)):
        errors.append(f"{p}: unknown field '{k}'")
    for k in defs["action"]["required"]:
        if k not in a:
            errors.append(f"{p}: missing required field '{k}'")
    at = a.get("action_type")
    for field, ref in (("action_type", None), ("unit_type", "unit_type"), ("building_type", "building_type"),
                       ("tech_id", "tech_id"), ("target_zone", "target_zone")):
        if field in a:
            enum = props["action_type"]["enum"] if ref is None else defs[ref]["enum"]
            if not isinstance(a[field], str) or a[field] not in enum:
                errors.append(f"{p}.{field}: not an allowed value")
    if "count" in a and (not _is_int(a["count"]) or not 1 <= a["count"] <= 20):
        errors.append(f"{p}.count: must be an integer between 1 and 20")
    if "reason" in a:
        _str(a["reason"], f"{p}.reason", 1, 250, errors)
    # Conditional rules (allOf/if/then in the schema).
    for rule in defs["action"]["allOf"]:
        if rule["if"]["properties"]["action_type"]["const"] != at:
            continue
        then = rule["then"]
        for req in then.get("required", []):
            if req not in a:
                errors.append(f"{p}: {at} requires '{req}'")
        for forb in then.get("not", {}).get("anyOf", []):
            f = forb["required"][0]
            if f in a:
                errors.append(f"{p}: '{f}' is not allowed for {at}")
    return errors


def _validate_jsonschema(plan: Any) -> list[str]:
    import jsonschema  # noqa: PLC0415

    validator = jsonschema.Draft202012Validator(load_schema())
    return sorted(f"{'/'.join(str(x) for x in e.absolute_path) or 'plan'}: {e.message}"[:200]
                  for e in validator.iter_errors(plan))


def validate_plan(plan: Any, use_jsonschema: bool = True) -> list[str]:
    """Return a list of error strings (empty = schema-valid)."""
    if use_jsonschema:
        try:
            import jsonschema  # noqa: F401, PLC0415
        except ImportError:
            return _validate_builtin(plan)
        return _validate_jsonschema(plan)
    return _validate_builtin(plan)


def schema_backend() -> str:
    try:
        import jsonschema  # noqa: F401, PLC0415
        return "jsonschema"
    except ImportError:
        return "builtin"
