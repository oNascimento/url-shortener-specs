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
command_index = 0


def run(*command):
    global command_index
    result = subprocess.run(command, cwd=ROOT, text=True, encoding='utf-8', errors='replace', stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    command_index += 1
    if results.exists():
        log = results / f'command-{command_index:02}.log'
        log.write_text(subprocess.list2cmdline(command) + '\n' + result.stdout, encoding='utf-8')
        print(('PASS: ' if result.returncode == 0 else 'FAIL: ') + subprocess.list2cmdline(command) + ' (log: ' + str(log) + ')', flush=True)
    if result.returncode:
        print('\n'.join(result.stdout.splitlines()[-20:]), flush=True)
        raise SystemExit(result.returncode)


if stage >= 1:
    subprocess.run(['docker', 'info', '--format', '{{.ServerVersion}}'], cwd=ROOT, check=True, timeout=30)
run(sys.executable, 'scripts/check_redirection.py', str(results), '--task', task, '--record')
(results / 'environment.txt').write_text('\n'.join([
    'task: ' + task, 'base: ' + args.base,
    'dotnet: ' + subprocess.check_output(['dotnet', '--version'], text=True).strip(),
    'python: ' + sys.version, 'platform: ' + sys.platform,
    'PostgreSQL: 17.6; RabbitMQ: 4.1.4-management; xUnit: 2.9.3; Coverlet: 6.0.4; Debug']) + '\n')
if stage >= 1:
    try:
        run('dotnet', 'test', 'tests/Shortener.Redirection.Tests', '--no-build', '-c', 'Debug',
            '--settings', 'tests/redirection.runsettings', '--logger', 'trx', '--results-directory', str(results / 'feature'))
    finally:
        # Retain immutable image identities, including failed runs; a tag alone is not provenance.
        images = ['postgres:17.6', 'rabbitmq:4.1.4-management'] + (['caddy:2.10.2-alpine'] if stage >= 5 else [])
        inspection = subprocess.run(['docker', 'image', 'inspect', *images,
            '--format', '{{.Id}} {{json .RepoDigests}}'], text=True, capture_output=True)
        (results / 'images.txt').write_text(inspection.stdout + inspection.stderr, encoding='utf-8')
    # Shared production methods retain complete coverage through the existing regression suites.
    shared = json.loads((ROOT / 'tests/redirection-coverage.json').read_text())['methods']
    for project in (['Shortener.LinkManagement.Tests', 'Shortener.Authentication.Tests'] if shared else []):
        run('dotnet', 'test', 'tests/' + project, '--no-build', '-c', 'Debug',
            '--settings', 'tests/redirection.runsettings', '--logger', 'trx', '--results-directory', str(results / project))
run(sys.executable, 'scripts/check_redirection.py', str(results), '--task', task, '--base', args.base)
print('Evidence: ' + str(results))
