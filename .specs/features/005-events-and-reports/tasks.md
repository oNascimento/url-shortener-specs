# Tarefas — 005

Entrega em um único PR, com checkpoints e evidências por tarefa. Marcar conclusão apenas após entregar código/configuração e a evidência descrita. Dependências são IDs globais; `—` significa nenhuma. A 004 está integrada na main `a403ce0`; seus checkboxes históricos não são alterados por esta entrega.

| Estado | ID | Tarefa | Depende de | Evidência para concluir |
|---|---|---|---|---|
| [x] | F005-T01 | Implementar consumo idempotente, agregação em lote e acknowledgements. | F004-T05 | [Evidência T01](evidence/F005-T01.md): T11/T12 e gates aprovados. |
| [ ] | F005-T02 | Implementar retry transitório e quarentena com identidade preservada. | F005-T01 | Falhas transitórias pausam sem descartar lote; mensagem inválida não gera total. |
| [ ] | F005-T03 | Implementar API de estatísticas/eventos e autorização por proprietário. | F005-T02 | T17/T20 e AF-07/13; intervalos UTC, dias zero e precisão corretos. |
| [ ] | F005-T04 | Implementar monitor da coleta, incidentes e indicação de atraso/desconhecimento. | F005-T03 | T14; fila vazia sem tráfego não implica atraso e último evento não prova completude. |
| [ ] | F005-T05 | Implementar partições e retenção horária; verificar replay e integração dos relatórios. | F005-T04 | T15 e AF-04/06/07/13; replay após 90 dias não incrementa agregados. |

[Plano](plan.md) · [Especificação](spec.md) · [Índice de funcionalidades](../../README.md)
