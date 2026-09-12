"""F004 task/cumulative coverage and requirement gate. Never consumes stale evidence."""
import argparse
import json
import pathlib
import sys
import xml.etree.ElementTree as ET

from check_link_coverage import evaluate
from feature_evidence import ROOT, changes, fingerprint, git, known_manifests, ownership_errors, task_manifest


def requirements(paths, matrix, stage):
    ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
    tests, errors = {}, []
    for path in paths:
        tree = ET.parse(path).getroot()
        definitions = {item.attrib['id']: item.find('t:TestMethod', ns).attrib
                       for item in tree.findall('.//t:UnitTest', ns)}
        for item in tree.findall('.//t:UnitTestResult', ns):
            method = definitions[item.attrib['testId']]
            name = method['className'].split(',')[0] + '.' + method['name']
            tests.setdefault(name, []).append(item.attrib['outcome'])
    for name, outcomes in tests.items():
        if any(outcome != 'Passed' for outcome in outcomes):
            errors.append('Failed or skipped: ' + name)
    active = [row for row in matrix['requirements'] if row['stage'] <= stage]
    for row in active:
        if not row['tests'] or not all(any(name == ref and all(o == 'Passed' for o in outcomes)
                for name, outcomes in tests.items()) for ref in row['tests']):
            errors.append('Missing passing evidence: ' + row['id'])
    if stage >= 1 and (not tests or not active):
        errors.append('No functional evidence')
    if stage >= 5:
        if {row['requirement'] for row in active} != {'RF-19', 'RF-20', 'RF-21'}:
            errors.append('Incomplete RF coverage')
        if not {'T09', 'T10', 'T13'} <= {scenario for row in active for scenario in row['scenarios']}:
            errors.append('Incomplete scenario coverage')
    return {'passed': not errors, 'testCases': sum(map(len, tests.values())), 'errors': errors,
            'verified': [row['id'] for row in active if 'Missing passing evidence: ' + row['id'] not in errors]}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('results', type=pathlib.Path)
    parser.add_argument('--task', required=True, choices=[f'F004-T{i:02}' for i in range(7)])
    parser.add_argument('--base', default='origin/main')
    parser.add_argument('--feature-base', default='origin/main')
    parser.add_argument('--record', action='store_true', help='Record provenance BEFORE a fresh test run.')
    args = parser.parse_args()
    provenance = args.results / 'provenance.json'
    identity = fingerprint()
    if args.record:
        args.results.mkdir(parents=True, exist_ok=False)
        provenance.write_text(json.dumps({'sha': git('rev-parse', 'HEAD'), 'fingerprint': identity}, indent=2) + '\n')
        return 0
    if not provenance.is_file() or json.loads(provenance.read_text())['fingerprint'] != identity:
        raise SystemExit('Missing or stale evidence provenance; use a NEW results directory and rerun tests.')
    stage = int(args.task[-2:])
    manifest = json.loads((ROOT / 'tests/redirection-coverage.json').read_text())
    matrix = json.loads((ROOT / 'tests/redirection-requirements.json').read_text())
    reports = list(args.results.rglob('coverage.json'))
    feature_base, changed, added = changes(args.feature_base)
    task_base, task_changed, task_added = changes(args.base)
    cumulative = evaluate(reports, manifest, changed, 95) if manifest['files'] or manifest['methods'] else None
    selected = task_manifest(reports, manifest, task_changed, task_added)
    task = evaluate(reports, selected, task_changed, 95) if selected['files'] or selected['methods'] else None
    requirement_result = requirements(list(args.results.rglob('*.trx')), matrix, min(stage, 5))
    unowned = ownership_errors(changed, added, known_manifests())
    passed = not unowned and requirement_result['passed']
    if stage >= 1:
        passed = passed and cumulative is not None and cumulative['passed']
    if task_changed or task_added:
        passed = passed and task is not None and task['passed']
    result = {'passed': bool(passed), 'taskId': args.task, 'featureBase': feature_base, 'taskBase': task_base,
              'fingerprint': identity, 'threshold': 95, 'cumulative': cumulative, 'task': task,
              'requirements': requirement_result, 'unownedSources': unowned,
              'externalAcceptance': matrix['externalAcceptance']}
    (args.results / 'summary.json').write_text(json.dumps(result, indent=2) + '\n')
    print(json.dumps(result, indent=2))
    return 0 if passed else 1


if __name__ == '__main__':
    sys.exit(main())
