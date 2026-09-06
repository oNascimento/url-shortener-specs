---
name: dotnet-service-change
description: Orienta alterações nos serviços .NET, domínio, persistência, contratos, mensageria e testes do encurtador.
---

# Alteração em serviços .NET

Use esta skill para mudanças em `src/Shortener.Api`, `Shortener.Application`, `Shortener.Domain`, `Shortener.Infrastructure`, `Shortener.Jobs`, `Shortener.Redirector`, `Shortener.ServiceDefaults` ou `Shortener.Worker`.

## Antes de editar

- Localize a funcionalidade e a tarefa `Fxxx-Txx` correspondente.
- Leia os contratos e os tipos existentes antes de criar uma nova abstração.
- Confirme se a mudança altera schema, migração, endpoint, evento, retry ou observabilidade.
- Identifique o teste direcionado mais barato que possa detectar uma regressão.

## Implementação

- Preserve separação entre domínio, aplicação, infraestrutura e composição dos hosts.
- Use tipos existentes e evite `any`, casts silenciosos e perda de precisão numérica.
- Mantenha Problem Details públicos sem dados sensíveis.
- Não registre exceções brutas, corpos, headers, SQL, credenciais ou URLs privadas.
- Para persistência, mantenha migrações e índices alinhados ao modelo da funcionalidade.
- Para mensageria e jobs, preserve idempotência, limites, retry e comportamento de falha documentados.
- Não faça telemetria participar da transação de negócio.

## Verificação

Execute primeiro o teste afetado ou o projeto relacionado. Depois, quando aplicável:

```sh
rtk err dotnet build --no-restore -c Release
rtk test dotnet test --no-build -c Release
```

Se o contrato, migração ou documentação foi alterado, execute também os validadores definidos em `.agent/rules/20-validation-evidence.md`.
