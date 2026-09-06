---
name: spec-driven-delivery
description: Executa mudanças guiadas pelas especificações, mantendo rastreabilidade entre requisitos, planos, tarefas, contratos, testes e evidências.
---

# Entrega orientada por especificação

Use esta skill para implementar, revisar ou planejar uma funcionalidade do encurtador.

## Procedimento

1. Leia `.specs/README.md` e identifique a funcionalidade relevante.
2. Localize o requisito, critério de aceitação, cenário de teste e entrada correspondente em `.specs/traceability.md`.
3. Leia `.specs/product.md` antes de decisões de produto e `.specs/architecture.md` antes de decisões técnicas.
4. Consulte o `plan.md` e `tasks.md` da funcionalidade. Preserve IDs `Fxxx-Txx` e não marque tarefas sem evidência.
5. Verifique o contrato em `.specs/contracts/openapi.json` antes de alterar APIs ou tipos.
6. Formule uma hipótese local sobre a mudança e identifique um teste ou validação que possa falsificá-la.
7. Faça a menor alteração necessária, mantendo a rastreabilidade no código, testes ou documentação apropriada.
8. Execute validação direcionada e depois os validadores documentais quando o contrato ou especificações forem afetados.
9. Relate comportamento implementado, evidência, limitações e itens ainda não verificados.

## Regras de decisão

- Não implemente uma interpretação que contradiga produto, arquitetura ou contrato.
- Não confunda texto de planejamento com capacidade disponível no sistema.
- Se a especificação estiver inconsistente, pare no ponto de conflito e registre a decisão necessária em vez de inventar comportamento.
