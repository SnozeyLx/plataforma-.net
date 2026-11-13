# Playlab frontend

Protótipo do portal em Next.js App Router, React e TypeScript. O Next funciona como Backend for Frontend (BFF): o navegador não recebe os JWTs, e os tokens de acesso/renovação ficam em cookies `HttpOnly`.

## Executar localmente

1. Inicie o MySQL, aplique as migrations e inicie a Auth API conforme o README da raiz.
2. Na pasta `web/plataforma-web`, copie `.env.example` para `.env.local`.
3. Execute `npm ci` e depois `npm run dev`.
4. Abra `http://localhost:3000`.

`AUTH_API_BASE_URL` é uma variável apenas do servidor Next. Ela aponta para a Auth API, por padrão em `http://localhost:5000`. O backend precisa estar configurado com `ConnectionStrings__Platform` e `Jwt__Issuer`, `Jwt__Audience` e `Jwt__SigningKeyBase64`.

O protótipo oferece cadastro, login, consulta do perfil autenticado e logout. A rota de sessão renova automaticamente o access token usando o refresh token. Nenhum token é salvo em `localStorage` ou exposto ao código JavaScript do navegador.

O cliente Unity WebGL deve ser hospedado na mesma origem do portal. Assim, ele reutiliza a sessão HttpOnly da plataforma. As rotas `/api/matchmaking/massive/tickets` fazem proxy do matchmaking no servidor Next e não entregam o JWT ao jogo. Em Docker, `MASSIVE_API_BASE_URL` já aponta para o serviço `massive-server`; fora do Compose, pode ser configurada no ambiente do Next (o padrão local é `http://localhost:5001`).
