using Games.MassiveServer.Hosting;

var app = GameServerHost.CreateApplication(args, GameServerPresets.Coop);
await app.RunAsync();
