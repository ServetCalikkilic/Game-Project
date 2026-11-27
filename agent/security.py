"""Security helpers: input sanitizing, injection detection, tool-argument policy, output guard.

These checks are defense in depth. The primary protection is structural: the MCP tools
cannot return hidden state and take no identity arguments, so even a fully "jailbroken"
model has nothing hidden to leak and no way to change whose data it reads.
"""
from __future__ import annotations

import re
from typing import Any

from .config import IDENTITY_KEYS, MAX_QUESTION_CHARS

_INJECTION_PATTERNS = {
    "ignore_rules": re.compile(r"ignore\s+(all\s+|any\s+|your\s+)?(previous|prior|above|the)\s+(rules?|instructions?|prompts?)", re.I),
    "reveal_hidden": re.compile(r"(reveal|show|tell|leak|expose).{0,50}(hidden|secret|opponent|enemy).{0,50}(base|position|location|resources?|gold|army|economy)"
                                r"|even\s+if\s+(they|it|them)\s*(are|is)?\s*hidden|see\s+through\s+(the\s+)?fog", re.I),
    "identity_switch": re.compile(r"player_id|match_id|session_id|act\s+as\s+(the\s+)?(opponent|admin|server)", re.I),
    "call_all_tools": re.compile(r"call\s+(any|every|all)\s+tools?", re.I),
    "prompt_leak": re.compile(r"(print|show|reveal|repeat)\s+.{0,20}(system\s+prompt|your\s+instructions)", re.I),
    "role_override": re.compile(r"(you\s+are\s+now|developer\s+mode|jailbreak|disregard\s+(the\s+)?(rules|policy))", re.I),
}

_SECRET_PATTERNS = [
    re.compile(r"sk-[A-Za-z0-9_\-]{10,}"),
    re.compile(r"(?i)bearer\s+[A-Za-z0-9._\-]{8,}"),
    re.compile(r"(?i)(api[_-]?key|token|secret|password)\s*[=:]\s*\S+"),
]
_COORDS = re.compile(r"\(\s*-?\d+(\.\d+)?\s*,\s*-?\d+(\.\d+)?(\s*,\s*-?\d+(\.\d+)?)?\s*\)")
_ENEMY_ECON = re.compile(r"(enemy|opponent)('s|s')?\s+(gold|food|wood|resources?|economy|stockpile)\s*(is|are|=|:|of)?\s*\d+", re.I)
_CTRL = re.compile(r"[\x00-\x08\x0b\x0c\x0e-\x1f\x7f]")

REMOVED = "[removed: instruction-like content]"


def sanitize_question(text: Any) -> str:
    if not isinstance(text, str):
        raise ValueError("question must be a string")
    text = _CTRL.sub(" ", text).strip()
    if not text:
        raise ValueError("question must not be empty")
    return text[:MAX_QUESTION_CHARS]


def detect_injection(text: str) -> list[str]:
    return [label for label, rx in _INJECTION_PATTERNS.items() if rx.search(text)]


def redact(text: str) -> str:
    for rx in _SECRET_PATTERNS:
        text = rx.sub("[REDACTED]", text)
    return text


def sanitize_tool_result(value: Any) -> tuple[Any, int]:
    """Treat tool output as data: neutralize instruction-like strings. Returns (value, n_flagged)."""
    if isinstance(value, str):
        if detect_injection(value):
            return REMOVED, 1
        return value, 0
    if isinstance(value, list):
        out, n = [], 0
        for v in value:
            s, k = sanitize_tool_result(v)
            out.append(s)
            n += k
        return out, n
    if isinstance(value, dict):
        out, n = {}, 0
        for k_, v in value.items():
            s, k = sanitize_tool_result(v)
            out[k_] = s
            n += k
        return out, n
    return value, 0


def check_tool_args(name: str, args: Any, input_schema: dict | None) -> str | None:
    """Return an error string if the model's arguments violate policy, else None."""
    if not isinstance(args, dict):
        return "arguments must be an object"
    banned = sorted(set(args) & IDENTITY_KEYS)
    if banned:
        return f"identity arguments are not allowed: {banned}"
    props = (input_schema or {}).get("properties")
    if props is not None:
        unknown = sorted(set(args) - set(props))
        if unknown:
            return f"unknown arguments: {unknown}"
    return None


def _plan_strings(plan: dict) -> list[str]:
    out = [str(plan.get("summary", "")), str(plan.get("limitations", ""))]
    out += [str(a.get("reason", "")) for a in plan.get("actions", []) if isinstance(a, dict)]
    return out


def check_output_text(plan: dict) -> list[str]:
    """Labels for leaks in the free-text fields of a plan (secrets, coordinates, enemy economy)."""
    labels: set[str] = set()
    for s in _plan_strings(plan):
        if any(rx.search(s) for rx in _SECRET_PATTERNS):
            labels.add("secret_in_output")
        if _COORDS.search(s):
            labels.add("coordinates_in_output")
        if _ENEMY_ECON.search(s):
            labels.add("enemy_economy_claim")
    return sorted(labels)
