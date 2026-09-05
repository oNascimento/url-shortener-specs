# 005 — Eventos e relatórios

**Estado:** especificado; implementação pendente.

## Objetivo

Persistir eventos sem duplicação técnica e disponibilizar totais diários, eventos e saúde da coleta.

## Regras e limites

Aplicam-se os limites e perfis de [product.md](../../product.md). O contrato e as regras técnicas compartilhadas são normativos; esta funcionalidade não acrescenta pagamentos ou elegibilidade financeira.

<a id="rf-11"></a>

**RF-11 — Detalhes:** destino, código, endereço curto, criação, estado e totais diários para um período de datas UTC. Período padrão: últimos 30 dias incluindo o dia atual; máximo de 366 dias por consulta. Mostrar “Dias em UTC”; não reagrupar esses totais pelo fuso do navegador.

<a id="rf-12"></a>

**RF-12 — Eventos:** tabela paginada com instante e IP, até os últimos 90 dias corridos. Instantes aparecem no fuso do navegador com indicação do fuso; oferecer visualização UTC. IPv4 e IPv6 são suportados. Não usar IP como identidade de pessoa nem agrupar visitas repetidas.

<a id="rf-13"></a>

**RF-13 — Atualização:** relatórios normalmente refletem eventos em até 60 segundos. Mostrar horário de geração, estado da coleta e aviso de atraso quando conhecido. “Sem eventos” é diferente de “dados indisponíveis”. Falha da API nunca deve ser apresentada como total zero.

<a id="rf-14"></a>

**RF-14 — Lacunas:** durante problemas de coleta, mostrar aviso de que alguns acessos podem não estar incluídos. O número exato de perdas pode ser desconhecido; uma publicação sem confirmação pode ter sido processada. Incidentes globais sobrepostos ao período consultado devem gerar aviso conservador, sem afirmar que um link específico perdeu acessos.

Os filtros de totais recebem início e fim inclusivos, sem datas futuras, e incluem dias com zero acessos. Eventos usam início inclusivo e fim exclusivo dentro da retenção. Alterar o filtro reinicia a paginação; atualizar a primeira página permite ver eventos recém-processados. Se um evento vencer durante a navegação, ele não aparece nas próximas páginas. Apresentar a saúde da coleta como “Normal”, “Atrasada”, “Com falhas” ou “Desconhecida”, conforme o contrato; não inferir atraso apenas por ausência de visitas.

<a id="rf-22"></a>

**RF-22 — Retenção:** eventos com IP duram 90 dias corridos; totais diários, enquanto existir a conta. A eliminação física dos eventos vencidos ocorre no ciclo horário seguinte. Consultas nunca retornam eventos anteriores ao corte, mesmo antes da limpeza física.

## Aceitação

Critérios relacionados: AF-04, AF-06, AF-07, AF-13. Consulte as definições em [product.md](../../product.md).

Cenários relacionados: T10, T11, T12, T14, T15, T17, T20. Definições e metas em [operations.md](../../operations.md).

[Plano técnico](plan.md) · [Tarefas](tasks.md) · [Rastreabilidade](../../traceability.md)
