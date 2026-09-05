# 003 — Gestão de links e Base62

**Estado:** especificado; implementação pendente.

## Objetivo

Criar, listar, consultar e desativar links próprios com códigos curtos únicos e destino imutável.

## Regras e limites

Aplicam-se os limites e perfis de [product.md](../../product.md). O contrato e as regras técnicas compartilhadas são normativos; esta funcionalidade não acrescenta pagamentos ou elegibilidade financeira.

<a id="rf-06"></a>

**RF-06 — Criar:** campo obrigatório “URL de destino”, botão “Encurtar” e estado de envio. Aceitar somente URL absoluta HTTP/HTTPS, com até 8.192 caracteres. Remover espaços externos; rejeitar controles, quebras de linha, credenciais embutidas e o próprio domínio de redirecionamento ou seus aliases configurados. Não buscar a página de destino para validar ou produzir prévia.

<a id="rf-07"></a>

**RF-07 — Resultado:** mostrar endereço curto completo, destino e botão “Copiar link”. Informar sucesso ou falha da cópia. Cada envio intencional cria um novo código, inclusive para uma URL repetida. Uma repetição técnica do mesmo envio deve retornar o mesmo resultado usando a chave de idempotência da API.

A interface desabilita o botão durante o envio e conserva a mesma chave em novas tentativas após erro de rede. A garantia vale por 24 horas; após esse prazo, uma nova criação exige ação explícita. Se a resposta se perder, não afirmar que a criação falhou definitivamente: mostrar resultado desconhecido e oferecer nova tentativa com a chave original. Chave repetida com outro destino apresenta conflito, sem criar link.

<a id="rf-08"></a>

**RF-08 — Listagem:** ordenar do mais novo para o mais antigo, com destino, endereço curto, data de criação e estado. Paginação por cursor; 50 itens por página por padrão, máximo de 100. Estados de carregamento, lista vazia, erro e nova tentativa são obrigatórios. Não calcular totais históricos em cada linha da listagem; eles ficam na tela de detalhes.

<a id="rf-09"></a>

**RF-09 — Destino fixo:** não oferecer edição do destino. Explicar que mudar o destino exige outro link. O destino preserva caminho, consulta e fragmento recebidos, sem acrescentar parâmetros do link curto.

<a id="rf-10"></a>

**RF-10 — Desativar:** ação com confirmação mostrando o link selecionado. Desativação pelo proprietário é definitiva nesta versão. O histórico permanece disponível até a exclusão da conta e respeita a retenção de eventos. Um código nunca é reutilizado.

<a id="rf-17"></a>

**RF-17 — Código:** usar somente `0–9`, `a–z`, `A–Z`; maiúsculas e minúsculas são diferentes. Código gerado automaticamente, sem preenchimento e sem escolha pelo usuário. Exemplo ilustrativo: `https://s.example/Z`.

<a id="rf-18"></a>

**RF-18 — Crescimento:** novos códigos crescem conforme a sequência avança; antigos nunca mudam. Os links são públicos e previsíveis. Não anunciar o código como senha ou proteção de conteúdo.

## Aceitação

Critérios relacionados: AF-01, AF-02, AF-03, AF-05, AF-11, AF-12. Consulte as definições em [product.md](../../product.md).

Cenários relacionados: T01, T02, T03, T08, T17, T20, T21, T22. Definições e metas em [operations.md](../../operations.md).

[Plano técnico](plan.md) · [Tarefas](tasks.md) · [Rastreabilidade](../../traceability.md)
