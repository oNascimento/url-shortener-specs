# Especificações do projeto

Fonte de verdade para a futura implementação do encurtador no mesmo repositório. Documentação reorganizada a partir do commit `fb1a84ae4850a2520ae545092202955023dfd1c9`, preservando as regras aprovadas e explicitando .NET 10.

## Documentos compartilhados

- [Produto, perfis, limites e aceitação](product.md)
- [Arquitetura e modelo de dados](architecture.md)
- [Operação, recuperação e 24 cenários de teste](operations.md)
- [Rastreabilidade de requisitos, aceites, cenários e operações](traceability.md)
- [Contrato OpenAPI 3.1](contracts/openapi.json)

## Funcionalidades e dependências

| ID | Funcionalidade | Dependências | Plano | Tarefas |
|---|---|---|---|---|
| 001 | [Fundação e ambiente](features/001-foundation/spec.md) | — | [Plano](features/001-foundation/plan.md) | [Tarefas](features/001-foundation/tasks.md) |
| 002 | [Autenticação e sessões](features/002-authentication/spec.md) | 001 | [Plano](features/002-authentication/plan.md) | [Tarefas](features/002-authentication/tasks.md) |
| 003 | [Gestão de links e Base62](features/003-link-management/spec.md) | 002 | [Plano](features/003-link-management/plan.md) | [Tarefas](features/003-link-management/tasks.md) |
| 004 | [Redirecionamento e captura](features/004-redirection/spec.md) | 003 | [Plano](features/004-redirection/plan.md) | [Tarefas](features/004-redirection/tasks.md) |
| 005 | [Eventos e relatórios](features/005-events-and-reports/spec.md) | 004 | [Plano](features/005-events-and-reports/plan.md) | [Tarefas](features/005-events-and-reports/tasks.md) |
| 006 | [Interface web do usuário](features/006-web-dashboard/spec.md) | 002, 003, 005 | [Plano](features/006-web-dashboard/plan.md) | [Tarefas](features/006-web-dashboard/tasks.md) |
| 007 | [Administração e privacidade](features/007-administration-and-privacy/spec.md) | 006 | [Plano](features/007-administration-and-privacy/plan.md) | [Tarefas](features/007-administration-and-privacy/tasks.md) |
| 008 | [Preparação para produção](features/008-production-readiness/spec.md) | 005, 007 | [Plano](features/008-production-readiness/plan.md) | [Tarefas](features/008-production-readiness/tasks.md) |

## Como trabalhar

1. Ler produto e arquitetura, depois spec.md da funcionalidade.
2. Seguir plan.md e as dependências em tasks.md.
3. Manter IDs existentes; acrescentar novos IDs sem renumerar os já publicados.
4. Atualizar comportamento, contrato, matriz e testes na mesma mudança quando necessário.
5. Concluir tarefa somente com código/configuração e evidência; revisar o aceite integrado quando houver dependências.

## Validação documental

Na raiz do repositório:

```sh
python scripts/build_contract.py
python scripts/validate_deliverables.py
```

O contrato é gerado por scripts/build_contract.py. O validador confere estrutura, referências, rastreabilidade e dependências sem executar testes da aplicação. As metas de operação permanecem futuras.
