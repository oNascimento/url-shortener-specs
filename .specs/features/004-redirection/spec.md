# 004 — Redirecionamento e captura

**Estado:** especificado; implementação pendente.

## Objetivo

Resolver códigos públicos e redirecionar, tentando capturar cada GET elegível sem depender da disponibilidade da coleta.

## Regras e limites

Aplicam-se os limites e perfis de [product.md](../../product.md). O contrato e as regras técnicas compartilhadas são normativos; esta funcionalidade não acrescenta pagamentos ou elegibilidade financeira.

<a id="rf-19"></a>

**RF-19 — Redirecionamento:** GET válido de link disponível tenta registrar evento e retorna 302 com o destino em Location e `Cache-Control: no-store`. Não há tela intermediária, anúncio nem autenticação do visitante.

<a id="rf-20"></a>

**RF-20 — Falhas e métodos:** HEAD resolve o destino sem criar evento. Código ausente/inválido retorna 404; link indisponível por desativação, bloqueio ou exclusão retorna 410, sem revelar o motivo interno. Métodos diferentes de GET e HEAD retornam 405 com `Allow: GET, HEAD`. Falha na resolução do destino retorna 503; falha apenas na coleta não impede o 302.

<a id="rf-21"></a>

**RF-21 — Contagem:** cada GET elegível tem evento distinto. Reentrega técnica do mesmo evento não duplica a contagem. Consultas inválidas, HEAD, 404, 410, 405 e erros anteriores à resolução não contam. Uma interrupção após a geração do evento pode contar mesmo sem o visitante receber a resposta; não há garantia de “redirecionamento concluído”.

## Aceitação

Critérios relacionados: AF-04, AF-05, AF-06, AF-10, AF-11, AF-12. Consulte as definições em [product.md](../../product.md).

Cenários relacionados: T09, T10, T13. Definições e metas em [operations.md](../../operations.md).

[Plano técnico](plan.md) · [Tarefas](tasks.md) · [Rastreabilidade](../../traceability.md)
