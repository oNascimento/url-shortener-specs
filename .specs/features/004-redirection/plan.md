# Plano técnico — 004

## Objetivo e dependências

Entregar resolução pública, captura e publicação independentemente da coleta. Dependência funcional: [003](../003-link-management/plan.md), F003-T05; seus checkboxes históricos continuam separados do backend já verificado.

Escopo aprovado: somente 004. Worker, deduplicação, contagem e painel pertencem a 005/006; failover PostgreSQL, carga global e recuperação integral pertencem a 008.

## Interfaces e invariantes

| Operação | Método e rota |
|---|---|
| `redirect` | `GET /{code}` |
| `inspectRedirect` | `HEAD /{code}` |

Preservar [OpenAPI](../../contracts/openapi.json), [arquitetura](../../architecture.md) e RF-19–21. Alterações de contrato passam pelo gerador. Reutilizar AccessRecorded, IAccessPublisher e PublishOutcome; nenhuma nova API pública ou migração de negócio.

- Um segmento ASCII alfanumérico de 1–11 caracteres, caixa exata e uma consulta link/conta no primário, incluindo tombstones. Sem cache ou réplica.
- GET/HEAD disponível: 302, Location original, no-store e no-referrer; query adicional ignorada. HEAD sem corpo/publicação. Ausente/inválido: 404; desativação/bloqueio/exclusão: 410 genérico; outro método: 405 com Allow: GET, HEAD; leitura indisponível: 503 e Retry-After: 1.
- Pipeline público independente de autenticação/antiforgery/limites de gestão. Readiness do redirecionador depende do primário; coleta é separada.
- UUID v4 por GET, linkId string decimal e UTC com microssegundos; envelope imutável. IP via conexão/middleware confiável, normalizando IPv4-mapped. IP ausente mantém 302 e registra falha.
- Exchange access e fila quorum access.recorded.v1 duráveis, routing key recorded.v1; persistência, mandatory e confirms. Conexão/canais reutilizados, capacidade limitada e exclusão mútua por canal.
- Orçamento total 100 ms inclui capacidade, envio e confirmação. Timeout é Unknown; mandatory return/nack é Rejected; return prevalece sobre ack. Nenhuma fila/retry ilimitada após responder.
- Métricas de elegibilidade, resolução, captura, resultado e duração; sem IP, destino, código ou identificadores individuais em logs/labels.

## Sete tarefas e PRs encadeados

T00 prepara planejamento/gates; T01 resolve HTTP; T02 captura; T03 publica; T04 instrumenta disponibilidade; T05 integra; T06 consolida. Detalhes em [tasks.md](tasks.md).

Branches feat/004-redirection-t00 até t06. T00 parte da main atualizada; descendentes partem da anterior. Cada tarefa tem commit de entrega com testes e commit documental de evidências; correções ficam no mesmo PR. Abrir draft cedo, título iniciado pelo ID, base na branch anterior. Após validar a atual, avançar sem aguardar merge. Squash merge somente após aprovação explícita e CI verde, reposicionando descendentes e bases. PRs intermediários explicitam etapas ainda pendentes. Liberação exige T06.

## Testes e gates

>=95% linhas e >=95% branches pela união de unitários e integrações em cada PR funcional: escopo acumulado da 004 e escopo da tarefa. Arquivos novos completos e métodos compartilhados alterados completos, inclusive inicialização; sem excluir lógica manual. Manter 100% da 003. Todo C# alterado deve pertencer a manifesto; sobreposição compartilhada é permitida. Base real do PR para tarefa e ancestral da main para acumulado.

xUnit, Coverlet, HTTP e PostgreSQL/RabbitMQ reais via Testcontainers. Matriz executável associa requisitos/cenários a testes, ativada progressivamente. Ausência/falha/skip reprova; cobertura não substitui comportamento. Relatérios ausentes, escopos omitidos ou medições incompatéveis reprovam. Comparar percentuais sem arredondamento.

- T09: GET/HEAD/métodos, z/Z, código inválido/ausente, todos os estados, falha de primário, headers, query/fragmento e concorrência após commit.
- T10: IPv4/IPv6/mapped, falsificação, cadeia confiável/hops, IP ausente; dez GETs distintos e nenhuma publicação inelegível.
- T13: confirms, broker ausente desde startup/interrompido, binding ausente, nack, timeout/confirm tardio, saturação e recuperação mantendo 302.
- T05: API -> proxy/redirecionador -> RabbitMQ e destino local; somente cliente segue Location. Ensaios finais com três nós quorum e dois redirecionadores.
- Relógio controlado comprova orçamento; tempos reais de publicação/HTTP separados. Verificar métricas e ausência de dados sensíveis.

Desenvolvimento usa testes direcionados; fechamento executa suíte acumulada e regressões compartilhadas. Smoke quando serviços/proxy/observabilidade mudarem. CI evita duplicidade push de branch/PR e cancela execuções obsoletas; mudanças só documentais validam documentos, referenciando medições compatéveis.

## Evidências e continuidade

Cada tarefa entrega evidence/F004-Txx.md: objetivo, limitações, branch/PR/SHA, comandos/ambiente, testes/cobertura com numeradores e denominadores, rastreabilidade, links CI/artefatos e próximo passo. Execuções usam diretórios próprios e provenance.json com identidade de código, testes, dependências, configurações e ferramentas; não combinar medições incompatéveis. Commit documental referencia SHA funcional anterior sem alegar nova medição.

T06 entrega evidence.md geral auditando sete PRs/parciais, sem somar percentuais. AF-04 comprova identidades; deduplicação/contagem dependem de 005. AF-06 comprova 302; painel depende de 005/006. Implementado/verificado e aceite formal sóo separados: dependências históricas não sóo marcadas artificialmente.
