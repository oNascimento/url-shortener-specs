# Plano técnico — 006

## Dependências

[002](../002-authentication/plan.md), [003](../003-link-management/plan.md), [005](../005-events-and-reports/plan.md)

## Componentes e dados

Frontend React/TypeScript, cliente HTTP, estado de sessão em memória e componentes de formulário/listagem/relatórios.

Somente modelos do contrato no cliente; JWT em memória; contagens e IDs permanecem strings.

As invariantes de dados, segurança, falha e concorrência estão em [architecture.md](../../architecture.md); não alterar essas decisões por conveniência de implementação.

## Interfaces

Nenhuma nova operação HTTP. Consumir os contratos das funcionalidades 002, 003 e 005; telas administrativas pertencem à 007.

Requests, responses, erros e segurança: [OpenAPI](../../contracts/openapi.json). Mudanças de comportamento exigem atualização coordenada da especificação e do gerador.

## Sequência de trabalho

1. Implementar cliente de autenticação e coordenação de refresh entre abas.
2. Construir cadastro/login/verificação/recuperação integrados aos contratos existentes.
3. Construir criação, cópia, listagem, desativação, filtros e relatórios sem refazer regras de negócio no navegador.
4. Revisar acessibilidade, composição e desempenho com as skills aprovadas, aplicando apenas regras compatíveis com React + API .NET.

## Testes e conclusão

Executar T06, T17, T18, T20 nos escopos desta funcionalidade. Para regras que cruzam funcionalidades, o aceite final depende da integração correspondente. Guardar comandos, resultados e ambiente como evidência; este documento não afirma que esses testes passaram.

[Tarefas e dependências](tasks.md) · [Especificação](spec.md)
