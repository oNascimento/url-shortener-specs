# Plano de operação e testes — Encurtador de URLs

Versão 1.0 • 5 de setembro de 2026

## 1. Critérios de entrada em produção

Este documento define verificações a executar quando houver implementação. Nenhum resultado de carga, disponibilidade ou recuperação foi medido nesta entrega documental.

Produção inicial: duas instâncias da API, duas do redirecionador e duas do worker; um executor ativo dos jobs por lock consultivo PostgreSQL. PostgreSQL com primário e standby em domínio de falha distinto; RabbitMQ com três nós e quorum queues. Usar balanceador TLS e relógios sincronizados. Desenvolvimento pode usar instâncias únicas, mas os testes de falha exigem topologia equivalente à produção.

Infraestrutura pode ser gerenciada ou em contêineres; não é necessário Kubernetes. Antes do teste de aceite, registrar provedor, região, CPU, memória, IOPS, discos, rede, versões exatas, limites de conexão e configuração do balanceador em um manifesto de execução. Escolher tamanhos a partir da medição; não apresentar esta topologia como garantia de vazão.

| Meta | Medição e condição |
|---|---|
| Capacidade inicial | 1.000.000 de links persistidos e pico de 1.000 GET/s por 15 minutos. |
| Latência | p95 < 200 ms no serviço, incluindo leitura e tentativa de publicação; excluir rede do visitante e destino. |
| Erros em carga saudável | Menos de 0,1% de respostas 5xx; nenhum código duplicado ou redirecionamento incorreto. |
| Atualização de relatórios | p99 de persistência menos occurredAt <= 60 s em condição normal; filas drenam após o pico. |
| Publicação | Caminho da tentativa limitado a 100 ms, sem fila em memória ilimitada. |
| Disponibilidade proposta | 99,9% mensal para resolução de links ativos; perdas de eventos são métrica separada. |
| Recuperação | RPO de banco <= 5 min e RTO <= 60 min, comprovados em exercício. Eventos ainda não confirmados não têm RPO garantido. |
| Exclusão | Dados ativos da conta eliminados em até 24 h; backup expira em até 14 dias. |

RPO é a janela potencial de dados perdidos; RTO é o tempo para restaurar o serviço. Não prometer zero perda de links, eventos ou sessões em desastre. Falha de restauração pode perder códigos divulgados após o ponto restaurado; eles devem retornar 404 e nunca ser atribuídos a outra URL.

## 2. Capacidade e crescimento

Usar tráfego médio para dimensionar retenção e pico para CPU/conexões. Mil acessos por segundo durante 15 minutos produzem 900.000 tentativas; sustentados por 24 horas, 86.400.000; por 90 dias, 7.776.000.000.

| Média ilustrativa | Eventos/dia | Eventos/90 dias | Volume a 200 bytes/evento |
|---|---:|---:|---:|
| 10/s | 864.000 | 77.760.000 | 15,55 GB |
| 100/s | 8.640.000 | 777.600.000 | 155,52 GB |
| 1.000/s | 86.400.000 | 7.776.000.000 | 1,56 TB |

200 bytes é apenas hipótese de cálculo, não tamanho medido. Valores decimais excluem índices adicionais, WAL, réplicas, espaço livre, fila e backups. Medir `pg_total_relation_size` após um milhão de eventos representativos; calcular bytes/evento com índices e multiplicar pela taxa média, 90 dias e fator de segurança de 1,5. Dimensionar WAL e backup separadamente. Manter ao menos 30% de disco livre no banco e broker.

Fila: dimensionar para 1 hora de pico (3,6 milhões de eventos) mais margem, medindo tamanho real das mensagens persistidas e replicação. Configurar limite de bytes de fila compatível com esse orçamento e overflow que rejeite novas publicações, sem descartar silenciosamente mensagens antigas. Rejeições tornam-se falhas de coleta; o redirecionamento continua.

Ampliar consumidores se idade da fila passar de 30 s por 5 min e banco tiver folga. Investigar banco se CPU/I/O exceder 70% por 15 min ou locks limitarem consumo. Planejar expansão de armazenamento ao atingir 60%; agir urgentemente a 75%. Isolar eventos em armazenamento separado antes que afetem a latência de resolução. Todas as mudanças repetem o ensaio de carga relevante.

## 3. Observabilidade e incidentes

Instrumentar com OpenTelemetry, métricas Prometheus e painéis Grafana, ou serviços que preservem essas mesmas métricas. Reter logs técnicos por 14 dias, métricas por 90 dias e incidentes por 1 ano. Logs não contêm senha, token, cookie, IP de visitante ou URL completa. traceId não é eventId público e não deve incorporar informação do usuário.

| Sinal | Alerta inicial | Resposta |
|---|---|---|
| Latência do redirecionador | p95 > 200 ms por 5 min | Examinar consulta do link, pool e limite de publicação. |
| Erros de resolução | 5xx > 1% por 5 min | Verificar banco e conexões; não confundir 404/410 com indisponibilidade. |
| Publicação rejeitada/sem confirmação | Qualquer ocorrência abre incidente; > 0,1% por 5 min aciona plantão | Conferir broker, roteamento, confirmação e alarmes de disco. |
| Idade da fila | > 60 s por 2 min | Sinalizar atraso e recuperar consumidores. |
| Quarentena | Qualquer mensagem | Inspecionar versão/schema sem divulgar IP em logs; corrigir e reprocessar. |
| Coleta desconhecida | Monitor sem atualização por 60 s | Painel apresenta unknown; investigar monitor. |
| Partições | Menos de 2 dias futuros disponíveis | Reexecutar provisionamento antes da virada UTC. |
| Retenção/exclusão | Job sem sucesso por 2 h; exclusão pendente > 20 h | Corrigir antes dos prazos de 90 dias + ciclo horário / 24 h. |
| Backup/WAL | Backup diário ausente ou archive lag > 5 min | Restaurar captura; declarar descumprimento de RPO se aplicável. |

Métricas usam rótulos de serviço, resultado e classe de erro; não usar linkId, URL, IP ou eventId como labels de alta cardinalidade. Comparar GETs elegíveis, publicações confirmadas, resultados desconhecidos, eventos inseridos e duplicatas descartadas. Contadores de processos também podem se perder em crash; não apresentar diferença como número exato de acessos perdidos.

Incidente de coleta: registrar intervalo assim que detectado; manter redirecionamento; recuperar broker/worker; acompanhar drenagem e duplicatas; fechar intervalo após saúde estável por 5 min. Não inventar eventos a partir de totais. Relatórios preservam aviso sobre o intervalo mesmo após recuperação.

## 4. Backup, retenção e recuperação

Backup físico diário PostgreSQL com arquivamento contínuo de WAL, criptografado, fora do domínio de falha principal e retenção de 14 dias. Excluir automaticamente cópias expiradas e testar isso. Configurações de broker, IaC e chaves públicas JWT são versionadas; segredos têm recuperação protegida. Réplica RabbitMQ não é backup nem substitui idempotência.

Manter registro de exclusões e limite superior de IDs reservados em armazenamento durável separado do PITR principal, com escrita condicional linearizável e acesso restrito. Não restaurar esse registro para uma versão antiga ao restaurar o banco. Uma reserva só está autorizada após confirmação durável; resultado ambíguo exige releitura, sem presumir falha nem emitir IDs de uma faixa ainda não autorizada. Se o registro não puder ser recuperado com confiança, manter criação indisponível até resolver a integridade; redirecionamentos existentes podem continuar.

Para evitar reutilização após restauração, reservar faixas de um milhão de IDs com limite **inclusivo** U: confirmar no registro externo o novo limite antes de permitir emissão da faixa; um único coordenador com compare-and-swap avança o limite. A primeira faixa é [1, 1.000.000]. Aplicar `MAXVALUE=U` na sequência; elevar esse limite somente após confirmar nova reserva externa. A sequência PostgreSQL continua alocando cada ID; instâncias da API não recebem geradores locais. Se o registro estiver indisponível, a faixa já autorizada pode continuar; novas faixas não podem ser abertas. Solicitar extensão quando restarem 20% da faixa, sem reiniciar a sequência. Saltos são aceitáveis.

Após restore, ler o limite externo anterior U, reservar nova faixa e elevar MAXVALUE antes de executar `setval(sequence, U + 1, false)` com criação ainda suspensa. Nunca usar `MAX(id)+1`. Usar aritmética verificada para calcular `min(1.000.000, BIGINT_MAX - U)`; se U já for BIGINT_MAX, não somar 1 e retornar 503 para criação por capacidade esgotada. O limite inclusivo evita exigir BIGINT_MAX+1 no registro. Validar que apenas o primário eleito emite IDs: isolar o primário antigo, invalidar suas credenciais/conexões e impedir seu retorno como escritor antes de promover ou restaurar outro banco. A reserva de faixas não substitui esse isolamento contra dois primários ativos.

Runbook de restauração:

1. Isolar ambiente restaurado; suspender criação, redirecionamento público e consumidores. Confirmar isolamento do primário antigo e exclusividade do novo escritor antes de conectar serviços.
2. Restaurar backup e WAL até ponto escolhido; registrar início, ponto recuperado e perdas potenciais.
3. Reaplicar registro externo de exclusões, limpar dados vencidos e bloquear sessões restauradas; exigir novo login de todos os usuários.
4. Ler U externo, autorizar nova faixa e iniciar sequência em U+1 conforme o procedimento acima, antes de reabrir criação. Conferir amostra de códigos e destinos sem reutilizar IDs ausentes; se não houver nova faixa, manter somente criação indisponível.
5. Conferir partições, constraints, totais e idempotência. Recuperar consumidores; broker disponível pode conter eventos já persistidos, que serão deduplicados.
6. Executar smoke tests e reabrir tráfego gradualmente; registrar lacunas que não possam ser recuperadas.

Eventos confirmados e já consumidos após o ponto restaurado podem ser perdidos no PITR; a fila não garante replay de mensagens já confirmadas pelo consumidor. Documentar esse intervalo, sem prometer reconstrução.

Exercício mensal em ambiente isolado: restaurar, reaplicar exclusão de uma conta de teste, confirmar ausência de IPs vencidos e validar que código emitido após o backup não é reutilizado. Retenção de auditoria: 1 ano, com remoção/anonimização de conteúdo pessoal de contas excluídas. Registro externo de exclusões dura 30 dias; reservas de IDs são permanentes.

O aceite da exclusão exige marca durável no registro externo antes da resposta de sucesso. Se a gravação não puder ser confirmada, retornar falha temporária e permitir repetição idempotente; não declarar a exclusão concluída. O registro conserva apenas identificador da conta, instante e estado, sem e-mail, URL ou IP. Reaplicações também removem dados de autenticação e informações pessoais de auditoria. O job registra progresso por lote para retomar sem reativar a conta. Medir o prazo de 24 horas a partir do primeiro aceite; reenvios não reiniciam esse relógio.

A limpeza de 24 horas inclui banco, fila principal e quarentena. Consumidores descartam eventos de contas em exclusão ou de links tombstonados (deleted_at preenchido, mesmo com owner_id já nulo); o operador deve garantir que mensagens elegíveis para descarte sejam consumidas dentro do prazo, inclusive em quarentena. RabbitMQ não oferece exclusão seletiva arbitrária por conta na fila: se necessário, executar consumidor de saneamento com o worker normal pausado, descartar envelopes dos links excluídos e republicar os demais preservando identidade, com confirmação antes do ack. Usar fila de destino separada para não reler indefinidamente mensagens republicadas: criar destino durável, adicionar binding antes de remover o binding da origem e então drenar a origem. A sobreposição pode duplicar mensagens, mas evita uma janela sem roteamento. Retomar consumidores na nova fila por configuração versionada; remover a fila anterior somente depois de zerar mensagens prontas e não confirmadas. Aplicar o procedimento separadamente à fila principal e à quarentena. Envelopes malformados sem linkId atribuível devem ser descartados conservadoramente durante saneamento da quarentena antes do prazo de 24 horas, com métrica e incidente, sem registrar o conteúdo pessoal. Duplicatas durante falhas serão deduplicadas pelo worker. Nunca purgar toda a fila para apagar uma conta. Se uma indisponibilidade impedir o prazo, registrar incidente de exclusão e comunicar o atraso; não afirmar cumprimento. Backups seguem a exceção declarada de 14 dias, com exclusões reaplicadas antes de reabertura.

## 5. Matriz de testes

Usar testes unitários para regras determinísticas, integração com PostgreSQL/RabbitMQ reais em contêineres e testes ponta a ponta com navegador. Relógio injetável permite validar expiração e retenção sem esperar dias. Nenhum teste envia e-mail real nem abre destino externo; usar servidor local de captura.

| ID / referência | Cenário | Resultado esperado |
|---|---|---|
| T01 / RF-17–18 | Vetores Base62 1, 9, 10, 35, 36, 61, 62, 3843, 3844, 62^6-1, 62^6 e BIGINT máximo | Alfabeto correto, transições sem padding, máximo <= 11 caracteres, sem overflow. |
| T02 / RF-07 | 10.000 criações com 100 clientes concorrentes; abortar parte das transações | Nenhum código duplicado/reutilizado; lacunas aceitas. |
| T03 / RF-07 | Mesma chave e URL concorrentes; mesma chave com outra URL; chave nova com mesma URL | Um link no primeiro caso, 409 no segundo, outro link no terceiro. |
| T04 / RF-01–05 | Verificação/reset expirados, token reutilizado, e-mail inexistente e senha inválida | Tokens de uso único, mensagens sem enumeração e sessões revogadas após reset. |
| T05 / RF-04 | JWT expirado, assinatura errada, algoritmo diferente, iss/aud incorretos | 401; nenhuma operação executada. |
| T06 / RF-04 | Refresh simultâneo, reuso consumido, resposta perdida e logout | Rotação atômica; reuso revoga família; cliente pede login em ambiguidade; logout imediato. |
| T07 / RF-02–05 | POST sem antiforgery, Origin estranho e GET de link de verificação por scanner | 403 para CSRF inválido; GET não consome token. |
| T08 / RF-06 | HTTP/HTTPS, IDN, path case-sensitive, `%2F`, query repetida, fragmento, CR/LF, credenciais, host próprio com caixa/ponto/porta | Preservação válida e rejeição das entradas proibidas; nenhum fetch no backend. |
| T09 / RF-19–21 | GET/HEAD, código Z versus z, inexistente, desativado, POST e query extra no código | Status e headers previstos; eventos só para GET elegível; query extra não altera destino. |
| T10 / RF-12,21 | IPv4, IPv6, IPv4-mapped, X-Forwarded-For falso e cadeia de proxies confiáveis | IP correto, sem aceitar falsificação pelo visitante. |
| T11 / RF-21 | 100 GETs mesmo IP; reentregar cada envelope 10 vezes; falhar após commit antes de ack | Exatamente 100 eventos e soma 100. |
| T12 / RF-21 | Mesmo lote com mensagens duplicadas; falha no UPSERT de totais | Deduplicação interna; rollback também dos eventos; retry não perde contagem. |
| T13 / RF-20 | Broker parado, fila sem binding, nack, timeout e saturação do pool | 302 com destino correto; orçamento de publicação respeitado; resultado desconhecido distinguido de rejeição. |
| T14 / RF-13–14 | Worker parado, monitor parado, nenhuma visita, retomada da fila | delayed, unknown, healthy sem tráfego e recuperação corretos; máximo timestamp não vira watermark. |
| T15 / RF-22 | Eventos em torno de agora-90 dias e meia-noite UTC; replay de evento expirado | Consulta aplica corte exato; job remove vencidos; replay não altera totais. |
| T16 / RF-15–16 | Exclusão concorrente com consumo; block/unblock e desativação do proprietário | Não reinserir dados excluídos; desbloqueio não reativa desativação definitiva. |
| T17 / RF-08,11–12 | Cursor adulterado, filtro alterado, timestamps empatados, datas inválidas, zero eventos | 400 para cursor inválido; ordem estável; zero real distinto de falha. |
| T18 / RF-23 | Navegação por teclado, celular, cópia sem permissão e URL com HTML | Fluxos acessíveis, erro de cópia compreensível, texto sem execução. |
| T19 / operação | Restore de backup anterior a exclusão e emissão de novos IDs | Exclusões reaplicadas, sessão antiga inválida, nenhum código reutilizado. |
| T20 / contrato | Requests/responses contra OpenAPI; IDs acima de 2^53 | Contrato respeitado e strings preservam precisão. |
| T21 / operação | Registro de reservas indisponível, confirmação de reserva perdida e restauração com primário antigo ainda acessível | Emissão só dentro da faixa confirmada; releitura resolve ambiguidade; criação não reabre sem isolamento do escritor anterior. |
| T22 / operação | Faixa final de BIGINT e sequência esgotada | Última faixa não ultrapassa BIGINT_MAX; criação retorna 503 ao esgotar, sem overflow, reutilização ou alteração de códigos antigos. |
| T23 / RF-16 | Aceite de exclusão com registro externo fora do ar; tombstone gravado e resposta perdida; saneamento com mensagens em fila/quarentena | Nenhum falso sucesso; retry mantém mesma exclusão/prazo; IP removido em até 24 h no ensaio saudável; outros eventos preservados e deduplicados. |
| T24 / operação | Rotação da chave JWT, restauração de segredos e expiração de backups/logs | Chaves anteriores aceitas apenas na janela de transição; sessões restauradas revogadas; cópias vencidas eliminadas e evidência do exercício sem segredos. |

## 6. Ensaio de carga e liberação

Gerador de carga separado da aplicação, usando k6 com taxa de chegada constante. Sem seguir 302; caso contrário, a medição incluiria o destino. Popular um milhão de links e eventos representativos; aquecer por 5 minutos fora da medição. Usar IPs de teste sem dados pessoais reais.

Executar dois ensaios independentes de 15 minutos a 1.000 GET/s: distribuição uniforme e 90% de acessos no mesmo link. No segundo, incluir 10 criações/s e 20 consultas de relatório/s autenticadas para verificar competição no banco. Não aceitar teste com iterações descartadas pelo gerador; aumentar capacidade do gerador e repetir se necessário.

Coletar histograma no serviço e latência do gerador separadamente; verificar 900.000 GETs por ensaio, códigos/destinos corretos, p95, erro, publicações, fila e soma de agregados depois da drenagem. Em execução saudável sem falha de publicação, eventos únicos e total devem coincidir com GETs elegíveis. Tempo para drenar após o ensaio <= 60 segundos.

Executar ensaio de falha separado: desligar worker por 60 segundos; recuperar e medir drenagem. Interromper broker por 60 segundos e verificar continuidade dos 302 e avisos de coleta. Interromper banco e verificar 503 explícito, sem redirecionamento fabricado. Testar queda de um nó RabbitMQ e failover PostgreSQL com relatório dos tempos observados. Não misturar resultados de falha com a meta de carga saudável.

Liberação: passar matriz crítica T01–T24, contrato, carga e restauração; executar migrações em staging; aplicar migrações aditivas em produção antes dos serviços; subir uma instância nova, fazer smoke test e ampliar. Rollback reverte imagem preservando schema compatível e dados. Mudança destrutiva de esquema exige migração posterior separada. Entregar relatório com resultados reais, manifesto do ambiente e limitações, sem preencher metas como se fossem medições.

## 7. Ordem de implementação sugerida

1. Modelo, migrações, Base62 e contratos; autenticação e autorização.
2. Criação/listagem/desativação, resolução pública e testes de URL.
3. Mensageria, deduplicação transacional, agregados e relatórios.
4. Interface, administração, exclusão e retenção.
5. Observabilidade, reserva de IDs para recuperação, backups, testes de carga/falha e liberação.

Revisar especificações e contrato juntos em cada mudança. A implementação financeira permanece fora desta versão.
