# Arquitetura e segurança

Estas invariantes devem ser preservadas em qualquer implementação ou revisão.

## Stack e domínio

- Respeite .NET 10 / ASP.NET Core 10, React/TypeScript, PostgreSQL e RabbitMQ.
- Códigos públicos usam Base62 sobre sequência numérica; destinos são imutáveis.
- Valores e totais de 64 bits devem permanecer como strings no contrato consumido pelo frontend.
- Falhas de coleta de métricas de acesso não podem impedir um redirecionamento elegível.
- Não invente endpoints de negócio nesta entrega fundacional: confirme primeiro o contrato e a funcionalidade correspondente.

## Segredos e dados sensíveis

- Nunca registre, comite ou exponha `.env`, senhas, tokens, credenciais, dados reais de visitantes ou certificados locais.
- Não habilite logging automático de corpos HTTP, headers, comandos SQL ou exceções brutas.
- Logs devem conter somente campos operacionais aprovados e categorias `Shortener` em nível Information ou superior.
- A fronteira HTTP deve retornar Problem Details sem detalhes sensíveis de bibliotecas, SQL, credenciais ou URLs privadas.
- Não use certificados locais de desenvolvimento em produção.

## Persistência e integração

- Mantenha separados o banco principal e o registro externo, inclusive em restaurações.
- Migrações devem pertencer à funcionalidade que introduz o modelo; a fundação cria somente metadados de esquema.
- Telemetria não participa da transação de negócio e pode perder dados sob saturação.
- Preserve limites, expiração e comportamento de retry documentados antes de alterar infraestrutura.
