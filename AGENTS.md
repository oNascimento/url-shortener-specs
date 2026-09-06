# Eficiência de tokens e contexto

Otimize o uso de contexto e tokens sem comprometer a correção da solução.

## Exploração do repositório

* Não analise o repositório inteiro, a menos que seja estritamente necessário.
* Antes de abrir arquivos, localize o código relevante usando buscas como `rg`, `find` ou busca por símbolos.
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

* Evite comandos que produzam grandes volumes de saída.
* Filtre logs e resultados antes de analisá-los.
* Quando possível, utilize `rg`, `grep`, `head`, `tail` ou filtros equivalentes para reduzir a saída.
* Durante a implementação, execute testes direcionados ao código alterado.
* Não execute toda a suíte de testes repetidamente durante a investigação.
* Execute testes mais amplos apenas quando houver necessidade ou antes da conclusão da tarefa.
* Quando um comando falhar, analise primeiro apenas os erros relevantes e as linhas próximas ao erro.
* Não carregue logs completos quando algumas linhas forem suficientes para identificar o problema.

## Projetos .NET

* Prefira executar testes do projeto ou conjunto de testes diretamente relacionado à alteração.
* Evite executar `dotnet test` para toda a solução durante cada iteração.
* Utilize filtros de testes quando possível.
* Evite builds completos da solução se apenas um projeto foi alterado.
* Prefira `dotnet build` no projeto afetado antes de executar um build completo da solução.
* Ao analisar erros de compilação, concentre-se nos primeiros erros relevantes em vez de processar toda a saída.
* Não restaure pacotes repetidamente sem necessidade.
* Não analise todos os projetos da solution se a alteração estiver isolada em uma API, biblioteca ou worker específico.
* Ao investigar DI, configurações ou middleware, abra somente os arquivos diretamente relacionados ao fluxo em questão.

## Projetos React

* Não analise toda a árvore de componentes sem necessidade.
* Localize primeiro o componente, hook, contexto, store, serviço ou rota diretamente relacionada à tarefa.
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
* Não faça uma busca global extensa por tipos se uma busca específica por nome for suficiente.
* Evite `any` quando o tipo puder ser determinado facilmente a partir do código existente.
* Não altere tipos compartilhados sem avaliar os usos diretamente afetados.
* Não execute verificações de TypeScript em todo o monorepo quando apenas um pacote ou aplicação foi alterado.

### Testes React

* Execute primeiro os testes relacionados ao componente ou módulo alterado.
* Com Jest ou Vitest, utilize filtros por arquivo, nome do teste ou projeto sempre que possível.
* Não execute toda a suíte de testes após cada pequena alteração.
* Com React Testing Library, priorize validar o comportamento afetado pela mudança.
* Não atualize snapshots automaticamente sem verificar se a alteração é realmente esperada.
* Execute testes mais amplos apenas após a implementação estar estável.

### Build, lint e formatação

* Não execute `npm run build`, `yarn build` ou `pnpm build` completo após cada alteração.
* Prefira verificações direcionadas quando o projeto permitir.
* Evite executar lint em todo o repositório se somente poucos arquivos foram alterados.
* Utilize lint apenas nos arquivos afetados quando possível.
* Não execute formatadores sobre o projeto inteiro.
* Não altere arquivos somente devido a diferenças de formatação fora do escopo da tarefa.
* Evite executar `npm install`, `yarn install` ou `pnpm install` se as dependências já estiverem disponíveis.

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
2. Localize os arquivos e símbolos relevantes.
3. Investigue somente essa área inicialmente.
4. Formule uma hipótese sobre a causa ou solução.
5. Faça a menor alteração viável.
6. Execute validações direcionadas.
7. Expanda a investigação somente se os resultados indicarem necessidade.
8. Execute validações mais amplas apenas ao final, quando apropriado.

## Gerenciamento de contexto

* Evite repetir informações que já estejam disponíveis no contexto atual.
* Não releia arquivos sem necessidade.
* Não carregue arquivos grandes por completo quando apenas uma parte for necessária.
* Resuma descobertas intermediárias em vez de manter grandes volumes de dados brutos no contexto.
* Preserve apenas informações necessárias para continuar a tarefa.
* Quando uma investigação produzir muita saída, retenha apenas conclusões, arquivos relevantes e evidências necessárias.

## Prioridade

Sempre priorize, nesta ordem:

1. Correção.
2. Alteração mínima.
3. Validação direcionada.
4. Baixo uso de contexto.
5. Baixo uso de tokens.
6. Expansão da investigação somente quando necessária.
