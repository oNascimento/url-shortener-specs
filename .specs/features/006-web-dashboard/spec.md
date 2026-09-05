# 006 — Interface web do usuário

**Estado:** especificado; implementação pendente.

## Objetivo

Entregar os fluxos React de autenticação, gestão e consulta com acessibilidade e tratamento fiel dos estados da API.

## Regras e limites

Aplicam-se os limites e perfis de [product.md](../../product.md). O contrato e as regras técnicas compartilhadas são normativos; esta funcionalidade não acrescenta pagamentos ou elegibilidade financeira.

<a id="rf-23"></a>

**RF-23 — Experiência:** interface em português, responsiva em celular e desktop, navegação por teclado, campos com rótulos, foco visível e mensagens de erro associadas aos campos. Estados não dependem apenas de cor. Endereços e valores fornecidos pelo usuário são exibidos como texto, sem execução de HTML.

Em limitação de frequência, apresentar o tempo de espera indicado pela API e preservar os campos. Erros de autorização, validação e indisponibilidade têm mensagens distintas, sem detalhes internos. Na falha da API, permitir tentativa manual; não repetir silenciosamente operações de desativação, exclusão ou moderação. IPs e URLs completos não aparecem em notificações fora do painel autenticado.

As jornadas de autenticação, links e relatórios são definidas respectivamente em [002](../002-authentication/spec.md), [003](../003-link-management/spec.md) e [005](../005-events-and-reports/spec.md). A interface implementa essas jornadas; não mantém uma segunda versão das regras.

## Aceitação

Critérios relacionados: AF-01, AF-02, AF-03, AF-13, AF-15, AF-16. Consulte as definições em [product.md](../../product.md).

Cenários relacionados: T06, T17, T18, T20. Definições e metas em [operations.md](../../operations.md).

[Plano técnico](plan.md) · [Tarefas](tasks.md) · [Rastreabilidade](../../traceability.md)
