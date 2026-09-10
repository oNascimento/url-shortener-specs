"""Fail if backend requirement/scenario evidence is missing, failed or skipped."""
import argparse
import json
import pathlib
import re
import sys
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parents[1]
NS = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}


def evaluate(paths, matrix):
    tests = {}
    for path in paths:
        root = ET.parse(path).getroot()
        definitions = {}
        for test in root.findall('.//t:UnitTest', NS):
            method = test.find('t:TestMethod', NS)
            definitions[test.attrib['id']] = method.attrib['className'].split(',')[0] + '.' + method.attrib['name']
        for result in root.findall('.//t:UnitTestResult', NS):
            name = definitions[result.attrib['testId']]
            tests.setdefault(name, []).append(result.attrib['outcome'])
    errors = []
    if not tests:
        errors.append('No test results found')
    for name, results in tests.items():
        if any(r != 'Passed' for r in results):
            errors.append(f'Failed or skipped test: {name}')
    requirements = {}
    def verified(refs):
        return bool(refs) and all(any(name.endswith(ref) and all(r == 'Passed' for r in outcomes)
                                      for name, outcomes in tests.items()) for ref in refs)
    for row in matrix['requirements']:
        requirements[row['id']] = verified(row['tests'])
    scenarios = {key: verified(refs) for key, refs in matrix['scenarios'].items()}
    for key, passed in (requirements | scenarios).items():
        if not passed:
            errors.append(f'Missing passing evidence: {key}')
    expected = set(re.findall(r'\*\*(RF-\d+)', (ROOT / '.specs/features/003-link-management/spec.md').read_text(encoding='utf-8')))
    covered = {key.split('.')[0] for key in requirements}
    if expected - covered:
        errors.append('Missing RF mappings: ' + ', '.join(sorted(expected - covered)))
    required_scenarios = {'T01', 'T02', 'T03', 'T08', 'T17', 'T20', 'T21', 'T22'}
    if required_scenarios - scenarios.keys():
        errors.append('Missing required scenario mappings')
    return {'passed': not errors, 'testCases': sum(map(len, tests.values())),
            'requirements': requirements, 'scenarios': scenarios, 'errors': errors,
            'externalAcceptance': matrix['externalAcceptance']}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('results', type=pathlib.Path)
    parser.add_argument('--output', type=pathlib.Path)
    args = parser.parse_args()
    matrix = json.loads((ROOT / 'tests/link-management-requirements.json').read_text(encoding='utf-8'))
    result = evaluate(list(args.results.rglob('*.trx')), matrix)
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(result, indent=2))
    return 0 if result['passed'] else 1


if __name__ == '__main__':
    sys.exit(main())
