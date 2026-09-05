# Plano técnico — 005

## Dependências

[004](../004-redirection/plan.md)

## Componentes e dados

Worker, agregação transacional, API de relatórios, monitor de coleta e jobs de retenção.

access_events particionados, daily_totals em 16 faixas e collection_incidents; deduplicação por occurred_at/event_id.

As invariantes de dados, segurança, falha e concorrência estão em [architecture.md](../../architecture.md); não alterar essas decisões por conveniência de implementação.

## Interfaces

| Operação | Método e rota |
|---|---|
| `getLinkStats` | `GET /api/v1/links/{linkId}/stats` |
| `listLinkEvents` | `GET /api/v1/links/{linkId}/events` |

Requests, responses, erros e segurança: [OpenAPI](../../contracts/openapi.json). Mudanças de comportamento exigem atualização coordenada da especificação e do gerador.

## Sequência de trabalho

1. Implementar consumo em lote, validação, ack após commit e quarentena confirmada.
2. Inserir eventos e atualizar agregados apenas para linhas novas, na mesma transação.
3. Implementar consultas, cursor/retention window e saúde conservadora sem watermark falso.
4. Implementar partições futuras, expurgo e descarte de replay vencido sem alterar totais.

## Testes e conclusão

Executar T10, T11, T12, T14, T15, T17, T20 nos escopos desta funcionalidade. Para regras que cruzam funcionalidades, o aceite final depende da integração correspondente. Guardar comandos, resultados e ambiente como evidência; este documento não afirma que esses testes passaram.

[Tarefas e dependências](tasks.md) · [Especificação](spec.md)
