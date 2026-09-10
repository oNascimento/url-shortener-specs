# Desenvolvimento local

## Preparação

Requer Docker Engine com contêineres Linux, Compose v2, SDK definido em `global.json`, Node 22.19.0 e Python 3.12+. No Windows, iniciar Docker Desktop antes dos testes. Ferramentas locais em `.tools/` são ignoradas pelo Git.

```sh
python scripts/bootstrap.py
docker compose up -d postgres registry rabbitmq mailpit collector loki prometheus grafana
docker compose build
docker compose run --rm jobs --migrate
docker compose up -d
```

O bootstrap cria `.env` uma única vez com senhas aleatórias. A senha administrativa do Grafana fica nesse arquivo. Nunca colar seu conteúdo em logs, issues ou PRs. Não alterar essas senhas depois de criar os volumes sem rotacioná-las nos respectivos serviços.

A interface fica em `https://localhost`; domínio curto em `https://s.localhost`; Grafana em `http://localhost:3000` (usuário `admin`); e-mails de teste em `http://localhost:8025`. A CA local do Caddy precisa ser confiada pelo navegador para validar HTTPS e cookies Secure nas próximas funcionalidades. Exportar com `docker compose cp proxy:/data/caddy/pki/authorities/local/root.crt artifacts/local-root.crt` após criar `artifacts/`; importar somente essa CA de desenvolvimento no ambiente local. Não usar os certificados locais em produção.

A API oferece autenticação e sessões; as telas correspondentes serão entregues na feature 006. Health checks `/health/live` e `/health/ready` só são acessíveis dentro da rede Compose; o proxy os bloqueia. Readiness exige a migração inicial e a disponibilidade de PostgreSQL, registro externo, RabbitMQ e Mailpit; telemetria permanece best-effort. Banco principal e registro externo têm volumes distintos; restaurar o banco principal nunca restaura o registro externo para trás.

## Verificação

```sh
python scripts/build_contract.py
python scripts/validate_deliverables.py
dotnet restore --locked-mode
dotnet build --no-restore -c Release
dotnet test --no-build -c Release
cd web
npm ci
npm run generate
npm test
npm run build
```

`dotnet test` inclui PostgreSQL real via Testcontainers. Os testes não enviam e-mail ou acessam destinos externos. Usar `--filter Category!=Integration` apenas para diagnosticar regras unitárias; isso não substitui o aceite integrado.

O contrato OpenAPI é gerado exclusivamente por `scripts/build_contract.py`. O frontend consome os tipos gerados, mantendo IDs e totais de 64 bits como strings. Migrações de negócio serão acrescentadas pelas respectivas funcionalidades; a fundação cria apenas metadados de esquema.

## Autenticação e testes da feature 002

```sh
rtk test dotnet test tests/Shortener.Authentication.Tests --filter 'Category!=Integration'
rtk test dotnet test tests/Shortener.Authentication.Tests --filter 'Category=Integration'
```

Os testes unitários usam relógio controlável e não iniciam contêineres. A integração usa `WebApplicationFactory`, PostgreSQL 17.6 e Mailpit 1.27.8 reais, com bancos isolados por teste. Os e-mails ficam somente no Mailpit; links de ação usam fragmento para o token e exigem POST explícito. A CI executa unitários e integração em etapas separadas, sob o check obrigatório `foundation`.

JWT exige `Jwt:Issuer`, `Jwt:Audience`, `Jwt:KeyId` e uma chave RSA privada de pelo menos 2048 bits em `Jwt:PrivateKeyPem` ou `Jwt:PrivateKeyPath`. O Compose de desenvolvimento gera a chave uma vez e a conserva no volume `auth-keys`, junto às chaves antiforgery/Data Protection. A geração automática é proibida fora de Development. Em outros ambientes, provisionar esses segredos e compartilhar o key ring Data Protection entre réplicas usando `DataProtection:KeyPath`, com armazenamento e permissões protegidos.

Para transição JWT, distribuir primeiro a nova chave pública em `Jwt:ValidationKeys:<índice>:KeyId` e `PublicKeyPem` em todas as instâncias. Depois, trocar a chave privada e o `Jwt:KeyId` de emissão, mantendo a chave pública anterior na lista e informando seu `RetiredAt` UTC: o instante em que ela deixou de emitir. A chave anterior é rejeitada após 900 segundos mais 30 segundos de tolerância, mesmo se permanecer configurada. Atualizações de configuração exigem reiniciar as instâncias. Nunca reutilizar `kid` para outro material criptográfico.

Login usa 10 tentativas por IP/10 minutos; envios de e-mail compartilham 3 por e-mail/hora e 20 por IP/hora entre cadastro, reenvio e recuperação. Rotas protegidas usam 300 requisições por usuário/minuto. Os contadores transacionais ficam no PostgreSQL, com janela a partir da primeira tentativa, e `429` inclui `Retry-After`. Os identificadores são hashes, sem IP/e-mail em texto. O Compose confia somente no endereço fixo do Caddy para os headers encaminhados; `TrustedProxies` deve identificar os proxies de cada ambiente.

O refresh bloqueia a linha do usuário e depois a sessão, dentro da transação. Reset e logout seguem a mesma ordem de coordenação. Reuso do refresh revoga a família; resultado perdido exige novo login no futuro cliente da feature 006. A migração complementar é aditiva e pode ser aplicada sobre a feature 002 existente.

## Gestão de links e evidência da feature 003

A API implementa POST/GET `/api/v1/links`, GET `/api/v1/links/{linkId}` e POST `/api/v1/links/{linkId}/deactivate`. Exige Bearer válido; criação exige `Idempotency-Key` UUID por ação e tem orçamento separado de 60/minuto por usuário. As demais rotas protegidas mantêm 300/minuto. A interface e o redirecionamento público pertencem às próximas funcionalidades.

`Origins:Short` fornece a origem absoluta do endereço curto; `Origins:ShortAliases:<índice>` aceita origens HTTP/HTTPS adicionais a rejeitar como destino. Comparar hosts independe de porta e esquema. Compartilhar o key ring Data Protection entre réplicas também é obrigatório para os cursores, válidos por 24 horas. A migração de links é aditiva; códigos e IDs não devem ser apagados ou reciclados no rollback da aplicação.

O banco principal coordena alocação e mantém a sequência. O registro externo autoriza faixas inclusivas de um milhão antes de elevar MAXVALUE; a primeira emissão é 1. A migração deixa a sequência esgotada até a autorização inicial. Falhas podem deixar lacunas. O teste concorrente usa 100 clientes e PostgreSQL com `max_connections=250`; isso é configuração do ensaio, não uma recomendação de capacidade de produção.

Com Docker ativo, coletar tudo em um diretório novo por execução:

```sh
dotnet restore --locked-mode
dotnet build --no-restore -c Debug
rtk test dotnet test tests/Shortener.LinkManagement.Tests --no-build --settings tests/link-management.runsettings --logger trx --results-directory artifacts/link-management/feature
rtk test dotnet test tests/Shortener.Authentication.Tests --no-build --settings tests/link-management.runsettings --logger trx --results-directory artifacts/link-management/auth
rtk proxy python scripts/check_link_coverage.py artifacts/link-management --output artifacts/link-management/coverage-summary.json
rtk proxy python scripts/check_link_requirements.py artifacts/link-management/feature --output artifacts/link-management/requirements-summary.json
```

O check `link-management` exige 100% das linhas/ramificações declaradas, sem arredondar, e todos os cenários backend. O manifesto inclui todo código novo e métodos compartilhados alterados; o gate também compara arquivos/métodos com `origin/main`. O artefato `link-management-evidence` contém TRX, relatórios e ambiente. Cobertura mede execução de código; a matriz de requisitos comprova separadamente os resultados funcionais testados.

### Preparação da alocação após restore

Suspender criação antes da recuperação, usando `docker compose run --rm jobs --suspend-link-creation`. Manter o ambiente restaurado isolado conforme operations.md. O comando de preparação exige a suspensão persistida; não inferir segurança pela indisponibilidade de rede do escritor anterior.

Depois de o operador revogar LOGIN do papel de escrita antigo, encerrar suas conexões e impedir seu retorno pela infraestrutura, provisionar `ConnectionStrings:OldPrimary` (conexão administrativa de leitura ao primário antigo) e `Recovery:WriterRole` no gerenciador de segredos. Executar `docker compose run --rm jobs --prepare-link-restore` com essas configurações injetadas no serviço. O comando verifica positivamente NOLOGIN e ausência de sessões, reserva acima do limite externo anterior e posiciona a sequência antes de liberar criação. Falha mantém suspensão; repetir é seguro e pode deixar outra lacuna. Nunca restaurar o registro externo para trás. Não usar o papel administrativo verificador como papel da aplicação.

A verificação cobre um papel de escrita configurado: o operador deve garantir que ele seja o único escritor da aplicação no primário antigo. Fencing de rede, eleição do primário, PITR, reaplicação de exclusões e retomada dos demais serviços permanecem parte do runbook integral da feature 008. Com BIGINT esgotado, manter criação suspensa.

## Observabilidade

Aplicação → exportador OTLP em lote → Collector → Loki; métricas → Collector → Prometheus. Grafana provisiona as duas fontes e o dashboard **Shortener — Aplicação**. No Explore, consultar `{service_name=~"shortener-.+"}`. Para uma falha correlacionada, filtrar `trace_id` com o identificador retornado em Problem Details.

Logs JSON no console e OTLP aceitam somente categorias `Shortener` em nível Information ou superior. Templates da aplicação devem conter apenas campos operacionais aprovados. Não passar exceções diretamente para `ILogger`: mensagens de bibliotecas podem conter URLs, credenciais e SQL. A fronteira HTTP registra uma falha sem os detalhes sensíveis; retornos públicos também não os incluem. Não habilitar logging automático de corpos, headers ou comandos SQL.

Loki indexa apenas serviço e ambiente; traceId é metadado estruturado. Logs expiram em 14 dias pelo compactor, métricas em 90 dias. O Collector tem limite de memória, fila limitada e retry finito. O exportador .NET tem fila de 2.048 registros e timeout de 1 segundo. Telemetria não participa da transação de negócio; em saturação podem existir perdas, não um histórico completo garantido.

Prometheus contém alertas de indisponibilidade do Collector e falhas de exportação. Na falha, conferir conectividade interna, saúde do Loki, espaço em disco e métricas do Collector antes de reiniciá-lo. Reiniciar pode perder sua fila em memória. Alertas de negócio, correlação por RabbitMQ e dashboards de consumo/jobs serão entregues com as funcionalidades correspondentes; destinos externos de notificação serão configurados na preparação de produção.

Referências de implementação: [ingestão OTLP nativa do Loki](https://grafana.com/docs/loki/latest/send-data/otel/) e [exportador OpenTelemetry .NET](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/src/OpenTelemetry.Exporter.OpenTelemetryProtocol/README.md).

## Branches e revisão

Cada funcionalidade usa `feat/NNN-nome`, criada a partir da `main` atualizada após merge da anterior. Commits referenciam tarefas Fxxx-Txx. Abrir PR draft cedo; apresentar comportamento, contrato/migrações, testes, telemetria e limitações antes de solicitar revisão.

Nenhum merge automático. Após aprovação explícita e CI verde, usar squash merge; então remover a branch concluída e criar a próxima. Configurar proteção da main com PR, check `foundation` e resolução de comentários; aprovação formal de outro colaborador depende da existência de revisor elegível. Aprovação do autor não conta como revisão independente.

`python scripts/configure_repository.py` mostra a configuração de proteção sem modificá-la. Com GitHub CLI autenticado, `--apply` aplica as regras; acrescentar `--approvals 1` quando houver outro revisor elegível. Em repositório individual, zero revisões formais não dispensa sua aprovação explícita antes do merge. A disponibilidade da proteção depende das permissões e do plano do repositório GitHub.
