"""Build the RAG index: python rag/build_index.py

Reads rag/knowledge/*, validates unique IDs, writes rag/index/index.json (gitignored).
The retriever rebuilds in memory if the index is missing or stale, so this step is
an optimization and a reproducible, inspectable artifact.
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from knowledge_loader import KnowledgeError  # noqa: E402
from retriever import INDEX_PATH, build_index_data  # noqa: E402


def main() -> int:
    try:
        data = build_index_data()
    except KnowledgeError as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 1
    INDEX_PATH.parent.mkdir(parents=True, exist_ok=True)
    INDEX_PATH.write_text(json.dumps(data, ensure_ascii=False), encoding="utf-8")
    by_type: dict[str, int] = {}
    for d in data["docs"]:
        by_type[d["type"]] = by_type.get(d["type"], 0) + 1
    print(f"Indexed {len(data['docs'])} records {by_type} -> {INDEX_PATH}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
