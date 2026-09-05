# Plano técnico — 004

## Dependências

[003](../003-link-management/plan.md)

## Componentes e dados

Processo de redirecionamento, middleware de proxies confiáveis e publicador RabbitMQ.

Leitura consistente de links/contas no primário; envelope InternalAccessEnvelope imutável para reentregas.

As invariantes de dados, segurança, falha e concorrência estão em [architecture.md](../../architecture.md); não alterar essas decisões por conveniência de implementação.

## Interfaces

| Operação | Método e rota |
|---|---|
| `redirect` | `GET /{code}` |
| `inspectRedirect` | `HEAD /{code}` |

Requests, responses, erros e segurança: [OpenAPI](../../contracts/openapi.json). Mudanças de comportamento exigem atualização coordenada da especificação e do gerador.

## Sequência de trabalho

1. Implementar roteamento de um segmento, distinção de caixa e consulta do estado no primário.
2. Aplicar métodos, status e headers do contrato, sem cache de redirecionamento nem query forwarding.
3. Capturar UTC/IP confiável, publicar com confirmação e orçamento total de 100 ms.
4. Separar falhas da coleta das falhas de resolução e instrumentar resultados desconhecidos.

## Testes e conclusão

Executar T09, T10, T13 nos escopos desta funcionalidade. Para regras que cruzam funcionalidades, o aceite final depende da integração correspondente. Guardar comandos, resultados e ambiente como evidência; este documento não afirma que esses testes passaram.

[Tarefas e dependências](tasks.md) · [Especificação](spec.md)
