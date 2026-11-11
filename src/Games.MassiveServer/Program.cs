using Games.MassiveServer.Hosting;

var app = GameServerHost.CreateApplication(args, GameServerPresets.Massive);
await app.RunAsync();
