---
name: operations-and-observability
description: Orienta mudanças em Docker Compose, proxy, migrações, readiness, OTLP, Collector, Loki, Prometheus e Grafana.
---

# Operação e observabilidade

Use esta skill para alterações em `compose.yaml`, `Dockerfile`, `infra`, scripts operacionais ou configuração de telemetria.

## Procedimento

1. Leia a seção correspondente de `DEVELOPMENT.md` e a especificação de operações.
2. Identifique dependências, volumes, portas, health checks, readiness e ordem de inicialização afetados.
3. Preserve a separação entre banco principal e registro externo.
4. Para mudanças de schema, confirme a migração e o comando de aplicação da funcionalidade responsável.
5. Para telemetria, preserve o fluxo aplicação -> Collector -> Loki/Prometheus/Grafana e os limites documentados.
6. Teste primeiro a configuração ou serviço diretamente afetado; depois execute smoke test quando o fluxo completo estiver disponível.

## Segurança operacional

- Nunca exponha `.env`, credenciais, certificados, tokens ou logs brutos.
- Health checks `/health/live` e `/health/ready` devem permanecer acessíveis somente dentro da rede Compose quando o proxy assim exigir.
- Não use certificados locais em produção.
- Não habilite coleta automática de corpos HTTP, headers ou comandos SQL.
- Considere perdas possíveis em filas de telemetria e não trate observabilidade como parte da transação de negócio.

## Verificação

Use os comandos oficiais do repositório e registre a evidência:

```sh
python scripts/validate_deliverables.py
docker compose config
docker compose ps
```

Quando aplicável, execute o smoke test documentado e confirme readiness após migrações. Diagnostique conectividade, saúde do Loki, espaço em disco e métricas do Collector antes de reiniciar componentes.
