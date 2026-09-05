# Especificação funcional — Encurtador de URLs

Versão 1.0 • 5 de setembro de 2026 • Base para implementação

## 1. Objetivo e limites

Permitir que uma pessoa autenticada cadastre uma URL, receba um endereço curto e consulte os acessos recebidos. Visitantes acessam o endereço curto sem autenticação e são redirecionados para o destino cadastrado.

Esta versão contabiliza acessos. Não calcula royalties, receita, saldo, cobranças ou pagamentos. O termo “acesso” significa uma requisição GET elegível recebida pelo redirecionador; não comprova que uma pessoa abriu ou leu o destino. Robôs, pré-visualizações que usam GET e visitas repetidas contam.

Entregáveis relacionados: [especificação técnica](02-especificacao-tecnica.md), [operação e testes](03-operacao-e-testes.md) e [contrato OpenAPI](openapi.json). Estes arquivos especificam o produto; não são uma aplicação implementada.

## 2. Pessoas e permissões

| Perfil | Capacidades |
|---|---|
| Visitante | Acessar links públicos; cadastrar conta; verificar e-mail; recuperar senha. |
| Usuário ativo com e-mail verificado | Criar e consultar seus links, desativá-los, consultar seus eventos e relatórios, encerrar sessão e excluir sua conta. |
| Administrador | Todas as funções de usuário para seus próprios links; pesquisar contas e links, bloquear e desbloquear contas e links, consultar auditoria. |

Nenhuma listagem de usuário pode revelar links ou eventos de outro usuário. A consulta de um identificador de outro proprietário retorna 404. O administrador não recebe, nesta versão, uma tela de consulta de IPs de visitantes de outros usuários.

Contas não verificadas não recebem sessão de acesso. Contas bloqueadas não podem autenticar, renovar sessão nem usar a API protegida. Seus links retornam 410 enquanto durar o bloqueio.

## 3. Jornadas e telas

### 3.1 Cadastro e sessão

**RF-01 — Cadastro:** formulário de e-mail e senha, com senha de 12 a 128 caracteres, permitindo espaços, colagem e gerenciadores de senha. Validar e-mail no servidor e apresentar instruções claras de correção. Resposta genérica para e-mail novo ou já existente, sem expor a existência da conta. Enviar confirmação pelo canal de e-mail configurado.

**RF-02 — Verificação:** token de uso único, válido por 24 horas. O link abre uma tela; a confirmação exige ação explícita por POST, evitando que um GET de um scanner de e-mail consuma o token. Reenvio usa resposta genérica e respeita limitação de frequência.

**RF-03 — Login:** e-mail e senha. Credenciais inválidas, conta bloqueada e e-mail não verificado produzem a mesma resposta externa de falha; a tela oferece recuperação e reenvio de confirmação. Ao autenticar, abrir “Meus links”.

**RF-04 — Sessão:** renovar silenciosamente o JWT de 15 minutos enquanto a sessão de até 30 dias estiver válida. No vencimento ou revogação, voltar ao login com mensagem de sessão encerrada, preservando apenas o texto não sensível do formulário de URL. Logout invalida imediatamente a sessão atual, inclusive seu JWT na API. Cada dispositivo tem sessão própria.

**RF-05 — Recuperação:** solicitar e-mail com resposta genérica; token de redefinição de uso único válido por 30 minutos. A nova senha invalida todas as sessões e demais tokens de recuperação. Não autenticar automaticamente após redefinir a senha.

### 3.2 Meus links

**RF-06 — Criar:** campo obrigatório “URL de destino”, botão “Encurtar” e estado de envio. Aceitar somente URL absoluta HTTP/HTTPS, com até 8.192 caracteres. Remover espaços externos; rejeitar controles, quebras de linha, credenciais embutidas e o próprio domínio de redirecionamento ou seus aliases configurados. Não buscar a página de destino para validar ou produzir prévia.

**RF-07 — Resultado:** mostrar endereço curto completo, destino e botão “Copiar link”. Informar sucesso ou falha da cópia. Cada envio intencional cria um novo código, inclusive para uma URL repetida. Uma repetição técnica do mesmo envio deve retornar o mesmo resultado usando a chave de idempotência da API.

A interface desabilita o botão durante o envio e conserva a mesma chave em novas tentativas após erro de rede. A garantia vale por 24 horas; após esse prazo, uma nova criação exige ação explícita. Se a resposta se perder, não afirmar que a criação falhou definitivamente: mostrar resultado desconhecido e oferecer nova tentativa com a chave original. Chave repetida com outro destino apresenta conflito, sem criar link.

**RF-08 — Listagem:** ordenar do mais novo para o mais antigo, com destino, endereço curto, data de criação e estado. Paginação por cursor; 50 itens por página por padrão, máximo de 100. Estados de carregamento, lista vazia, erro e nova tentativa são obrigatórios. Não calcular totais históricos em cada linha da listagem; eles ficam na tela de detalhes.

**RF-09 — Destino fixo:** não oferecer edição do destino. Explicar que mudar o destino exige outro link. O destino preserva caminho, consulta e fragmento recebidos, sem acrescentar parâmetros do link curto.

**RF-10 — Desativar:** ação com confirmação mostrando o link selecionado. Desativação pelo proprietário é definitiva nesta versão. O histórico permanece disponível até a exclusão da conta e respeita a retenção de eventos. Um código nunca é reutilizado.

### 3.3 Detalhes e relatórios

**RF-11 — Detalhes:** destino, código, endereço curto, criação, estado e totais diários para um período de datas UTC. Período padrão: últimos 30 dias incluindo o dia atual; máximo de 366 dias por consulta. Mostrar “Dias em UTC”; não reagrupar esses totais pelo fuso do navegador.

**RF-12 — Eventos:** tabela paginada com instante e IP, até os últimos 90 dias corridos. Instantes aparecem no fuso do navegador com indicação do fuso; oferecer visualização UTC. IPv4 e IPv6 são suportados. Não usar IP como identidade de pessoa nem agrupar visitas repetidas.

**RF-13 — Atualização:** relatórios normalmente refletem eventos em até 60 segundos. Mostrar horário de geração, estado da coleta e aviso de atraso quando conhecido. “Sem eventos” é diferente de “dados indisponíveis”. Falha da API nunca deve ser apresentada como total zero.

**RF-14 — Lacunas:** durante problemas de coleta, mostrar aviso de que alguns acessos podem não estar incluídos. O número exato de perdas pode ser desconhecido; uma publicação sem confirmação pode ter sido processada. Incidentes globais sobrepostos ao período consultado devem gerar aviso conservador, sem afirmar que um link específico perdeu acessos.

Os filtros de totais recebem início e fim inclusivos, sem datas futuras, e incluem dias com zero acessos. Eventos usam início inclusivo e fim exclusivo dentro da retenção. Alterar o filtro reinicia a paginação; atualizar a primeira página permite ver eventos recém-processados. Se um evento vencer durante a navegação, ele não aparece nas próximas páginas. Apresentar a saúde da coleta como “Normal”, “Atrasada”, “Com falhas” ou “Desconhecida”, conforme o contrato; não inferir atraso apenas por ausência de visitas.

### 3.4 Administração e conta

**RF-15 — Administração:** busca paginada por e-mail exato de conta ou código exato de link; visualizar estado, proprietário e destino para moderação. Bloquear/desbloquear exige motivo de 10 a 1.000 caracteres. Auditoria guarda ator, alvo, ação, motivo e instante. Não é permitido bloquear a própria conta administrativa pela interface.

O bloqueio administrativo de link é reversível, mas desbloquear não reativa link desativado pelo proprietário. Bloqueio de conta revoga suas sessões; desbloqueio permite novo login e volta a disponibilizar apenas links que não tenham outro impedimento.

**RF-16 — Exclusão de conta:** solicitar senha atual e confirmação explícita. Bloquear imediatamente a conta e todos os links; agendar eliminação de dados em até 24 horas. A tela explica que cópias de segurança expiram em até 14 dias e não retornam ao serviço sem reaplicação das exclusões. Códigos permanecem reservados, sem URL de destino ou identificação do titular após a limpeza.

| Situação do link | Exibição no painel | Resposta pública |
|---|---|---|
| Conta disponível, sem bloqueio ou desativação | Ativo | GET e HEAD: 302; somente GET conta. |
| Proprietário desativou, com ou sem bloqueio administrativo | Desativado | 410; nenhuma nova visita elegível. |
| Bloqueio administrativo do link ou da conta, sem desativação | Bloqueado | 410; o motivo interno não aparece ao visitante. |
| Exclusão de conta solicitada ou concluída | Conta sem acesso ao painel | 410 para código reservado. |

A indisponibilidade vale para resoluções iniciadas após a confirmação da alteração. Requisições já resolvidas e eventos gerados antes de um bloqueio podem terminar e aparecer depois no relatório. A exclusão de conta também impede a persistência posterior de seus eventos pendentes. Ações administrativas repetidas sem mudança de estado não duplicam a auditoria; alvos em exclusão não podem ser desbloqueados.

## 4. Links curtos e regras públicas

**RF-17 — Código:** usar somente `0–9`, `a–z`, `A–Z`; maiúsculas e minúsculas são diferentes. Código gerado automaticamente, sem preenchimento e sem escolha pelo usuário. Exemplo ilustrativo: `https://s.example/Z`.

**RF-18 — Crescimento:** novos códigos crescem conforme a sequência avança; antigos nunca mudam. Os links são públicos e previsíveis. Não anunciar o código como senha ou proteção de conteúdo.

**RF-19 — Redirecionamento:** GET válido de link disponível tenta registrar evento e retorna 302 com o destino em Location e `Cache-Control: no-store`. Não há tela intermediária, anúncio nem autenticação do visitante.

**RF-20 — Falhas e métodos:** HEAD resolve o destino sem criar evento. Código ausente/inválido retorna 404; link indisponível por desativação, bloqueio ou exclusão retorna 410, sem revelar o motivo interno. Métodos diferentes de GET e HEAD retornam 405 com `Allow: GET, HEAD`. Falha na resolução do destino retorna 503; falha apenas na coleta não impede o 302.

**RF-21 — Contagem:** cada GET elegível tem evento distinto. Reentrega técnica do mesmo evento não duplica a contagem. Consultas inválidas, HEAD, 404, 410, 405 e erros anteriores à resolução não contam. Uma interrupção após a geração do evento pode contar mesmo sem o visitante receber a resposta; não há garantia de “redirecionamento concluído”.

## 5. Retenção, qualidade e aceitação

**RF-22 — Retenção:** eventos com IP duram 90 dias corridos; totais diários, enquanto existir a conta. A eliminação física dos eventos vencidos ocorre no ciclo horário seguinte. Consultas nunca retornam eventos anteriores ao corte, mesmo antes da limpeza física.

**RF-23 — Experiência:** interface em português, responsiva em celular e desktop, navegação por teclado, campos com rótulos, foco visível e mensagens de erro associadas aos campos. Estados não dependem apenas de cor. Endereços e valores fornecidos pelo usuário são exibidos como texto, sem execução de HTML.

Em limitação de frequência, apresentar o tempo de espera indicado pela API e preservar os campos. Erros de autorização, validação e indisponibilidade têm mensagens distintas, sem detalhes internos. Na falha da API, permitir tentativa manual; não repetir silenciosamente operações de desativação, exclusão ou moderação. IPs e URLs completos não aparecem em notificações fora do painel autenticado.

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

## 6. Premissas fechadas

Sem expiração automática de links, códigos personalizados, domínios de usuários, exportação em massa, identificação de visitantes únicos, filtragem de robôs ou pagamento. Domínio, fornecedor de e-mail e infraestrutura são configurações de implantação. A implementação usará React, ASP.NET Core, PostgreSQL e RabbitMQ. Mudanças nessas decisões exigem revisão desta versão da especificação.
