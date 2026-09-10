import importlib.util
import json
import pathlib
import subprocess
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]


def load(name):
    spec = importlib.util.spec_from_file_location(name, ROOT / 'scripts' / (name + '.py'))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


class CoverageGateTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.folder = pathlib.Path(self.temp.name)
        self.manifest = {'files': ['src/Feature.cs'], 'methods': {}}
        self.evaluate = load('check_link_coverage').evaluate

    def report(self, name, hits, branches):
        path = self.folder / name
        path.write_text(json.dumps({'Feature.dll': {'/repo/src/Feature.cs': {'Feature': {'Run': {
            'Lines': {'1': hits}, 'Branches': [{'Line': 1, 'Offset': 2, 'EndOffset': 3 + i, 'Path': i,
                'Ordinal': i, 'Hits': hit} for i, hit in enumerate(branches)]}}}}}))
        return path

    def test_empty_or_missing_files_cannot_report_one_hundred_percent(self):
        self.assertFalse(self.evaluate([], self.manifest)['passed'])
        report = self.report('one.json', 1, [1, 1])
        self.manifest['files'].append('src/Missing.cs')
        self.assertFalse(self.evaluate([report], self.manifest)['passed'])

    def test_one_uncovered_branch_fails_even_when_every_line_is_covered(self):
        report = self.report('one.json', 1, [1, 0])
        result = self.evaluate([report], self.manifest)
        self.assertEqual({'covered': 1, 'total': 1}, result['lines'])
        self.assertFalse(result['passed'])

    def test_merging_hits_does_not_duplicate_denominators(self):
        a = self.report('one.json', 1, [1, 0])
        b = self.report('two.json', 1, [0, 1])
        result = self.evaluate([a, b], self.manifest)
        self.assertTrue(result['passed'])
        self.assertEqual({'covered': 2, 'total': 2}, result['branches'])

    def test_declared_method_must_appear(self):
        report = self.report('one.json', 1, [1, 1])
        self.manifest = {'files': [], 'methods': {'src/Feature.cs': ['Missing']}}
        self.assertFalse(self.evaluate([report], self.manifest)['passed'])

    def test_changed_method_cannot_be_hidden_by_a_narrow_manifest(self):
        report = self.report('one.json', 1, [1, 1])
        manifest = {'files': [], 'methods': {'src/Feature.cs': ['Other']}}
        result = self.evaluate([report], manifest, {'src/Feature.cs': {1}})
        self.assertIn('src/Feature.cs: Feature::Run', result['unlistedModifiedMethods'])
        self.assertFalse(result['passed'])

    def test_base_branch_changes_are_not_attributed_to_feature(self):
        def git(*args):
            return subprocess.check_output(['git', *args], cwd=self.folder, text=True).strip()
        git('init', '-b', 'main')
        git('config', 'user.name', 'Gate test')
        git('config', 'user.email', 'gate@example.invalid')
        (self.folder / 'Shared.cs').write_text('original')
        git('add', '.')
        git('commit', '-qm', 'base')
        original = git('rev-parse', 'HEAD')
        git('branch', 'feature')
        (self.folder / 'Shared.cs').write_text('main only')
        git('commit', '-qam', 'main advances')
        git('checkout', '-q', 'feature')
        (self.folder / 'Feature.cs').write_text('feature')
        git('add', '.')
        git('commit', '-qm', 'feature changes')
        base = load('check_link_coverage').comparison_base('main', self.folder)
        self.assertEqual(original, base)
        self.assertEqual('Feature.cs', git('diff', '--name-only', base))
        (self.folder / 'Shared.cs').write_text('feature also changes shared code')
        self.assertEqual(['Feature.cs', 'Shared.cs'], git('diff', '--name-only', base).splitlines())


class RequirementsGateTests(unittest.TestCase):
    def test_missing_trx_cannot_claim_requirements(self):
        matrix = json.loads((ROOT / 'tests/link-management-requirements.json').read_text())
        result = load('check_link_requirements').evaluate([], matrix)
        self.assertFalse(result['passed'])
        self.assertEqual(0, result['testCases'])

    def test_skipped_and_failed_tests_are_rejected(self):
        matrix = json.loads((ROOT / 'tests/link-management-requirements.json').read_text())
        for outcome in ['NotExecuted', 'Failed']:
            with tempfile.TemporaryDirectory() as folder:
                path = pathlib.Path(folder) / 'test.trx'
                path.write_text(f'''<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
                <TestDefinitions><UnitTest id="1"><TestMethod className="Example" name="Test"/></UnitTest></TestDefinitions>
                <Results><UnitTestResult testId="1" outcome="{outcome}"/></Results></TestRun>''')
                result = load('check_link_requirements').evaluate([path], matrix)
                self.assertFalse(result['passed'])
                self.assertIn('Failed or skipped test: Example.Test', result['errors'])


if __name__ == '__main__':
    unittest.main()
