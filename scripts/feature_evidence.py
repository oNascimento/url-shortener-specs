"""Shared ownership, task scope and reproducible evidence identity for feature gates."""
import hashlib
import json
import pathlib
import re
import subprocess

ROOT = pathlib.Path(__file__).resolve().parents[1]


def git(*args, root=ROOT):
    return subprocess.check_output(['git', *args], cwd=root, text=True, encoding='utf-8').strip()


def changes(base, root=ROOT):
    ancestor = git('merge-base', base, 'HEAD', root=root)
    modified, source = {}, None
    for line in git('diff', '--no-ext-diff', '--unified=0', ancestor, '--', 'src', root=root).splitlines():
        if line.startswith('+++ b/'):
            source = line[6:]
        match = re.match(r'@@ -\d+(?:,\d+)? \+(\d+)(?:,(\d+))? @@', line)
        if match and source and source.endswith('.cs'):
            start, count = int(match[1]), int(match[2] or 1)
            modified.setdefault(source, set()).update(range(start, start + count))
    added = set(git('diff', '--name-only', '--diff-filter=A', ancestor, '--', 'src', root=root).splitlines())
    added.update(git('ls-files', '--others', '--exclude-standard', '--', 'src', root=root).splitlines())
    return ancestor, modified, {name for name in added if name.endswith('.cs')}


def known_manifests(root=ROOT):
    return [json.loads(path.read_text()) for path in sorted((root / 'tests').glob('*-coverage.json'))]


def ownership_errors(changed, added, manifests):
    full = {file for manifest in manifests for file in manifest['files']}
    partial = {file for manifest in manifests for file in manifest['methods']}
    return sorted((set(changed) - full - partial) | (set(added) - full))


def task_manifest(paths, full_manifest, changed, added):
    result = {'files': sorted(set(full_manifest['files']) & set(added)), 'methods': {}}
    owned = set(full_manifest['files']) | set(full_manifest['methods'])
    for path in paths:
        for module in json.loads(path.read_text(encoding='utf-8-sig')).values():
            for source, classes in module.items():
                normalized = source.replace('\\', '/')
                relative = next((f for f in owned if normalized == f or normalized.endswith('/' + f)), None)
                if relative is None or relative in result['files']:
                    continue
                for class_name, methods in classes.items():
                    for name, body in methods.items():
                        numbers = [int(n) for n in body['Lines']]
                        if numbers and any(min(numbers) <= n <= max(numbers) for n in changed.get(relative, [])):
                            result['methods'].setdefault(relative, set()).add(class_name + '::' + name)
    result['methods'] = {file: sorted(methods) for file, methods in result['methods'].items()}
    return result


def fingerprint(root=ROOT):
    """Content identity excludes documentation and generated build/test output, includes tooling."""
    names = set(git('ls-files', '--cached', '--others', '--exclude-standard', root=root).splitlines())
    digest = hashlib.sha256()
    for name in sorted(names):
        if not (name.startswith(('src/', 'tests/', 'scripts/', 'infra/', '.github/', 'web/'))
                or name in ('global.json', 'Directory.Build.props', 'Shortener.slnx', 'Dockerfile', 'compose.yaml',
                            '.specs/contracts/openapi.json')):
            continue
        path = root / name
        if path.is_file():
            # Checkout newline conventions must not change the identity across Windows/Linux.
            content = path.read_bytes().replace(b'\r\n', b'\n')
            digest.update(name.encode() + b'\0' + content + b'\0')
    return digest.hexdigest()
