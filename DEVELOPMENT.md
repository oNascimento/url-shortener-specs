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

Não há endpoints de negócio nesta primeira entrega. A interface informa essa condição. Health checks `/health/live` e `/health/ready` só são acessíveis dentro da rede Compose; o proxy os bloqueia. Readiness exige a migração inicial e a disponibilidade de PostgreSQL, registro externo, RabbitMQ e Mailpit; telemetria permanece best-effort. Banco principal e registro externo têm volumes distintos; restaurar o banco principal nunca restaura o registro externo para trás.

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
