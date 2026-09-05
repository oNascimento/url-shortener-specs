# Plano técnico — 007

## Dependências

[006](../006-web-dashboard/plan.md)

## Componentes e dados

API e telas administrativas, auditoria, registro externo de exclusões, reconciliador e saneamento coordenado.

admin_audit, deletion_jobs, marcador externo e links tombstonados; exclusão abrange sessões, eventos, agregados e envelopes.

As invariantes de dados, segurança, falha e concorrência estão em [architecture.md](../../architecture.md); não alterar essas decisões por conveniência de implementação.

## Interfaces

| Operação | Método e rota |
|---|---|
| `requestAccountDeletion` | `POST /api/v1/me/deletion` |
| `adminListUsers` | `GET /api/v1/admin/users` |
| `adminListLinks` | `GET /api/v1/admin/links` |
| `adminBlockUsers` | `POST /api/v1/admin/users/{userId}/block` |
| `adminUnblockUsers` | `POST /api/v1/admin/users/{userId}/unblock` |
| `adminBlockLinks` | `POST /api/v1/admin/links/{linkId}/block` |
| `adminUnblockLinks` | `POST /api/v1/admin/links/{linkId}/unblock` |
| `adminListAudit` | `GET /api/v1/admin/audit` |

Requests, responses, erros e segurança: [OpenAPI](../../contracts/openapi.json). Mudanças de comportamento exigem atualização coordenada da especificação e do gerador.

## Sequência de trabalho

1. Implementar autorização administrativa atual e mudanças de estado auditadas.
2. Implementar exclusão com confirmação de senha, marcador externo durável e resposta somente após commit local.
3. Implementar reconciliação, eliminação em lotes e bloqueios compartilhados com o worker.
4. Implementar interfaces e saneamento de filas com confirmação antes de ack, cumprindo o runbook sem purga geral.

## Testes e conclusão

Executar T16, T19, T23, T24 nos escopos desta funcionalidade. Para regras que cruzam funcionalidades, o aceite final depende da integração correspondente. Guardar comandos, resultados e ambiente como evidência; este documento não afirma que esses testes passaram.

[Tarefas e dependências](tasks.md) · [Especificação](spec.md)
