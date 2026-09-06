"""Prepare/apply main protection using the authenticated GitHub CLI, without handling tokens."""
import argparse
import json
from pathlib import Path
import shutil
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument('--apply', action='store_true', help='Apply the displayed protection settings to GitHub.')
parser.add_argument('--approvals', type=int, choices=(0, 1), default=0,
                    help='Use 1 when another eligible reviewer exists; the PR author cannot approve their own PR.')
args = parser.parse_args()
settings = {
    'required_status_checks': {'strict': True, 'contexts': ['foundation']},
    'enforce_admins': True,
    'required_pull_request_reviews': {
        'dismiss_stale_reviews': True,
        'require_code_owner_reviews': False,
        'required_approving_review_count': args.approvals,
    },
    'restrictions': None,
    'required_conversation_resolution': True,
    'allow_force_pushes': False,
    'allow_deletions': False,
}
payload = json.dumps(settings)
if args.apply:
    root = Path(__file__).resolve().parent.parent
    local = root / '.tools/gh/bin/gh.exe'
    gh = str(local) if local.exists() else shutil.which('gh')
    if not gh:
        raise SystemExit('GitHub CLI is required; authenticate using gh auth login.')
    subprocess.run([gh, 'api', '--method', 'PUT', 'repos/oNascimento/url-shortener-specs/branches/main/protection',
                    '--input', '-', '--silent'], input=payload, text=True, check=True, cwd=root)
    print('main protection applied. Explicit approval remains required before merge.')
else:
    print(json.dumps(settings, indent=2))
    print('Dry run only. Use --apply after authenticating; select --approvals 1 when a separate reviewer is available.')
