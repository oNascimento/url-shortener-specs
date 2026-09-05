import argparse
from collections import Counter
import json
import re
from pathlib import Path

parser = argparse.ArgumentParser(description='Validar as especificações e o contrato, sem executar a aplicação.')
parser.add_argument('--root', type=Path, default=Path(__file__).resolve().parent.parent)
repository = parser.parse_args().root.resolve()
root = repository / '.specs'
api = json.loads((root / 'contracts/openapi.json').read_text(encoding='utf-8'))
checks = 0


def check(condition, message):
    global checks
    assert condition, message
    checks += 1


def walk(value):
    if isinstance(value, dict):
        if '$ref' in value:
            pointer = value['$ref']
            check(pointer.startswith('#/'), f'Referência externa inesperada: {pointer}')
            target = api
            for key in pointer[2:].split('/'):
                key = key.replace('~1', '/').replace('~0', '~')
                check(key in target, f'Referência ausente: {pointer}')
                target = target[key]
        if value.get('type') == 'object' and 'properties' in value:
            check(set(value.get('required', [])).issubset(value['properties']), 'Required sem propriedade')
        for child in value.values():
            walk(child)
    elif isinstance(value, list):
        for child in value:
            walk(child)


check(api['openapi'] == '3.1.0', 'Versão OpenAPI')
walk(api)
ids = set()
for path, methods in api['paths'].items():
    for method, operation in methods.items():
        check(method in ['get', 'post', 'head'], f'Método inesperado: {method}')
        check(operation['operationId'] not in ids, 'operationId duplicado')
        ids.add(operation['operationId'])
        expected = set(re.findall(r'\{([^}]+)\}', path))
        params = operation.get('parameters', [])
        actual = {p['name'] for p in params if p['in'] == 'path'}
        check(expected == actual, f'Parâmetros de path inconsistentes: {path}')
        check(all(p['required'] for p in params if p['in'] == 'path'), 'Path opcional')
        check(len({(p['name'], p['in']) for p in params}) == len(params), 'Parâmetro duplicado')
        check(any(k.startswith('2') or k == '302' for k in operation['responses']), 'Sem resposta de sucesso')
        for status, response in operation['responses'].items():
            if status == '204' or method == 'head':
                check('content' not in response, 'Corpo indevido em 204/HEAD')
        for requirement in operation.get('security', api['security']):
            check(set(requirement).issubset(api['components']['securitySchemes']), 'SecurityScheme ausente')
        if path.startswith('/api/v1/auth/') and method == 'post':
            check(any(p['name'] == 'X-CSRF-Token' and p['required'] for p in params), 'Auth POST sem CSRF')
        if path.startswith('/api/v1/admin/'):
            check(operation.get('security', api['security']) == [{'bearerAuth': []}], 'Admin sem JWT')


def markdown_rows(text):
    for line in text.splitlines():
        if line.startswith('|') and line.endswith('|'):
            yield [cell.strip() for cell in line[1:-1].split('|')]


def section(text, title):
    marker = '## ' + title + '\n'
    check(marker in text, f'Seção ausente: {title}')
    return text.split(marker, 1)[1].split('\n## ', 1)[0]


def acyclic(graph, label):
    visited, active = set(), set()
    def visit(node):
        check(node not in active, f'Ciclo em {label}: {node}')
        if node in visited:
            return
        active.add(node)
        for dependency in graph[node]:
            check(dependency in graph, f'Dependência ausente em {label}: {dependency}')
            visit(dependency)
        active.remove(node)
        visited.add(node)
    for node in graph:
        visit(node)


expected_features = {f'{i:03d}' for i in range(1, 9)}
expected_rf = {f'RF-{i:02d}' for i in range(1, 24)}
expected_af = {f'AF-{i:02d}' for i in range(1, 17)}
expected_tests = {f'T{i:02d}' for i in range(1, 25)}
features = sorted((root / 'features').iterdir())
check({p.name[:3] for p in features} == expected_features, 'Conjunto de funcionalidades incorreto')
check(len(features) == 8, 'Funcionalidades duplicadas')
for p in features:
    for name in ('spec.md', 'plan.md', 'tasks.md'):
        check((p / name).is_file(), f'Documento ausente: {p.name}/{name}')

markdown_files = [repository/'README.md', *sorted((repository/'docs').glob('*.md')), *sorted(root.rglob('*.md'))]
texts = {p: p.read_text(encoding='utf-8') for p in markdown_files}
for path, text in texts.items():
    check('\ufffd' not in text, f'UTF-8 inválido: {path}')
    check(not re.search(r'\b(TODO|TBD|FIXME)\b', text), f'Marcador pendente: {path}')
    for link in re.findall(r'\]\(([^)\n]+)\)', text):
        if link.startswith(('https://', 'http://', 'mailto:')):
            continue
        relative, _, fragment = link.partition('#')
        target = (path.parent / relative).resolve() if relative else path
        check(target.is_relative_to(repository), f'Link fora do repositório: {link}')
        check(target.is_file(), f'Link local quebrado: {path}: {link}')
        if fragment:
            check(f'id="{fragment}"' in target.read_text(encoding='utf-8'), f'Âncora local ausente: {link}')
    check('](openapi.json)' not in text and '](docs/openapi.json)' not in text, 'Link de contrato antigo')
check(not (repository/'docs/openapi.json').exists(), 'Contrato antigo duplicado em docs')
for path in (repository/'docs').glob('*.md'):
    check(len(texts[path].split()) < 120, f'Página legada ainda duplica conteúdo: {path.name}')
check('.NET 10' in texts[root/'architecture.md'], 'Versão .NET 10 ausente na arquitetura')

rf_definitions = {}
for feature in features:
    for rid in re.findall(r'^\*\*(RF-\d{2}) —', texts[feature/'spec.md'], re.M):
        check(rid not in rf_definitions, f'Requisito duplicado: {rid}')
        rf_definitions[rid] = feature/'spec.md'
check(set(rf_definitions) == expected_rf, 'Cobertura de requisitos RF incompleta')
af_definitions = re.findall(r'^\| (AF-\d{2}) \|', texts[root/'product.md'], re.M)
test_definitions = re.findall(r'^\| (T\d{2}) /', texts[root/'operations.md'], re.M)
check(set(af_definitions) == expected_af and len(af_definitions) == len(expected_af), 'Definições AF incorretas')
check(set(test_definitions) == expected_tests and len(test_definitions) == len(expected_tests), 'Definições T incorretas')

trace = texts[root/'traceability.md']
rf_rows = [row for row in markdown_rows(section(trace, 'Requisitos RF')) if re.fullmatch(r'RF-\d{2}', row[0])]
af_rows = [row for row in markdown_rows(section(trace, 'Critérios AF')) if re.fullmatch(r'AF-\d{2}', row[0])]
test_rows = [row for row in markdown_rows(section(trace, 'Cenários T')) if re.fullmatch(r'T\d{2}', row[0])]
for rows, expected, label in ((rf_rows, expected_rf, 'RF'), (af_rows, expected_af, 'AF'), (test_rows, expected_tests, 'T')):
    check({r[0] for r in rows} == expected and len(rows) == len(expected), f'Rastreabilidade {label} incompleta/duplicada')
    for row in rows:
        check(len(row) == 4, f'Colunas inválidas em {label}')
for row in rf_rows:
    rf = row[0]
    relative = rf_definitions[rf].relative_to(root).as_posix()
    check(relative + '#' + rf.lower() in row[1], f'Responsável RF errado: {rf}')
    check(bool(re.findall(r'AF-\d{2}', row[2])), f'RF sem aceite: {rf}')
    check(set(re.findall(r'AF-\d{2}', row[2])).issubset(expected_af), f'AF desconhecido: {rf}')
    check(bool(re.findall(r'T\d{2}', row[3])), f'RF sem cenário: {rf}')
    check(set(re.findall(r'T\d{2}', row[3])).issubset(expected_tests), f'T desconhecido: {rf}')
for row in af_rows:
    related = set(re.findall(r'RF-\d{2}', row[3]))
    check(bool(related) and related.issubset(expected_rf), f'AF sem RF válido: {row[0]}')
    forward = {r[0] for r in rf_rows if row[0] in re.findall(r'AF-\d{2}', r[2])}
    check(related == forward, f'Relação RF/AF não é recíproca: {row[0]}')
for row in test_rows:
    related = set(re.findall(r'RF-\d{2}', row[3]))
    forward = {r[0] for r in rf_rows if row[0] in re.findall(r'T\d{2}', r[3])}
    check(related == forward, f'Relação RF/T não é recíproca: {row[0]}')

op_rows = [r for r in markdown_rows(section(trace, 'Operações OpenAPI')) if r[0].startswith('`')]
check(len(op_rows) == 26, 'Quantidade de operações rastreadas incorreta')
check({r[0].strip('`') for r in op_rows} == ids, 'Operações ausentes/duplicadas na matriz')
operation_by_id = {o['operationId']: (method.upper(), path) for path, methods in api['paths'].items() for method, o in methods.items()}
for row in op_rows:
    check(len(row) == 4, 'Colunas de operação inválidas')
    method, route = operation_by_id[row[0].strip('`')]
    check(row[1] == f'`{method} {route}`', f'Rota divergente: {row[0]}')
    linked = re.search(r'\]\(([^)]+)\)', row[2])
    check(linked is not None, 'Operação sem responsável')
    plan = (root / linked.group(1)).resolve()
    check(row[0] in texts[plan], f'Operação ausente do plano responsável: {row[0]}')

task_graph, task_states = {}, {}
for feature in features:
    for row in markdown_rows(texts[feature/'tasks.md']):
        if len(row) < 2 or not re.fullmatch(r'F\d{3}-T\d{2}', row[1]):
            continue
        check(len(row) == 5, 'Colunas de tarefa inválidas')
        state, tid, task, depends, evidence = row
        check(tid not in task_graph, f'ID de tarefa duplicado: {tid}')
        check(tid[1:4] == feature.name[:3], f'Tarefa em funcionalidade incorreta: {tid}')
        check(state in ('[ ]','[x]'), f'Estado de tarefa inválido: {tid}')
        check(bool(task) and bool(evidence), f'Tarefa sem descrição/evidência: {tid}')
        dependencies = [] if depends == '—' else [p.strip() for p in depends.split(',')]
        check(all(re.fullmatch(r'F\d{3}-T\d{2}', p) for p in dependencies), f'Dependência malformada: {tid}')
        task_graph[tid] = dependencies
        task_states[tid] = state
check(len(task_graph) >= 40, 'Tarefas da implementação incompletas')
acyclic(task_graph, 'tarefas')
for tid, deps in task_graph.items():
    if task_states[tid] == '[x]':
        check(all(task_states[d] == '[x]' for d in deps), f'Tarefa concluída com dependência pendente: {tid}')

feature_graph = {}
for row in markdown_rows(section(texts[root/'README.md'], 'Funcionalidades e dependências')):
    if re.fullmatch(r'\d{3}', row[0]):
        check(row[0] not in feature_graph, 'Funcionalidade duplicada no índice')
        feature_graph[row[0]] = [] if row[2] == '—' else [p.strip() for p in row[2].split(',')]
check(set(feature_graph) == expected_features, 'Índice de funcionalidades incompleto')
acyclic(feature_graph, 'funcionalidades')
for feature, deps in feature_graph.items():
    found = {dep[1:4] for tid, ds in task_graph.items() if tid[1:4] == feature for dep in ds if dep[1:4] != feature}
    check(found == set(deps), f'Dependências do índice divergem das tarefas: {feature}')

alphabet = '0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ'
def base62(n):
    result = ''
    while n:
        n, remainder = divmod(n, 62)
        result = alphabet[remainder] + result
    return result
for number, code in {1:'1', 9:'9', 10:'a', 35:'z', 36:'A', 61:'Z', 62:'10', 3843:'ZZ', 3844:'100'}.items():
    check(base62(number) == code, 'Vetor Base62 incorreto')
check(62**6 == 56800235584 and 62**7 == 3521614606208, 'Capacidade Base62')
check(len(base62(2**63-1)) == 11, 'Limite BIGINT')
check(1000*60*15 == 900000 and 1000*86400*90 == 7776000000, 'Cálculos de carga')

print(f'PASS: {checks} verificações documentais e estruturais.')
print(f'8 funcionalidades; {len(task_graph)} tarefas ({sum(s == "[ ]" for s in task_states.values())} pendentes); 23 RF; 16 AF; 24 T; {len(ids)} operações.')
print('Links, referências OpenAPI, rastreabilidade e grafos conferidos. Nenhum teste da aplicação foi executado.')
