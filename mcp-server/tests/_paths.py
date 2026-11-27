"""Make mcp-server/ and rag/ importable when tests run from anywhere."""
import sys
from pathlib import Path

_SERVER = Path(__file__).resolve().parents[1]
for p in (_SERVER, _SERVER.parent / "rag"):
    if str(p) not in sys.path:
        sys.path.insert(0, str(p))
