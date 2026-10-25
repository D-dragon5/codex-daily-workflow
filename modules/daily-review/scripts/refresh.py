"""Convenience entry point; canonical implementation ships with the skill."""
from pathlib import Path
import runpy

if __name__ == "__main__":
    runpy.run_path(str(Path(__file__).resolve().parents[1] / "skills" / "daily-review" / "scripts" / "refresh.py"), run_name="__main__")
