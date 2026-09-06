# Plano técnico — 001

## Dependências

Nenhuma funcionalidade anterior.

## Componentes e dados

Solução .NET 10; API, redirecionador, worker e jobs separados; frontend React/TypeScript; PostgreSQL, RabbitMQ e e-mail de desenvolvimento.

Esquema compartilhado, relógio UTC injetável, limites transacionais e configuração do registro externo de recuperação.

As invariantes de dados, segurança, falha e concorrência estão em [architecture.md](../../architecture.md); não alterar essas decisões por conveniência de implementação.

## Interfaces

Nenhuma nova operação HTTP. Fornecer infraestrutura e verificações para os contratos existentes.

Requests, responses, erros e segurança: [OpenAPI](../../contracts/openapi.json). Mudanças de comportamento exigem atualização coordenada da especificação e do gerador.

## Sequência de trabalho

1. Preparar solução e projetos sem acoplar a interface ao processo de redirecionamento.
2. Definir configuração validada, segredos de desenvolvimento fora do Git e pontos de integração para relógio, mensageria e registro de recuperação.
3. Preparar PostgreSQL, RabbitMQ e captura local de e-mail, com bootstrap repetível e health checks internos.
4. Disponibilizar os tipos de contrato e a validação para os serviços e a interface.
5. Instrumentar serviços com ILogger JSON e OpenTelemetry; provisionar Collector, Loki, Prometheus e Grafana. Validar o caminho real dos logs e sua privacidade desde a fundação.
6. Criar CI, template de PR e branch `feat/001-foundation`; solicitar aprovação sobre código e evidências antes do merge. A próxima funcionalidade começa na main atualizada após esse merge.

## Escolhas da primeira implementação

Vite, React Router e TanStack Query na interface; xUnit/Testcontainers no backend e Vitest no frontend. O registro externo usa PostgreSQL separado, com volume e recuperação independentes. A migração inicial prepara metadados; entidades de negócio serão acrescentadas pelas funcionalidades responsáveis. Consulte [desenvolvimento local](../../../DEVELOPMENT.md).

Logs usam apenas templates operacionais da aplicação; exceções não são serializadas diretamente. Loki indexa serviço e ambiente, mantendo traceId como metadado. Buffers e retries são limitados, sem bloquear operações por indisponibilidade da coleta de telemetria. Retenção física de 14 dias e alertas operacionais completos terão aceite adicional na 008.

## Testes e conclusão

Executar T20 nos escopos desta funcionalidade. Para regras que cruzam funcionalidades, o aceite final depende da integração correspondente. Guardar comandos, resultados e ambiente como evidência; este documento não afirma que esses testes passaram.

[Tarefas e dependências](tasks.md) · [Especificação](spec.md)
