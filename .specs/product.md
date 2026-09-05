# Produto, limites e aceitação

Permitir que uma pessoa autenticada cadastre uma URL, receba um endereço curto e consulte os acessos recebidos. Visitantes acessam o endereço curto sem autenticação e são redirecionados para o destino cadastrado.

Esta versão contabiliza acessos. Não calcula royalties, receita, saldo, cobranças ou pagamentos. O termo “acesso” significa uma requisição GET elegível recebida pelo redirecionador; não comprova que uma pessoa abriu ou leu o destino. Robôs, pré-visualizações que usam GET e visitas repetidas contam.


## 2. Pessoas e permissões

| Perfil | Capacidades |
|---|---|
| Visitante | Acessar links públicos; cadastrar conta; verificar e-mail; recuperar senha. |
| Usuário ativo com e-mail verificado | Criar e consultar seus links, desativá-los, consultar seus eventos e relatórios, encerrar sessão e excluir sua conta. |
| Administrador | Todas as funções de usuário para seus próprios links; pesquisar contas e links, bloquear e desbloquear contas e links, consultar auditoria. |

Nenhuma listagem de usuário pode revelar links ou eventos de outro usuário. A consulta de um identificador de outro proprietário retorna 404. O administrador não recebe, nesta versão, uma tela de consulta de IPs de visitantes de outros usuários.

Contas não verificadas não recebem sessão de acesso. Contas bloqueadas não podem autenticar, renovar sessão nem usar a API protegida. Seus links retornam 410 enquanto durar o bloqueio.

## Premissas aprovadas

Sem expiração automática de links, códigos personalizados, domínios de usuários, exportação em massa, identificação de visitantes únicos, filtragem de robôs ou pagamento. Domínio, fornecedor de e-mail e infraestrutura são configurações de implantação. A implementação usará React, ASP.NET Core, PostgreSQL e RabbitMQ. Mudanças nessas decisões exigem revisão desta versão da especificação.

## Base tecnológica

Backend em **.NET 10 / ASP.NET Core 10**; frontend em React/TypeScript; PostgreSQL e RabbitMQ. O mesmo repositório receberá o código. Esta reorganização não implementa a aplicação.

## Critérios funcionais de aceitação

| Aceite funcional | Resultado verificável |
|---|---|
| AF-01 | Usuário verificado cria, copia e acessa um link com destino preservado. |
| AF-02 | Usuário B não lista nem consulta links/eventos de A; acesso por ID retorna 404. |
| AF-03 | Dois envios intencionais da mesma URL criam códigos diferentes; retry da mesma chave não cria outro. |
| AF-04 | Dez GETs repetidos geram dez eventos; dez reentregas do mesmo evento geram uma contagem. |
| AF-05 | Link desativado retorna 410 e não recebe novos eventos elegíveis. |
| AF-06 | Queda da coleta mantém 302 quando o destino pode ser lido; painel sinaliza incidente conhecido. |
| AF-07 | Evento vencido desaparece da consulta, mas seu total diário permanece. |
| AF-08 | Logout, bloqueio e redefinição de senha invalidam as sessões correspondentes. |
| AF-09 | Exclusão bloqueia links imediatamente e elimina dados conforme os prazos. |
| AF-10 | HEAD de link ativo retorna 302 sem evento; código inexistente retorna 404 e POST público retorna 405. |
| AF-11 | Códigos `z` e `Z` resolvem seus respectivos links; crescimento do código não altera os antigos. |
| AF-12 | URL com caminho, parâmetros repetidos e fragmento mantém o destino; credenciais, controles e domínio próprio são rejeitados. |
| AF-13 | Totais usam datas UTC e exibem dias com zero; falha de consulta e coleta desconhecida não viram total zero. |
| AF-14 | Desbloquear administrativamente não reativa link desativado; usuário comum não acessa funções administrativas. |
| AF-15 | Cadastro, verificação e recuperação oferecem mensagens genéricas; tokens expirados ou já usados não executam a ação. |
| AF-16 | Fluxos de cadastro, criação e consulta funcionam por teclado e em celular; falha de cópia permite selecionar o endereço. |

Cada RF possui definição única em uma funcionalidade. Consulte a [matriz de rastreabilidade](traceability.md) para localizar requisitos, critérios, testes e contratos.
