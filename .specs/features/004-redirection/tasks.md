# Tarefas — 004

Todas as tarefas abaixo são da futura implementação e começam pendentes. Marcar conclusão apenas após entregar código/configuração e a evidência descrita. Dependências são IDs globais; `—` significa nenhuma.

| Estado | ID | Tarefa | Depende de | Evidência para concluir |
|---|---|---|---|---|
| [ ] | F004-T01 | Implementar GET/HEAD e resolução pública dos códigos. | F003-T05 | T09 e AF-10/11; status e Location corretos, sem eventos em HEAD. |
| [ ] | F004-T02 | Implementar captura de UTC/IP e construção do envelope. | F004-T01 | T10; não aceitar X-Forwarded-For de remetente não confiável. |
| [ ] | F004-T03 | Implementar publicação persistente mandatory com confirms e limite de 100 ms. | F004-T02 | T13; timeout/nack/unroutable não impedem 302 quando o destino foi resolvido. |
| [ ] | F004-T04 | Implementar métricas de elegibilidade e falhas de coleta/resolução. | F004-T03 | Resultados confirmados, rejeitados e desconhecidos separados, sem IP/URL nos logs. |
| [ ] | F004-T05 | Validar redirecionamento ponta a ponta com destino local de captura. | F004-T04 | AF-05/06/10/12; nenhuma requisição aos destinos durante criação ou validação. |

[Plano](plan.md) · [Especificação](spec.md) · [Índice de funcionalidades](../../README.md)
