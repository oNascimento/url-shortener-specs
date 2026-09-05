# 002 — Autenticação e sessões

**Estado:** especificado; implementação pendente.

## Objetivo

Permitir cadastro, verificação, login e recuperação com JWT e sessões revogáveis, isolando as contas.

## Regras e limites

Aplicam-se os limites e perfis de [product.md](../../product.md). O contrato e as regras técnicas compartilhadas são normativos; esta funcionalidade não acrescenta pagamentos ou elegibilidade financeira.

<a id="rf-01"></a>

**RF-01 — Cadastro:** formulário de e-mail e senha, com senha de 12 a 128 caracteres, permitindo espaços, colagem e gerenciadores de senha. Validar e-mail no servidor e apresentar instruções claras de correção. Resposta genérica para e-mail novo ou já existente, sem expor a existência da conta. Enviar confirmação pelo canal de e-mail configurado.

<a id="rf-02"></a>

**RF-02 — Verificação:** token de uso único, válido por 24 horas. O link abre uma tela; a confirmação exige ação explícita por POST, evitando que um GET de um scanner de e-mail consuma o token. Reenvio usa resposta genérica e respeita limitação de frequência.

<a id="rf-03"></a>

**RF-03 — Login:** e-mail e senha. Credenciais inválidas, conta bloqueada e e-mail não verificado produzem a mesma resposta externa de falha; a tela oferece recuperação e reenvio de confirmação. Ao autenticar, abrir “Meus links”.

<a id="rf-04"></a>

**RF-04 — Sessão:** renovar silenciosamente o JWT de 15 minutos enquanto a sessão de até 30 dias estiver válida. No vencimento ou revogação, voltar ao login com mensagem de sessão encerrada, preservando apenas o texto não sensível do formulário de URL. Logout invalida imediatamente a sessão atual, inclusive seu JWT na API. Cada dispositivo tem sessão própria.

<a id="rf-05"></a>

**RF-05 — Recuperação:** solicitar e-mail com resposta genérica; token de redefinição de uso único válido por 30 minutos. A nova senha invalida todas as sessões e demais tokens de recuperação. Não autenticar automaticamente após redefinir a senha.

## Aceitação

Critérios relacionados: AF-08, AF-15. Consulte as definições em [product.md](../../product.md).

Cenários relacionados: T04, T05, T06, T07, T24. Definições e metas em [operations.md](../../operations.md).

[Plano técnico](plan.md) · [Tarefas](tasks.md) · [Rastreabilidade](../../traceability.md)
