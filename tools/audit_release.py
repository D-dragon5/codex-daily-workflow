#!/usr/bin/env python3
"""Conservative release checks; reports locations/rules, never matched values.

These checks cannot establish the absence of semantic personal information.
Private terms stay in an optional external JSON file. No network is used.
"""
from __future__ import annotations

import argparse
import ipaddress
import json
import os
from pathlib import Path, PurePosixPath
import re
import stat
import subprocess
import sys
import tempfile
import uuid
import zipfile

SKIP_DIRS = {".git", ".venv", "venv", "node_modules", "dist", "build", "bin", "obj",
             "__pycache__", ".pytest_cache", ".mypy_cache", ".audit-tmp", "artifacts", "sandbox"}
TEXT_EXTENSIONS = {".md", ".txt", ".json", ".jsonc", ".yaml", ".yml", ".toml", ".py",
                   ".ps1", ".psm1", ".psd1", ".cs", ".csproj", ".xaml", ".manifest", ".xml",
                   ".html", ".htm", ".js", ".mjs", ".cjs", ".ts", ".tsx", ".jsx", ".css",
                   ".svg", ".sh", ".cmd", ".bat", ".ini", ".cfg", ".config", ".example", ".env"}
TEXT_NAMES = {"LICENSE", "NOTICE", "COPYING", ".gitignore", ".gitattributes", ".editorconfig"}
RUNTIME_NAMES = {"automation.toml", "installation.json", ".codex-workflow-install.json",
                 "workflow.config.json", "workflow.config.local.json", "显示设置.json", "日程配置.json",
                 "test-preferences.json", "qa-preferences.json", "鼠标验证状态.json", "native-verification.json",
                 "verification.json", "smoke-result.json", "smoke-error.txt", "运行错误.txt", "验证失败.txt", "验证结果.json"}
RUNTIME_NAMES.update({'tasks.json', '每日任务与科研总账.md', '任务详情.html', '任务总览.xlsx'})
SYNTHETIC_FILES = {"examples/tasks.example.json", "examples/workflow.config.json",
                   "examples/data/tasks.json", "examples/data/每日任务与科研总账.md",
                   "modules/desktop-board/examples/workflow.config.json",
                   "modules/desktop-board/examples/data/tasks.json",
                   "modules/desktop-board/examples/data/每日任务与科研总账.md"}
WINDOWS_COMPATIBILITY_IDS = {"e2011457-1546-43c5-a5fe-008deee3d3f0", "35138b9a-5d96-4fbd-8e2d-a2440225f93a",
                             "4a2f28e3-53b9-4441-ba9c-d69d4a4a6e38", "1f676c76-80e1-4239-95bb-83d0f6d0da78",
                             "8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a", "00000000-0000-0000-0000-000000000000"}
RULES = (
    ("credential-token", re.compile(r"\b(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|sk-[A-Za-z0-9_-]{20,}|AKIA[A-Z0-9]{16})\b")),
    ("private-key", re.compile(r"-----BEGIN (?:[A-Z ]+ )?PRIVATE KEY-----")),
    ("absolute-user-path", re.compile(r"\b[A-Za-z]:[\\/]+Users[\\/]+(?!Public\b|Default\b|<|\$|%)[^\\/\s\"'<>]+", re.IGNORECASE)),
    ("machine-workspace-path", re.compile(r"\b[A-Za-z]:[\\/]+(?:Codex|pyprojects)[\\/]", re.IGNORECASE)),
    ("absolute-home-path", re.compile(r"/(?:home|Users)/(?!<|\$|\{)[A-Za-z0-9_.-]+/")),
)
UUID_PATTERN = re.compile(r"\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b", re.IGNORECASE)
EMAIL_PATTERN = re.compile(r"\b[A-Za-z0-9.!#$%&'*+/=?^_`{|}~-]+@([A-Za-z0-9.-]+\.[A-Za-z]{2,})\b")
IP_PATTERN = re.compile(r"(?<![\w.])(?:\d{1,3}\.){3}\d{1,3}(?![\w.])|(?<![\w:])(?:[0-9a-fA-F]{0,4}:){2,}[0-9a-fA-F:]{0,39}(?![\w:])")
MAX_BYTES = 5_000_000
MAX_ARCHIVE_BYTES = 50_000_000
MAX_ARCHIVE_MEMBERS = 2_000


def placeholder_email(domain: str) -> bool:
    domain = domain.lower()
    return domain in {"example.com", "example.org", "example.net"} or domain.endswith((".example", ".invalid", ".test", ".localhost", ".noreply.github.com"))


def codes_for_line(line: str, private_values: tuple[str, ...] = ()) -> set[str]:
    codes = {name for name, pattern in RULES if pattern.search(line)}
    if any(m.group(0).lower() not in WINDOWS_COMPATIBILITY_IDS for m in UUID_PATTERN.finditer(line)):
        codes.add("embedded-runtime-uuid")
    if any(not placeholder_email(m.group(1)) for m in EMAIL_PATTERN.finditer(line)):
        codes.add("non-example-email")
    for match in IP_PATTERN.finditer(line):
        # Four-part assembly versions are public build metadata, not addresses.
        if re.search(r"\b(?:version|assemblyversion)\s*=\s*[\"']$", line[max(0, match.start()-40):match.start()], re.IGNORECASE):
            continue
        try:
            addr = ipaddress.ip_address(match.group())
            documented = any(addr in ipaddress.ip_network(net) for net in
                             (("192.0.2.0/24", "198.51.100.0/24", "203.0.113.0/24") if addr.version == 4 else ("2001:db8::/32",)))
            if not (addr.is_loopback or addr.is_unspecified or documented):
                codes.add("network-address")
        except ValueError:
            pass
    if any(value and value in line for value in private_values):
        codes.add("local-private-value")
    return codes


def finding(label: str, line: int, rule: str, private_values: tuple[str, ...] = ()) -> dict:
    # Paths can themselves contain a private term; never echo those matches.
    safe = "[redacted-path]" if codes_for_line(label, private_values) else label
    return {"file": safe, "line": line, "rule": rule}


def normalized_source(label: str) -> str:
    parts = PurePosixPath(label.replace("\\", "/")).parts
    if parts and (parts[0] in {"codex-daily-workflow", "daily-review", "desktop-board"}
                  or '/'.join(parts[1:]) in SYNTHETIC_FILES):
        parts = parts[1:]
    return "/".join(parts)


def path_rules(label: str) -> set[str]:
    parts = PurePosixPath(label.replace("\\", "/")).parts
    name = parts[-1] if parts else ""
    rules = set()
    if any(part.startswith('.') and part not in {'.github', '.agents', '.githooks', '.gitignore', '.gitattributes', '.editorconfig'} for part in parts):
        rules.add("private-file-in-release")
    if name.startswith(".env") or name in {".credentials", "credentials.json", "auth.json", "id_rsa", "id_ed25519"} or name.endswith((".pem", ".key", ".pfx", ".p12")):
        rules.add("private-file-in-release")
    if name in RUNTIME_NAMES and normalized_source(label) not in SYNTHETIC_FILES:
        rules.add("runtime-state-in-release")
    if any(part in {'data', 'runtime', 'logs', 'state', 'sandbox', 'weekly-reports'} for part in parts) and normalized_source(label) not in SYNTHETIC_FILES:
        rules.add("runtime-state-in-release")
    return rules


def is_text_file(label: str) -> bool:
    path = PurePosixPath(label.replace("\\", "/"))
    return (path.suffix.lower() in TEXT_EXTENSIONS or path.name in TEXT_NAMES or path.name.startswith('.env')
            or normalized_source(label) == '.githooks/pre-push')


def audit_bytes(label: str, data: bytes, private_values: tuple[str, ...] = ()) -> tuple[int, list[dict]]:
    found = [finding(label, 0, rule, private_values) for rule in sorted(path_rules(label) | codes_for_line(label, private_values))]
    path = PurePosixPath(label.replace("\\", "/"))
    if not is_text_file(label):
        return 0, found + [finding(label, 0, "unsupported-file-type", private_values)]
    if len(data) > MAX_BYTES:
        return 0, found + [finding(label, 0, "text-too-large-to-audit", private_values)]
    try:
        text = data.decode("utf-8-sig")
    except UnicodeError:
        return 0, found + [finding(label, 0, "unreadable-text", private_values)]
    if any(ord(char) < 32 and char not in '\r\n\t' for char in text):
        return 0, found + [finding(label, 0, "binary-content", private_values)]
    for number, line in enumerate(text.splitlines(), 1):
        found.extend(finding(label, number, code, private_values) for code in sorted(codes_for_line(line, private_values)))
    return 1, found


def tree_files(root: Path):
    for folder, dirs, names in os.walk(root, followlinks=False):
        for name in sorted(dirs):
            if (Path(folder) / name).is_symlink():
                yield Path(folder) / name
        dirs[:] = sorted(d for d in dirs if d not in SKIP_DIRS and not (Path(folder) / d).is_symlink())
        yield from (Path(folder) / name for name in sorted(names))


def scan(root: Path, private_values: tuple[str, ...] = ()) -> tuple[int, list[dict]]:
    root = root.resolve()
    scanned, findings = 0, []
    for file in tree_files(root):
        label = file.relative_to(root).as_posix()
        if file.is_symlink() or not file.resolve().is_relative_to(root):
            findings.append(finding(label, 0, "symlink-in-release", private_values))
            continue
        try:
            if file.stat().st_size > MAX_BYTES:
                findings.append(finding(label, 0, "text-too-large-to-audit", private_values))
                continue
            count, found = audit_bytes(label, file.read_bytes(), private_values)
            scanned += count
            findings.extend(found)
        except OSError:
            findings.append(finding(label, 0, "unreadable-file", private_values))
    return scanned, findings


def git_output(root: Path, *args: str) -> bytes:
    result = subprocess.run(["git", "-C", str(root), *args], stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if result.returncode:
        raise ValueError("Git snapshot could not be audited")
    return result.stdout


def scan_git(root: Path, private_values: tuple[str, ...] = (), *, history: bool = False, index: bool = False) -> tuple[int, list[dict]]:
    """Audit tracked paths/blobs without worktree directory exclusions."""
    entries = []
    if index:
        for record in git_output(root, "ls-files", "--stage", "-z").split(b"\0"):
            if record:
                meta, path = record.split(b"\t", 1)
                mode, oid, stage = meta.split()
                if stage != b'0':
                    return 0, [finding('git-index', 0, 'unmerged-index', private_values)]
                entries.append(("index", mode, oid, path))
    if history:
        if git_output(root, "rev-parse", "--is-shallow-repository").strip() == b"true":
            return 0, [finding("git-history", 0, "shallow-history", private_values)]
        for commit in git_output(root, "rev-list", "--all").splitlines():
            for record in git_output(root, "ls-tree", "-r", "-z", commit.decode("ascii")).split(b"\0"):
                if record:
                    meta, path = record.split(b"\t", 1)
                    mode, kind, oid = meta.split()
                    entries.append(("history", mode, oid, path))
    scanned, findings, seen = 0, [], set()
    for source, mode, oid, raw_path in entries:
        key = (source, mode, oid, raw_path)
        if key in seen:
            continue
        seen.add(key)
        path = raw_path.decode("utf-8", errors="replace")
        label = source + ":" + path
        if mode not in {b"100644", b"100755"}:
            findings.append(finding(label, 0, "unsupported-git-mode", private_values))
            continue
        if int(git_output(root, "cat-file", "-s", oid.decode("ascii"))) > MAX_BYTES:
            findings.append(finding(label, 0, "text-too-large-to-audit", private_values))
            continue
        count, found = audit_bytes(path, git_output(root, "cat-file", "blob", oid.decode("ascii")), private_values)
        scanned += count
        findings.extend({**item, "file": finding(source + ":" + item["file"], 0, "", private_values)["file"]} for item in found)
    return scanned, findings


def unsafe_member(name: str) -> bool:
    value = name.replace("\\", "/")
    path = PurePosixPath(value)
    parts = value.rstrip('/').split('/')
    return (not value or "\0" in value or value.startswith("/") or bool(re.match(r"^[A-Za-z]:", value))
            or any(part in {'', '.', '..', '.git'} or part.endswith((' ', '.'))
                   or any(char in part for char in ':<>"|?*') for part in parts))


def scan_archives(location: Path, private_values: tuple[str, ...] = ()) -> tuple[int, list[dict]]:
    scanned, findings = 0, []
    if location.is_symlink():
        return 0, [finding("archives", 0, "symlink-in-release", private_values)]
    archives = [location] if location.is_file() else sorted(p for p in location.rglob('*') if p.suffix.lower() == '.zip') if location.is_dir() else []
    if not archives:
        return 0, [finding("archives", 0, "no-archives-found", private_values)]
    for archive in archives:
        label = archive.name
        findings.extend(finding(label, 0, rule, private_values) for rule in sorted(codes_for_line(label, private_values)))
        if archive.is_symlink():
            findings.append(finding(label, 0, "symlink-in-release", private_values))
            continue
        try:
            with zipfile.ZipFile(archive) as zipped:
                members = zipped.infolist()
                if len(members) > MAX_ARCHIVE_MEMBERS or sum(m.file_size for m in members) > MAX_ARCHIVE_BYTES:
                    findings.append(finding(label, 0, "archive-limit-exceeded", private_values))
                    continue
                seen = set()
                for member in members:
                    path = member.filename.replace("\\", "/")
                    member_label = label + ":" + path
                    if unsafe_member(path):
                        findings.append(finding(member_label, 0, "unsafe-archive-path", private_values))
                        continue
                    key = path.casefold().rstrip("/")
                    if key in seen:
                        findings.append(finding(member_label, 0, "duplicate-archive-member", private_values))
                        continue
                    seen.add(key)
                    mode = member.external_attr >> 16
                    if stat.S_ISLNK(mode) or (stat.S_IFMT(mode) not in {0, stat.S_IFREG, stat.S_IFDIR}):
                        findings.append(finding(member_label, 0, "archive-symlink-or-special", private_values))
                        continue
                    if member.is_dir():
                        continue
                    if member.file_size > MAX_BYTES or member.flag_bits & 1:
                        findings.append(finding(member_label, 0, "unsupported-archive-member", private_values))
                        continue
                    count, found = audit_bytes(path, zipped.read(member), private_values)
                    scanned += count
                    findings.extend({**item, "file": finding(label + ":" + item["file"], 0, "", private_values)["file"]} for item in found)
        except (OSError, ValueError, RuntimeError, zipfile.BadZipFile, NotImplementedError):
            findings.append(finding(label, 0, "unreadable-archive", private_values))
    return scanned, findings


def load_private_values(path: Path | None, root: Path) -> tuple[str, ...]:
    if path is None:
        return ()
    if path.resolve().is_relative_to(root.resolve()):
        raise ValueError("private-values file must stay outside the release tree")
    try:
        loaded = json.loads(path.read_text(encoding="utf-8-sig"))
    except (OSError, ValueError) as error:
        raise ValueError("private-values file could not be loaded") from error
    if not isinstance(loaded, list) or any(not isinstance(v, str) for v in loaded):
        raise ValueError("private-values file must contain a JSON array of strings")
    return tuple(v for v in loaded if v)


def self_test(temp_dir: Path | None = None) -> None:
    if temp_dir:
        temp_dir.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="workflow-release-audit-", dir=temp_dir) as directory:
        root = Path(directory)
        (root / "README.md").write_text("Contact team@example.com.\n", encoding="utf-8")
        assert not scan(root)[1]
        token = "ghp_" + "A" * 36
        (root / "unsafe.txt").write_text(token + "\n" + str(uuid.uuid4()), encoding="utf-8")
        assert {f["rule"] for f in scan(root)[1]} == {"credential-token", "embedded-runtime-uuid"}
    print(json.dumps({"selfTest": "passed"}))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", nargs="?", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--private-values-file", type=Path, help="External local-only JSON array; never bundled or printed")
    parser.add_argument("--git-history", action="store_true", help="Audit every reachable committed path/blob; requires full history")
    parser.add_argument("--git-index", action="store_true", help="Audit the staged snapshot, including ignored tracked paths")
    parser.add_argument("--archives", type=Path, help="Audit a ZIP or every ZIP below a directory without extracting")
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--test-dir", type=Path)
    args = parser.parse_args()
    if args.self_test:
        self_test(args.test_dir)
        return 0
    root = args.root.resolve()
    if not root.is_dir():
        parser.error("release root is not a directory")
    try:
        values = load_private_values(args.private_values_file, root)
        scanned, findings = scan(root, values)
        if args.git_history or args.git_index:
            count, found = scan_git(root, values, history=args.git_history, index=args.git_index)
            scanned += count
            findings.extend(found)
        if args.archives:
            count, found = scan_archives(args.archives, values)
            scanned += count
            findings.extend(found)
    except (ValueError, OSError):
        # Do not print exception text: it may contain local paths or private terms.
        print(json.dumps({"passed": False, "findings": [{"file": "audit", "line": 0, "rule": "audit-input-error"}]}))
        return 1
    print(json.dumps({"scannedTextFiles": scanned, "passed": not findings, "findings": findings}, ensure_ascii=False, indent=2))
    return 1 if findings else 0


if __name__ == "__main__":
    sys.exit(main())
