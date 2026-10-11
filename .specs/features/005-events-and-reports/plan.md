# Plano técnico — 005

## Dependências

[004](../004-redirection/plan.md)

## Entrega aprovada

Um único PR draft, branch `feat/005-events-and-reports`, criado da main atualizada após integração da 004. Base funcional confirmada: `a403ce0`. Preservar F005-T01–T05; commits e evidências por tarefa dentro do mesmo PR. Não realizar merge automático.

Preparação de testes/gates pertence à T01; consolidação à T05. Interface visual pertence à 006, exclusão integral à 007 e carga global/failover/recuperação à 008.

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

Gate obrigatório >=95% de linhas e branches, sem arredondamento, por tarefa e acumulado, com arquivos novos completos e métodos compartilhados alterados completos. Manter gates da 003/004. Checkpoints de commits identificam a base de cada tarefa; medições devem ter fingerprint, SHA, ferramentas, imagens e ambiente compatíveis. Não combinar medições de implementações diferentes nem somar percentuais.

Suíte dedicada `Shortener.Events.Tests`, PostgreSQL/RabbitMQ reais e relógio controlado. Matriz executável reprova cenário ausente, falho ou ignorado. CI executa o gate da 005 e regressões; smoke obrigatório para mudanças em migrações, serviços e observabilidade.

Monitor independente no serviço Jobs, amostragem de 15 segundos e liderança por advisory lock. Observador privado do broker autorizado, sem consumo para monitorar. Fontes ausentes/reiniciadas/desatualizadas impedem declarar normalidade. Incidentes fecham após cinco minutos estáveis. Limite de reentregas da fila principal desabilitado; retry não descarta eventos válidos por número de tentativas.

Executar T10, T11, T12, T14, T15, T17, T20 nos escopos desta funcionalidade. Para regras que cruzam funcionalidades, o aceite final depende da integração correspondente. Guardar comandos, resultados e ambiente como evidência; este documento não afirma que esses testes passaram.

[Tarefas e dependências](tasks.md) · [Especificação](spec.md)
