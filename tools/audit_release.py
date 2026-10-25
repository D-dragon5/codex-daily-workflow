#!/usr/bin/env python3
"""Scan a release tree without printing matched values or sending data anywhere.

This is a focused pre-publication check, not a complete secret-detection product.
An optional private-values file stays outside the release tree and may contain a
JSON array of local-only terms. It is never copied into the package or printed.
"""
from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import re
import sys
import tempfile
import uuid


SKIP_DIRS = {
    ".git", ".venv", "venv", "node_modules", "dist", "build", "bin", "obj",
    "__pycache__", ".pytest_cache", ".mypy_cache", ".audit-tmp", "artifacts",
}
TEXT_EXTENSIONS = {
    ".md", ".txt", ".json", ".jsonc", ".yaml", ".yml", ".toml", ".py",
    ".ps1", ".psm1", ".psd1", ".cs", ".csproj", ".xaml", ".manifest",
    ".xml", ".html", ".htm", ".js", ".mjs", ".cjs", ".ts", ".sh",
    ".cmd", ".bat", ".ini", ".cfg", ".config", ".example", ".env",
}
TEXT_NAMES = {"LICENSE", "NOTICE", "COPYING", ".gitignore", ".gitattributes"}
RUNTIME_NAMES = {
    "automation.toml", "显示设置.json", "test-preferences.json",
    "qa-preferences.json", "鼠标验证状态.json", "native-verification.json",
    "verification.json", "运行错误.txt", "验证失败.txt",
}
WINDOWS_COMPATIBILITY_IDS = {
    "e2011457-1546-43c5-a5fe-008deee3d3f0",
    "35138b9a-5d96-4fbd-8e2d-a2440225f93a",
    "4a2f28e3-53b9-4441-ba9c-d69d4a4a6e38",
    "1f676c76-80e1-4239-95bb-83d0f6d0da78",
    "8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a",
    "00000000-0000-0000-0000-000000000000",
}
RULES = (
    ("credential-token", re.compile(
        r"\b(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|"
        r"sk-[A-Za-z0-9_-]{20,}|AKIA[A-Z0-9]{16})\b")),
    ("private-key", re.compile(r"-----BEGIN (?:[A-Z ]+ )?PRIVATE KEY-----")),
    ("absolute-user-path", re.compile(
        r"\b[A-Za-z]:[\\/]+Users[\\/]+(?!Public\b|Default\b|<|\$|%)[^\\/\s\"'<>]+",
        re.IGNORECASE)),
    ("machine-workspace-path", re.compile(
        r"\b[A-Za-z]:[\\/]+(?:Codex|pyprojects)[\\/]", re.IGNORECASE)),
    ("absolute-home-path", re.compile(r"/(?:home|Users)/(?!<|\$|\{)[A-Za-z0-9_.-]+/")),
)
UUID_PATTERN = re.compile(
    r"\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b",
    re.IGNORECASE,
)
EMAIL_PATTERN = re.compile(r"\b[A-Za-z0-9.!#$%&'*+/=?^_`{|}~-]+@([A-Za-z0-9.-]+\.[A-Za-z]{2,})\b")


def placeholder_email(domain: str) -> bool:
    domain = domain.lower()
    return (
        domain in {"example.com", "example.org", "example.net"}
        or domain.endswith((".example", ".invalid", ".test", ".localhost"))
        or domain.endswith(".noreply.github.com")
    )


def tree_files(root: Path):
    for folder, dirs, names in os.walk(root, followlinks=False):
        dirs[:] = sorted(d for d in dirs if d not in SKIP_DIRS and not (Path(folder) / d).is_symlink())
        for name in sorted(names):
            file = Path(folder) / name
            if not file.is_symlink():
                yield file


def scan(root: Path, private_values: tuple[str, ...] = ()) -> tuple[int, list[dict]]:
    findings: list[dict] = []
    scanned = 0
    for file in tree_files(root):
        relative = file.relative_to(root).as_posix()
        is_example = any(p in {"templates", "examples", "fixtures"} for p in file.relative_to(root).parts)
        if file.name in RUNTIME_NAMES and not is_example:
            findings.append({"file": relative, "line": 0, "rule": "runtime-state-in-release"})
        if file.suffix.lower() not in TEXT_EXTENSIONS and file.name not in TEXT_NAMES and not file.name.startswith(".env"):
            continue
        if file.stat().st_size > 5_000_000:
            findings.append({"file": relative, "line": 0, "rule": "text-too-large-to-audit"})
            continue
        try:
            text = file.read_text(encoding="utf-8-sig")
        except (UnicodeError, OSError):
            findings.append({"file": relative, "line": 0, "rule": "unreadable-text"})
            continue
        scanned += 1
        for number, line in enumerate(text.splitlines(), 1):
            codes = {name for name, pattern in RULES if pattern.search(line)}
            if any(m.group(0).lower() not in WINDOWS_COMPATIBILITY_IDS for m in UUID_PATTERN.finditer(line)):
                codes.add("embedded-runtime-uuid")
            if any(not placeholder_email(m.group(1)) for m in EMAIL_PATTERN.finditer(line)):
                codes.add("non-example-email")
            if any(value and value in line for value in private_values):
                codes.add("local-private-value")
            for code in sorted(codes):
                findings.append({"file": relative, "line": number, "rule": code})
    return scanned, findings


def self_test(temp_dir: Path | None = None) -> None:
    if temp_dir:
        temp_dir.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="workflow-release-audit-", dir=temp_dir) as directory:
        root = Path(directory)
        (root / "README.md").write_text("Contact team@example.com.\n", encoding="utf-8")
        skipped = root / ".git"
        skipped.mkdir()
        fake_token = "ghp_" + "A" * 36
        (skipped / "ignored.txt").write_text(fake_token, encoding="utf-8")
        assert not scan(root)[1], "Safe sample or ignored directory was rejected"
        user_path = "Q:" + chr(92) + "Users" + chr(92) + "FixturePerson" + chr(92) + "data"
        (root / "unsafe.txt").write_text(
            fake_token + "\n" + user_path + "\n" + str(uuid.uuid4()) + "\nlocal-only-value\n",
            encoding="utf-8",
        )
        _, found = scan(root, ("local-only-value",))
        assert {f["rule"] for f in found} == {
            "credential-token", "absolute-user-path", "embedded-runtime-uuid", "local-private-value",
        }
        assert all(set(f) == {"file", "line", "rule"} for f in found), "Matched values leaked into output"
        assert found[0]["line"] == 1
        (root / ".env.local").write_text(fake_token, encoding="utf-8")
        assert any(f["file"] == ".env.local" for f in scan(root)[1]), "Environment files were skipped"
    print(json.dumps({"selfTest": "passed"}))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", nargs="?", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--private-values-file", type=Path, help="Local-only JSON array of terms; never bundled or printed")
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--test-dir", type=Path, help="Optional writable base for isolated self-test files")
    args = parser.parse_args()
    if args.self_test:
        self_test(args.test_dir)
        return 0
    values: tuple[str, ...] = ()
    if args.private_values_file:
        loaded = json.loads(args.private_values_file.read_text(encoding="utf-8-sig"))
        if not isinstance(loaded, list) or any(not isinstance(v, str) for v in loaded):
            parser.error("private-values file must contain a JSON array of strings")
        values = tuple(v for v in loaded if v)
    root = args.root.resolve()
    if not root.is_dir():
        parser.error("release root is not a directory")
    if args.private_values_file and args.private_values_file.resolve().is_relative_to(root):
        parser.error("private-values file must stay outside the release tree")
    scanned, findings = scan(root, values)
    print(json.dumps({"scannedTextFiles": scanned, "passed": not findings, "findings": findings}, ensure_ascii=False, indent=2))
    return 1 if findings else 0


if __name__ == "__main__":
    sys.exit(main())
