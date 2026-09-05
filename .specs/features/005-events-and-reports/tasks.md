# Tarefas — 005

Todas as tarefas abaixo são da futura implementação e começam pendentes. Marcar conclusão apenas após entregar código/configuração e a evidência descrita. Dependências são IDs globais; `—` significa nenhuma.

| Estado | ID | Tarefa | Depende de | Evidência para concluir |
|---|---|---|---|---|
| [ ] | F005-T01 | Implementar consumo idempotente, agregação em lote e acknowledgements. | F004-T05 | T11 e T12; falha entre commit e ack não duplica totais. |
| [ ] | F005-T02 | Implementar retry transitório e quarentena com identidade preservada. | F005-T01 | Falhas transitórias pausam sem descartar lote; mensagem inválida não gera total. |
| [ ] | F005-T03 | Implementar API de estatísticas/eventos e autorização por proprietário. | F005-T02 | T17/T20 e AF-07/13; intervalos UTC, dias zero e precisão corretos. |
| [ ] | F005-T04 | Implementar monitor da coleta, incidentes e indicação de atraso/desconhecimento. | F005-T03 | T14; fila vazia sem tráfego não implica atraso e último evento não prova completude. |
| [ ] | F005-T05 | Implementar partições e retenção horária; verificar replay e integração dos relatórios. | F005-T04 | T15 e AF-04/06/07/13; replay após 90 dias não incrementa agregados. |

[Plano](plan.md) · [Especificação](spec.md) · [Índice de funcionalidades](../../README.md)
