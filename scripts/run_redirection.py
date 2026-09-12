"""One reproducible feature run; each invocation creates a fresh evidence directory."""
import argparse
import datetime
import json
import os
import pathlib
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument('--base', default=os.environ.get('EVIDENCE_BASE', 'origin/main'))
parser.add_argument('--results', type=pathlib.Path)
args = parser.parse_args()
stage = json.loads((ROOT / 'tests/redirection-requirements.json').read_text())['stage']
task = f'F004-T{stage:02}'
results = args.results or ROOT / 'artifacts/redirection' / task / datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%S%fZ')


def run(*command):
    subprocess.run(command, cwd=ROOT, check=True)


run(sys.executable, 'scripts/check_redirection.py', str(results), '--task', task, '--record')
(results / 'environment.txt').write_text('\n'.join([
    'task: ' + task, 'base: ' + args.base,
    'dotnet: ' + subprocess.check_output(['dotnet', '--version'], text=True).strip(),
    'python: ' + sys.version, 'platform: ' + sys.platform,
    'PostgreSQL: 17.6; RabbitMQ: 4.1.4-management; xUnit: 2.9.3; Coverlet: 6.0.4; Debug']) + '\n')
if stage >= 1:
    run('dotnet', 'test', 'tests/Shortener.Redirection.Tests', '--no-build', '-c', 'Debug',
        '--settings', 'tests/redirection.runsettings', '--logger', 'trx', '--results-directory', str(results / 'feature'))
    # Shared production methods retain complete coverage through the existing regression suites.
    for project in ['Shortener.LinkManagement.Tests', 'Shortener.Authentication.Tests']:
        run('dotnet', 'test', 'tests/' + project, '--no-build', '-c', 'Debug',
            '--settings', 'tests/redirection.runsettings', '--logger', 'trx', '--results-directory', str(results / project))
run(sys.executable, 'scripts/check_redirection.py', str(results), '--task', task, '--base', args.base)
print('Evidence: ' + str(results))
