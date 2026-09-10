"""Merge Coverlet JSON hits and enforce the reviewed F003 implementation denominator."""
import argparse
import json
import pathlib
import re
import subprocess
import sys

ROOT = pathlib.Path(__file__).resolve().parents[1]


def comparison_base(base, root=ROOT):
    return subprocess.check_output(['git', 'merge-base', base, 'HEAD'], cwd=root,
                                   text=True, encoding='utf-8').strip()


def evaluate(paths, manifest, changed_lines=None):
    files = set(manifest['files']) | set(manifest['methods'])
    found = set()
    methods_found = set()
    lines, branches = {}, {}
    unlisted = set()
    for path in paths:
        report = json.loads(path.read_text(encoding='utf-8-sig'))
        for module in report.values():
            for source, classes in module.items():
                normalized = source.replace('\\', '/')
                relative = next((f for f in files if normalized.endswith('/' + f) or normalized == f), None)
                if relative is None:
                    continue
                for class_name, methods in classes.items():
                    for method_name, body in methods.items():
                        identity = class_name + '::' + method_name
                        if relative in manifest['methods']:
                            matches = [m for m in manifest['methods'][relative] if m in identity]
                            if not matches:
                                numbers = [int(n) for n in body['Lines']]
                                if numbers and any(min(numbers) <= n <= max(numbers) for n in (changed_lines or {}).get(relative, [])):
                                    unlisted.add(relative + ': ' + identity)
                                continue
                            methods_found.update((relative, m) for m in matches)
                        if not body['Lines']:
                            continue
                        found.add(relative)
                        for number, hits in body['Lines'].items():
                            key = (relative, int(number))
                            lines[key] = lines.get(key, False) or hits > 0
                        for branch in body['Branches']:
                            key = (relative, identity, branch['Line'], branch['Offset'], branch['EndOffset'], branch['Path'], branch['Ordinal'])
                            branches[key] = branches.get(key, False) or branch['Hits'] > 0
    missing = sorted(files - found)
    missing_methods = sorted((f, m) for f, names in manifest['methods'].items() for m in names if (f, m) not in methods_found)
    uncovered_lines = [f'{f}:{n}' for (f, n), hit in sorted(lines.items()) if not hit]
    uncovered_branches = [f'{k[0]}:{k[2]} ({k[1]}, path {k[5]})' for k, hit in sorted(branches.items()) if not hit]
    return {'passed': bool(lines) and bool(branches) and not (missing or missing_methods or uncovered_lines or uncovered_branches or unlisted),
            'lines': {'covered': sum(lines.values()), 'total': len(lines)},
            'branches': {'covered': sum(branches.values()), 'total': len(branches)},
            'missingFiles': missing, 'missingMethods': missing_methods,
            'unlistedModifiedMethods': sorted(unlisted),
            'uncoveredLines': uncovered_lines, 'uncoveredBranches': uncovered_branches}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('results', type=pathlib.Path)
    parser.add_argument('--output', type=pathlib.Path)
    parser.add_argument('--base', default='origin/main')
    args = parser.parse_args()
    base = comparison_base(args.base)
    manifest = json.loads((ROOT / 'tests/link-management-coverage.json').read_text())
    def git(*command):
        return subprocess.check_output(['git', *command], cwd=ROOT, text=True, encoding='utf-8')
    changed = {}
    source = None
    for line in git('diff', '--no-ext-diff', '--unified=0', base, '--', 'src').splitlines():
        if line.startswith('+++ b/'):
            source = line[6:]
        match = re.match(r'@@ -\d+(?:,\d+)? \+(\d+)(?:,(\d+))? @@', line)
        if match and source and source.endswith('.cs'):
            start, count = int(match[1]), int(match[2] or 1)
            changed.setdefault(source, set()).update(range(start, start + count))
    new_sources = set(git('diff', '--name-only', '--diff-filter=A', base, '--', 'src').splitlines())
    new_sources.update(git('ls-files', '--others', '--exclude-standard', '--', 'src').splitlines())
    new_sources = {f for f in new_sources if f.endswith('.cs')}
    result = evaluate(list(args.results.rglob('coverage.json')), manifest, changed)
    result['unlistedFiles'] = sorted((set(changed) - set(manifest['files']) - set(manifest['methods'])) | (new_sources - set(manifest['files'])))
    result['passed'] = result['passed'] and not result['unlistedFiles']
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(result, indent=2))
    return 0 if result['passed'] else 1


if __name__ == '__main__':
    sys.exit(main())
