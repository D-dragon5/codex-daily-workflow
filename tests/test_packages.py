import importlib.util
from pathlib import Path
import tempfile
import unittest
import zipfile
from unittest import mock

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('packager', ROOT / 'scripts' / 'package.py')
packager = importlib.util.module_from_spec(spec)
spec.loader.exec_module(packager)

class PackageTests(unittest.TestCase):
    def test_private_hidden_and_unknown_outputs_are_never_selected(self):
        with tempfile.TemporaryDirectory() as temp:
            base = Path(temp)
            names = ('README.md', '.env', '.env.local', '.credentials', 'screen.png',
                     'notes.log', 'private.sqlite', 'payload.7z', 'sandbox/app/config.json',
                     'src/smoke-result.json', 'src/workflow.config.json',
                     'src/tasks.json', 'src/weekly-reports/progress.md',
                     'src/safe.cs', '.github/workflows/checks.yml', 'AGENTS.md',
                     'LICENSE-UPSTREAM.txt', 'build.ps1', 'examples/workflow.config.json')
            for name in names:
                path = base / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text('{}', encoding='utf-8')
            selected = {str(rel).replace('\\', '/') for _, rel in packager.public_files(base)}
            self.assertEqual(selected, {'README.md', 'src/safe.cs', '.github/workflows/checks.yml',
                                        'AGENTS.md', 'LICENSE-UPSTREAM.txt', 'build.ps1',
                                        'examples/workflow.config.json'})

    def test_selected_secret_fails_before_final_archive_is_created(self):
        with tempfile.TemporaryDirectory() as temp:
            base = Path(temp) / 'source'
            base.mkdir()
            (base / 'README.md').write_text('ghp_' + 'A' * 36, encoding='utf-8')
            target = Path(temp) / 'release.zip'
            with self.assertRaises(ValueError):
                packager.archive(target, [(base, 'package')])
            self.assertFalse(target.exists())

    def test_private_values_reject_selected_text_without_echoing(self):
        with tempfile.TemporaryDirectory() as temp:
            base = Path(temp) / 'source'
            base.mkdir()
            value = 'fictional-' + 'private-term'
            (base / 'README.md').write_text(value, encoding='utf-8')
            target = Path(temp) / 'release.zip'
            with self.assertRaises(ValueError) as caught:
                packager.archive(target, [(base, 'package')], (value,))
            self.assertNotIn(value, str(caught.exception))
            self.assertFalse(target.exists())

    def test_symlink_detection_fails_closed_without_opening_target(self):
        with tempfile.TemporaryDirectory() as temp:
            base = Path(temp)
            (base / 'README.md').write_text('safe', encoding='utf-8')
            with mock.patch.object(Path, 'is_symlink', return_value=True):
                with self.assertRaises(ValueError):
                    list(packager.public_files(base))

    def test_failed_audit_preserves_previous_reviewed_archive(self):
        with tempfile.TemporaryDirectory() as temp:
            base = Path(temp) / 'source'
            base.mkdir()
            (base / 'README.md').write_text('safe', encoding='utf-8')
            target = Path(temp) / 'release.zip'
            packager.archive(target, [(base, 'package')])
            previous = target.read_bytes()
            (base / 'README.md').write_text('ghp_' + 'B' * 36, encoding='utf-8')
            with self.assertRaises(ValueError):
                packager.archive(target, [(base, 'package')])
            self.assertEqual(target.read_bytes(), previous)

    def test_only_exact_public_hook_is_selected(self):
        with tempfile.TemporaryDirectory() as temp:
            base = Path(temp)
            (base / '.githooks').mkdir()
            (base / '.githooks/pre-push').write_text('#!/bin/sh\nexit 0\n', encoding='utf-8')
            (base / '.githooks/private.md').write_text('local notes', encoding='utf-8')
            selected = [rel.as_posix() for _, rel in packager.public_files(base)]
            self.assertEqual(selected, ['.githooks/pre-push'])

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
