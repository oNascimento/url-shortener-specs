# Evidências — gestão de links, escopo backend

**100% of feature 003’s backend scope implemented and verified.** Esta conclusão é limitada ao backend aprovado: RF-06–10 e RF-17–18 nas parcelas de validação, persistência, API, autorização, paginação, estado e alocação. Interface, redirecionamento/eventos e recuperação operacional integral permanecem dependências explicitadas abaixo.

Código verificado: [`2e95d7d`](https://github.com/oNascimento/url-shortener-specs/commit/2e95d7dcc34480c0beab96f29dbcaa52bd7fa37c). A documentação de evidência e as referências de rastreabilidade são registradas separadamente; não alteram o código medido.

## Ambiente e resultados reais

Execução local em 9–10 de setembro de 2026: Windows, .NET SDK 10.0.400, Docker Engine 29.7.2, PostgreSQL 17.6 em contêineres separados para primário e registro, xUnit 2.9.3, Coverlet 6.0.4 e build Debug. Autenticação usa Mailpit 1.27.8. Node local 20.15.1; a imagem frontend e a CI usam Node 22.19.0. Nenhum teste enviou e-mail real ou abriu destinos externos.

| Verificação | Resultado medido |
|---|---|
| Feature 003: unitários e integração HTTP/PostgreSQL/Jobs | 56 aprovados, 0 falhas, 0 ignorados |
| Regressões da autenticação | 76 aprovados, 0 falhas, 0 ignorados |
| Regressões da fundação | 9 aprovados, 0 falhas, 0 ignorados |
| Testes dos gates de evidência | 7 aprovados |
| Frontend: precisão de IDs/contagens | 2 aprovados; geração e build aprovados |
| Linhas do escopo de implementação | **514/514 — 100%** |
| Ramificações do escopo de implementação | **288/288 — 100%** |
| Matriz de requisitos backend | **18/18 parcelas verificadas**, cobrindo os sete RF da feature |
| Cenários obrigatórios da feature | **8/8 verificados** no escopo backend |
| T02: 10.000 tentativas, 100 clientes concorrentes | 9.000 links confirmados; 1.000 rollbacks deliberados; nenhum código duplicado ou reutilizado |
| Contrato reproduzível | OpenAPI e tipos TypeScript regenerados sem alteração |
| Validação documental/estrutural | 2.643 verificações aprovadas com este documento |
| Compose: build e migração aditiva | Aprovados |
| Smoke da fundação | Quatro serviços prontos; logs no Loki, métricas no Prometheus, Grafana, HTTPS e rotas de saúde privadas |

Relatórios finais da feature/autenticação e resumos em `artifacts/link-20260910`; regressão da fundação em `artifacts/link-release/foundation`. São artefatos locais ignorados pelo Git. A CI publica `link-management-evidence` e `foundation-test-results`; resultados locais não substituem checks remotos.

## Rastreabilidade verificável

O [manifesto de requisitos](../../../tests/link-management-requirements.json) liga cada parcela a métodos de teste existentes. O gate lê TRX e reprova ausência, falha ou teste ignorado. O [manifesto de cobertura](../../../tests/link-management-coverage.json) inclui todos os arquivos backend novos e métodos compartilhados alterados integralmente, incluindo migrações e inicialização da API/Jobs. O gate compara esse escopo com `origin/main`, exige arquivos/métodos presentes e combina hits sem duplicar denominadores. Nenhuma linha ou ramificação descoberta permanece sem execução; SQL é verificado por resultados e constraints em PostgreSQL real.

| Requisito / tarefa | Evidência automatizada |
|---|---|
| RF-06, RF-09; F003-T02; T08 | URLs absolutas HTTP/HTTPS, 8.192 caracteres, IDN/aliases, caixa/ponto/porta, credenciais e controles; caminho `%2F`, query repetida e fragmento preservados. Listener local sem conexão e hostname `.invalid` demonstram ausência de fetch/DNS. |
| RF-07; F003-T02; T03 | Mesma chave concorrente retorna um link; URLs conflitantes produzem um sucesso e 409; chave por proprietário; nova chave cria outro link; expiração atômica imediatamente antes e em 24 h; replay retorna estado atual. Resposta HTTP descartada após commit pode ser repetida com a chave original sem novo link. |
| RF-08; F003-T03; T17/AF-02 | Ordem estável em timestamps empatados, inserção concorrente no topo, páginas vazias/finais, padrão 50/máximo 100, cursor compartilhado entre réplicas; usuário/rota/filtro/limite/versão/expiração inválidos rejeitados; outro dono recebe 404. |
| RF-09–10; F003-T04 | Ausência de edição HTTP, desativação idempotente, timestamp original preservado, destino/código/histórico mantidos e precedência disabled sobre blocked. Desbloquear não reativa desativação do proprietário. |
| RF-17–18; F003-T01; T01/T02/T22 | Todos os vetores Base62 normativos, transições sem padding, `z` e `Z` distintos no banco, BIGINT máximo, faixa final parcial e 503 por esgotamento. Rollbacks permitem lacunas, sem reciclagem. |
| RF-18; F003-T01; T21 | Registro separado, confirmação perdida reconciliada por leitura, falha antes do commit, CAS concorrente, extensão aos 20%, falha de prefetch sem invalidar faixa confirmada, falta de capacidade e registro regressivo rejeitados. Cem coordenadores independentes compartilham uma única faixa. |
| F003-T01; T21, parcela restore | Jobs suspende criação; preparação exige suspensão e inspeção positiva de NOLOGIN e ausência de sessões no escritor antigo. NOLOGIN com conexão ainda aberta é rejeitado. Retomada começa acima do limite externo anterior; reinício normal não reposiciona a sequência. |
| F003-T05; T20/AF-03/AF-11/AF-12, parcelas backend | Requests inválidos e responses comparados ao contrato; IDs acima de 2^53 como strings, Location de gestão, Problem Details, traceId e no-store. Bearer real, revogação de sessão/conta, cookies sem identidade, limites 60/300 compartilhados e Retry-After. |
| Persistência e falhas | Migração em banco vazio e upgrade preservando autenticação, reaplicação, constraints, cancelamento, rollback e 503 explícito em falhas de banco/alocação. Logs não contêm o destino de teste. |

## Reprodução e gates

Na raiz, com Docker ativo e diretório de resultados novo:

```sh
dotnet restore --locked-mode
dotnet build --no-restore -c Debug
rtk test dotnet test tests/Shortener.LinkManagement.Tests --no-build --settings tests/link-management.runsettings --logger trx --results-directory artifacts/link-management/feature
rtk test dotnet test tests/Shortener.Authentication.Tests --no-build --settings tests/link-management.runsettings --logger trx --results-directory artifacts/link-management/auth
rtk proxy python scripts/check_link_coverage.py artifacts/link-management --output artifacts/link-management/coverage-summary.json
rtk proxy python scripts/check_link_requirements.py artifacts/link-management/feature --output artifacts/link-management/requirements-summary.json
rtk test python -m unittest discover -s tests/evidence-gates
rtk test dotnet test tests/Shortener.Foundation.Tests --no-build --logger trx
rtk proxy python scripts/validate_deliverables.py
rtk proxy python scripts/smoke_foundation.py
```

O smoke exige o Compose iniciado e migrado conforme [DEVELOPMENT.md](../../../DEVELOPMENT.md). O [workflow](../../../.github/workflows/ci.yaml) mantém a regressão da fundação e acrescenta o check `link-management`, com Docker obrigatório, cobertura exata e matriz de requisitos. Os sete testes do gate incluem relatório ausente, ramificação descoberta, método omitido, merge sem duplicar denominador e testes ignorados/falhos.

## Limites e dependências de aceite

- **006:** formulário, loading/erro/nova tentativa, cópia, confirmação, acessibilidade e gestão de chaves pelo navegador. Não se afirma AF-01 completo nem jornadas de UI.
- **004–005:** 302/410 públicos, Location de redirecionamento, resolução pública de `z`/`Z`, query extra, elegibilidade de eventos e relatórios. AF-05/AF-11/AF-12 são demonstrados aqui apenas na gestão/persistência.
- **007:** fluxos administrativos, exclusão de conta, tombstones de privacidade e retenção. O backend 003 preserva estados e não oferece reativação/edição de destino.
- **008:** eleição/fencing de infraestrutura, backup/PITR, segredos, reaplicação de exclusões e exercício completo de recuperação. O verificador implementado inspeciona um papel antigo configurado; a infraestrutura deve garantir que seja o único escritor da aplicação e impedir seu retorno. Ver [procedimento local](../../../DEVELOPMENT.md).

Os checkboxes históricos permanecem sujeitos às dependências F001/F002 e à aprovação. Não foram marcados artificialmente, nem o validador foi enfraquecido. Não houve merge automático.
