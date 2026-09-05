# Rastreabilidade

RF são definidos uma única vez nos spec.md. AF são definidos em product.md; T em operations.md. As relações abaixo não substituem essas definições. Não há execução de testes de aplicação nesta entrega.

## Requisitos RF

| RF | Definição e responsável | Critérios AF | Cenários T |
|---|---|---|---|
| RF-01 | [002](features/002-authentication/spec.md#rf-01) | AF-15, AF-16 | T04 |
| RF-02 | [002](features/002-authentication/spec.md#rf-02) | AF-15 | T04, T07 |
| RF-03 | [002](features/002-authentication/spec.md#rf-03) | AF-15, AF-16 | T04, T05 |
| RF-04 | [002](features/002-authentication/spec.md#rf-04) | AF-08 | T05, T06, T24 |
| RF-05 | [002](features/002-authentication/spec.md#rf-05) | AF-08, AF-15 | T04, T06, T07 |
| RF-06 | [003](features/003-link-management/spec.md#rf-06) | AF-01, AF-12 | T08 |
| RF-07 | [003](features/003-link-management/spec.md#rf-07) | AF-01, AF-03 | T02, T03 |
| RF-08 | [003](features/003-link-management/spec.md#rf-08) | AF-02 | T17 |
| RF-09 | [003](features/003-link-management/spec.md#rf-09) | AF-12 | T08, T09 |
| RF-10 | [003](features/003-link-management/spec.md#rf-10) | AF-05, AF-14 | T09, T16 |
| RF-11 | [005](features/005-events-and-reports/spec.md#rf-11) | AF-13 | T17, T20 |
| RF-12 | [005](features/005-events-and-reports/spec.md#rf-12) | AF-02, AF-07, AF-13 | T10, T17 |
| RF-13 | [005](features/005-events-and-reports/spec.md#rf-13) | AF-06, AF-13 | T14 |
| RF-14 | [005](features/005-events-and-reports/spec.md#rf-14) | AF-06, AF-13 | T13, T14 |
| RF-15 | [007](features/007-administration-and-privacy/spec.md#rf-15) | AF-08, AF-14 | T16 |
| RF-16 | [007](features/007-administration-and-privacy/spec.md#rf-16) | AF-09 | T16, T19, T23 |
| RF-17 | [003](features/003-link-management/spec.md#rf-17) | AF-11 | T01, T09, T22 |
| RF-18 | [003](features/003-link-management/spec.md#rf-18) | AF-11 | T01, T02, T19, T21, T22 |
| RF-19 | [004](features/004-redirection/spec.md#rf-19) | AF-01, AF-06, AF-10 | T09, T13 |
| RF-20 | [004](features/004-redirection/spec.md#rf-20) | AF-05, AF-06, AF-10 | T09, T13 |
| RF-21 | [004](features/004-redirection/spec.md#rf-21) | AF-04 | T09, T10, T11, T12 |
| RF-22 | [005](features/005-events-and-reports/spec.md#rf-22) | AF-07, AF-09 | T15, T23 |
| RF-23 | [006](features/006-web-dashboard/spec.md#rf-23) | AF-16 | T18 |

## Critérios AF

| AF | Definição | Responsável pela integração | RF relacionados |
|---|---|---|---|
| AF-01 | [Produto](product.md) | [006](features/006-web-dashboard/spec.md) | RF-06, RF-07, RF-19 |
| AF-02 | [Produto](product.md) | [003](features/003-link-management/spec.md) | RF-08, RF-12 |
| AF-03 | [Produto](product.md) | [003](features/003-link-management/spec.md) | RF-07 |
| AF-04 | [Produto](product.md) | [005](features/005-events-and-reports/spec.md) | RF-21 |
| AF-05 | [Produto](product.md) | [004](features/004-redirection/spec.md) | RF-10, RF-20 |
| AF-06 | [Produto](product.md) | [004](features/004-redirection/spec.md) | RF-13, RF-14, RF-19, RF-20 |
| AF-07 | [Produto](product.md) | [005](features/005-events-and-reports/spec.md) | RF-12, RF-22 |
| AF-08 | [Produto](product.md) | [002](features/002-authentication/spec.md) | RF-04, RF-05, RF-15 |
| AF-09 | [Produto](product.md) | [007](features/007-administration-and-privacy/spec.md) | RF-16, RF-22 |
| AF-10 | [Produto](product.md) | [004](features/004-redirection/spec.md) | RF-19, RF-20 |
| AF-11 | [Produto](product.md) | [003](features/003-link-management/spec.md) | RF-17, RF-18 |
| AF-12 | [Produto](product.md) | [003](features/003-link-management/spec.md) | RF-06, RF-09 |
| AF-13 | [Produto](product.md) | [005](features/005-events-and-reports/spec.md) | RF-11, RF-12, RF-13, RF-14 |
| AF-14 | [Produto](product.md) | [007](features/007-administration-and-privacy/spec.md) | RF-10, RF-15 |
| AF-15 | [Produto](product.md) | [002](features/002-authentication/spec.md) | RF-01, RF-02, RF-03, RF-05 |
| AF-16 | [Produto](product.md) | [006](features/006-web-dashboard/spec.md) | RF-01, RF-03, RF-23 |

## Cenários T

| T | Definição | Responsável principal | RF relacionados |
|---|---|---|---|
| T01 | [Operação](operations.md) | [003](features/003-link-management/plan.md) | RF-17, RF-18 |
| T02 | [Operação](operations.md) | [003](features/003-link-management/plan.md) | RF-07, RF-18 |
| T03 | [Operação](operations.md) | [003](features/003-link-management/plan.md) | RF-07 |
| T04 | [Operação](operations.md) | [002](features/002-authentication/plan.md) | RF-01, RF-02, RF-03, RF-05 |
| T05 | [Operação](operations.md) | [002](features/002-authentication/plan.md) | RF-03, RF-04 |
| T06 | [Operação](operations.md) | [002](features/002-authentication/plan.md) | RF-04, RF-05 |
| T07 | [Operação](operations.md) | [002](features/002-authentication/plan.md) | RF-02, RF-05 |
| T08 | [Operação](operations.md) | [003](features/003-link-management/plan.md) | RF-06, RF-09 |
| T09 | [Operação](operations.md) | [004](features/004-redirection/plan.md) | RF-09, RF-10, RF-17, RF-19, RF-20, RF-21 |
| T10 | [Operação](operations.md) | [004](features/004-redirection/plan.md) | RF-12, RF-21 |
| T11 | [Operação](operations.md) | [005](features/005-events-and-reports/plan.md) | RF-21 |
| T12 | [Operação](operations.md) | [005](features/005-events-and-reports/plan.md) | RF-21 |
| T13 | [Operação](operations.md) | [004](features/004-redirection/plan.md) | RF-14, RF-19, RF-20 |
| T14 | [Operação](operations.md) | [005](features/005-events-and-reports/plan.md) | RF-13, RF-14 |
| T15 | [Operação](operations.md) | [005](features/005-events-and-reports/plan.md) | RF-22 |
| T16 | [Operação](operations.md) | [007](features/007-administration-and-privacy/plan.md) | RF-10, RF-15, RF-16 |
| T17 | [Operação](operations.md) | [005](features/005-events-and-reports/plan.md) | RF-08, RF-11, RF-12 |
| T18 | [Operação](operations.md) | [006](features/006-web-dashboard/plan.md) | RF-23 |
| T19 | [Operação](operations.md) | [008](features/008-production-readiness/plan.md) | RF-16, RF-18 |
| T20 | [Operação](operations.md) | [001](features/001-foundation/plan.md) | RF-11 |
| T21 | [Operação](operations.md) | [003](features/003-link-management/plan.md) | RF-18 |
| T22 | [Operação](operations.md) | [003](features/003-link-management/plan.md) | RF-17, RF-18 |
| T23 | [Operação](operations.md) | [007](features/007-administration-and-privacy/plan.md) | RF-16, RF-22 |
| T24 | [Operação](operations.md) | [008](features/008-production-readiness/plan.md) | RF-04 |

## Operações OpenAPI

| operationId | Método e rota | Responsável | Contrato |
|---|---|---|---|
| `getCsrfToken` | `GET /api/v1/auth/csrf` | [002](features/002-authentication/plan.md) | [OpenAPI](contracts/openapi.json) |
| `register` | `POST /api/v1/auth/register` | [002](features/002-authentication/plan.md) | [OpenAPI](contracts/openapi.json) |
| `verifyEmail` | `POST /api/v1/auth/verify-email` | [002](features/002-authentication/plan.md) | [OpenAPI](contracts/openapi.json) |
| `resendVerification` | `POST /api/v1/auth/resend-verification` | [002](features/002-authentication/plan.md) | [OpenAPI](contracts/openapi.json) |
| `login` | `POST /api/v1/auth/login` | [002](features/002-authentication/plan.md) | [OpenAPI](contracts/openapi.json) |
| `forgotPassword` | `POST /api/v1/auth/forgot-password` | [002](features/002-authentication/plan.md) | [OpenAPI](contracts/openapi.json) |
| `resetPassword` | `POST /api/v1/auth/reset-password` | [002](features/002-authentication/plan.md) | [OpenAPI](contracts/openapi.json) |
| `refreshSession` | `POST /api/v1/auth/refresh` | [002](features/002-authentication/plan.md) | [OpenAPI](contracts/openapi.json) |
| `logout` | `POST /api/v1/auth/logout` | [002](features/002-authentication/plan.md) | [OpenAPI](contracts/openapi.json) |
| `getMe` | `GET /api/v1/me` | [002](features/002-authentication/plan.md) | [OpenAPI](contracts/openapi.json) |
| `requestAccountDeletion` | `POST /api/v1/me/deletion` | [007](features/007-administration-and-privacy/plan.md) | [OpenAPI](contracts/openapi.json) |
| `createLink` | `POST /api/v1/links` | [003](features/003-link-management/plan.md) | [OpenAPI](contracts/openapi.json) |
| `listLinks` | `GET /api/v1/links` | [003](features/003-link-management/plan.md) | [OpenAPI](contracts/openapi.json) |
| `getLink` | `GET /api/v1/links/{linkId}` | [003](features/003-link-management/plan.md) | [OpenAPI](contracts/openapi.json) |
| `deactivateLink` | `POST /api/v1/links/{linkId}/deactivate` | [003](features/003-link-management/plan.md) | [OpenAPI](contracts/openapi.json) |
| `getLinkStats` | `GET /api/v1/links/{linkId}/stats` | [005](features/005-events-and-reports/plan.md) | [OpenAPI](contracts/openapi.json) |
| `listLinkEvents` | `GET /api/v1/links/{linkId}/events` | [005](features/005-events-and-reports/plan.md) | [OpenAPI](contracts/openapi.json) |
| `adminListUsers` | `GET /api/v1/admin/users` | [007](features/007-administration-and-privacy/plan.md) | [OpenAPI](contracts/openapi.json) |
| `adminListLinks` | `GET /api/v1/admin/links` | [007](features/007-administration-and-privacy/plan.md) | [OpenAPI](contracts/openapi.json) |
| `adminBlockUsers` | `POST /api/v1/admin/users/{userId}/block` | [007](features/007-administration-and-privacy/plan.md) | [OpenAPI](contracts/openapi.json) |
| `adminUnblockUsers` | `POST /api/v1/admin/users/{userId}/unblock` | [007](features/007-administration-and-privacy/plan.md) | [OpenAPI](contracts/openapi.json) |
| `adminBlockLinks` | `POST /api/v1/admin/links/{linkId}/block` | [007](features/007-administration-and-privacy/plan.md) | [OpenAPI](contracts/openapi.json) |
| `adminUnblockLinks` | `POST /api/v1/admin/links/{linkId}/unblock` | [007](features/007-administration-and-privacy/plan.md) | [OpenAPI](contracts/openapi.json) |
| `adminListAudit` | `GET /api/v1/admin/audit` | [007](features/007-administration-and-privacy/plan.md) | [OpenAPI](contracts/openapi.json) |
| `redirect` | `GET /{code}` | [004](features/004-redirection/plan.md) | [OpenAPI](contracts/openapi.json) |
| `inspectRedirect` | `HEAD /{code}` | [004](features/004-redirection/plan.md) | [OpenAPI](contracts/openapi.json) |

As funcionalidades 001, 006 e 008 não acrescentam endpoints. Elas preparam infraestrutura, consomem o contrato e verificam a operação. Tarefas são identificadas por `Fnnn-Tnn`, evitando confusão com os cenários `Tnn`.
