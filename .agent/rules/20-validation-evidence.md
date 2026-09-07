# Validação e evidências

Toda alteração deve terminar com uma validação proporcional ao risco e uma descrição honesta do que foi verificado.

## Validação documental e contrato

```sh
python scripts/build_contract.py
python scripts/validate_deliverables.py
```

O contrato OpenAPI é gerado exclusivamente por `scripts/build_contract.py`. Não edite o contrato gerado manualmente.

## Validação .NET

```sh
rtk dotnet restore --locked-mode
rtk dotnet build --no-restore -c Release
rtk dotnet test --no-build -c Release
```

Use primeiro o projeto ou teste afetado. `rtk dotnet test` inclui PostgreSQL real via Testcontainers; `--filter Category!=Integration` serve apenas para diagnóstico e não substitui o aceite integrado.

## Validação frontend

```sh
cd web
npm ci
npm run generate
npm test
npm run build
```

Preserve os tipos gerados, os IDs de rastreabilidade e os totais de 64 bits como strings.

## Infraestrutura e operação

- Para o fluxo completo, siga `DEVELOPMENT.md` e use Docker Compose com Docker Engine, Compose v2, SDK do `global.json`, Node 22.19.0 e Python 3.12+.
- O smoke test deve ser executado quando a alteração afetar Compose, proxy, serviços, migrações ou observabilidade.
- Não cole `.env`, credenciais do Grafana, certificados ou logs sensíveis na saída de validação.
- Registre testes executados, falhas conhecidas, limitações e qualquer cenário não verificado.
