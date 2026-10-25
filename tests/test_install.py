"""Exercise install choices and existing-data protection in isolated folders."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
POWERSHELL = shutil.which('powershell.exe')

@unittest.skipUnless(os.name == 'nt' and POWERSHELL, 'Windows PowerShell required')
class InstallerTests(unittest.TestCase):
    def setUp(self):
        base = ROOT / 'artifacts' / 'install-tests'
        base.mkdir(parents=True, exist_ok=True)
        self.temp = tempfile.TemporaryDirectory(dir=str(base))
        self.addCleanup(self.temp.cleanup)
        self.folder = Path(self.temp.name)
        self.app, self.data, self.skills = [self.folder / p for p in ('app', 'records', 'skills')]

    def install(self, component='all', expected=0):
        result = subprocess.run([
            POWERSHELL, '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', str(ROOT / 'install.ps1'),
            '-Component', component, '-InstallRoot', str(self.app), '-DataDir', str(self.data),
            '-SkillRoot', str(self.skills), '-NoShortcut'
        ], capture_output=True, text=True, encoding='utf-8', errors='replace')
        if expected == 0:
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        else:
            self.assertNotEqual(result.returncode, 0)
        return result

    def test_complete_install_and_repeat_keep_data(self):
        self.install()
        self.assertTrue((self.app / 'desktop-board' / 'DesktopBoard.exe').is_file())
        self.assertTrue((self.data / 'tasks.json').is_file())
        self.assertTrue((self.data / '任务总览.xlsx').is_file())
        marker = json.loads((self.app / '.codex-workflow-install.json').read_text(encoding='utf-8-sig'))
        self.assertEqual(sorted(marker['components']), ['review', 'widget'])
        self.assertFalse(marker['scheduleRegistered'])
        ledger = self.data / '每日任务与科研总账.md'
        ledger.write_text(ledger.read_text(encoding='utf-8') + '\n本人新增记录，应在重复安装后保留。\n', encoding='utf-8')
        before = hashlib.sha256(ledger.read_bytes()).hexdigest()
        self.install()
        self.assertEqual(hashlib.sha256(ledger.read_bytes()).hexdigest(), before)
        self.assertIn('本人新增记录', ledger.read_text(encoding='utf-8'))

    def test_foreign_skill_is_not_overwritten(self):
        skill = self.skills / 'daily-review'
        skill.mkdir(parents=True)
        sentinel = skill / 'SKILL.md'
        sentinel.write_text('owned by somebody else', encoding='utf-8')
        self.install('review', expected=1)
        self.assertEqual(sentinel.read_text(encoding='utf-8'), 'owned by somebody else')

    def test_widget_then_review_does_not_replace_manual_tasks(self):
        self.install('widget')
        task_file = self.data / 'tasks.json'
        manual = {'schemaVersion': 1, 'tasks': [{'id': 'mine', 'title': 'my manual task'}]}
        task_file.write_text(json.dumps(manual), encoding='utf-8')
        before = task_file.read_bytes()
        self.install('review', expected=1)
        self.assertEqual(task_file.read_bytes(), before)
        self.assertFalse((self.data / '每日任务与科研总账.md').exists())

    def test_foreign_install_marker_is_rejected(self):
        self.app.mkdir()
        marker = self.app / '.codex-workflow-install.json'
        marker.write_text('{"package":"somebody-else"}', encoding='utf-8')
        self.install('review', expected=1)
        self.assertEqual(json.loads(marker.read_text())['package'], 'somebody-else')

if __name__ == '__main__':
    unittest.main()
