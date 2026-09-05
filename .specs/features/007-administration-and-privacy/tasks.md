# Tarefas — 007

Todas as tarefas abaixo são da futura implementação e começam pendentes. Marcar conclusão apenas após entregar código/configuração e a evidência descrita. Dependências são IDs globais; `—` significa nenhuma.

| Estado | ID | Tarefa | Depende de | Evidência para concluir |
|---|---|---|---|---|
| [ ] | F007-T01 | Implementar pesquisa, block/unblock e auditoria administrativos. | F006-T05 | AF-14 e T16; repetir ação sem mudança não duplica auditoria. |
| [ ] | F007-T02 | Implementar pedido idempotente de exclusão com marcador externo anterior ao aceite. | F007-T01 | T23; falha externa retorna 503 e resultado ambíguo conserva a decisão durável. |
| [ ] | F007-T03 | Implementar reconciliador, limpeza em lotes e proteção contra reinserção por consumidores. | F007-T02 | T16/T23 e AF-09; prazo não reinicia e links permanecem reservados sem dados pessoais. |
| [ ] | F007-T04 | Implementar saneamento de fila/quarentena e anonimização da auditoria. | F007-T03 | T23; envelopes alheios preservados e nenhuma purga geral da fila. |
| [ ] | F007-T05 | Implementar telas de administração/exclusão e validar concorrência e revogação. | F007-T04 | AF-08/09/14; evidência de limpeza em 24 h em ensaio saudável, sem alegação de cumprimento em falha. |

[Plano](plan.md) · [Especificação](spec.md) · [Índice de funcionalidades](../../README.md)
