import json
import re
from pathlib import Path

root = Path(__file__).resolve().parent.parent / 'docs'
api = json.loads((root / 'openapi.json').read_text(encoding='utf-8'))
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

for name in ['01-especificacao-funcional.md', '02-especificacao-tecnica.md', '03-operacao-e-testes.md']:
    text = (root / name).read_text(encoding='utf-8')
    check(len(text.split()) >= 900, f'Documento incompleto: {name}')
    check('\ufffd' not in text, f'Problema UTF-8: {name}')
    check(not re.search(r'\b(TODO|TBD|FIXME)\b', text), f'Marcador pendente: {name}')
    for link in re.findall(r'\]\(([^)]+)\)', text):
        if not link.startswith(('https://', 'http://', '#')):
            check((root / link).exists(), f'Link local quebrado: {link}')

functional = (root / '01-especificacao-funcional.md').read_text(encoding='utf-8')
for i in range(1, 24):
    check(f'RF-{i:02d}' in functional, f'Requisito RF-{i:02d} ausente')
tests = (root / '03-operacao-e-testes.md').read_text(encoding='utf-8')
for i in range(1, 25):
    check(f'T{i:02d}' in tests, f'Teste T{i:02d} ausente')

alphabet = '0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ'


def base62(n):
    result = ''
    while n:
        n, remainder = divmod(n, 62)
        result = alphabet[remainder] + result
    return result


for number, code in {1:'1', 9:'9', 10:'a', 35:'z', 36:'A', 61:'Z', 62:'10', 3843:'ZZ', 3844:'100'}.items():
    check(base62(number) == code, 'Vetor Base62 incorreto')
check(62**6 == 56800235584, 'Capacidade 6')
check(62**7 == 3521614606208, 'Capacidade 7')
check(len(base62(2**63-1)) == 11, 'Limite BIGINT')
check(1000 * 60 * 15 == 900000, 'Carga 15 min')
check(1000 * 86400 * 90 == 7776000000, 'Retenção sob pico contínuo')

print(f'PASS: {checks} verificações estruturais e documentais; {len(ids)} operações HTTP.')
print('Escopo: sintaxe JSON, referências, contratos estruturais, links locais, cobertura nominal e cálculos.')
print('Não substitui validação integral OpenAPI por ferramenta especializada nem testes da futura aplicação.')
for path in sorted(root.iterdir()):
    print(f'{path.name}: {path.stat().st_size} bytes')
