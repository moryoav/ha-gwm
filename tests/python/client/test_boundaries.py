"""Architectural boundary tests for the standalone protocol package."""

from __future__ import annotations

import ast
from pathlib import Path

PACKAGE_DIR = Path(__file__).resolve().parents[3] / "gwm_ora_client"


def test_client_package_has_no_home_assistant_imports() -> None:
    imported_modules: set[str] = set()
    for source_path in PACKAGE_DIR.glob("*.py"):
        tree = ast.parse(source_path.read_text(encoding="utf-8"), filename=str(source_path))
        for node in ast.walk(tree):
            if isinstance(node, ast.Import):
                imported_modules.update(alias.name for alias in node.names)
            elif isinstance(node, ast.ImportFrom) and node.module is not None:
                imported_modules.add(node.module)

    assert not {module for module in imported_modules if module == "homeassistant" or module.startswith("homeassistant.")}
