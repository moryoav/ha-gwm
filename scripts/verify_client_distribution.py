"""Verify that the planned client archives are narrow and reproducible."""

from __future__ import annotations

import argparse
import tarfile
import zipfile
from email.message import Message
from email.parser import Parser
from pathlib import Path, PurePosixPath

_DISTRIBUTION_NAME = "gwm-ora-client"
_DISTRIBUTION_VERSION = "0.1.0"
_IMPORT_PACKAGE = "gwm_ora_client"
_DIST_INFO = f"{_IMPORT_PACKAGE}-{_DISTRIBUTION_VERSION}.dist-info"
_EGG_INFO = f"{_IMPORT_PACKAGE}.egg-info"
_LICENSE_EXPRESSION = "MIT AND LicenseRef-GWM-Protocol-Materials"
_REQUIREMENTS = {
    "aiohttp<4,>=3.13.3",
    "cryptography>=46.0.2",
    "yarl<2,>=1.22.0",
}
_PROHIBITED_DIRECTORIES = {"addons", "custom_components", "docs", "tests"}
_PROHIBITED_SUFFIXES = {".apk", ".cer", ".crt", ".der", ".key", ".p12", ".pem", ".pfx"}
_LICENSE_FILES = {"LICENSE", "THIRD_PARTY_NOTICES.md"}
_SOURCE_ROOT_FILES = {
    "LICENSE",
    "MANIFEST.in",
    "PKG-INFO",
    "THIRD_PARTY_NOTICES.md",
    "pyproject.toml",
    "setup.cfg",
}
_EGG_INFO_FILES = {
    "PKG-INFO",
    "SOURCES.txt",
    "dependency_links.txt",
    "requires.txt",
    "top_level.txt",
}
_DIST_INFO_FILES = {"METADATA", "RECORD", "WHEEL", "top_level.txt"}


def _arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("distribution_directory", type=Path)
    return parser.parse_args()


def _require_single(directory: Path, pattern: str) -> Path:
    matches = sorted(directory.glob(pattern))
    if len(matches) != 1:
        raise ValueError(f"expected_one_{pattern.replace('*', 'archive')}")
    return matches[0]


def _assert_safe_members(members: list[str], *, source_root: str | None = None) -> None:
    if not members:
        raise ValueError("archive_empty")
    for name in members:
        path = PurePosixPath(name)
        relevant_parts = path.parts[1:] if source_root is not None else path.parts
        if source_root is not None and (not path.parts or path.parts[0] != source_root):
            raise ValueError("source_root_invalid")
        if set(relevant_parts) & _PROHIBITED_DIRECTORIES:
            raise ValueError(f"archive_scope_invalid:{name}")
        if path.suffix.casefold() in _PROHIBITED_SUFFIXES:
            raise ValueError(f"protocol_material_in_client_archive:{name}")
        if any(part.startswith(".codex-temp-") for part in relevant_parts):
            raise ValueError(f"local_artifact_in_client_archive:{name}")


def _is_package_member(parts: tuple[str, ...], *, include_readme: bool) -> bool:
    if len(parts) == 1:
        return parts[0] == _IMPORT_PACKAGE
    if len(parts) != 2 or parts[0] != _IMPORT_PACKAGE:
        return False
    filename = parts[1]
    return filename.endswith(".py") or filename == "py.typed" or (include_readme and filename == "README.md")


def _assert_wheel_layout(members: list[str]) -> None:
    for name in members:
        parts = PurePosixPath(name).parts
        if _is_package_member(parts, include_readme=False):
            continue
        if len(parts) == 2 and parts[0] == _DIST_INFO and parts[1] in _DIST_INFO_FILES:
            continue
        if len(parts) == 3 and parts[:2] == (_DIST_INFO, "licenses") and parts[2] in _LICENSE_FILES:
            continue
        raise ValueError(f"wheel_layout_invalid:{name}")


def _assert_source_layout(members: list[str]) -> None:
    for name in members:
        parts = PurePosixPath(name).parts
        relevant = parts[1:]
        if not relevant:
            continue
        if len(relevant) == 1 and relevant[0] in _SOURCE_ROOT_FILES:
            continue
        if _is_package_member(relevant, include_readme=True):
            continue
        if len(relevant) == 1 and relevant[0] == _EGG_INFO:
            continue
        if len(relevant) == 2 and relevant[0] == _EGG_INFO and relevant[1] in _EGG_INFO_FILES:
            continue
        raise ValueError(f"source_layout_invalid:{name}")


def _assert_metadata(metadata: Message) -> None:
    if metadata["Name"] != _DISTRIBUTION_NAME:
        raise ValueError("distribution_name_invalid")
    if metadata["Version"] != _DISTRIBUTION_VERSION:
        raise ValueError("distribution_version_invalid")
    if metadata["Requires-Python"] != ">=3.13":
        raise ValueError("python_requirement_invalid")
    if metadata["License-Expression"] != _LICENSE_EXPRESSION:
        raise ValueError("license_expression_invalid")
    if set(metadata.get_all("Requires-Dist", [])) != _REQUIREMENTS:
        raise ValueError("runtime_requirements_invalid")
    license_files = set(metadata.get_all("License-File", []))
    if license_files != {"LICENSE", "THIRD_PARTY_NOTICES.md"}:
        raise ValueError("license_files_invalid")


def _verify_wheel(path: Path) -> None:
    with zipfile.ZipFile(path) as archive:
        members = archive.namelist()
        _assert_safe_members(members)
        _assert_wheel_layout(members)
        required = {
            f"{_IMPORT_PACKAGE}/__init__.py",
            f"{_IMPORT_PACKAGE}/py.typed",
        }
        if not required.issubset(members):
            raise ValueError("wheel_package_incomplete")
        metadata_names = [name for name in members if name.endswith(".dist-info/METADATA")]
        if len(metadata_names) != 1:
            raise ValueError("wheel_metadata_invalid")
        metadata = Parser().parsestr(archive.read(metadata_names[0]).decode("utf-8"))
        _assert_metadata(metadata)


def _verify_source_distribution(path: Path) -> None:
    source_root = f"gwm_ora_client-{_DISTRIBUTION_VERSION}"
    with tarfile.open(path, "r:gz") as archive:
        members = archive.getnames()
        _assert_safe_members(members, source_root=source_root)
        _assert_source_layout(members)
        required = {
            f"{source_root}/LICENSE",
            f"{source_root}/THIRD_PARTY_NOTICES.md",
            f"{source_root}/pyproject.toml",
            f"{source_root}/{_IMPORT_PACKAGE}/__init__.py",
            f"{source_root}/{_IMPORT_PACKAGE}/py.typed",
        }
        if not required.issubset(members):
            raise ValueError("source_distribution_incomplete")
        package_info = archive.extractfile(f"{source_root}/PKG-INFO")
        if package_info is None:
            raise ValueError("source_metadata_invalid")
        metadata = Parser().parsestr(package_info.read().decode("utf-8"))
        _assert_metadata(metadata)


def main() -> int:
    directory = _arguments().distribution_directory.resolve()
    if not directory.is_dir():
        raise ValueError("distribution_directory_invalid")
    wheel = _require_single(directory, "*.whl")
    source = _require_single(directory, "*.tar.gz")
    _verify_wheel(wheel)
    _verify_source_distribution(source)
    print(f"verified {_DISTRIBUTION_NAME} {_DISTRIBUTION_VERSION} wheel and source archive")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
