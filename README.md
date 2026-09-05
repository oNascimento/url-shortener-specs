# Especificações — Encurtador de URLs

Especificações em português para um encurtador com interface React, API ASP.NET Core, PostgreSQL e RabbitMQ. Versão documental 1.0, revisada em 5 de setembro de 2026 com revisão paralela de requisitos, arquitetura/contrato e operação/testes.

## Documentação

| Documento | Conteúdo |
|---|---|
| [Especificação funcional](docs/01-especificacao-funcional.md) | Jornadas, telas, permissões, 23 requisitos funcionais e critérios de aceitação. |
| [Especificação técnica](docs/02-especificacao-tecnica.md) | Arquitetura, Base62, dados, JWT, CSRF, idempotência, filas e retenção. |
| [Operação e testes](docs/03-operacao-e-testes.md) | Capacidade, monitoramento, recuperação, exclusões e 24 cenários de teste. |
| [OpenAPI 3.1](docs/openapi.json) | Contrato de 26 operações de autenticação, gestão, relatórios, administração e redirecionamento. |

Leia os documentos nessa ordem. Os domínios `.example` são ilustrativos e deverão ser substituídos na implantação.

## Decisões do produto

- Código público alfanumérico Base62, gerado a partir de uma sequência numérica, sem preenchimento e com crescimento automático. Maiúsculas e minúsculas são diferentes.
- Destino imutável, links sem expiração automática e códigos nunca reutilizados.
- Usuários autenticados por JWT gerenciam seus links; visitantes não precisam autenticar.
- Cada GET elegível tenta registrar instante UTC e IP; robôs e repetições contam. HEAD não conta.
- Falha apenas na coleta não impede o redirecionamento. Relatórios podem ter lacunas e não comprovam leitura da página de destino.
- Eventos individuais por 90 dias; totais diários enquanto existir a conta, sujeitos às regras de exclusão.
- Esta versão não calcula royalties nem realiza cobranças ou pagamentos.

## Estado da entrega

Este repositório contém especificações e ferramentas de manutenção do contrato. A aplicação ainda será implementada. Metas de carga, disponibilidade e recuperação são critérios futuros de aceite, não resultados medidos.

A referência inicial de capacidade é um milhão de links e pico de mil acessos por segundo. Seis caracteres Base62 oferecem 56.800.235.584 combinações de comprimento fixo, mas nenhum comprimento fixo oferece capacidade infinita.

## Manter o contrato

Com Python 3.12 ou superior, sem dependências externas para os comandos abaixo:

```sh
python scripts/build_contract.py
python scripts/validate_deliverables.py
```

O gerador mantém `docs/openapi.json` a partir de estruturas Python. A validação local confere JSON, referências, parâmetros, segurança declarada, links documentais, cobertura nominal e cálculos; não substitui testes da aplicação nem validação integral por ferramenta OpenAPI.

Para validar também com uma ferramenta especializada, em um ambiente Python isolado:

```sh
python -m pip install openapi-spec-validator==0.9.0
python -m openapi_spec_validator docs/openapi.json
```

Ao alterar comportamento, atualizar requisito funcional, especificação técnica, contrato e casos de teste relacionados na mesma mudança. Não registrar tokens, credenciais ou dados reais de visitantes em exemplos e commits.
