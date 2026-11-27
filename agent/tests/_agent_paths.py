"""Make the repo root, mcp-server/ and rag/ importable for agent tests."""
import sys
from pathlib import Path

_ROOT = Path(__file__).resolve().parents[2]
for p in (_ROOT, _ROOT / "mcp-server", _ROOT / "rag"):
    if str(p) not in sys.path:
        sys.path.insert(0, str(p))
