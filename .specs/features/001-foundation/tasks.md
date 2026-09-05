# Tarefas — 001

Todas as tarefas abaixo são da futura implementação e começam pendentes. Marcar conclusão apenas após entregar código/configuração e a evidência descrita. Dependências são IDs globais; `—` significa nenhuma.

| Estado | ID | Tarefa | Depende de | Evidência para concluir |
|---|---|---|---|---|
| [ ] | F001-T01 | Criar solução .NET 10 e projetos de API, redirecionador, worker, jobs e testes. | — | Build com SDK 10 e referências de projeto sem ciclos. |
| [ ] | F001-T02 | Preparar frontend React/TypeScript e convenções de tipos do contrato. | F001-T01 | Frontend compila e preserva IDs/contagens como strings. |
| [ ] | F001-T03 | Preparar serviços locais PostgreSQL, RabbitMQ, captura de e-mail e configuração do registro externo. | F001-T01 | Inicialização repetível; configuração ausente produz erro claro; segredos não versionados. |
| [ ] | F001-T04 | Preparar migrações compartilhadas, relógio injetável, serialização e validação do contrato. | F001-T03 | Migração em banco vazio e integração T20 com valores acima de 2^53. |
| [ ] | F001-T05 | Documentar e verificar bootstrap de desenvolvimento e comandos de validação. | F001-T02, F001-T04 | README de execução conferido em ambiente limpo; serviços internos acessíveis. |

[Plano](plan.md) · [Especificação](spec.md) · [Índice de funcionalidades](../../README.md)
