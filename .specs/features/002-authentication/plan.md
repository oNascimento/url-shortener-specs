# Plano técnico — 002

## Dependências

[001](../001-foundation/plan.md)

## Componentes e dados

API de autenticação, ASP.NET Core Identity, emissor JWT, armazenamento de sessões e envio de e-mail.

users, auth_sessions, refresh_tokens e action_tokens; sem armazenar senha ou token em texto puro.

As invariantes de dados, segurança, falha e concorrência estão em [architecture.md](../../architecture.md); não alterar essas decisões por conveniência de implementação.

## Interfaces

| Operação | Método e rota |
|---|---|
| `getCsrfToken` | `GET /api/v1/auth/csrf` |
| `register` | `POST /api/v1/auth/register` |
| `verifyEmail` | `POST /api/v1/auth/verify-email` |
| `resendVerification` | `POST /api/v1/auth/resend-verification` |
| `login` | `POST /api/v1/auth/login` |
| `forgotPassword` | `POST /api/v1/auth/forgot-password` |
| `resetPassword` | `POST /api/v1/auth/reset-password` |
| `refreshSession` | `POST /api/v1/auth/refresh` |
| `logout` | `POST /api/v1/auth/logout` |
| `getMe` | `GET /api/v1/me` |

Requests, responses, erros e segurança: [OpenAPI](../../contracts/openapi.json). Mudanças de comportamento exigem atualização coordenada da especificação e do gerador.

## Sequência de trabalho

1. Implementar cadastro e tokens de ação de uso único, com respostas genéricas.
2. Implementar validação JWT e consulta de conta/sessão em toda operação protegida.
3. Implementar contexto antiforgery anônimo nas rotas de autenticação e rotação transacional do refresh token.
4. Cobrir revogação, concorrência, resultados ambíguos e rotação de chaves; entregar comportamento para integração da interface.

## Testes e conclusão

Executar T04, T05, T06, T07, T24 nos escopos desta funcionalidade. Para regras que cruzam funcionalidades, o aceite final depende da integração correspondente. Guardar comandos, resultados e ambiente como evidência; este documento não afirma que esses testes passaram.

[Tarefas e dependências](tasks.md) · [Especificação](spec.md)
