"""Dependency-free BM25 retriever over the game knowledge base.

Usage:
    r = KnowledgeRetriever.load()          # uses rag/index/index.json if fresh, else builds in memory
    r.search("counter cavalry", top_k=3)   # -> [{"source_id", "score", "name", "type", "text"}]
"""
from __future__ import annotations

import json
import math
import re
import sys
from collections import Counter
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from knowledge_loader import KNOWLEDGE_DIR, knowledge_hash, load_records  # noqa: E402

INDEX_PATH = Path(__file__).resolve().parent / "index" / "index.json"
MAX_QUERY_LEN = 300
MAX_TOP_K = 5
K1, B = 1.5, 0.75

_TOKEN_RE = re.compile(r"[a-z0-9]+")
_STOPWORDS = {"the", "a", "an", "of", "to", "and", "or", "in", "is", "are", "for", "on", "it", "my", "what", "how", "do", "does", "i", "can", "me", "with", "be", "v1"}
# Query-side aliases so natural wording reaches the game's own terms.
_ALIASES = {
    "cavalry": ["raider"],
    "horse": ["raider"],
    "mounted": ["raider"],
    "ranged": ["archer"],
    "tank": ["guardian"],
    "siege": ["ram"],
    "age": ["iron"],
    "upgrade": ["technology", "tech"],
    "pop": ["population"],
}


def tokenize(text: str) -> list[str]:
    out = []
    for tok in _TOKEN_RE.findall(text.lower()):
        if tok in _STOPWORDS:
            continue
        if len(tok) > 3 and tok.endswith("s") and not tok.endswith("ss"):
            tok = tok[:-1]
        if len(tok) > 6 and tok.endswith("ing"):
            tok = tok[:-3]
        elif len(tok) > 5 and tok.endswith("ed"):
            tok = tok[:-2]
        out.append(tok)
    return out


def doc_tokens(rec: dict) -> list[str]:
    """Text tokens, plus the name (x2) and id words so exact-name queries rank first."""
    id_words = [w for w in rec["source_id"].split("_") if not re.fullmatch(r"v\d+", w)]
    return tokenize(rec["text"]) + tokenize(rec["name"]) * 2 + tokenize(" ".join(id_words))


def build_index_data(knowledge_dir: Path | str = KNOWLEDGE_DIR) -> dict:
    docs = []
    for rec in load_records(knowledge_dir):
        docs.append({**rec, "tokens": doc_tokens(rec)})
    return {"version": 1, "source_hash": knowledge_hash(knowledge_dir), "docs": docs}


class KnowledgeRetriever:
    def __init__(self, docs: list[dict]):
        self.docs = docs
        self._tf = [Counter(d["tokens"]) for d in docs]
        self._len = [len(d["tokens"]) for d in docs]
        self._avg = (sum(self._len) / len(docs)) if docs else 0.0
        df: Counter = Counter()
        for tf in self._tf:
            df.update(tf.keys())
        n = len(docs)
        self._idf = {t: math.log(1 + (n - c + 0.5) / (c + 0.5)) for t, c in df.items()}

    @classmethod
    def load(cls, index_path: Path | str = INDEX_PATH, knowledge_dir: Path | str = KNOWLEDGE_DIR):
        """Load a fresh on-disk index; otherwise build in memory (never serves a stale index)."""
        try:
            data = json.loads(Path(index_path).read_text(encoding="utf-8"))
            if data.get("version") == 1 and data.get("source_hash") == knowledge_hash(knowledge_dir):
                return cls(data["docs"])
        except (OSError, json.JSONDecodeError, KeyError):
            pass
        return cls(build_index_data(knowledge_dir)["docs"])

    def search(self, query: str, top_k: int = 3) -> list[dict]:
        if not isinstance(query, str) or not query.strip():
            raise ValueError("query must be a non-empty string")
        if len(query) > MAX_QUERY_LEN:
            raise ValueError(f"query too long (max {MAX_QUERY_LEN} chars)")
        if isinstance(top_k, bool) or not isinstance(top_k, int):
            raise ValueError("top_k must be an integer")
        top_k = max(1, min(top_k, MAX_TOP_K))

        # Original words weigh 1.0; alias expansions 0.5 so they help recall without dominating.
        q_weights: dict[str, float] = {}
        for tok in tokenize(query):
            q_weights[tok] = 1.0
            for alias in _ALIASES.get(tok, []):
                q_weights[alias] = max(q_weights.get(alias, 0.0), 0.5)
        scored = []
        for i, doc in enumerate(self.docs):
            tf, dl = self._tf[i], self._len[i]
            score = 0.0
            for t, w in q_weights.items():
                f = tf.get(t, 0)
                if f:
                    score += w * self._idf[t] * f * (K1 + 1) / (f + K1 * (1 - B + B * dl / self._avg))
            if score > 0:
                scored.append((score, doc["source_id"], doc))
        scored.sort(key=lambda s: (-s[0], s[1]))
        return [
            {
                "source_id": d["source_id"],
                "score": round(s, 4),
                "name": d["name"],
                "type": d["type"],
                "text": d["text"],
            }
            for s, _, d in scored[:top_k]
        ]


if __name__ == "__main__":
    q = " ".join(sys.argv[1:]) or "counter cavalry"
    for hit in KnowledgeRetriever.load().search(q):
        print(f"{hit['score']:7.3f}  {hit['source_id']}")
