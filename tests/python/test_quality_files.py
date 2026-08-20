"""Quality and community metadata tests."""

from __future__ import annotations

import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def _png_size(path: Path) -> tuple[int, int]:
    data = path.read_bytes()
    assert data.startswith(b"\x89PNG\r\n\x1a\n")
    return int.from_bytes(data[16:20], "big"), int.from_bytes(data[20:24], "big")


def test_translation_and_icon_files_are_valid_json() -> None:
    json.loads((ROOT / "custom_components/gwm_ora/translations/en.json").read_text(encoding="utf-8"))
    json.loads((ROOT / "custom_components/gwm_ora/icons.json").read_text(encoding="utf-8"))


def test_community_health_files_exist() -> None:
    for path in (
        "CODE_OF_CONDUCT.md",
        "CONTRIBUTING.md",
        "LICENSE",
        "README.md",
        "SECURITY.md",
        "SUPPORT.md",
        ".github/pull_request_template.md",
        ".github/ISSUE_TEMPLATE/bug_report.yml",
        ".github/ISSUE_TEMPLATE/feature_request.yml",
        ".github/ISSUE_TEMPLATE/config.yml",
    ):
        assert (ROOT / path).is_file()


def test_quality_scale_tracker_exists() -> None:
    quality_scale = ROOT / "custom_components/gwm_ora/quality_scale.yaml"
    text = quality_scale.read_text(encoding="utf-8")

    assert "config-flow: done" in text
    assert "diagnostics: done" in text
    assert "reconfiguration-flow: done" in text
    assert "test-coverage:" in text


def test_addon_schema_avoids_supervisor_string_range_validators() -> None:
    """Avoid Supervisor string/password length schema forms that validate as ranges."""
    config = (ROOT / "addons/gwm_ora/config.yaml").read_text(encoding="utf-8")

    assert "country: \"match(^[A-Za-z]{2}$)\"" in config
    assert "region: eu" in config
    assert "region: \"list(eu|aus|rus)\"" in config
    assert "username: email" in config
    assert not re.search(r":\s*[\"']?(?:str|password)\(", config)


def test_addon_metadata_declares_internal_api_and_discovery() -> None:
    config = (ROOT / "addons/gwm_ora/config.yaml").read_text(encoding="utf-8")
    dockerfile = (ROOT / "addons/gwm_ora/Dockerfile").read_text(encoding="utf-8")

    assert "discovery:\n  - gwm_ora" in config
    assert "ingress: true" in config
    assert "ingress_port: 8099" in config
    assert "8099/tcp: null" in config
    assert "ASPNETCORE_HTTP_PORTS: \"8099\"" in config
    assert "version: \"0.7.0\"" in config
    assert "GWM_ORA_ADDON_VERSION: \"0.7.0\"" in config
    assert "ASPNETCORE_URLS" not in config
    assert "ENV ASPNETCORE_HTTP_PORTS=8099" in dockerfile
    assert "ENV GWM_ORA_ADDON_VERSION=${BUILD_VERSION}" in dockerfile
    assert "gwm_root_rus.pem" in dockerfile
    assert "dotnet publish" in dockerfile and "--no-restore" in dockerfile
    assert "ASPNETCORE_URLS" not in dockerfile

    dockerignore = (ROOT / "addons/gwm_ora/.dockerignore").read_text(encoding="utf-8")
    assert "**/bin/" in dockerignore
    assert "**/obj/" in dockerignore

    addon_build = (ROOT / ".github/workflows/addon-build.yml").read_text(encoding="utf-8")
    assert "pull_request:\n    branches: [main]" in addon_build


def test_addon_presentation_assets_exist() -> None:
    addon_dir = ROOT / "addons/gwm_ora"

    assert (addon_dir / "README.md").is_file()
    assert (addon_dir / "DOCS.md").is_file()
    assert (addon_dir / "CHANGELOG.md").is_file()
    assert (ROOT / "CHANGELOG.md").is_file()
    assert (addon_dir / "CHANGELOG.md").read_text(encoding="utf-8") == (
        ROOT / "CHANGELOG.md"
    ).read_text(encoding="utf-8")

    icon_width, icon_height = _png_size(addon_dir / "icon.png")
    assert icon_width == icon_height
    assert icon_width >= 128
    logo_width, logo_height = _png_size(addon_dir / "logo.png")
    assert logo_width >= 200
    assert logo_height >= 80


def test_addon_uses_custom_apparmor_profile() -> None:
    profile = (ROOT / "addons/gwm_ora/apparmor.txt").read_text(encoding="utf-8")

    assert "profile gwm_ora" in profile
    assert "network inet stream" in profile
    assert "/data/** rwk" in profile
    assert "docker_api" not in (ROOT / "addons/gwm_ora/config.yaml").read_text(encoding="utf-8")
    assert "full_access" not in (ROOT / "addons/gwm_ora/config.yaml").read_text(encoding="utf-8")


def test_hacs_default_repository_readiness_files_exist() -> None:
    hacs = json.loads((ROOT / "hacs.json").read_text(encoding="utf-8"))
    manifest = json.loads((ROOT / "custom_components/gwm_ora/manifest.json").read_text(encoding="utf-8"))

    assert hacs == {
        "name": "GWM ORA",
        "homeassistant": "2026.1.0",
    }
    assert manifest["documentation"] == "https://github.com/moryoav/ha-gwm_ora"
    assert manifest["issue_tracker"] == "https://github.com/moryoav/ha-gwm_ora/issues"
    assert manifest["codeowners"] == ["@moryoav"]
    assert manifest["version"] == "0.7.0"

    custom_components = [path.name for path in (ROOT / "custom_components").iterdir() if path.is_dir()]
    assert custom_components == ["gwm_ora"]
    assert (ROOT / "custom_components/gwm_ora/brand/icon.png").is_file()

    hacs_workflow = (ROOT / ".github/workflows/validate.yml").read_text(encoding="utf-8")
    hassfest_workflow = (ROOT / ".github/workflows/hassfest.yml").read_text(encoding="utf-8")

    assert "uses: hacs/action@main" in hacs_workflow
    assert "category: integration" in hacs_workflow
    assert "ignore:" not in hacs_workflow
    assert "uses: home-assistant/actions/hassfest@master" in hassfest_workflow
