# Encurtador de URLs

Repositório de especificações e futura implementação de um encurtador com **.NET 10 / ASP.NET Core 10**, React/TypeScript, PostgreSQL e RabbitMQ.

## Planejamento

Comece pelo [índice de .specs](.specs/README.md). Ele organiza oito funcionalidades com requisitos, planos técnicos e tarefas pendentes.

- [Produto e critérios de aceitação](.specs/product.md)
- [Arquitetura](.specs/architecture.md)
- [Operação e testes](.specs/operations.md)
- [Rastreabilidade](.specs/traceability.md)
- [OpenAPI: 26 operações](.specs/contracts/openapi.json)

A implementação começa pela fundação dos serviços e do frontend. Consulte [Desenvolvimento local](DEVELOPMENT.md) para infraestrutura, builds, observabilidade e fluxo de PRs. Os endpoints de negócio serão entregues nas funcionalidades seguintes. Há 23 requisitos funcionais, 16 critérios de aceitação e 24 cenários de teste documentados; a matriz de evidências distingue implementação de metas ainda não verificadas.

## Decisões principais

Códigos Base62 com sequência numérica e crescimento automático, destino imutável e links públicos. Cada GET elegível tenta registrar UTC/IP; falha apenas na coleta não impede redirecionamento. Eventos são retidos por 90 dias e totais enquanto a conta existir. Esta versão não calcula royalties ou pagamentos.

## Manter as especificações

Com Python 3.12 ou superior, na raiz:

```sh
python scripts/build_contract.py
python scripts/validate_deliverables.py
```

Os comandos usam somente a biblioteca padrão. A validação confere a documentação e o contrato estruturalmente; não substitui teste de aplicação ou validador integral OpenAPI. Para validação especializada em ambiente Python isolado:

```sh
python -m pip install openapi-spec-validator==0.9.0
python -m openapi_spec_validator .specs/contracts/openapi.json
```

Os domínios `.example` são ilustrativos. Segredos e dados reais de visitantes nunca devem entrar no Git. O SDK .NET 10 será necessário quando a implementação começar; a documentação não depende da configuração pessoal de um desenvolvedor.
