# Plano técnico — 003

## Dependências

[002](../002-authentication/plan.md)

## Componentes e dados

API de links, validação de URL, alocador de IDs, codificador Base62 e paginação por cursor.

links e creation_requests; sequência BIGINT com reserva externa de faixas e códigos nunca reutilizados.

As invariantes de dados, segurança, falha e concorrência estão em [architecture.md](../../architecture.md); não alterar essas decisões por conveniência de implementação.

## Interfaces

| Operação | Método e rota |
|---|---|
| `createLink` | `POST /api/v1/links` |
| `listLinks` | `GET /api/v1/links` |
| `getLink` | `GET /api/v1/links/{linkId}` |
| `deactivateLink` | `POST /api/v1/links/{linkId}/deactivate` |

Requests, responses, erros e segurança: [OpenAPI](../../contracts/openapi.json). Mudanças de comportamento exigem atualização coordenada da especificação e do gerador.

## Sequência de trabalho

1. Implementar Base62 com o alfabeto congelado e as faixas de emissão definidas em operations.md.
2. Implementar validação e persistência do destino sem fetch, mantendo a string após trim.
3. Implementar idempotência transacional de criação e expiração atômica das chaves.
4. Implementar autorização por proprietário, listagem estável e desativação definitiva.

## Testes e conclusão

Implementação backend entregue em Domain (Base62/URL), Infrastructure (persistência SQL, cursor, sequência e verificação do escritor), API (quatro rotas existentes) e Jobs (suspensão/preparação da alocação após restore). Testes usam PostgreSQL/registro separados e autenticação real. Os gates de requisitos e cobertura exata estão na CI `link-management`; comandos, medições, referências e dependências de aceite estão em [evidence.md](evidence.md).

Executar T01, T02, T03, T08, T17, T20, T21, T22 nos escopos desta funcionalidade. Para regras que cruzam funcionalidades, o aceite final depende da integração correspondente. Guardar comandos, resultados e ambiente como evidência; este documento não afirma que esses testes passaram.

[Tarefas e dependências](tasks.md) · [Especificação](spec.md)
