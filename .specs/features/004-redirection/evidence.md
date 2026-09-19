# Evidência geral — Feature 004

**Implementação da 004 verificada. Aprovação e integração dos PRs restantes, além da reconciliação histórica F003-T05, permanecem pendentes.**

A execução final da T05 aprovou **26 testes, zero falhos e zero ignorados**, com **348/348 linhas (100%)** e **134/137 branches (97,81%)**. O gate de 95% passou sem arredondamento permissivo. Estes são os resultados de uma execução completa da implementação final; os percentuais dos parciais não foram somados.

## Execução final e proveniência

- Entrega funcional: `94c50b8`; commit documental da T05: `9875417`.
- SHA do checkout executado na CI: `5cf96fcc13c63abbcf688b5fb67c3d043f6c9806` (merge ref do PR #13).
- Fingerprint de código, testes, dependências, configuração e ferramentas: `9c6e9b201a85b5b80bf36b6ae21fefe100f8f06498ada36a8d858068f924dd0e`.
- [CI final aprovada](https://github.com/oNascimento/url-shortener-specs/actions/runs/35410519033): documents, redirection, link-management e foundation.
- [TRX, cobertura, resumos e proveniência da 004](https://github.com/oNascimento/url-shortener-specs/actions/runs/35410519033/artifacts/10574636553).
- [Regressão e gate da 003](https://github.com/oNascimento/url-shortener-specs/actions/runs/35410519033/artifacts/10573746768): **514/514 linhas e 288/288 branches (100%)**.
- [Build, testes foundation e smoke](https://github.com/oNascimento/url-shortener-specs/actions/runs/35410519033/artifacts/10574621655): aprovados com o Dockerfile padrão; HTTPS, saúde privada, Loki, Prometheus, Grafana e quatro serviços prontos.

Ambiente remoto: Ubuntu, SDK .NET 10.0.400, Python 3.12, xUnit 2.9.3, Coverlet 6.0.4, build Debug para cobertura; foundation valida Release. PostgreSQL 17.6, RabbitMQ 4.1.4-management e Caddy 2.10.2-alpine reais. `environment.txt` e `images.txt` registram versões e IDs/digests. Artefatos da 004 têm retenção de 90 dias.

A execução local `artifacts/redirection/F004-T05/20260919T004224470850Z`, em Windows, apresentou o mesmo fingerprint e os mesmos numeradores/denominadores. Seu SHA original e o commit equivalente estão em `provenance.json`. Os relatórios local e remoto permanecem separados.

As três ramificações descobertas continuam no denominador: canal fechado antes da publicação (`RabbitTransport.cs:101`), remoção concorrente durante limpeza (`RabbitTransport.cs:129`) e caminho HTTP nulo (`RedirectMiddleware.cs:9`). Não há linhas descobertas, arquivos/métodos omitidos nem C# de produção sem escopo declarado.

## Sete tarefas e PRs

| Tarefa | PR e base | Evidência parcial | Situação de integração |
|---|---|---|---|
| F004-T00 | [#8](https://github.com/oNascimento/url-shortener-specs/pull/8), main | [T00](evidence/F004-T00.md) | Integrado na main pelo usuário |
| F004-T01 | [#9](https://github.com/oNascimento/url-shortener-specs/pull/9), t00 | [T01](evidence/F004-T01.md) | Integrado em t00; segue para main via #11 |
| F004-T02 | [#10](https://github.com/oNascimento/url-shortener-specs/pull/10), t01 | [T02](evidence/F004-T02.md) | Integrado em t01; segue para main via #11 |
| F004-T03 | [#11](https://github.com/oNascimento/url-shortener-specs/pull/11), main | [T03](evidence/F004-T03.md) | Draft; CI verde |
| F004-T04 | [#12](https://github.com/oNascimento/url-shortener-specs/pull/12), t03 | [T04](evidence/F004-T04.md) | Draft; CI verde |
| F004-T05 | [#13](https://github.com/oNascimento/url-shortener-specs/pull/13), t04 | [T05](evidence/F004-T05.md) | Draft; CI verde |
| F004-T06 | [#14](https://github.com/oNascimento/url-shortener-specs/pull/14), t05 | [T06](evidence/F004-T06.md) | Draft; consolidação exclusivamente documental |

Os três merges de 12/09/2026 aconteceram em sequência sem reposicionar as bases: somente T00 chegou à main. T03 transporta T01/T02 já revisadas, preservando sete PRs. Seu gate mede o escopo real da revisão contra main e, adicionalmente, o escopo lógico da T03 contra `065902c`. Nenhum merge remoto foi realizado pelo agente. Depois de cada squash aprovado, as branches descendentes devem ser reposicionadas preservando seus commits e as bases dos PRs atualizadas.

| Tarefa | Testes 004 na execução auditada | Cobertura da tarefa: linhas / branches | Cobertura acumulada: linhas / branches |
|---|---:|---|---|
| T00 | 0; 15 testes dos gates aprovados | Não aplicável | Não aplicável |
| T01 | 5 | 82/82; 39/40 | 82/82; 39/40 |
| T02 | 10 | 117/117; 45/46 | 147/147; 61/62 |
| T03 | 23 | 144/144; 53/55 | 281/281; 114/117 |
| T04 | 25 | 150/150; 51/52 | 348/348; 135/137 |
| T05 | 26 | Não aplicável: testes e ferramentas, sem C# de produção alterado | 348/348; 134/137 |
| T06 | Sem nova execução funcional | Não aplicável: somente documentos | Referência compatível à medição final T05 |

Cada linha é uma execução independente, identificada no respectivo parcial. Variações de hits em ramos concorrentes entre execuções não autorizam combinar implementações nem somar percentuais. T05 manteve o gate acumulado obrigatório mesmo sem escopo novo de produção.

A T06 auditou os seis parciais e reavaliou cópias byte a byte do artefato final T05, em `artifacts/redirection/F004-T06/20260919T005456968128Z`. O fingerprint atual é idêntico ao da execução de origem; `reuse.json` registra hashes de cada arquivo copiado, o SHA original e a ausência de nova execução funcional. O gate da consolidação passou com os mesmos 26 resultados e 348/348 linhas, 134/137 branches. [Checks documentais da T06](https://github.com/oNascimento/url-shortener-specs/pull/14/checks) validam a documentação final sem repetir os jobs funcionais.

## Matriz de requisitos e cenários

A [matriz executável](../../../tests/redirection-requirements.json) associa os nomes completos dos testes aos requisitos e reprova ausência, falha ou skip. Todos os cenários abaixo passaram na execução final.

| Requisito / cenário | Comportamento comprovado | Testes |
|---|---|---|
| RF-19, RF-20 / T09 | GET/HEAD, z/Z, 1–11 caracteres ASCII, códigos inválidos/ausentes, métodos e headers, Location original com caminho/query repetida/fragmento; query curta ignorada | `ResolutionTests.Exact_codes_methods_and_original_destination_match_contract` |
| RF-20 / T09, AF-05 | Desativação, bloqueios de link/conta, exclusão e tombstones retornam 410 genérico; resoluções após commit observam o novo estado | `ResolutionTests.Committed_states_and_tombstones_are_generic_410_without_cache`; `EndToEndTests` |
| RF-20 / T09 | Falha de leitura retorna 503 com Retry-After; pipeline público não processa autenticação; PostgreSQL realmente recusa conexões e recupera | `ResolutionTests.Resolution_failure_is_503_and_public_requests_do_not_authenticate`; `EndToEndTests` |
| RF-21 / T10, parcela AF-04 | IPv4, IPv6, mapped, spoofing, proxies/redes confiáveis e limite de saltos; UTC em microssegundos; dez UUID v4 distintos, envelope imutável, ausência de IP, HEAD e inelegíveis sem publicação | `CaptureTests`; comparação do IP real observado por Caddy em `EndToEndTests` |
| RF-19, RF-20 / T13 | Persistência, quorum, mandatory return mesmo com ack, nack por capacidade da fila, confirmação perdida/tardia, saturação limitada, deadline total e recuperação | `RabbitTests`; `PublisherTests` com relógio controlado e relay TCP real |
| RF-20 / T13, parcela AF-06 | Readiness depende apenas da resolução no primário; captura/coleta/exportador indisponíveis preservam 302; resultados confirmado/rejeitado/desconhecido separados | `TelemetryTests`; `RabbitTests`; `EndToEndTests` |
| RF-19/20/21 / T09/T10/T13 | API de criação → Caddy real → duas instâncias Kestrel → fila quorum com três membros; startup sem brokers, perda de nó/maioria e recuperação; concorrência e ausência de fetch do destino | `EndToEndTests.Creation_proxy_two_redirectors_and_three_node_quorum_preserve_contract_during_failures` |
| AF-10/11/12, parcela 004 | HEAD sem corpo/evento, métodos públicos, caixa exata e destino preservado | `ResolutionTests`, `CaptureTests`, `EndToEndTests`; validação da criação permanece coberta pela regressão 003 |

Os dois redirecionadores têm hosts, portas, DI e conexões de mensageria independentes, dentro do processo de testes. PostgreSQL, os três brokers e Caddy executam em containers reais. O teste inspeciona os membros da fila quorum. O listener local registra zero acessos pelos serviços e exatamente um quando o cliente segue o redirecionamento. Detalhes e comandos constam do parcial T05.

O orçamento configurado de publicação é 100 ms, incluindo capacidade/envio/confirmação, validado com relógio controlado. Na execução local final, o experimento de perda de confirmação mediu 102,139 ms reais, incluindo agendamento; os HTTPs instrumentados mediram de 8,689 a 113,152 ms. As amostras estão separadas no TRX. Recuperação usa novos GETs/eventos para observar confirmação; não há republicação de eventos incertos. Carga global e metas operacionais completas pertencem à 008.

## Gates e reprodução

Os quinze testes dos gates validam limiar exato, preservação dos 100% da 003, união de hits sem duplicar denominadores, ausência de relatório/método, propriedade de C#, métodos alterados completos, bases encadeadas, cenários ausentes/falhos/ignorados e proveniência incompatível. O fechamento não excluiu lógica manual nem afrouxou o validador.

Em um checkout limpo de `94c50b8`, com Docker disponível:

```powershell
rtk err dotnet restore --locked-mode
rtk err dotnet build tests/Shortener.Redirection.Tests --no-restore -c Debug
rtk proxy python scripts/run_redirection.py --base effde1bd99b22e8fac622921de226ee27cddd92a
rtk test python -m unittest discover -s tests/evidence-gates
rtk proxy python scripts/validate_deliverables.py
```

Cada execução funcional cria um diretório próprio e registra proveniência antes de executar testes. O gate exige fingerprint compatível; relatórios antigos não entram automaticamente em execuções novas. A CI roda em PR e push de main, cancela execuções obsoletas e reserva a suíte 004 ao job dedicado. PR exclusivamente documental executa validação documental e referencia uma medição funcional compatível.

Os primeiros builds Docker locais encontraram NU1301/PartialChain. Os smokes locais T03/T04 usaram publish Release no host e o estágio runtime original, sem desabilitar TLS. O CI final compilou o Dockerfile padrão e executou o smoke completo. Falhas exploratórias de containers, sincronização e preparação dos testes estão preservadas nos diretórios diagnósticos e descritas nos parciais; não constituem medições aprovadas.

## Limites e continuidade

- F003-T05 continua desmarcada no planejamento histórico. Nenhum checkbox ou aceite inexistente foi criado para contornar essa dependência.
- AF-04 está comprovado quanto à geração de identidades distintas e preservação do envelope em redelivery. Deduplicação durável e contagem pertencem à 005.
- AF-06 está comprovado quanto à continuidade do 302. O aviso de incidente no painel depende de 005/006.
- Worker, agregação, painel, failover PostgreSQL, carga global e recuperação operacional integral não foram acrescentados à 004.
- A implementação e a consolidação estão verificadas; a liberação formal exige revisão da T06, aprovação explícita dos PRs restantes, integração ordenada e reconciliação da dependência histórica. O próximo ponto é a revisão dos PRs #11 → #12 → #13 → #14.

[Especificação](spec.md) · [Plano](plan.md) · [Tarefas](tasks.md)
