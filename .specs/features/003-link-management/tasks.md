# Tarefas — 003

O backend das tarefas abaixo foi implementado e testado; resultados e limites estão em [evidence.md](evidence.md). Os checkboxes históricos respeitam as dependências globais F001/F002 e não representam falta de código nesta entrega. Marcar conclusão formal somente após reconciliar essas dependências e a evidência exigida. `—` significa nenhuma dependência.

| Estado | ID | Tarefa | Depende de | Evidência para concluir |
|---|---|---|---|---|
| [ ] | F003-T01 | Implementar sequência, reserva externa confirmada e conversão Base62. | F002-T05 | T01, T21 e T22; nenhum overflow, reciclagem ou emissão fora da faixa autorizada. |
| [ ] | F003-T02 | Implementar validação de destino e criação transacional idempotente. | F003-T01 | T02, T03 e T08; concorrência sem duplicação e preservação exata do destino. |
| [ ] | F003-T03 | Implementar listagem e consulta por proprietário com cursor assinado. | F003-T02 | AF-02 e T17; outro proprietário recebe 404; filtros/cursor incompatíveis são rejeitados. |
| [ ] | F003-T04 | Implementar desativação e precedência dos estados do link. | F003-T03 | AF-05 no escopo de gestão; operação repetida não reativa nem altera destino. |
| [ ] | F003-T05 | Validar contratos, precisão numérica e integração da gestão. | F003-T04 | T20 e AF-03/11/12; contrato reproduzível e IDs acima de 2^53 preservados. |

[Plano](plan.md) · [Especificação](spec.md) · [Índice de funcionalidades](../../README.md)
