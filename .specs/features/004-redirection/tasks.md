# Tarefas — 004

Sete tarefas e sete PRs encadeados. Estado formal exige dependências concluídas; implementação verificada consta dos parciais. Gates funcionais: >=95% linhas e branches acumulados e da tarefa, mais todos os cenários obrigatérios. Cada entrega inclui testes e evidência. é significa nenhuma dependência.

| Estado | ID | Tarefa | Depende de | Evidência para concluir |
|---|---|---|---|---|
| [ ] | F004-T00 | Registrar plano, separar gates 003/004, preparar matriz e CI por tarefa. | — | Gates negativos, base encadeada, limiar exato, propriedade, provenance, validação documental e parcial T00. |
| [ ] | F004-T01 | Implementar GET/HEAD, resolução consistente, métodos/status/headers e isolamento de autenticação. | F003-T05, F004-T00 | T09 e AF-10/11; todos os estados, falhas 503, Location correto, HEAD sem corpo/publicação; parcial T01 e cobertura. |
| [ ] | F004-T02 | Implementar captura UTC/IP confiável e envelope imutável. | F004-T01 | T10; IPv4/IPv6/mapped, proxies/hops, spoofing, IP ausente e dez identidades distintas; parcial T02 e cobertura. |
| [ ] | F004-T03 | Publicar com persistência, mandatory/confirms, capacidade limitada e orçamento total 100 ms. | F004-T02 | T13; confirms/nack/unroutable/timeout, saturação/recuperação, 302 preservado e Unknown distinto; parcial T03 e cobertura. |
| [ ] | F004-T04 | Instrumentar elegibilidade, falhas e disponibilidade independente da coleta. | F004-T03 | Métricas confirmadas/rejeitadas/desconhecidas, duração, readiness e logs/labels sem dados sensíveis; parcial T04 e cobertura. |
| [ ] | F004-T05 | Validar API, proxy, redirecionador, RabbitMQ e destino local ponta a ponta. | F004-T04 | AF-05/06/10/11/12 e T09/T10/T13 no escopo 004; três nós quorum e dois redirecionadores, nenhum fetch pelo serviço; parcial T05 e cobertura final. |
| [ ] | F004-T06 | Auditar parciais/PRs e consolidar evidence.md geral. | F004-T05 | Sete parciais/PRs, SHA/provenance/CI verificáveis, matriz e cobertura final sem somar percentuais; limitações 005/006/008 explícitas. |

[Plano](plan.md) · [Especificação](spec.md) · [Índice](../../README.md)
