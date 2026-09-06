# Tarefas — 001

Todas as tarefas abaixo são da futura implementação e começam pendentes. Marcar conclusão apenas após entregar código/configuração e a evidência descrita. Dependências são IDs globais; `—` significa nenhuma.

| Estado | ID | Tarefa | Depende de | Evidência para concluir |
|---|---|---|---|---|
| [ ] | F001-T01 | Criar solução .NET 10 e projetos de API, redirecionador, worker, jobs e testes. | — | Build com SDK 10 e referências de projeto sem ciclos. |
| [ ] | F001-T02 | Preparar frontend React/TypeScript e convenções de tipos do contrato. | F001-T01 | Frontend compila e preserva IDs/contagens como strings. |
| [ ] | F001-T03 | Preparar serviços locais PostgreSQL, RabbitMQ, captura de e-mail e configuração do registro externo. | F001-T01 | Inicialização repetível; configuração ausente produz erro claro; segredos não versionados. |
| [ ] | F001-T04 | Preparar migrações compartilhadas, relógio injetável, serialização e validação do contrato. | F001-T03 | Migração em banco vazio e integração T20 com valores acima de 2^53. |
| [ ] | F001-T05 | Documentar e verificar bootstrap de desenvolvimento e comandos de validação. | F001-T02, F001-T04, F001-T06, F001-T07 | README de execução conferido em ambiente limpo; serviços internos acessíveis. |
| [ ] | F001-T06 | Implementar ILogger JSON, OTLP, Collector, Loki, Prometheus e Grafana com configuração versionada. | F001-T04 | Logs dos quatro serviços pesquisáveis; campos sensíveis ausentes; falha do exportador não bloqueia requisições. |
| [ ] | F001-T07 | Preparar CI, template de PR e fluxo de aprovação por funcionalidade. | F001-T01 | Builds, testes, contrato e proteção da main documentados; PR revisável sem merge automático. |

[Plano](plan.md) · [Especificação](spec.md) · [Índice de funcionalidades](../../README.md)
