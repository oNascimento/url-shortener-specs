# Eficiência de tokens e contexto

Otimize o uso de contexto e tokens sem comprometer a correção da solução.

## RTK — prioridade para economia de tokens

O ambiente possui o **RTK (rtk-ai/rtk)** instalado.

O RTK deve ser utilizado sempre que possível para reduzir a quantidade de saída enviada ao contexto do modelo.

### Regra principal

* Sempre prefira comandos através do `rtk` quando houver suporte para o comando ou fluxo necessário.
* Antes de executar diretamente um comando que possa gerar saída significativa, verifique se existe uma alternativa equivalente via `rtk`.
* Priorize `rtk` especialmente para:

  * leitura de arquivos;
  * busca de arquivos;
  * busca de texto;
  * Git;
  * testes;
  * lint;
  * TypeScript;
  * package managers;
  * Docker;
  * Kubernetes;
  * logs;
  * JSON;
  * comandos com saída extensa.
* Não utilize o comando original diretamente quando o `rtk` conseguir fornecer as informações necessárias.
* Utilize o comando original apenas quando:

  * o RTK não oferecer suporte adequado;
  * a saída compactada esconder uma informação necessária;
  * houver incompatibilidade;
  * for necessário investigar um problema específico que exija a saída completa.

### Exploração de arquivos com RTK

Prefira:

```bash
rtk ls .
```

em vez de:

```bash
ls
tree
```

Para localizar arquivos, prefira:

```bash
rtk find "<padrão>" .
```

Para pesquisar código, prefira:

```bash
rtk grep "<padrão>" .
```

Para leitura de arquivos, prefira:

```bash
rtk read <arquivo>
```

Quando apenas estrutura, assinaturas ou visão geral forem necessárias, utilize modos mais compactos quando disponíveis:

```bash
rtk read <arquivo> -l aggressive
```

ou:

```bash
rtk smart <arquivo>
```

Não carregue o arquivo completo se a visão compactada for suficiente.

### Git com RTK

Sempre que possível, prefira:

```bash
rtk git status
rtk git diff
rtk git log -n 10
rtk git add
rtk git commit
rtk git push
rtk git pull
```

Evite executar os equivalentes `git` diretamente quando a saída compactada do RTK for suficiente.

### Testes com RTK

Para ferramentas suportadas diretamente, utilize o wrapper específico.

Exemplos:

```bash
rtk jest
rtk vitest
rtk playwright test
```

Para runners sem integração específica, utilize preferencialmente o wrapper genérico:

```bash
rtk test <comando>
```

O objetivo é receber principalmente falhas e informações relevantes em vez de toda a saída dos testes.

### Erros de comandos

Quando o objetivo principal for identificar erros, prefira:

```bash
rtk err <comando>
```

em vez de executar o comando bruto e carregar toda a saída.

Exemplos:

```bash
rtk err dotnet build
rtk err dotnet restore
rtk err npm run build
```

### Comandos .NET com RTK

Quando não houver wrapper específico para `.NET`, utilize os wrappers genéricos do RTK sempre que forem adequados.

Para testes:

```bash
rtk test dotnet test <projeto>
```

Para builds quando o interesse principal forem erros:

```bash
rtk err dotnet build <projeto>
```

Para restore:

```bash
rtk err dotnet restore <projeto>
```

Não force o uso de RTK quando for necessária uma saída específica do `dotnet` que tenha sido removida pela compactação.

Nesse caso:

1. tente primeiro a saída compactada;
2. identifique qual informação está faltando;
3. consulte somente a saída completa necessária;
4. evite repetir desnecessariamente o comando inteiro.

### React, JavaScript e TypeScript com RTK

Para React e TypeScript, priorize:

```bash
rtk tsc
rtk lint
rtk prettier --check .
rtk jest
rtk vitest
rtk playwright test
```

Quando aplicável, utilize:

```bash
rtk pnpm list
```

Para builds Next.js:

```bash
rtk next build
```

Para outros comandos NPM, PNPM ou Yarn que possam gerar muita saída, considere:

```bash
rtk err <comando>
rtk test <comando>
rtk summary <comando>
```

conforme o tipo de operação.

### Docker e infraestrutura

Sempre que possível, prefira:

```bash
rtk docker ps
rtk docker images
rtk docker logs <container>
rtk docker compose ps
```

Para Kubernetes, prefira:

```bash
rtk kubectl pods
rtk kubectl logs <pod>
rtk kubectl services
```

Evite carregar logs brutos extensos no contexto.

### Logs e dados extensos

Para logs:

```bash
rtk log <arquivo>
```

Para JSON:

```bash
rtk json <arquivo>
```

Para comandos extensos sem filtro específico:

```bash
rtk summary <comando>
```

Se nenhuma otimização específica estiver disponível e ainda for útil rastrear o comando através do RTK:

```bash
rtk proxy <comando>
```

### Quando o RTK não for suficiente

Se uma saída compactada esconder informações necessárias:

* Não abandone imediatamente o RTK para todo o restante da tarefa.
* Utilize saída completa apenas para aquele ponto específico.
* Volte a utilizar RTK nos comandos seguintes.
* Evite executar novamente comandos caros apenas para recuperar algumas linhas.
* Quando disponível, utilize o arquivo de saída completa salvo pelo próprio RTK em falhas.

### Monitoramento da economia

Quando for relevante avaliar desperdício de contexto, podem ser utilizados:

```bash
rtk gain
rtk discover
```

Não execute esses comandos rotineiramente durante cada tarefa. Utilize-os apenas para diagnóstico ou otimização do fluxo.

## Exploração do repositório

* Não analise o repositório inteiro, a menos que seja estritamente necessário.
* Antes de abrir arquivos, localize o código relevante.
* Priorize `rtk grep`, `rtk find`, `rtk ls`, `rtk read` e `rtk smart`.
* Abra apenas arquivos que provavelmente estejam relacionados à tarefa.
* Não reabra arquivos que já foram analisados e não foram alterados.
* Prefira trechos específicos, símbolos, métodos e intervalos de linhas em vez de carregar arquivos completos.
* Não investigue módulos não relacionados ao problema atual.
* Expanda a investigação somente quando houver evidências de que isso é necessário.

## Escopo das alterações

* Faça a menor alteração possível que resolva corretamente o problema.
* Não realize refatorações que não estejam diretamente relacionadas à tarefa.
* Não altere documentação, comentários ou arquivos auxiliares sem necessidade.
* Não reescreva arquivos inteiros quando uma alteração localizada for suficiente.
* Preserve o comportamento existente fora do escopo solicitado.
* Evite mudanças cosméticas ou de formatação que não sejam necessárias.

## Uso de comandos

* Use RTK como primeira opção sempre que ele suportar a operação necessária.
* Evite comandos que produzam grandes volumes de saída.
* Filtre logs e resultados antes de analisá-los.
* Durante a implementação, execute testes direcionados ao código alterado.
* Não execute toda a suíte de testes repetidamente durante a investigação.
* Execute testes mais amplos apenas quando houver necessidade ou antes da conclusão da tarefa.
* Quando um comando falhar, analise primeiro apenas os erros relevantes.
* Prefira `rtk err` quando estiver interessado principalmente nos erros.
* Prefira `rtk test` para runners sem suporte específico.
* Não carregue logs completos quando algumas linhas forem suficientes para identificar o problema.

## Projetos .NET

* Prefira executar testes do projeto ou conjunto de testes diretamente relacionado à alteração.
* Prefira `rtk test dotnet test <projeto>` quando a saída compactada for suficiente.
* Evite executar `dotnet test` para toda a solution durante cada iteração.
* Utilize filtros de testes quando possível.
* Evite builds completos da solution se apenas um projeto foi alterado.
* Prefira validar primeiro o projeto afetado.
* Para erros de build, prefira `rtk err dotnet build <projeto>`.
* Ao analisar erros de compilação, concentre-se nos primeiros erros relevantes.
* Não restaure pacotes repetidamente sem necessidade.
* Não analise todos os projetos da solution se a alteração estiver isolada em uma API, biblioteca ou worker específico.
* Ao investigar DI, configurações ou middleware, abra somente os arquivos diretamente relacionados ao fluxo em questão.

## Projetos React

* Não analise toda a árvore de componentes sem necessidade.
* Localize primeiro o componente, hook, contexto, store, serviço ou rota diretamente relacionada à tarefa.
* Utilize `rtk grep` e `rtk find` para localizar componentes, hooks e referências.
* Utilize `rtk read` ou `rtk smart` antes de carregar arquivos grandes por completo.
* Ao investigar um componente, analise inicialmente apenas:

  * o próprio componente;
  * seus hooks diretamente utilizados;
  * os serviços ou APIs consumidos;
  * os tipos/interfaces relevantes;
  * componentes filhos apenas quando necessários para entender o problema.
* Não percorra toda a cadeia de componentes pai/filho se o problema puder ser identificado localmente.
* Não abra arquivos de estilo globais, temas ou configurações gerais se a alteração não envolver aparência ou layout.
* Evite refatorar componentes não relacionados apenas para padronizar código.
* Preserve a estrutura e os padrões já utilizados pelo projeto.
* Antes de criar um novo componente, hook, helper ou serviço, procure rapidamente se já existe algo equivalente.
* Não faça buscas amplas por componentes reutilizáveis sem evidência de que isso seja necessário.
* Prefira alterações localizadas em vez de reorganizar diretórios ou arquitetura.

### Estado e hooks

* Analise apenas o estado diretamente relacionado ao comportamento solicitado.
* Não investigue stores globais inteiras quando apenas uma propriedade ou action estiver envolvida.
* Em Redux, Zustand, Context API ou soluções equivalentes, abra apenas o slice, store, selector ou provider relevante.
* Não altere a estratégia global de gerenciamento de estado sem solicitação explícita.
* Ao investigar `useEffect`, `useMemo`, `useCallback` ou hooks customizados, concentre-se nas dependências e no fluxo diretamente relacionado ao problema.
* Não adicione memoização automaticamente sem evidência de problema de performance.
* Não transforme código em hooks customizados apenas por preferência estilística.

### TypeScript

* Prefira utilizar tipos e interfaces existentes.
* Antes de criar novos tipos, procure primeiro no módulo diretamente relacionado.
* Utilize `rtk grep` para localizar tipos e interfaces.
* Não faça uma busca global extensa quando uma busca específica por nome for suficiente.
* Evite `any` quando o tipo puder ser determinado facilmente a partir do código existente.
* Não altere tipos compartilhados sem avaliar os usos diretamente afetados.
* Para validação TypeScript, prefira `rtk tsc`.
* Não execute verificações de TypeScript em todo o monorepo quando apenas um pacote ou aplicação foi alterado.

### Testes React

* Execute primeiro os testes relacionados ao componente ou módulo alterado.
* Prefira `rtk jest` ou `rtk vitest` quando aplicável.
* Utilize filtros por arquivo, nome do teste ou projeto sempre que possível.
* Não execute toda a suíte de testes após cada pequena alteração.
* Com React Testing Library, priorize validar o comportamento afetado pela mudança.
* Não atualize snapshots automaticamente sem verificar se a alteração é realmente esperada.
* Execute testes mais amplos apenas após a implementação estar estável.

### Build, lint e formatação

* Não execute builds completos após cada alteração.
* Prefira verificações direcionadas quando o projeto permitir.
* Utilize `rtk lint` quando aplicável.
* Utilize `rtk tsc` para validações TypeScript.
* Utilize `rtk prettier --check` quando uma verificação de formatação for necessária.
* Evite executar lint em todo o repositório se somente poucos arquivos foram alterados.
* Não execute formatadores sobre o projeto inteiro.
* Não altere arquivos somente devido a diferenças de formatação fora do escopo da tarefa.
* Evite reinstalar dependências se já estiverem disponíveis.

### Dependências frontend

* Não adicione uma nova dependência sem necessidade clara.
* Antes de adicionar uma biblioteca, verifique se o projeto já possui uma solução equivalente.
* Não atualize versões de dependências que não estejam relacionadas à tarefa.
* Não altere lockfiles sem necessidade.
* Se uma dependência precisar ser adicionada, limite a investigação às opções relevantes para o problema.

### APIs e integração com backend

* Ao investigar chamadas HTTP, concentre-se no endpoint, serviço, hook ou client diretamente envolvido.
* Não analise todos os endpoints da aplicação.
* Preserve os padrões existentes para tratamento de erro, autenticação, loading e cache.
* Ao utilizar React Query, TanStack Query, SWR ou equivalente, investigue somente as queries e mutations relacionadas à tarefa.
* Não invalide caches globais quando uma invalidação mais específica for suficiente.

## Banco de dados e SQL

* Não analise schemas, tabelas ou scripts não relacionados à tarefa.
* Ao investigar queries, concentre-se nas tabelas, índices e planos de execução envolvidos.
* Evite executar consultas que retornem grandes volumes de dados apenas para investigação.
* Utilize filtros, limites e projeções sempre que possível.
* Não reproduza grandes conjuntos de resultados no contexto.

## Subagentes

* Não utilize subagentes para tarefas simples ou sequenciais.
* Utilize subagentes apenas quando houver investigações independentes que possam ser executadas em paralelo.
* Utilize no máximo 2 subagentes, salvo solicitação explícita em contrário.
* Não permita que vários agentes investiguem exatamente a mesma coisa.
* Cada subagente deve receber um objetivo pequeno, específico e independente.
* Evite repassar grandes quantidades de contexto para subagentes.

## Respostas

* Seja conciso.
* Não repita código que já esteja visível no diff ou nos arquivos alterados.
* Não descreva detalhadamente arquivos que não foram modificados.
* Não repita o histórico completo da investigação.
* Informe apenas:

  * decisões importantes;
  * alterações realizadas;
  * testes executados;
  * problemas encontrados;
  * pendências relevantes.

## Tarefas complexas

Para tarefas grandes, siga esta ordem:

1. Identifique a menor área possível do sistema relacionada ao problema.
2. Localize os arquivos e símbolos relevantes utilizando preferencialmente RTK.
3. Investigue somente essa área inicialmente.
4. Formule uma hipótese sobre a causa ou solução.
5. Faça a menor alteração viável.
6. Execute validações direcionadas, preferencialmente através do RTK.
7. Expanda a investigação somente se os resultados indicarem necessidade.
8. Execute validações mais amplas apenas ao final, quando apropriado.

## Gerenciamento de contexto

* RTK é a opção preferencial para comandos de terminal quando houver suporte.
* Evite repetir informações que já estejam disponíveis no contexto atual.
* Não releia arquivos sem necessidade.
* Não carregue arquivos grandes por completo quando apenas uma parte for necessária.
* Resuma descobertas intermediárias em vez de manter grandes volumes de dados brutos no contexto.
* Preserve apenas informações necessárias para continuar a tarefa.
* Quando uma investigação produzir muita saída, retenha apenas conclusões, arquivos relevantes e evidências necessárias.
* Caso precise consultar uma saída bruta após usar RTK, consulte somente a parte necessária e depois volte a utilizar os comandos compactados.

## Prioridade

Sempre priorize, nesta ordem:

1. Correção.
2. Alteração mínima.
3. Validação direcionada.
4. Uso de RTK quando aplicável.
5. Baixo uso de contexto.
6. Baixo uso de tokens.
7. Expansão da investigação somente quando necessária.
