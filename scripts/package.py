"""Build shareable archives from explicit public source allowlists."""
from pathlib import Path
import argparse
import hashlib
import zipfile

ROOT = Path(__file__).resolve().parents[1]
EXCLUDED = {'.git', '__pycache__', 'dist', 'build', 'artifacts', 'data', 'node_modules', 'runtime', '.venv'}
RUNTIME = {'installation.json', 'workflow.config.json', '显示设置.json', '日程配置.json', '验证结果.json', '验证失败.txt', '运行错误.txt'}

def public_files(base: Path):
    for path in sorted(base.rglob('*')):
        relative = path.relative_to(base)
        if not path.is_file() or any(x in EXCLUDED and not (x == 'data' and 'examples' in relative.parts) for x in relative.parts):
            continue
        # The examples/data directory deliberately contains synthetic sources.
        if path.name in RUNTIME and 'examples' not in relative.parts:
            continue
        if path.suffix.lower() in {'.exe', '.pdb', '.pyc', '.zip'}:
            continue
        yield path, relative

def archive(destination: Path, selections):
    with zipfile.ZipFile(destination, 'w', zipfile.ZIP_DEFLATED) as output:
        for base, prefix in selections:
            for path, relative in public_files(base):
                output.write(path, str(Path(prefix) / relative))

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--out', type=Path, default=ROOT / 'artifacts')
    args = parser.parse_args()
    args.out.mkdir(parents=True, exist_ok=True)
    plans = {
        'codex-daily-workflow-all.zip': [(ROOT, 'codex-daily-workflow')],
        'codex-daily-review.zip': [(ROOT / 'modules' / 'daily-review', 'daily-review')],
        'codex-desktop-board.zip': [(ROOT / 'modules' / 'desktop-board', 'desktop-board')],
    }
    hashes = []
    for name, selections in plans.items():
        target = args.out / name
        archive(target, selections)
        hashes.append(hashlib.sha256(target.read_bytes()).hexdigest() + '  ' + name)
        print(name)
    (args.out / 'SHA256SUMS.txt').write_text('\n'.join(hashes) + '\n', encoding='utf-8')

if __name__ == '__main__':
    main()
