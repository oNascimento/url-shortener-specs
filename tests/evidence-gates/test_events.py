import json
import pathlib
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[2] / 'scripts'))
import check_events


class EventsGateTests(unittest.TestCase):
    def test_missing_and_stale_provenance_fail(self):
        with tempfile.TemporaryDirectory() as folder:
            args = ['check_events', folder, '--task', 'F005-T01']
            with patch.object(sys, 'argv', args), patch.object(check_events, 'fingerprint', return_value='current'):
                with self.assertRaisesRegex(SystemExit, 'Missing or stale'):
                    check_events.main()
                pathlib.Path(folder, 'provenance.json').write_text(json.dumps({'fingerprint': 'old'}))
                with self.assertRaisesRegex(SystemExit, 'Missing or stale'):
                    check_events.main()

    def test_final_requires_every_events_requirement_and_scenario(self):
        self.assertFalse(check_events.requirements([], {'requirements': []}, 5)['passed'])
        matrix = {'requirements':[{'id':'idempotence','stage':1,'requirement':'RF-21','scenarios':['T11'], 'tests':['Example.Commit']}]}
        with tempfile.TemporaryDirectory() as folder:
            path = pathlib.Path(folder, 'result.trx')
            for outcome in ['Passed','Failed','NotExecuted']:
                path.write_text(f'''<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
                  <TestDefinitions><UnitTest id="1"><TestMethod className="Example" name="Commit"/></UnitTest></TestDefinitions>
                  <Results><UnitTestResult testId="1" outcome="{outcome}"/></Results></TestRun>''')
                self.assertEqual(outcome == 'Passed', check_events.requirements([path], matrix, 1)['passed'])
                self.assertFalse(check_events.requirements([path], matrix, 5)['passed'])
