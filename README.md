# MinhaPlataforma.Games

Base da plataforma em .NET 10, com autenticação ASP.NET Core Identity, MySQL e JWT. Os projetos de gateway e de servidores de jogo continuam como scaffolds.

## Projetos

- `Plataforma.Core`: contratos e tipos compartilhados.
- `Plataforma.Database`: Identity, contexto MySQL e migrations.
- `Plataforma.AuthApi`: registro, login, refresh, logout e perfil autenticado.
- `Plataforma.Gateway`: entrada HTTP; o proxy YARP ainda não está configurado.
- `Games.CoopServer` e `Games.MassiveServer`: processos dedicados; transporte e validação de sessão ainda não estão configurados.

## Requisitos

- .NET 10 SDK
- Docker Compose

## Banco local

1. Copie `.env.example` para `.env` e substitua as senhas de desenvolvimento.
2. Execute `docker compose up -d`.
3. Aguarde o MySQL ficar saudável: `docker compose ps`.

O volume `mysql_data` preserva os dados entre reinicializações. O arquivo `.env` é para uso local e não deve ser enviado ao Git.

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
- `GET /api/auth/me` — retorna a conta associada a um access token Bearer válido.

Refresh tokens são aleatórios, armazenados somente como hash SHA-256 e rotacionados em transação. Reutilização detectada revoga a família. O logout impede novas renovações, mas um access token JWT já emitido continua válido até expirar.

## Frontend Next.js

O protótipo em `web/plataforma-web` usa Next.js App Router e React. O Next age como Backend for Frontend para a Auth API: tokens ficam em cookies `HttpOnly`, e o browser não precisa acessar a API de identidade diretamente nem armazenar JWT no `localStorage`.

Com a Auth API ativa na porta `5000`, abra outro terminal PowerShell:

```powershell
Copy-Item .\web\plataforma-web\.env.example .\web\plataforma-web\.env.local
Set-Location .\web\plataforma-web
npm ci
npm run dev
```

Abra `http://localhost:3000` para criar uma conta, entrar e consultar o perfil. `AUTH_API_BASE_URL` é lida somente pelo servidor Next e pode apontar para outra URL se a API estiver em endereço diferente. Em produção, use HTTPS para que os cookies sejam enviados com o atributo `Secure`.

## Limitações antes de produção

Esta primeira etapa não implementa confirmação de e-mail, recuperação de senha, limitação de tentativas por IP nem deploy dos serviços no Docker. Configure esses controles antes de expor a API publicamente. Não compartilhe o segredo JWT ou credenciais do MySQL.

O provider MySQL selecionado é `MySql.EntityFrameworkCore` 10.0.12, que suporta EF Core 10; sua licença NuGet é `GPL-2.0-only WITH Universal-FOSS-exception-1.0`. Revise a licença para o modelo de distribuição pretendido antes de um lançamento comercial.
