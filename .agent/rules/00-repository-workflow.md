# Fluxo do repositório

Estas regras se aplicam a qualquer alteração neste repositório.

## Escopo e contexto

- Comece pelo arquivo, símbolo, teste ou comando que evidencia a tarefa.
- Consulte somente a menor área necessária; não mapeie o repositório inteiro sem necessidade.
- Preserve alterações existentes do usuário e nunca use operações destrutivas para limpar o workspace.
- Faça a menor mudança coerente com a arquitetura e os padrões locais.
- Não altere documentação, dependências ou arquivos auxiliares fora do escopo.

## Especificações

- Use `.specs/README.md` como índice das funcionalidades.
- Consulte produto e critérios de aceitação antes da arquitetura; consulte plano e tarefas antes de implementar uma funcionalidade.
- Preserve IDs de requisitos, tarefas, critérios e cenários, especialmente no formato `Fxxx-Txx`.
- Não trate metas documentadas como comportamento implementado sem evidência executável.

## Ferramentas

- Prefira `rtk` para leitura, busca, Git, testes, lint, TypeScript, builds, Docker e logs.
- Prefira `rtk err` quando o objetivo for identificar erros e `rtk test` para runners sem wrapper específico.
- Use saída completa somente quando a compactação esconder a informação necessária.
- Após editar, execute uma validação direcionada antes de ampliar a investigação.

## Git e revisão

- Funcionalidades usam branches `feat/NNN-nome` criadas a partir da `main` atualizada.
- Commits devem referenciar tarefas `Fxxx-Txx`.
- Abra PR draft cedo e descreva comportamento, contrato ou migrações, testes, telemetria e limitações.
- Não faça merge automático; merge exige aprovação explícita e CI verde.
