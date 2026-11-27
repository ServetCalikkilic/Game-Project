"""Load and normalize the game knowledge base into flat, retrievable records.

Sources (single source of truth for game data):
  rag/knowledge/units.json, buildings.json, technologies.json  -> JSON arrays
  rag/knowledge/rules.md                                       -> "## <id>" sections

Every record gets: source_id, type, name, text, tags. IDs must be unique and match
ID_RE (unit_*_v1, bld_*_v1, tech_*_v1, rule_*_v1).
"""
from __future__ import annotations

import hashlib
import json
import re
from pathlib import Path

KNOWLEDGE_DIR = Path(__file__).resolve().parent / "knowledge"
JSON_FILES = ("units.json", "buildings.json", "technologies.json")
RULES_FILE = "rules.md"
ID_RE = re.compile(r"^(unit|bld|tech|rule)_[a-z0-9_]+_v[0-9]+$")
_PREFIX_TYPE = {"unit": "unit", "bld": "building", "tech": "technology", "rule": "rule"}


class KnowledgeError(ValueError):
    """Raised when the knowledge base is malformed (bad/duplicate IDs, bad JSON)."""


def _fmt_value(value) -> str:
    if isinstance(value, dict):
        return ", ".join(f"{k} {v}" for k, v in value.items()) or "none"
    if isinstance(value, list):
        return ", ".join(str(v) for v in value) or "none"
    return str(value)


def render_record(rec: dict) -> str:
    """Turn a JSON record into 'key: value' lines, so every field is searchable."""
    lines = []
    for key, value in rec.items():
        if key in ("id", "type"):
            continue
        lines.append(f"{key.replace('_', ' ')}: {_fmt_value(value)}")
    return "\n".join(lines)


def _load_json_records(path: Path) -> list[dict]:
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise KnowledgeError(f"{path.name}: cannot read/parse ({exc})") from exc
    if not isinstance(data, list):
        raise KnowledgeError(f"{path.name}: top level must be a JSON array")
    out = []
    for rec in data:
        rid = rec.get("id") if isinstance(rec, dict) else None
        if not isinstance(rid, str) or not ID_RE.match(rid):
            raise KnowledgeError(f"{path.name}: invalid or missing id: {rid!r}")
        out.append(
            {
                "source_id": rid,
                "type": rec.get("type") or _PREFIX_TYPE[rid.split("_", 1)[0]],
                "name": str(rec.get("name", rid)),
                "text": f"{rec.get('name', rid)} ({rid})\n{render_record(rec)}",
                "tags": [],
            }
        )
    return out


_SECTION_RE = re.compile(r"^## (\S+)\s*$", re.MULTILINE)
_BULLET_RE = re.compile(r"^- (\w+):\s*(.*)$")


def _load_rules(path: Path) -> list[dict]:
    try:
        content = path.read_text(encoding="utf-8")
    except OSError as exc:
        raise KnowledgeError(f"{path.name}: cannot read ({exc})") from exc
    matches = list(_SECTION_RE.finditer(content))
    out = []
    for i, m in enumerate(matches):
        rid = m.group(1)
        if not ID_RE.match(rid):
            raise KnowledgeError(f"{path.name}: invalid rule id {rid!r}")
        end = matches[i + 1].start() if i + 1 < len(matches) else len(content)
        fields: dict[str, str] = {}
        for line in content[m.end():end].splitlines():
            b = _BULLET_RE.match(line.strip())
            if b:
                fields[b.group(1)] = b.group(2).strip()
        if "text" not in fields:
            raise KnowledgeError(f"{path.name}: {rid} has no '- text:' bullet")
        tags = [t.strip() for t in fields.get("tags", "").split(",") if t.strip()]
        name = rid.rsplit("_v", 1)[0].replace("_", " ")
        out.append(
            {
                "source_id": rid,
                "type": "rule",
                "name": name,
                "text": f"{name} ({rid})\ntags: {', '.join(tags)}\n{fields['text']}",
                "tags": tags,
            }
        )
    return out


def load_records(knowledge_dir: Path | str = KNOWLEDGE_DIR) -> list[dict]:
    """Return all records. Raises KnowledgeError on duplicate or malformed IDs."""
    kdir = Path(knowledge_dir)
    records: list[dict] = []
    for fname in JSON_FILES:
        records.extend(_load_json_records(kdir / fname))
    records.extend(_load_rules(kdir / RULES_FILE))
    seen: set[str] = set()
    for rec in records:
        if rec["source_id"] in seen:
            raise KnowledgeError(f"duplicate id: {rec['source_id']}")
        seen.add(rec["source_id"])
    return records


def knowledge_hash(knowledge_dir: Path | str = KNOWLEDGE_DIR) -> str:
    """Hash of the source files; lets the retriever detect a stale index."""
    h = hashlib.sha256()
    for fname in (*JSON_FILES, RULES_FILE):
        h.update(fname.encode())
        h.update((Path(knowledge_dir) / fname).read_bytes())
    return h.hexdigest()
