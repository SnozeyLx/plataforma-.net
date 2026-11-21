# MinhaPlataforma.Games

Base da plataforma em .NET 10, com autenticação ASP.NET Core Identity, MySQL, portal Next.js e o primeiro protótipo de matchmaking/handshake para partidas UDP.

## Projetos

- `Plataforma.Core`: contratos e tipos compartilhados.
- `Plataforma.Database`: Identity, contexto MySQL e migrations.
- `Plataforma.AuthApi`: registro, login, refresh, logout e perfil autenticado.
- `Plataforma.Gateway`: entrada HTTP; o proxy YARP ainda não está configurado.
- `Games.MassiveServer`: fila para partidas de 1 a 50 jogadores, validação de token pela Auth API e handshake UDP com ticket de uso único (LiteNetLib).
- `Games.CoopServer`: processo separado para partidas cooperativas de 1 a 4 jogadores; compartilha o transporte e a autenticação, mas tem fila e porta UDP próprias.

## Requisitos

- .NET 10 SDK
- Docker Compose

## Banco local

1. Copie `.env.example` para `.env`, substitua as senhas e configure `JWT_SIGNING_KEY_BASE64` com uma chave aleatória Base64 de pelo menos 32 bytes.
2. Execute `docker compose up --build -d`.
3. Confira o estado dos serviços e a execução das migrations com `docker compose ps` e `docker compose logs migrations`.

O Compose sobe MySQL, Auth API, servidores Massive e Coop, e portal web. O volume `mysql_data` preserva os dados entre reinicializações. As portas HTTP e UDP são publicadas somente em `127.0.0.1`; antes de acesso externo, configure um proxy HTTPS e proteja também o transporte UDP. O arquivo `.env` é local e não deve ser enviado ao Git.

## Deploy privado em Linux com CyberPanel

Docker e PM2 podem coexistir: os containers não interrompem os processos PM2, desde que as portas publicadas pelo Compose estejam livres. Esta configuração mantém o banco e as APIs em loopback; o OpenLiteSpeed/CyberPanel deve encaminhar HTTPS para as APIs locais. O portal web continua no serviço existente do PM2.

### Deploy automático pelo GitHub Actions

O workflow `.github/workflows/deploy-production.yml` faz deploy em produção após pushes para `main` que alterem a Auth API, os projetos compartilhados, o Compose ou o portal. Ele atualiza a Auth API e o portal Next.js via PM2; não reinicia os servidores Massive/Coop. Também pode ser iniciado manualmente em **Actions → Deploy platform production → Run workflow**.

Cadastre em **Settings → Secrets and variables → Actions → Variables**:

- `PLATFORM_DEPLOY_HOST`: hostname ou IP SSH do servidor.
- `PLATFORM_DEPLOY_PORT`: porta SSH, se diferente de `22` (padrão do workflow).
- `PLATFORM_DEPLOY_USER`: usuário Linux que fará o deploy.
- `PLATFORM_DEPLOY_PATH`: caminho absoluto do clone da plataforma no servidor.
- `PLATFORM_WEB_PM2_NAME`: nome do processo do portal mostrado por `pm2 list`.

Cadastre em **Repository secrets**:

- `PLATFORM_DEPLOY_SSH_PRIVATE_KEY`: chave privada exclusiva para o workflow.
- `PLATFORM_DEPLOY_KNOWN_HOSTS`: linha de `known_hosts` do servidor, obtida e verificada por um canal confiável.

Instale a chave pública correspondente em `~/.ssh/authorized_keys` do usuário de deploy. Esse usuário precisa poder executar `git pull` no clone, usar Docker Compose sem prompt interativo e controlar o processo PM2 do portal. O checkout precisa estar na branch `main`, e `.env.production` permanece no servidor, fora do Git. O workflow valida a configuração, compila o portal, atualiza `mysql`/`auth-api` e recarrega o portal; mudanças nos servidores de jogo continuam exigindo deploy separado.

1. No servidor, confirme `docker --version`, `docker compose version` e as portas TCP `13306`, `15000`–`15002` e UDP `9050`/`9051`. Se Docker não estiver instalado, instale o Docker Engine e o plugin Compose seguindo a documentação oficial para a versão do Ubuntu; não remova pacotes nem serviços PM2 existentes.
2. Clone/atualize este repositório no servidor. Copie `deploy-production.env.example` para `.env.production` e preencha as senhas, `JWT_SIGNING_KEY_BASE64` e as credenciais SMTP diretamente no servidor. Configure `EMAIL_SMTP_HOST`, `EMAIL_SMTP_PORT`, `EMAIL_SMTP_USERNAME`, `EMAIL_SMTP_PASSWORD`, `EMAIL_FROM_ADDRESS` (remetente verificado), `EMAIL_FROM_NAME` e `PASSWORD_RESET_PUBLIC_URL=https://plataforma.crefeed.com.br/redefinir-senha`. Para Brevo, use o host `smtp-relay.brevo.com`, porta `587` e a credencial SMTP, não a chave da API. Gere valores aleatórios, por exemplo com `openssl rand -hex 32` para senhas e `openssl rand -base64 48` para a chave JWT. Não envie `.env.production` por chat nem para o Git.
3. Crie registros DNS para os subdomínios de Auth, Massive e Coop do seu domínio (por exemplo, `auth.example.com`, `massive.example.com` e `coop.example.com`), todos apontando para o servidor. No CyberPanel/OpenLiteSpeed, configure HTTPS e proxy reverso para `127.0.0.1:15000`, `127.0.0.1:15001` e `127.0.0.1:15002`, respectivamente. Substitua os exemplos pelo domínio real; não publique as portas HTTP da aplicação nem a porta do MySQL diretamente na Internet.
4. Mantenha `GAME_SERVER_UDP_BIND_ADDRESS=127.0.0.1` até que o firewall da rede/provedor permita uma regra de entrada UDP para `9050` e `9051` somente a partir dos endereços dos clientes de teste. Depois de confirmar a regra, altere esse valor para `0.0.0.0`. O Docker pode contornar regras UFW convencionais para portas publicadas; prefira o firewall do provedor, anterior ao host, e verifique a regra antes de liberar o UDP. O protocolo UDP atual é de protótipo e não deve ficar aberto a qualquer origem.
5. Valide a configuração e inicie somente o banco, as APIs e os servidores de jogo (o portal `web` não é iniciado):

   ```bash
   docker compose --env-file .env.production config --quiet
   docker compose --env-file .env.production up --build -d mysql auth-api massive-server coop-server
   docker compose --env-file .env.production ps
   docker compose --env-file .env.production logs --tail=100 migrations auth-api massive-server coop-server
   ```

   A Auth API mantém o key ring do ASP.NET Data Protection no volume Docker `plataforma_auth_api_data_protection_keys`. Em uma stack já existente, antes da primeira recriação da Auth API com esta configuração, copie as chaves do container atual para esse volume:

   ```bash
   set -o pipefail
   AUTH_CONTAINER=$(docker compose --env-file .env.production ps -q auth-api)
   test -n "$AUTH_CONTAINER" || { echo "Auth API container not found"; exit 1; }
   AUTH_UID=$(docker exec "$AUTH_CONTAINER" id -u)
   AUTH_GID=$(docker exec "$AUTH_CONTAINER" id -g)
   docker volume create plataforma_auth_api_data_protection_keys
   docker cp "$AUTH_CONTAINER":/home/app/.aspnet/DataProtection-Keys/. - |
     docker run --rm -i -v plataforma_auth_api_data_protection_keys:/keys busybox sh -c "tar -C /keys -xf - && chown -R $AUTH_UID:$AUTH_GID /keys"
   ```

   O volume preserva as chaves ao recriar o container, mas não criptografa os arquivos no disco do servidor. Para parar os containers sem apagar os dados, use `docker compose --env-file .env.production down` — não acrescente `-v`.
6. No componente `MassiveOnlineMenu` da cena Unity, configure `authApiBaseUrl`, `massiveServerApiBaseUrl` e `coopServerApiBaseUrl` com as URLs HTTPS reais configuradas no proxy. Os endereços UDP anunciados pelos servidores vêm de `GAME_SERVER_PUBLIC_ADDRESS` e `COOP_GAME_SERVER_PUBLIC_ADDRESS`.

O proxy HTTPS atende somente as APIs HTTP; ele não encaminha UDP. Para uma beta pública, primeiro proteja e teste o transporte UDP e prepare mitigação contra abuso e negação de serviço. A fila atual e o estado das partidas ficam em memória em um processo por modo, e o relay gera tráfego proporcional ao número de jogadores; não há escala horizontal automática.

## Configuração da API

A API exige uma connection string e uma chave de assinatura JWT via configuração do .NET. Exemplo no PowerShell:

```powershell
$env:ConnectionStrings__Platform = "Server=localhost;Port=3306;Database=plataforma;User ID=plataforma_app;Password=SUA_SENHA_LOCAL;"
$env:Jwt__Issuer = "plataforma-auth"
$env:Jwt__Audience = "plataforma-games"
$rng = [Security.Cryptography.RandomNumberGenerator]::Create()
$keyBytes = New-Object 'System.Byte[]' 32
$rng.GetBytes($keyBytes)
$rng.Dispose()
$env:Jwt__SigningKeyBase64 = [Convert]::ToBase64String($keyBytes)
```

Gere uma chave diferente para cada ambiente e guarde-a em um gestor de segredos. A API falha ao iniciar se a chave for inválida ou tiver menos de 32 bytes. O access token dura 15 minutos por padrão; refresh tokens duram 14 dias.

## Migrations e execução

Execute na raiz da solution, com o MySQL local iniciado e as variáveis acima configuradas:

```powershell
dotnet tool install --global dotnet-ef --version 10.0.12
dotnet ef database update --project .\src\Plataforma.Database\Plataforma.Database.csproj --startup-project .\src\Plataforma.AuthApi\Plataforma.AuthApi.csproj --context Plataforma.Database.PlatformDbContext
dotnet run --project .\src\Plataforma.AuthApi\Plataforma.AuthApi.csproj
```

Em desenvolvimento, a especificação OpenAPI fica disponível em `/openapi/v1.json`; `/health` verifica se a API iniciou.

## Matchmaking e servidor de jogo (protótipo)

O cliente autentica por HTTPS na Auth API, envia o access token como Bearer para `POST /api/matchmaking/tickets` e consulta o ticket até o estado `ready`. O servidor de jogo valida o token chamando a rota central `/api/auth/me`; ele não recebe a chave de assinatura JWT. Ao conectar por UDP, o cliente envia somente o `connectionTicket` aleatório, de uso único e com validade curta — nunca o access token no pacote UDP.

Rotas do servidor HTTP:

- `POST /api/matchmaking/tickets` — entra na fila; retorna `202 Accepted` e URL do ticket.
- `GET /api/matchmaking/tickets/{ticketId}` — consulta fila, partida ou expiração.
- `DELETE /api/matchmaking/tickets/{ticketId}` — cancela uma entrada na fila ou ticket de conexão ainda não consumido.
- `GET /health` — health check HTTP.

O Compose executa os modos em processos isolados:

- Massive: HTTP `http://localhost:5001`, UDP `9050`, aceita de 1 a 50 jogadores. Por padrão, uma partida solo começa sem atraso.
- Coop: HTTP `http://localhost:5002`, UDP `9051`, aceita de 1 a 4 jogadores. Aguarda 2 segundos depois que o mínimo é alcançado para reunir jogadores que chegaram juntos; depois inicia mesmo com apenas um.

Para acompanhar os logs de rede em tempo real, abra um PowerShell na pasta do servidor e execute:

```powershell
docker compose logs -f --since 5m massive-server
```

Para o modo Coop, troque `massive-server` por `coop-server`. `Ctrl+C` encerra apenas a visualização dos logs, não o servidor. O modelo de produção usa nível `Information`; para diagnosticar temporariamente a rejeição de pacotes, defina `GAME_SERVER_LOG_LEVEL=Debug` em `.env.production` e recrie os serviços de jogo. Depois do diagnóstico, retorne para `Information`. Tickets e tokens não são registrados.

`MASSIVE_MIN_PLAYERS` define o mínimo do Massive e aceita valores de 1 a 50. `COOP_MIN_PLAYERS` define o mínimo do Coop (1 a 4); use `COOP_MIN_PLAYERS=2` se quiser exigir pelo menos dois jogadores. `COOP_MAX_PLAYERS` pode reduzir a capacidade, mas não ultrapassar quatro. `COOP_MATCH_START_DELAY_SECONDS` configura a janela de agrupamento (0 a 30 segundos). Os dois modos têm fila e estado em memória separados.

O limite de 50 jogadores é por partida, não por processo: a fila pode separar grupos maiores em várias partidas, mas elas ainda são alocadas pelo mesmo processo e endereço UDP. O Compose não cria instâncias automaticamente.

O cliente Unity usa o `connectionTicket` para conectar por UDP, confirma o handshake de jogo e envia snapshots visuais de posição e animação. O servidor retransmite esses dados aos demais jogadores da mesma partida, mas não simula movimento, dano ou física, nem implementa reconexão ou condições de vitória. Pontuações e recompensas ainda não podem ser recebidas do cliente.

Configure `authApiBaseUrl` no componente `MassiveOnlineMenu` para o endereço da Auth API. No WebGL, `CheckWebSession` usa a origem da página e requer o portal no mesmo domínio.

Para escalar horizontalmente será necessário separar o coordenador de matchmaking dos servidores de partida, manter fila/tickets em armazenamento compartilhado e adicionar registro/health-check e alocação de instâncias por um orquestrador. Isso não está ativado nesta versão; escolher Docker Swarm, Kubernetes ou um provedor de nuvem depende do ambiente de deploy.

Os valores de tamanho de fila, atraso de agrupamento, endereço e portas são configuráveis pelo `.env` usando as variáveis `MASSIVE_*` e `COOP_*`. Os arquivos [Matchmaking.http](./src/Games.MassiveServer/Matchmaking.http) e [Matchmaking.http do Coop](./src/Games.CoopServer/Matchmaking.http) mostram como chamar as rotas; o payload inicial UDP é a string `connectionTicket` enviada pelo cliente LiteNetLib.

Para executar um servidor fora do Docker, inicie a Auth API em `http://localhost:5000` e rode o projeto desejado:

```powershell
dotnet run --project .\src\Games.MassiveServer\Games.MassiveServer.csproj
dotnet run --project .\src\Games.CoopServer\Games.CoopServer.csproj
```

Os profiles de desenvolvimento escutam Massive em HTTP `5001`/UDP `9050` e Coop em HTTP `5002`/UDP `9051`.

## Catálogo e progressão

O modelo de dados inclui:

- `Games`: catálogo com slug único e estados draft, publicado e arquivado. Arquivar preserva o histórico.
- `GamePlayerProgress`: progresso agregado com nível, experiência, partidas e tempo jogado; há no máximo um registro por jogador/jogo.
- `GameSessions`: sessão identificada por UUID, com período, pontuação, resultado e experiência concedida. O UUID permite detectar reenvios; o futuro endpoint ainda deverá tratar a repetição de modo idempotente.
- `GameAchievements` e `UserAchievementProgress`: definições de conquistas por jogo e o progresso/desbloqueio de cada usuário.

Esta entrega prepara as tabelas e migrations, mas ainda não expõe endpoints administrativos para cadastrar jogos nem endpoints para receber resultados dos servidores de jogo. Uma sessão requer que o progresso do jogador para aquele jogo já exista. Esses resultados não devem ser aceitos diretamente do cliente do jogador; a próxima integração precisa autenticar os servidores e atualizar sessão e progresso de forma transacional e idempotente.

Para criar as tabelas em um banco local que já tenha as migrations de autenticação:

```powershell
dotnet ef database update --project .\src\Plataforma.Database\Plataforma.Database.csproj --startup-project .\src\Plataforma.AuthApi\Plataforma.AuthApi.csproj --context Plataforma.Database.PlatformDbContext
```

## Economia e pagamentos (planejamento)

A economia não faz parte desta entrega. O modelo futuro distingue a moeda da plataforma — a única que poderá participar de conversão com dinheiro real — das moedas internas que cada jogo poderá criar. Moedas de jogo não terão conversão automática para saldo da plataforma.

O serviço de pagamentos existente continua responsável pelo checkout e pelo provedor externo. A plataforma deverá registrar a concessão confirmada em um ledger imutável, com uma referência idempotente para impedir créditos duplicados; ações internas de jogo não devem gerar chamadas ao provedor. Antes de implementar essa integração, é necessário revisar o contrato/API do serviço existente e decidir as regras de conversão, estorno e eventual saque.

## Rotas de autenticação

- `POST /api/auth/register` — cria conta com e-mail e senha de pelo menos 12 caracteres.
- `POST /api/auth/login` — valida a senha e emite access token e refresh token.
- `POST /api/auth/refresh` — rotaciona o refresh token; cada token só pode ser usado uma vez.
- `POST /api/auth/logout` — revoga a família de refresh tokens.
- `POST /api/auth/forgot-password` — solicita um link de redefinição por e-mail; a resposta é genérica para não revelar se a conta existe.
- `POST /api/auth/reset-password` — valida o token recebido, define a nova senha e revoga os refresh tokens da conta. O link expira em 30 minutos; access tokens emitidos anteriormente continuam válidos até expirarem (15 minutos por padrão).
- `GET /api/auth/me` — retorna a conta associada a um access token Bearer válido.

Refresh tokens são aleatórios, armazenados somente como hash SHA-256 e rotacionados em transação. Reutilização detectada revoga a família. O logout impede novas renovações, mas um access token JWT já emitido continua válido até expirar.

## Frontend Next.js

O protótipo em `web/plataforma-web` usa Next.js App Router e React. O Next age como Backend for Frontend para a Auth API: tokens ficam em cookies `HttpOnly`, e o browser não precisa acessar a API de identidade diretamente nem armazenar JWT no `localStorage`.

Na tela de login, “Esqueceu sua senha?” abre um modal para solicitar o link por e-mail. O link leva à página `/redefinir-senha`, onde o token expira em 30 minutos.

Com a Auth API ativa na porta `5000`, abra outro terminal PowerShell:

```powershell
Set-Location .\web\plataforma-web
npm ci
npm run dev
```

Abra `http://localhost:3000` para criar uma conta, entrar e consultar o perfil. `AUTH_API_BASE_URL` é lida somente pelo servidor Next e pode apontar para outra URL se a API estiver em endereço diferente. Em produção, use HTTPS para que os cookies sejam enviados com o atributo `Secure`.

## Loja demonstrativa

`http://localhost:3000/loja` apresenta um catálogo visual com jogos e itens fictícios, busca, filtros por gênero e uma sacola temporária no navegador. Não há checkout real, cobrança, persistência de pedidos ou concessão de jogos/itens às contas; os dados e preços servem somente para testar a interface.

A logo temporária está em `web/plataforma-web/public/logo_teste.svg`; ela é usada nos cabeçalhos e como favicon. Substitua esse arquivo pela logo oficial quando estiver disponível.

As páginas `http://localhost:3000/jogos/{slug}` demonstram a experiência de jogo. Dash & Crash consulta a biblioteca da API e solicita links temporários para os builds; os outros títulos ainda usam dados locais de demonstração.

## Propriedade e entrega segura de builds

A migration `AddGameEntitlements` cria propriedade persistente e cadastra `dash-and-crash`. A API autenticada expõe `GET /api/library` e `GET /api/games/{slug}/access?platform=windows|webgl`; `GET /api/launcher/{platform}/access` é público para baixar o launcher antes do login. O acesso aos builds de jogo verifica o entitlement. Quando o artefato está configurado, a API emite uma URL temporária HMAC-SHA256 da Bunny CDN. O modo sandbox concede o jogo de teste sob demanda a cada conta autenticada, sem checkout, saldo ou compra simulada.

Para validar localmente, copie os campos de entrega para `.env` e use `GAME_DELIVERY_SANDBOX_TEST_MODE_ENABLED=true` somente no ambiente local de teste. Em produção o valor deve permanecer `false`. Configure Token Authentication na Pull Zone e guarde sua chave em `BUNNY_CDN_TOKEN_AUTH_KEY`; configure também `BUNNY_CDN_BASE_URL` com a URL HTTPS da Pull Zone. Os instaladores publicados diretamente na pasta `Plataform-Build/` são `Plataforma_laucher.exe`, `Plataforma_laucher_macos_x64.pkg`, `Plataforma_laucher_macos_arm64.pkg` e `Plataforma_laucher_linux_x64.AppImage`; configure os caminhos correspondentes em `PLATFORM_LAUNCHER_WINDOWS_OBJECT_PATH`, `PLATFORM_LAUNCHER_MACOS_X64_OBJECT_PATH`, `PLATFORM_LAUNCHER_MACOS_ARM64_OBJECT_PATH` e `PLATFORM_LAUNCHER_LINUX_X64_OBJECT_PATH`. `PLATFORM_LAUNCHER_UPDATES_DIRECTORY_PATH` aponta para `Plataform-Build/updates`, raiz dos feeds Velopack. O limite `GAME_DELIVERY_LAUNCHER_UPDATE_TOKEN_LIFETIME_SECONDS` controla os links assinados dos feeds e pacotes (60 a 3600 segundos). `DASH_AND_CRASH_WINDOWS_OBJECT_PATH` recebe o caminho do ZIP do jogo, `DASH_AND_CRASH_WEBGL_DIRECTORY_PATH` o prefixo do build WebGL, e `DASH_AND_CRASH_WEBGL_ENTRY_POINT` normalmente é `index.html`. Um caminho vazio mantém a respectiva opção indisponível.

Essas configurações são entregues somente à Auth API via Docker Compose. O portal separa “Jogar no PC” de “Baixar instalador”: o primeiro abre `playlab://game/{slug}` e, se necessário, o launcher baixa o ZIP do jogo após validar a licença; a área de downloads oferece instaladores Windows x64, macOS Intel, macOS Apple Silicon e Linux x64. O launcher instalado verifica o canal do seu sistema, baixa deltas quando disponíveis e reinicia apenas o próprio launcher para aplicar a atualização. Os feeds ficam em subpastas `updates/{canal}` e são servidos por `GET /api/launcher/updates/{channel}/{assetName}`, que permite somente os canais e nomes de assets Velopack esperados e redireciona para um link Bunny temporário. Os builds iniciais macOS não são assinados/notarizados e são apenas para teste. Se não houver build Windows publicada, o portal informa isso e mantém WebGL disponível quando configurado. Depois da instalação, a pessoa inicia o jogo pelo launcher, sem execução automática. Se o arquivo do jogo contiver mais de um executável além dos auxiliares conhecidos do Unity, a pessoa escolhe qual iniciar. O launcher e o portal recebem links CDN curtos, nunca a chave HMAC ou credenciais S3. Como o Bunny Storage é a origem da Pull Zone, as credenciais de Storage são necessárias somente no processo protegido de publicação/upload, nunca no cliente nem no repositório. As rotas e exemplos de chamadas estão em [GameDelivery.http](./src/Plataforma.AuthApi/GameDelivery.http).

## Chat entre amigos

O dashboard autenticado oferece um painel de conversa direta 1:1. Somente amizades aceitas podem abrir conversas; mensagens de texto de até 2000 caracteres são persistidas em `FriendMessages`. O histórico usa páginas de até 100 mensagens (`GET /api/chat/conversations/{friendUserId}/messages?page=1&pageSize=50`).

O hub SignalR fica em `/api/chat/hub`. O frontend usa Long Polling pelo proxy Next (`/api/platform/chat/hub`) para manter os JWTs em cookies `HttpOnly`, sem expor tokens ao JavaScript. O hub valida a amizade ao entrar e novamente ao enviar cada mensagem. A migration `AddFriendChatMessages` é aplicada pelo serviço `migrations` no Compose; para executar manualmente, use `dotnet ef database update` conforme a seção de catálogo.

## Limitações antes de produção

Esta primeira etapa não implementa confirmação de e-mail, recuperação de senha, limitação de tentativas por IP nem um proxy TLS. O matchmaking é de instância única e volátil. LiteNetLib ainda não está protegido por criptografia de aplicação; não publique a porta UDP nem use tickets de jogadores reais até projetar e validar confidencialidade, integridade e proteção contra replay para o transporte. Em produção, use HTTPS para login e matchmaking, proxy com certificados válidos, firewall restritivo, gestão segura de segredos e armazenamento distribuído se houver múltiplas réplicas. Credenciais de banco que já tenham sido expostas devem ser rotacionadas antes de staging.

O provider MySQL selecionado é `MySql.EntityFrameworkCore` 10.0.12, que suporta EF Core 10; sua licença NuGet é `GPL-2.0-only WITH Universal-FOSS-exception-1.0`. Revise a licença para o modelo de distribuição pretendido antes de um lançamento comercial.
