# MinhaPlataforma.Games

Estrutura inicial de uma plataforma de jogos em .NET 10. Os projetos sao apenas scaffolds: autenticacao, persistencia, proxy e protocolo de jogo ainda nao foram implementados.

## Projetos

- `Plataforma.Core`: contratos e tipos compartilhados.
- `Plataforma.Database`: persistencia da plataforma; referencia `Core`.
- `Plataforma.AuthApi`: futura API de identidade; referencia `Core` e `Database`.
- `Plataforma.Gateway`: entrada HTTP da plataforma. O proxy YARP ainda nao esta configurado.
- `Games.CoopServer` e `Games.MassiveServer`: processos dedicados independentes; o transporte e a validacao de sessao ainda nao estao configurados.

## Requisitos

- .NET 10 SDK
- Docker Compose

## Subir o MySQL local

1. Copie `.env.example` para `.env` e substitua as senhas de desenvolvimento.
2. Execute `docker compose up -d`.
3. Confira a prontidao com `docker compose ps`.

O volume `mysql_data` preserva os dados entre reinicializacoes. As credenciais do `.env` sao somente para desenvolvimento local; use um gestor de segredos no ambiente hospedado.

## Compilar

Depois de instalar o SDK .NET 10:

```powershell
dotnet restore .\MinhaPlataforma.Games.sln
dotnet build .\MinhaPlataforma.Games.sln
```

Esta base nao inclui implementacao de login nem emite JWT. A integracao de EF Core/MySQL, refresh tokens e validacao de tokens deve ser adicionada como uma etapa seguinte.
