# Tarefas — 006

Todas as tarefas abaixo são da futura implementação e começam pendentes. Marcar conclusão apenas após entregar código/configuração e a evidência descrita. Dependências são IDs globais; `—` significa nenhuma.

| Estado | ID | Tarefa | Depende de | Evidência para concluir |
|---|---|---|---|---|
| [ ] | F006-T01 | Implementar cliente HTTP e sessão em memória com refresh serializado entre abas. | F002-T05, F003-T05, F005-T05 | T06/T20; não persistir JWT em storage e não repetir refresh ambíguo. |
| [ ] | F006-T02 | Implementar telas de cadastro, login, verificação e recuperação. | F006-T01 | AF-15/16; scanner GET não confirma token e mensagens não enumeram contas. |
| [ ] | F006-T03 | Implementar criação/cópia/listagem/detalhe/desativação de links. | F006-T02 | AF-01/02/03; retry técnico mantém chave e operações destrutivas exigem ação explícita. |
| [ ] | F006-T04 | Implementar filtros, paginação, totais/eventos e mensagens de coleta. | F006-T03 | AF-13 e T17; falha de consulta não aparece como total zero. |
| [ ] | F006-T05 | Revisar teclado, celular, erros de cópia, renderização segura e composição React. | F006-T04 | T18 e AF-16; revisão das três skills com achados resolvidos ou justificados. |

[Plano](plan.md) · [Especificação](spec.md) · [Índice de funcionalidades](../../README.md)
