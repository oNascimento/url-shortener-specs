import json
from pathlib import Path

OUT = Path(__file__).resolve().parent.parent / '.specs' / 'contracts'


def ref(name):
    return {'$ref': f'#/components/schemas/{name}'}


def obj(properties, required=None, description=None):
    value = {'type': 'object', 'additionalProperties': False, 'properties': properties,
             'required': list(properties) if required is None else required}
    if description:
        value['description'] = description
    return value


def string(**kw):
    return {'type': 'string', **kw}


def nullable(schema):
    return {'anyOf': [schema, {'type': 'null'}]}


def array(schema):
    return {'type': 'array', 'items': schema}


DT = string(format='date-time', description='Instante RFC 3339 UTC.')
DATE = string(format='date', description='Data UTC.')
UUID = string(format='uuid')
ID = string(pattern='^[1-9][0-9]{0,18}$', description='BIGINT positivo <= 9223372036854775807, serializado como string.')
COUNT = string(pattern='^(0|[1-9][0-9]{0,18})$', description='Inteiro não negativo <= 9223372036854775807, como string.')
EMAIL = string(format='email', maxLength=254)
PASSWORD = string(minLength=12, maxLength=128)
TOKEN = string(minLength=1, maxLength=2048)
CODE = string(pattern='^[0-9a-zA-Z]{1,11}$', description='Base62 sensível à caixa; representação canônica sem preenchimento.')
URI = string(format='uri', maxLength=8192, description='URL absoluta HTTP/HTTPS; demais regras na especificação técnica.')
REASON = obj({'reason': string(minLength=10, maxLength=1000)})

schemas = {
    'Problem': obj({
        'type': string(format='uri-reference'), 'title': string(),
        'status': {'type': 'integer', 'minimum': 400, 'maximum': 599},
        'detail': string(), 'instance': string(format='uri-reference'),
        'code': string(), 'traceId': string(),
        'errors': {'type': 'object', 'additionalProperties': array(string())},
    }, ['type', 'title', 'status', 'code', 'traceId']),
    'Message': obj({'message': string()}),
    'CsrfToken': obj({'requestToken': TOKEN}),
    'RegisterInput': obj({'email': EMAIL, 'password': PASSWORD}),
    'EmailInput': obj({'email': EMAIL}),
    'ActionTokenInput': obj({'token': TOKEN}),
    'LoginInput': obj({'email': EMAIL, 'password': string(minLength=1, maxLength=128)}),
    'ResetPasswordInput': obj({'token': TOKEN, 'newPassword': PASSWORD}),
    'AuthResult': obj({'accessToken': TOKEN, 'tokenType': string(const='Bearer'),
                       'expiresIn': {'type': 'integer', 'const': 900}, 'user': ref('User')}),
    'User': obj({'id': UUID, 'email': EMAIL, 'emailVerified': {'type': 'boolean'},
                 'role': string(enum=['user', 'admin']), 'createdAt': DT}),
    'DeletionInput': obj({'password': string(minLength=1, maxLength=128), 'confirmation': string(const='EXCLUIR')}),
    'DeletionAccepted': obj({'jobId': UUID, 'requestedAt': DT, 'purgeDueAt': DT}),
    'CreateLinkInput': obj({'destinationUrl': URI}),
    'Link': obj({'id': ID, 'code': CODE, 'shortUrl': URI, 'destinationUrl': URI,
                 'createdAt': DT, 'status': string(enum=['active', 'disabled', 'blocked'])}),
    'AccessEvent': obj({'eventId': UUID, 'occurredAt': DT,
                       'sourceIp': string(description='Endereço textual IPv4 ou IPv6.') }),
    'CollectionIncident': obj({'id': UUID, 'startedAt': DT, 'endedAt': nullable(DT),
                               'kind': string(enum=['publication_failure', 'publication_unconfirmed', 'consumer_delay', 'monitor_unavailable', 'recovery_gap', 'history_unavailable']),
                               'message': string()}),
    'CollectionHealth': obj({'status': string(enum=['healthy', 'delayed', 'degraded', 'unknown']),
                              'observedAt': nullable(DT), 'oldestPendingAt': nullable(DT),
                              'lastPersistedEventAt': nullable(DT),
                              'incidents': array(ref('CollectionIncident'))},
                             description='Saúde atual global, precedência unknown > degraded > delayed > healthy. Incidentes sobrepostos à consulta permanecem mesmo encerrados. lastPersistedEventAt não é watermark; incidentes não comprovam perda em um link específico.'),
    'DailyCount': obj({'date': DATE, 'count': COUNT}),
    'LinkStats': obj({'linkId': ID, 'from': DATE, 'to': DATE, 'timezone': string(const='UTC'),
                      'total': COUNT, 'days': array(ref('DailyCount')), 'generatedAt': DT,
                      'collectionHealth': ref('CollectionHealth')}),
    'AdminUser': obj({'id': UUID, 'email': EMAIL, 'role': string(enum=['user', 'admin']),
                     'createdAt': DT, 'status': string(enum=['unverified', 'active', 'blocked', 'deleting'])}),
    'AdminLink': obj({'id': ID, 'code': CODE, 'ownerId': nullable(UUID),
                     'destinationUrl': nullable(URI), 'createdAt': DT,
                     'ownerDisabledAt': nullable(DT), 'adminBlockedAt': nullable(DT),
                     'deletedAt': nullable(DT)}),
    'ModerationInput': REASON,
    'AuditEntry': obj({'id': UUID, 'actorId': nullable(UUID),
                       'targetType': string(enum=['user', 'link']), 'targetId': nullable(string()),
                       'action': string(enum=['block', 'unblock']), 'reason': string(), 'occurredAt': DT}),
    'InternalAccessEnvelope': obj({'schemaVersion': {'type': 'integer', 'const': 1},
                                   'eventId': UUID, 'linkId': ID, 'occurredAt': DT,
                                   'sourceIp': string(description='IPv4 ou IPv6, validado pelo consumidor.')},
                                  description='Contrato RabbitMQ access.recorded.v1; não é endpoint público. eventId e occurredAt são imutáveis em retries.'),
}
for name, item in [('LinkPage', 'Link'), ('EventPage', 'AccessEvent'), ('AdminUserPage', 'AdminUser'),
                   ('AdminLinkPage', 'AdminLink'), ('AuditPage', 'AuditEntry')]:
    props = {'items': array(ref(item)), 'nextCursor': nullable(string())}
    if name == 'EventPage':
        props.update({'from': DT, 'to': DT, 'generatedAt': DT, 'collectionHealth': ref('CollectionHealth')})
    schemas[name] = obj(props)


def parameter(name, location, schema, required=False, description=None):
    p = {'name': name, 'in': location, 'required': required, 'schema': schema}
    if description:
        p['description'] = description
    return p


def page_params():
    return [parameter('limit', 'query', {'type': 'integer', 'minimum': 1, 'maximum': 100, 'default': 50}),
            parameter('cursor', 'query', string(maxLength=2048), description='Cursor assinado, válido por 24h e vinculado a usuário, rota, filtros e limite. Omitir filtros na continuação ou repetir os mesmos; se limit omitido, usar o valor do cursor. Cursor inválido/vencido retorna 400 invalid_cursor.')]


NO_STORE = {'Cache-Control': {'schema': string(const='no-store'), 'description': 'Não armazenar a resposta.'}}
COOKIE = {'Set-Cookie': {'schema': string(), 'description': '__Secure-refresh; Path=/api/v1/auth; HttpOnly; Secure; SameSite=Strict; host-only. Pode haver cabeçalho adicional para antiforgery. Logout expira o cookie.'}}


def response(description, model=None, headers=None):
    r = {'description': description, 'headers': {**NO_STORE, **(headers or {})}}
    if model:
        r['content'] = {'application/json': {'schema': ref(model)}}
    return r


ERROR_DESCS = {400: 'Entrada, intervalo ou cursor inválido.', 401: 'Autenticação inválida, sessão expirada/revogada ou credenciais não aceitas.',
               403: 'Papel insuficiente ou proteção CSRF/origem inválida.', 404: 'Recurso inexistente ou pertencente a outro usuário.',
               409: 'Conflito de estado ou chave idempotente com outro conteúdo.', 410: 'Link indisponível.',
               429: 'Limite de requisições excedido.', 503: 'Dependência necessária indisponível.'}


def error(code):
    r = {'description': ERROR_DESCS[code], 'headers': dict(NO_STORE),
         'content': {'application/problem+json': {'schema': ref('Problem')}}}
    if code in (429, 503):
        r['headers']['Retry-After'] = {'schema': string(pattern='^[0-9]+$'), 'description': 'Espera em segundos.'}
    if code == 401:
        r['headers']['WWW-Authenticate'] = {'schema': string(), 'description': 'Bearer em operações protegidas por JWT.'}
    return r


paths = {}


def add(path, method, op_id, summary, *, tag, model=None, status=200, body=None,
        params=None, security=None, csrf=False, errors=(400, 401, 403, 429, 503), description='', headers=None):
    op = {'operationId': op_id, 'summary': summary, 'tags': [tag],
          'responses': {str(status): response(summary, model, headers)}}
    op['responses'].update({str(c): error(c) for c in errors})
    op['responses']['default'] = {'description': 'Erro inesperado; mensagem genérica sem detalhes internos.',
                                'content': {'application/problem+json': {'schema': ref('Problem')}}}
    if description:
        op['description'] = description
    if security is not None:
        op['security'] = security
    p = list(params or [])
    if csrf:
        p.append(parameter('X-CSRF-Token', 'header', TOKEN, True,
                           'Request token emitido por GET /api/v1/auth/csrf; validar com cookie antiforgery e Origin obrigatório da gestão. Rotas auth usam contexto anônimo e não recebem Bearer.'))
    if p:
        op['parameters'] = p
    if body:
        op['requestBody'] = {'required': True, 'content': {'application/json': {'schema': ref(body)}}}
    paths.setdefault(path, {})[method] = op


add('/api/v1/auth/csrf', 'get', 'getCsrfToken', 'Obter token antiforgery', tag='Autenticação',
    model='CsrfToken', security=[], errors=(429, 503),
    headers={'Set-Cookie': {'schema': string(), 'description': 'Cookie antiforgery HttpOnly, Secure, SameSite=Strict, host-only, Path=/api/v1/auth.'}})

auth_posts = [
    ('register', 'register', 'Solicitar cadastro e verificação de e-mail', 'RegisterInput', 202, 'Message'),
    ('verify-email', 'verifyEmail', 'Confirmar e-mail com token de uso único', 'ActionTokenInput', 204, None),
    ('resend-verification', 'resendVerification', 'Reenviar confirmação com resposta genérica', 'EmailInput', 202, 'Message'),
    ('login', 'login', 'Abrir sessão de até 30 dias', 'LoginInput', 200, 'AuthResult'),
    ('forgot-password', 'forgotPassword', 'Solicitar recuperação com resposta genérica', 'EmailInput', 202, 'Message'),
    ('reset-password', 'resetPassword', 'Redefinir senha e revogar todas as sessões', 'ResetPasswordInput', 204, None),
]
for suffix, opid, summary, body, status, model in auth_posts:
    add('/api/v1/auth/' + suffix, 'post', opid, summary, tag='Autenticação', model=model, status=status,
        body=body, security=[], csrf=True, headers=COOKIE if suffix == 'login' else None,
        errors=(400, 401, 403, 429, 503) if suffix == 'login' else (400, 403, 429, 503),
        description='Regras de validade, resposta genérica e uso único conforme especificação funcional RF-01 a RF-05.')
add('/api/v1/auth/refresh', 'post', 'refreshSession', 'Rotacionar refresh token e emitir JWT', tag='Autenticação',
    model='AuthResult', security=[{'refreshCookie': []}], csrf=True, headers=COOKIE,
    description='Transação atômica; reutilização de token consumido revoga a família. Não repetir automaticamente resultado ambíguo.')
add('/api/v1/auth/logout', 'post', 'logout', 'Revogar sessão atual e remover cookie', tag='Autenticação',
    status=204, security=[{'refreshCookie': []}, {}], csrf=True, headers=COOKIE,
    errors=(403, 429, 503), description='Idempotente; retorna 204 para cookie ausente/expirado. Exige proteção CSRF mesmo sem cookie de sessão.')

add('/api/v1/me', 'get', 'getMe', 'Consultar conta autenticada', tag='Conta', model='User')
add('/api/v1/me/deletion', 'post', 'requestAccountDeletion', 'Solicitar exclusão da conta', tag='Conta',
    status=202, model='DeletionAccepted', body='DeletionInput', errors=(400, 401, 403, 409, 429, 503),
    description='Exige senha atual; gravar marcador externo durável antes de aceitar. Falha externa retorna 503. Após commit local revoga sessões e impede redirecionamentos. Limpeza inclusive filas em até 24h. Marcador externo já persistido será reconciliado mesmo se a resposta falhar.')
add('/api/v1/links', 'post', 'createLink', 'Criar endereço curto', tag='Links', status=201, model='Link', body='CreateLinkInput',
    params=[parameter('Idempotency-Key', 'header', UUID, True, 'UUID novo por ação; mesma URL após trim por 24h retorna mesmo link e 201, com estado atual; conteúdo diferente retorna 409. Após vencimento pode criar novo link.')],
    errors=(400, 401, 403, 409, 429, 503),
    headers={'Location': {'schema': string(), 'description': 'Caminho do recurso de gestão /api/v1/links/{linkId}.'}})
add('/api/v1/links', 'get', 'listLinks', 'Listar links próprios', tag='Links', model='LinkPage', params=page_params())
link_param = parameter('linkId', 'path', ID, True)
add('/api/v1/links/{linkId}', 'get', 'getLink', 'Consultar link próprio', tag='Links', model='Link', params=[link_param],
    errors=(400, 401, 403, 404, 429, 503))
add('/api/v1/links/{linkId}/deactivate', 'post', 'deactivateLink', 'Desativar definitivamente link próprio', tag='Links',
    model='Link', params=[link_param], errors=(400, 401, 403, 404, 429, 503),
    description='Idempotente. Não altera o destino nem remove histórico.')
add('/api/v1/links/{linkId}/stats', 'get', 'getLinkStats', 'Consultar totais diários UTC', tag='Relatórios', model='LinkStats',
    params=[link_param, parameter('from', 'query', DATE), parameter('to', 'query', DATE)],
    errors=(400, 401, 403, 404, 429, 503),
    description='Fornecer ambas as datas ou nenhuma. Limites inclusivos, máximo 366 dias, sem datas futuras. Padrão: últimos 30 dias incluindo hoje UTC. Dias sem eventos retornam zero.')
add('/api/v1/links/{linkId}/events', 'get', 'listLinkEvents', 'Consultar eventos individuais dos últimos 90 dias', tag='Relatórios',
    model='EventPage', params=[link_param, parameter('from', 'query', DT), parameter('to', 'query', DT), *page_params()],
    errors=(400, 401, 403, 404, 429, 503),
    description='Fornecer ambos os instantes ou nenhum. Intervalo [from,to), limitado a 90 dias até agora. Padrão congelado na primeira página e preservado no cursor. Filtro mais antigo retorna 400 retention_window; nunca retornar evento já vencido.')

add('/api/v1/admin/users', 'get', 'adminListUsers', 'Pesquisar contas para moderação', tag='Administração', model='AdminUserPage',
    params=[parameter('email', 'query', EMAIL, description='Busca exata após normalização; omitido lista contas.'), *page_params()],
    description='Exige papel admin atual no banco.')
add('/api/v1/admin/links', 'get', 'adminListLinks', 'Pesquisar links para moderação', tag='Administração', model='AdminLinkPage',
    params=[parameter('code', 'query', CODE, description='Busca exata; omitido lista links.'), *page_params()],
    description='Exige papel admin. Reservas de códigos excluídos não revelam destino ou proprietário.')
for resource, ident, schema in [('users', 'userId', UUID), ('links', 'linkId', ID)]:
    for action in ('block', 'unblock'):
        add(f'/api/v1/admin/{resource}/{{{ident}}}/{action}', 'post', f'admin{action.title()}{resource.title()}',
            f'{"Bloquear" if action == "block" else "Desbloquear"} {"conta" if resource == "users" else "link"}',
            tag='Administração', status=204, body='ModerationInput', params=[parameter(ident, 'path', schema, True)],
            errors=(400, 401, 403, 404, 409, 429, 503),
            description='Exige admin. Operação idempotente; auditar somente mudança efetiva. Não permitir bloquear a própria conta nem alterar conta em exclusão. Desbloqueio não desfaz desativação pelo proprietário.')
add('/api/v1/admin/audit', 'get', 'adminListAudit', 'Consultar auditoria administrativa', tag='Administração', model='AuditPage',
    params=page_params(), description='Exige admin. Ordem por occurredAt e id decrescentes; retenção de um ano.')

redirect_headers = {**NO_STORE, 'Location': {'schema': URI, 'description': 'Destino original validado.'},
                    'Referrer-Policy': {'schema': string(const='no-referrer')}}
for method in ('get', 'head'):
    op = {'operationId': 'redirect' if method == 'get' else 'inspectRedirect',
          'summary': 'Redirecionar e tentar registrar acesso' if method == 'get' else 'Consultar destino sem contabilizar',
          'tags': ['Redirecionamento'], 'security': [],
          'servers': [{'url': 'https://s.example', 'description': 'Domínio curto ilustrativo; substituir na implantação.'}],
          'parameters': [parameter('code', 'path', CODE, True)],
          'description': 'Sem autenticação. Query extra é ignorada. Código inválido retorna 404. Outros métodos retornam 405 com Allow: GET, HEAD. HEAD nunca retorna corpo. Falha somente na coleta não impede 302.',
          'responses': {'302': {'description': 'Destino encontrado; coleta em melhor esforço para GET.', 'headers': redirect_headers},
                        '404': error(404), '410': error(410), '503': error(503),
                        '405': {'description': 'Método não permitido.', 'headers': {'Allow': {'schema': string(const='GET, HEAD')}, **NO_STORE}}}}
    if method == 'head':
        for r in op['responses'].values():
            r.pop('content', None)
    paths.setdefault('/{code}', {})[method] = op

contract = {
    'openapi': '3.1.0',
    'info': {'title': 'Encurtador de URLs — API e redirecionamento', 'version': '1.0.0',
             'description': 'Contrato para implementação. API ASP.NET Core, interface React e contabilização sem pagamentos. IDs e contagens BIGINT são strings decimais. Rotas admin exigem papel atual admin além do JWT. Domínios .example são ilustrativos.'},
    'servers': [{'url': 'https://app.example', 'description': 'Origem comum de React e API; substituir na implantação.'}],
    'security': [{'bearerAuth': []}],
    'tags': [{'name': n} for n in ['Autenticação', 'Conta', 'Links', 'Relatórios', 'Administração', 'Redirecionamento']],
    'paths': paths,
    'components': {'securitySchemes': {
        'bearerAuth': {'type': 'http', 'scheme': 'bearer', 'bearerFormat': 'JWT', 'description': 'RS256; TTL 900 s; sessão e usuário verificados no banco.'},
        'refreshCookie': {'type': 'apiKey', 'in': 'cookie', 'name': '__Secure-refresh', 'description': 'Token opaco rotativo, sessão absoluta de 30 dias; exige antiforgery.'}},
        'schemas': schemas},
}
(OUT / 'openapi.json').write_text(json.dumps(contract, ensure_ascii=False, indent=2) + '\n', encoding='utf-8', newline='\n')
print(f'Contrato gerado: {len(paths)} caminhos; {sum(len(p) for p in paths.values())} operações; {len(schemas)} schemas.')
