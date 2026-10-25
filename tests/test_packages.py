import importlib.util
from pathlib import Path
import tempfile
import unittest
import zipfile

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('packager', ROOT / 'scripts' / 'package.py')
packager = importlib.util.module_from_spec(spec)
spec.loader.exec_module(packager)

class PackageTests(unittest.TestCase):
    def test_synthetic_examples_remain_but_runtime_is_excluded(self):
        folder = ROOT / 'artifacts' / 'package-tests'
        folder.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(dir=str(folder)) as temp:
            base = Path(temp)
            for name in ('README.md', 'examples/data/tasks.json', 'data/tasks.json', 'dist/program.exe', 'installation.json', '.git/config'):
                path = base / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text('{}', encoding='utf-8')
            target = base / 'result.zip'
            packager.archive(target, [(base, 'package')])
            with zipfile.ZipFile(target) as output:
                self.assertEqual(set(output.namelist()), {'package/README.md', 'package/examples/data/tasks.json'})

    def test_all_marketplace_sources_and_skill_links_exist(self):
        import json
        catalog = json.loads((ROOT / '.agents/plugins/marketplace.json').read_text(encoding='utf-8'))
        for plugin in catalog['plugins']:
            path = (ROOT / plugin['source']['path']).resolve()
            self.assertTrue(path.is_relative_to(ROOT))
            self.assertTrue((path / 'plugin.json').is_file())
            self.assertTrue(any((path / 'skills').rglob('SKILL.md')))
        self.assertTrue((ROOT / 'modules/daily-review/skills/daily-review/assets/ledger-template.md').is_file())

if __name__ == '__main__':
    unittest.main()
