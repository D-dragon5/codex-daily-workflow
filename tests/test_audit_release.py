"""Exercise release boundaries using disposable, deliberately synthetic inputs."""
import importlib.util
import json
from pathlib import Path
import shutil
import stat
import subprocess
import sys
import tempfile
import unittest
import warnings
import zipfile


ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('release_audit', ROOT / 'tools/audit_release.py')
audit = importlib.util.module_from_spec(spec)
spec.loader.exec_module(audit)


class AuditReleaseTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='workflow-audit-tests-')
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name) / 'source'
        self.root.mkdir()

    def write(self, name, content='synthetic fixture\n'):
        path = self.root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding='utf-8')
        return path

    def token(self):
        return 'ghp_' + 'A' * 36

    def archive(self, entries):
        path = Path(self.temp.name) / 'candidate.zip'
        with warnings.catch_warnings():
            warnings.simplefilter('ignore', UserWarning)
            with zipfile.ZipFile(path, 'w') as output:
                for name, content in entries:
                    output.writestr(name, content)
        return path

    def test_public_authorship_and_placeholder_contact_remain_allowed(self):
        self.write('README.md', 'Copyright D-dragon5; TanxeStudio / tanxestudio.\nteam@example.com\n')
        self.write('LICENSE', 'MIT License\nCopyright Desktop Tasks Widget Contributors\n')
        count, findings = audit.scan(self.root)
        self.assertEqual(count, 2)
        self.assertFalse(findings)

    def test_hidden_environment_and_credentials_are_rejected(self):
        self.write('.env.local', self.token())
        self.write('.credentials', self.token())
        _, findings = audit.scan(self.root)
        files = {finding['file'] for finding in findings}
        self.assertIn('.env.local', files)
        self.assertIn('.credentials', files)

    def test_runtime_files_and_unrecognized_outputs_are_rejected(self):
        names = ('installation.json', 'workflow.config.json', 'smoke-result.json', 'outputs/session.log')
        for name in names:
            self.write(name, '{}')
        _, findings = audit.scan(self.root)
        self.assertTrue(set(names).issubset({finding['file'] for finding in findings}))

    def test_file_symlinks_are_rejected_without_reading_target(self):
        target = Path(self.temp.name) / 'outside.txt'
        target.write_text(self.token(), encoding='utf-8')
        link = self.root / 'linked.txt'
        try:
            link.symlink_to(target)
        except (OSError, NotImplementedError):
            self.skipTest('File symlinks unavailable for this account')
        _, findings = audit.scan(self.root)
        self.assertTrue(any(finding['file'] == 'linked.txt' for finding in findings))

    def test_safe_zip_text_is_scanned(self):
        path = self.archive([('package/README.md', 'Synthetic example.\n')])
        count, findings = audit.scan_archives(path)
        self.assertEqual(count, 1)
        self.assertFalse(findings)

    def test_zip_binary_media_and_recursive_archives_are_rejected(self):
        names = ('package/screen.png', 'package/records.xlsx', 'package/nested.zip', 'package/backup.7z')
        path = self.archive([(name, b'fixture') for name in names])
        _, findings = audit.scan_archives(path)
        self.assertGreaterEqual(len(findings), len(names))

    def test_zip_unsafe_paths_and_duplicate_members_are_rejected(self):
        unsafe = ('../escape.txt', '/absolute.txt', 'Q:' + '/absolute.txt', '..' + chr(92) + 'escape.txt')
        path = self.archive([(name, 'fixture') for name in unsafe] + [('same.txt', 'a'), ('same.txt', 'b')])
        _, findings = audit.scan_archives(path)
        self.assertGreaterEqual(len(findings), len(unsafe) + 1)

    def test_zip_symlink_member_is_rejected(self):
        entry = zipfile.ZipInfo('package/link.txt')
        entry.create_system = 3
        entry.external_attr = (stat.S_IFLNK | 0o777) << 16
        path = self.archive([(entry, '../outside.txt')])
        _, findings = audit.scan_archives(path)
        self.assertTrue(findings)

    def test_zip_text_and_private_terms_never_echo_matched_values(self):
        term = 'synthetic-local-' + 'private-term'
        path = self.archive([('package/README.md', self.token() + '\n' + term)])
        _, findings = audit.scan_archives(path, (term,))
        self.assertTrue(findings)
        encoded = json.dumps(findings)
        self.assertNotIn(term, encoded)
        self.assertNotIn(self.token(), encoded)
        self.assertTrue(any(finding['rule'] == 'local-private-value' for finding in findings))

    def test_worktree_private_terms_never_echo_matched_values(self):
        term = 'synthetic-local-' + 'private-term'
        self.write('README.md', term)
        _, findings = audit.scan(self.root, (term,))
        self.assertTrue(findings)
        self.assertNotIn(term, json.dumps(findings))

    def test_worktree_private_filename_is_redacted(self):
        term = 'synthetic-filename-' + 'private-term'
        self.write('docs/' + term + '.md')
        _, findings = audit.scan(self.root, (term,))
        self.assertTrue(any(finding['rule'] == 'local-private-value' for finding in findings))
        self.assertNotIn(term, json.dumps(findings))

    def test_zip_private_member_name_is_redacted(self):
        term = 'synthetic-filename-' + 'private-term'
        path = self.archive([('package/docs/' + term + '.md', 'Safe fixture text.')])
        _, findings = audit.scan_archives(path, (term,))
        self.assertTrue(any(finding['rule'] == 'local-private-value' for finding in findings))
        self.assertNotIn(term, json.dumps(findings))

    def test_cli_reads_only_external_private_word_list_and_redacts_output(self):
        term = 'synthetic-local-' + 'private-term'
        self.write('README.md', term)
        private_file = Path(self.temp.name) / 'local-values.json'
        private_file.write_text(json.dumps([term]), encoding='utf-8')
        result = subprocess.run(
            [sys.executable, '-B', str(ROOT / 'tools/audit_release.py'), str(self.root),
             '--private-values-file', str(private_file)],
            capture_output=True, text=True, encoding='utf-8', errors='replace',
        )
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse(json.loads(result.stdout)['passed'])
        self.assertNotIn(term, result.stdout + result.stderr)


@unittest.skipUnless(shutil.which('git'), 'Git required for snapshot tests')
class GitAuditTests(unittest.TestCase):
    write = AuditReleaseTests.write
    token = AuditReleaseTests.token

    def setUp(self):
        AuditReleaseTests.setUp(self)
        self.git('init', '-q')
        self.git('config', 'user.name', 'Release Fixture')
        self.git('config', 'user.email', 'fixture@example.com')

    def git(self, *arguments):
        return subprocess.check_output(['git', '-C', str(self.root), *arguments], stderr=subprocess.STDOUT)

    def commit(self):
        self.git('commit', '-qm', 'synthetic test fixture')

    def test_deleted_secret_remains_detectable_in_reachable_history(self):
        self.write('README.md', self.token())
        self.git('add', 'README.md')
        self.commit()
        self.git('rm', '-q', 'README.md')
        self.commit()
        self.assertFalse(audit.scan(self.root)[1])
        _, findings = audit.scan_git(self.root, history=True)
        self.assertTrue(any(finding['rule'] == 'credential-token' for finding in findings))
        self.assertNotIn(self.token(), json.dumps(findings))

    def test_tracked_ignored_output_is_included_in_history_scan(self):
        self.write('.gitignore', 'dist/\n')
        self.write('dist/output.txt', self.token())
        self.git('add', '.gitignore')
        self.git('add', '-f', 'dist/output.txt')
        self.commit()
        _, findings = audit.scan_git(self.root, history=True)
        self.assertTrue(any(finding['rule'] == 'credential-token' for finding in findings))

    def test_index_scan_uses_staged_blob_not_current_file(self):
        self.write('README.md', self.token())
        self.git('add', 'README.md')
        self.write('README.md', 'Safe replacement left unstaged.\n')
        self.assertFalse(audit.scan(self.root)[1])
        _, findings = audit.scan_git(self.root, index=True)
        self.assertTrue(any(finding['rule'] == 'credential-token' for finding in findings))

    def test_index_scan_checks_force_added_ignored_directory_before_commit(self):
        self.write('.gitignore', 'dist/\n')
        self.write('dist/output.txt', self.token())
        self.git('add', '.gitignore')
        self.git('add', '-f', 'dist/output.txt')
        _, findings = audit.scan_git(self.root, index=True)
        self.assertTrue(any(finding['rule'] == 'credential-token' for finding in findings))
        self.assertNotIn(self.token(), json.dumps(findings))


if __name__ == '__main__':
    unittest.main()
