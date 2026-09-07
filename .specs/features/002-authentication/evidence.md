# Evidências — complemento da autenticação

PR: [#5](https://github.com/oNascimento/url-shortener-specs/pull/5). O PR permanece sujeito à aprovação explícita, sem merge automático.

## Execução local em 7 de setembro de 2026

Windows, .NET SDK 10.0.400, PostgreSQL 17.6 e Mailpit 1.27.8 em contêineres Linux. Dados, chaves e endereços de e-mail são exclusivos dos testes; nenhuma mensagem foi enviada a um provedor externo.

Os primeiros testes de regressão foram registrados antes da implementação (`b2512d7`): 13 executados, 9 aprovados e 4 falhas esperadas. As falhas demonstraram refresh de conta bloqueada, reset inválido alterando dados e configuração JWT ausente/ignorada. A implementação foi registrada separadamente (`195337c`); a cobertura unitária complementar está em `451fc11`.

```sh
rtk test dotnet test tests/Shortener.Authentication.Tests --no-restore --disable-build-servers -m:1 --filter Category!=Integration --logger trx --results-directory artifacts/auth-unit-complete
rtk test dotnet test --no-restore --disable-build-servers -m:1 --logger trx --results-directory artifacts/pr-002-regression
rtk proxy python scripts/validate_deliverables.py
```

| Verificação | Resultado local |
|---|---|
| Unitários de autenticação | 48 aprovados, 0 falhas, 0 ignorados |
| Integração de autenticação | 28 aprovados, 0 falhas, 0 ignorados |
| Regressão da fundação | 9 aprovados, 0 falhas, 0 ignorados |
| Validação documental/estrutural | 2.624 verificações aprovadas |

TRX local em `artifacts/auth-unit-complete` e `artifacts/pr-002-regression`; a CI publica os resultados em `foundation-test-results`. O status remoto deve ser conferido no PR, sem inferir aprovação da CI a partir destes resultados locais.

## Cobertura e limites

| Cenário | Evidência |
|---|---|
| T04 | Cadastro, e-mail real em Mailpit, reenvio utilizável, verificação/reset com expiração e uso único, respostas genéricas e revogação |
| T05 | JWT com algoritmo, assinatura, issuer, audience, kid ou validade incorretos; conta/sessão inválida |
| T06 | Refresh concorrente entre duas instâncias com banco compartilhado, uma rotação e revogação por reuso; logout; reset concorrente; prazo absoluto da sessão |
| T07 | Todos os oito POSTs auth rejeitam CSRF ausente e Origin ausente/estranho; GET do link não consome token; cookies seguros e Bearer ignorado em auth |
| T20, parcela auth | Campos e tipos dos DTOs, Problem Details, no-store, cookies e status; contrato existente preservado |
| T24, parcela JWT | Publicação antecipada da chave pública, troca de emissor, aceitação anterior por 900 + 30 segundos e rejeição após a janela |
| Persistência e limites | Migração em banco vazio e upgrade da autenticação anterior, reaplicação, limites de IP/e-mail/usuário compartilhados, concorrência e 503 na falha do banco |

Os testes unitários não iniciam contêineres. Os testes HTTP usam a API e sua configuração real de autenticação, não um esquema falso; somente o relógio e o key ring antiforgery são controlados no host de teste. O teste entre réplicas compartilha esse key ring, como exige a configuração operacional documentada.

Jornadas de navegador e tratamento visual de resposta perdida pertencem à feature 006. Administração/exclusão pertencem à 007. Restore operacional, retenção de backups e ensaios completos de produção de T24 pertencem à 008. Estes resultados não afirmam esses aceites. O checklist histórico de dependências permanece separado destas evidências de execução.
