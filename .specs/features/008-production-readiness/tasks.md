# Tarefas — 008

Todas as tarefas abaixo são da futura implementação e começam pendentes. Marcar conclusão apenas após entregar código/configuração e a evidência descrita. Dependências são IDs globais; `—` significa nenhuma.

| Estado | ID | Tarefa | Depende de | Evidência para concluir |
|---|---|---|---|---|
| [ ] | F008-T01 | Preparar topologia, manifesto de execução, métricas, alertas e limites de armazenamento. | F005-T05, F007-T05 | Configuração inventariada; alertas de latência/fila/retenção/exclusão testados. |
| [ ] | F008-T02 | Configurar backup/WAL e ensaio isolado de restauração com escritor único. | F008-T01 | T19/T21/T22/T24; RPO/RTO medidos, nenhuma reutilização de código nem conta ressuscitada. |
| [ ] | F008-T03 | Executar carga uniforme e link quente com um milhão de links. | F008-T02 | Dois relatórios de 1.000 GET/s por 15 min, p95 < 200 ms e drenagem <= 60 s no ensaio saudável. |
| [ ] | F008-T04 | Executar ensaios de falha do banco, broker, worker e saneamento. | F008-T03 | T13/T14/T23; 302 continua em falha de coleta, 503 explícito em falha de resolução. |
| [ ] | F008-T05 | Consolidar matriz de aceite, migração aditiva, smoke test e rollback de imagem. | F008-T04 | T01–T24 e AF-01–AF-16 com evidências; nenhuma meta tratada como medição sem ensaio. |

[Plano](plan.md) · [Especificação](spec.md) · [Índice de funcionalidades](../../README.md)
