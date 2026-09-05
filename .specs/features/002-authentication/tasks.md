# Tarefas — 002

Todas as tarefas abaixo são da futura implementação e começam pendentes. Marcar conclusão apenas após entregar código/configuração e a evidência descrita. Dependências são IDs globais; `—` significa nenhuma.

| Estado | ID | Tarefa | Depende de | Evidência para concluir |
|---|---|---|---|---|
| [ ] | F002-T01 | Implementar contas Identity, cadastro, verificação e recuperação por e-mail. | F001-T05 | T04 e AF-15; expiração/uso único; GET de scanner não consome token. |
| [ ] | F002-T02 | Implementar login, emissão/validação JWT, sessão e GET /me. | F002-T01 | T05; assinatura, algoritmo, emissor, audiência e sessão inválidos são rejeitados. |
| [ ] | F002-T03 | Implementar antiforgery, cookie de refresh e validação de Origin. | F002-T02 | T07; login, refresh e logout rejeitam CSRF e origem inválidos. |
| [ ] | F002-T04 | Implementar rotação atômica, logout, reset com revogação e limites de frequência. | F002-T03 | T06; reuso revoga família e logout invalida JWT correspondente. |
| [ ] | F002-T05 | Verificar contratos de autenticação e transição de chaves. | F002-T04 | AF-08 no escopo de sessão/reset; T24 para transição de chave; nenhuma credencial em logs. |

[Plano](plan.md) · [Especificação](spec.md) · [Índice de funcionalidades](../../README.md)
