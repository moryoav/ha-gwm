"""Architectural boundary tests for the standalone protocol package."""

from __future__ import annotations

import ast
import json
from pathlib import Path

import pytest

PACKAGE_DIR = Path(__file__).resolve().parents[3] / "gwm_ora_client"
FIXTURE_DIR = Path(__file__).with_name("fixtures")


def test_client_package_has_no_home_assistant_imports() -> None:
    imported_modules: set[str] = set()
    for source_path in PACKAGE_DIR.rglob("*.py"):
        tree = ast.parse(source_path.read_text(encoding="utf-8"), filename=str(source_path))
        for node in ast.walk(tree):
            if isinstance(node, ast.Import):
                imported_modules.update(alias.name for alias in node.names)
            elif isinstance(node, ast.ImportFrom) and node.module is not None:
                imported_modules.add(node.module)

    forbidden = {
        module
        for module in imported_modules
        if module == "homeassistant"
        or module.startswith("homeassistant.")
        or module == "custom_components"
        or module.startswith("custom_components.")
    }
    assert not forbidden


def test_production_client_does_not_import_disposable_live_poc() -> None:
    production_sources = [
        path
        for path in PACKAGE_DIR.rglob("*.py")
        if path.name != "live_poc.py"
    ]

    for source_path in production_sources:
        tree = ast.parse(source_path.read_text(encoding="utf-8"), filename=str(source_path))
        imported = _absolute_import_targets(tree, source_path)
        assert not {
            target
            for target in imported
            if target == "gwm_ora_client.live_poc"
            or target.startswith("gwm_ora_client.live_poc.")
        }


def _absolute_import_targets(tree: ast.AST, source_path: Path) -> set[str]:
    relative_parts = source_path.relative_to(PACKAGE_DIR).with_suffix("").parts
    package_parts = ["gwm_ora_client", *relative_parts[:-1]]
    targets: set[str] = set()
    for node in ast.walk(tree):
        if isinstance(node, ast.Import):
            targets.update(alias.name for alias in node.names)
        elif isinstance(node, ast.ImportFrom):
            if node.level:
                parent_count = node.level - 1
                base_parts = package_parts[: len(package_parts) - parent_count]
                module_parts = node.module.split(".") if node.module else []
                module = ".".join([*base_parts, *module_parts])
            else:
                module = node.module or ""
            if module:
                targets.add(module)
                targets.update(f"{module}.{alias.name}" for alias in node.names)
    return targets


def test_client_fixtures_are_versioned_and_explicitly_synthetic() -> None:
    fixture_paths = sorted(FIXTURE_DIR.glob("*.json"))
    assert fixture_paths

    for fixture_path in fixture_paths:
        text = fixture_path.read_text(encoding="utf-8")
        payload = json.loads(text)
        assert payload["schema_version"] == 1
        assert "-----BEGIN PRIVATE KEY-----" not in text
        assert "-----BEGIN RSA PRIVATE KEY-----" not in text
        assert '"password"' not in text
        assert '"refresh_token"' not in text
        _assert_sensitive_fixture_values_are_synthetic(payload)


def _assert_sensitive_fixture_values_are_synthetic(value: object) -> None:
    if isinstance(value, dict):
        for key, child in value.items():
            normalized_key = key.replace("_", "").lower()
            if normalized_key in {"accesstoken", "refreshtoken"}:
                assert isinstance(child, str) and child.startswith("SYNTHETIC-")
            if key == "identifier":
                assert isinstance(child, str) and child.startswith("SYNTHETIC")
            _assert_sensitive_fixture_values_are_synthetic(child)
    elif isinstance(value, list):
        for child in value:
            _assert_sensitive_fixture_values_are_synthetic(child)


@pytest.mark.parametrize("key", ["access_token", "accessToken", "refresh_token", "refreshToken"])
def test_fixture_guard_covers_wire_and_python_token_spellings(key: str) -> None:
    with pytest.raises(AssertionError):
        _assert_sensitive_fixture_values_are_synthetic({key: "REAL-TOKEN-MUST-FAIL"})
