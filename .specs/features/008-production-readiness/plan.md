# Plano técnico — 008

## Dependências

[005](../005-events-and-reports/plan.md), [007](../007-administration-and-privacy/plan.md)

## Componentes e dados

Infraestrutura, monitoramento, backups/WAL, automação de restauração, teste k6 e pipeline de liberação.

Manifesto de execução, métricas sem alta cardinalidade, ledger externo preservado e evidências de testes.

As invariantes de dados, segurança, falha e concorrência estão em [architecture.md](../../architecture.md); não alterar essas decisões por conveniência de implementação.

## Interfaces

Nenhuma nova operação HTTP. Fornecer infraestrutura e verificações para os contratos existentes.

Requests, responses, erros e segurança: [OpenAPI](../../contracts/openapi.json). Mudanças de comportamento exigem atualização coordenada da especificação e do gerador.

## Sequência de trabalho

1. Configurar topologia de produção, alertas, retenções e backups segundo operations.md.
2. Automatizar ensaio de restore com isolamento do escritor anterior, reservas de IDs e reaplicação de exclusões.
3. Executar ensaios de carga saudável e de falha separadamente, incluindo link quente.
4. Registrar medições reais e bloquear liberação se algum critério obrigatório não tiver evidência.

## Testes e conclusão

Executar T13, T14, T19, T21, T22, T23, T24 nos escopos desta funcionalidade. Para regras que cruzam funcionalidades, o aceite final depende da integração correspondente. Guardar comandos, resultados e ambiente como evidência; este documento não afirma que esses testes passaram.

[Tarefas e dependências](tasks.md) · [Especificação](spec.md)
