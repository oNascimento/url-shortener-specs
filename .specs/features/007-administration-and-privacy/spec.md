# 007 — Administração e privacidade

**Estado:** especificado; implementação pendente.

## Objetivo

Disponibilizar moderação auditável e exclusão durável de contas e dados sem reintrodução por filas ou restauração.

## Regras e limites

Aplicam-se os limites e perfis de [product.md](../../product.md). O contrato e as regras técnicas compartilhadas são normativos; esta funcionalidade não acrescenta pagamentos ou elegibilidade financeira.

<a id="rf-15"></a>

**RF-15 — Administração:** busca paginada por e-mail exato de conta ou código exato de link; visualizar estado, proprietário e destino para moderação. Bloquear/desbloquear exige motivo de 10 a 1.000 caracteres. Auditoria guarda ator, alvo, ação, motivo e instante. Não é permitido bloquear a própria conta administrativa pela interface.

O bloqueio administrativo de link é reversível, mas desbloquear não reativa link desativado pelo proprietário. Bloqueio de conta revoga suas sessões; desbloqueio permite novo login e volta a disponibilizar apenas links que não tenham outro impedimento.

<a id="rf-16"></a>

**RF-16 — Exclusão de conta:** solicitar senha atual e confirmação explícita. Bloquear imediatamente a conta e todos os links; agendar eliminação de dados em até 24 horas. A tela explica que cópias de segurança expiram em até 14 dias e não retornam ao serviço sem reaplicação das exclusões. Códigos permanecem reservados, sem URL de destino ou identificação do titular após a limpeza.

| Situação do link | Exibição no painel | Resposta pública |
|---|---|---|
| Conta disponível, sem bloqueio ou desativação | Ativo | GET e HEAD: 302; somente GET conta. |
| Proprietário desativou, com ou sem bloqueio administrativo | Desativado | 410; nenhuma nova visita elegível. |
| Bloqueio administrativo do link ou da conta, sem desativação | Bloqueado | 410; o motivo interno não aparece ao visitante. |
| Exclusão de conta solicitada ou concluída | Conta sem acesso ao painel | 410 para código reservado. |

A indisponibilidade vale para resoluções iniciadas após a confirmação da alteração. Requisições já resolvidas e eventos gerados antes de um bloqueio podem terminar e aparecer depois no relatório. A exclusão de conta também impede a persistência posterior de seus eventos pendentes. Ações administrativas repetidas sem mudança de estado não duplicam a auditoria; alvos em exclusão não podem ser desbloqueados.

## Aceitação

Critérios relacionados: AF-08, AF-09, AF-14. Consulte as definições em [product.md](../../product.md).

Cenários relacionados: T16, T19, T23, T24. Definições e metas em [operations.md](../../operations.md).

[Plano técnico](plan.md) · [Tarefas](tasks.md) · [Rastreabilidade](../../traceability.md)
