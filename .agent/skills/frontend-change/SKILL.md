---
name: frontend-change
description: Orienta mudanças React/TypeScript no frontend, com contrato gerado, precisão numérica, acessibilidade, responsividade e testes.
---

# Alteração no frontend

Use esta skill para mudanças em `web`.

## Antes de editar

- Localize a funcionalidade, contrato gerado, API usada e teste existente.
- Confirme os estados de carregamento, erro, vazio, sucesso e indisponibilidade esperados.
- Procure componentes, hooks e estilos equivalentes antes de criar novos.

## Implementação

- Consuma os tipos gerados pelo contrato; não replique manualmente modelos de API.
- Trate IDs e totais de 64 bits como strings para evitar perda de precisão em JavaScript.
- Preserve acessibilidade, navegação por teclado, semântica, feedback de erro e responsividade.
- Não exiba segredos, dados reais de visitantes ou detalhes internos de falhas.
- Mantenha a implementação compatível com a linguagem visual existente e evite refatorações fora da tarefa.
- Diferencie claramente funcionalidades implementadas de estados ainda fundacionais.

## Verificação

Execute primeiro o teste relacionado e, quando aplicável:

```sh
cd web
rtk vitest run
rtk err npm run build
```

Se o contrato gerado ou a integração API foi alterada, execute `npm run generate` e os validadores documentais.
