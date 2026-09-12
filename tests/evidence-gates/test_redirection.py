import json
import pathlib
import sys
import tempfile
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[2] / 'scripts'))
from check_link_coverage import evaluate
from check_redirection import requirements
from feature_evidence import ownership_errors, task_manifest, changes, fingerprint
import test_evidence


class RedirectionGateTests(unittest.TestCase):
    setUp = test_evidence.CoverageGateTests.setUp
    report = test_evidence.CoverageGateTests.report
    def test_exact_threshold_without_rounding(self):
        path = self.report('threshold.json', 1, [1] * 19 + [0])
        self.assertTrue(evaluate([path], self.manifest, threshold=95)['passed'])
        self.assertFalse(evaluate([path], self.manifest, threshold=96)['passed'])
        self.assertFalse(evaluate([path], self.manifest)['passed'])
        path = self.report('below.json', 1, [1] * 9499 + [0] * 501)
        self.assertFalse(evaluate([path], self.manifest, threshold=95)['passed'])

    def test_union_ownership_and_new_file_must_be_full(self):
        manifests = [self.manifest, {'files': [], 'methods': {'src/Shared.cs': ['Changed']}}]
        self.assertEqual([], ownership_errors({'src/Shared.cs': {1}}, {'src/Feature.cs'}, manifests))
        self.assertEqual(['src/Shared.cs'], ownership_errors({}, {'src/Shared.cs'}, manifests))
        self.assertEqual(['src/Unknown.cs'], ownership_errors({'src/Unknown.cs': {1}}, set(), manifests))

    def test_task_measures_entire_changed_method(self):
        path = self.report('task.json', 1, [1, 0])
        selected = task_manifest([path], self.manifest, {'src/Feature.cs': {1}}, set())
        self.assertEqual({'src/Feature.cs': ['Feature::Run']}, selected['methods'])
        self.assertFalse(evaluate([path], selected, threshold=95)['passed'])
        self.assertEqual(['src/Feature.cs'], task_manifest([path], self.manifest, {}, {'src/Feature.cs'})['files'])

    def test_stacked_task_excludes_ancestor_work(self):
        import subprocess
        def git(*args):
            return subprocess.check_output(['git', *args], cwd=self.folder, text=True).strip()
        git('init', '-b', 'main')
        git('config', 'user.name', 'Gate test')
        git('config', 'user.email', 'gate@example.invalid')
        (self.folder / 'src').mkdir()
        (self.folder / 'src/Base.cs').write_text('base')
        git('add', '.')
        git('commit', '-qm', 'base')
        git('checkout', '-qb', 'task1')
        (self.folder / 'src/First.cs').write_text('first')
        git('add', '.')
        git('commit', '-qm', 'first')
        git('checkout', '-qb', 'task2')
        (self.folder / 'src/Second.cs').write_text('second')
        git('add', '.')
        git('commit', '-qm', 'second')
        self.assertEqual({'src/Second.cs'}, changes('task1', self.folder)[2])
        self.assertEqual({'src/First.cs', 'src/Second.cs'}, changes('main', self.folder)[2])
        before = fingerprint(self.folder)
        (self.folder / 'evidence.md').write_text('document only')
        self.assertEqual(before, fingerprint(self.folder))
        (self.folder / 'src/Second.cs').write_text('changed code')
        self.assertNotEqual(before, fingerprint(self.folder))


class RedirectionRequirementsTests(unittest.TestCase):
    def test_missing_and_stale_provenance_fail_before_reading_reports(self):
        import check_redirection
        from unittest.mock import patch
        with tempfile.TemporaryDirectory() as folder:
            arguments = ['check_redirection', folder, '--task', 'F004-T00']
            with patch.object(sys, 'argv', arguments), patch.object(check_redirection, 'fingerprint', return_value='current'):
                with self.assertRaisesRegex(SystemExit, 'Missing or stale'):
                    check_redirection.main()
                pathlib.Path(folder, 'provenance.json').write_text(json.dumps({'fingerprint': 'old'}))
                with self.assertRaisesRegex(SystemExit, 'Missing or stale'):
                    check_redirection.main()

    def test_future_requirements_are_not_claimed(self):
        matrix = {'requirements': [{'id': 'capture', 'stage': 2, 'tests': ['Example.Capture']}]}
        self.assertTrue(requirements([], matrix, 0)['passed'])
        self.assertFalse(requirements([], matrix, 2)['passed'])

    def test_missing_failed_skipped_and_partial_names_fail(self):
        matrix = {'requirements': [{'id': 'http', 'stage': 1, 'tests': ['Example.Redirect']}]}
        with tempfile.TemporaryDirectory() as folder:
            path = pathlib.Path(folder) / 'result.trx'
            for outcome, name, expected in [('Passed', 'Redirect', True), ('Failed', 'Redirect', False),
                    ('NotExecuted', 'Redirect', False), ('Passed', 'OtherRedirect', False)]:
                path.write_text(f'''<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
                <TestDefinitions><UnitTest id="1"><TestMethod className="Example" name="{name}"/></UnitTest></TestDefinitions>
                <Results><UnitTestResult testId="1" outcome="{outcome}"/></Results></TestRun>''')
                self.assertEqual(expected, requirements([path], matrix, 1)['passed'])
