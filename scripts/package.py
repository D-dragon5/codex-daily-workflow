"""Build audited source archives from an explicit public path/type allowlist."""
from pathlib import Path, PurePosixPath
import argparse
import hashlib
import importlib.util
import json
import os
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parents[1]
_spec = importlib.util.spec_from_file_location('release_audit', ROOT / 'tools' / 'audit_release.py')
audit = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(audit)

# Local/generated folders never belong to source archives, regardless of Git ignore.
EXCLUDED = {'.git', '__pycache__', 'dist', 'build', 'bin', 'obj', 'artifacts', 'data',
            'node_modules', 'runtime', '.venv', 'venv', 'sandbox', '.audit-tmp', '.pytest_cache', '.mypy_cache'}
PUBLIC_DIRS = {'modules', 'skills', 'scripts', 'tools', 'tests', 'docs', 'examples',
               'src', 'assets', 'references', 'agents', '.github', '.agents', '.githooks'}
PUBLIC_ROOT_FILES = {'README.md', 'LICENSE', 'NOTICE', 'COPYING', 'AGENTS.md', 'CHANGELOG.md',
                     'CONTRIBUTING.md', 'SECURITY.md', 'plugin.json', 'install.cmd', 'install.ps1',
                     'THIRD_PARTY.md', 'LICENSE-UPSTREAM.txt', 'build.ps1',
                     'workflow.config.example.json', 'pyproject.toml', 'requirements.txt',
                     'package.json', 'package-lock.json', 'tsconfig.json', '.gitignore', '.gitattributes', '.editorconfig'}
RUNTIME = audit.RUNTIME_NAMES


def public_files(base: Path):
    """Select known source paths/types; links are rejected, never followed."""
    base = base.resolve()
    if not base.is_dir():
        raise ValueError('Public source directory is missing')
    for folder, dirs, names in os.walk(base, followlinks=False):
        for name in dirs + names:
            if (Path(folder) / name).is_symlink():
                raise ValueError('Symbolic links cannot be included in a public package')
        dirs[:] = sorted(name for name in dirs if name not in EXCLUDED or
                         (name == 'data' and (Path(folder) / name).relative_to(base).as_posix() in
                          {'examples/data', 'modules/desktop-board/examples/data'}))
        dirs[:] = [name for name in dirs if not name.startswith('.') or name in {'.github', '.agents', '.githooks'}]
        for name in sorted(names):
            path = Path(folder) / name
            relative = path.relative_to(base)
            label = relative.as_posix()
            if not path.resolve().is_relative_to(base):
                raise ValueError('Public source escapes its release directory')
            synthetic = label in audit.SYNTHETIC_FILES
            if len(relative.parts) == 1:
                if name not in PUBLIC_ROOT_FILES:
                    continue
            elif relative.parts[0] not in PUBLIC_DIRS:
                continue
            if relative.parts[0] == '.githooks' and label != '.githooks/pre-push':
                continue
            if any(part.startswith('.') and part not in {'.github', '.agents', '.githooks', '.gitignore', '.gitattributes', '.editorconfig'} for part in relative.parts):
                continue
            if any(part in EXCLUDED for part in relative.parts) and not synthetic:
                continue
            if name in RUNTIME and not synthetic:
                continue
            if audit.path_rules(label):
                continue
            if not audit.is_text_file(label):
                continue
            yield path, relative


def archive(destination: Path, selections, private_values: tuple[str, ...] = ()):
    """Audit selected bytes and a temporary ZIP before replacing the result."""
    contents = []
    for base, prefix in selections:
        if audit.unsafe_member(str(prefix)):
            raise ValueError('Unsafe archive prefix')
        for path, relative in public_files(base):
            name = (PurePosixPath(prefix) / PurePosixPath(relative.as_posix())).as_posix()
            if path.stat().st_size > audit.MAX_BYTES:
                raise ValueError('Public source exceeds the audit size limit')
            data = path.read_bytes()
            _, findings = audit.audit_bytes(name, data, private_values)
            if findings:
                raise ValueError('Release privacy audit failed: ' + json.dumps(findings, ensure_ascii=False))
            contents.append((name, data))
    if not contents:
        raise ValueError('Public package would be empty')
    destination.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix='workflow-package-', dir=destination.parent) as staging:
        staged = Path(staging) / destination.name
        with zipfile.ZipFile(staged, 'w', zipfile.ZIP_DEFLATED) as output:
            for name, data in contents:
                output.writestr(name, data)
        _, findings = audit.scan_archives(staged, private_values)
        if findings:
            raise ValueError('Packaged privacy audit failed: ' + json.dumps(findings, ensure_ascii=False))
        staged.replace(destination)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--out', type=Path, default=ROOT / 'artifacts')
    parser.add_argument('--private-values-file', type=Path, help='External local-only JSON array; never bundled')
    args = parser.parse_args()
    try:
        values = audit.load_private_values(args.private_values_file, ROOT)
        args.out.mkdir(parents=True, exist_ok=True)
        plans = {
            'codex-daily-workflow-all.zip': [(ROOT, 'codex-daily-workflow')],
            'codex-daily-review.zip': [(ROOT / 'modules' / 'daily-review', 'daily-review')],
            'codex-desktop-board.zip': [(ROOT / 'modules' / 'desktop-board', 'desktop-board')],
        }
        # Stage every package before publishing any new result.
        with tempfile.TemporaryDirectory(prefix='workflow-release-', dir=args.out) as staging:
            stage = Path(staging)
            hashes = []
            for name, selections in plans.items():
                target = stage / name
                archive(target, selections, values)
                hashes.append(hashlib.sha256(target.read_bytes()).hexdigest() + '  ' + name)
            (stage / 'SHA256SUMS.txt').write_text('\n'.join(hashes) + '\n', encoding='utf-8')
            for name in [*plans, 'SHA256SUMS.txt']:
                (stage / name).replace(args.out / name)
        for name in plans:
            print(name)
    except (ValueError, OSError, zipfile.BadZipFile):
        parser.exit(1, 'Release audit/package failed; no new unaudited package was published.\n')


if __name__ == '__main__':
    main()
