FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY . .
RUN dotnet publish src/Games.CoopServer/Games.CoopServer.csproj \
    --configuration Release \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
EXPOSE 9051/udp
COPY --from=build /app/publish .
USER app
ENTRYPOINT ["dotnet", "Games.CoopServer.dll"]
