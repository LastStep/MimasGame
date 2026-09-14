# Build context = repo root:  docker build -f server/Dockerfile -t mimas-server .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY shared/Mimas.Core/Runtime/ shared/Mimas.Core/Runtime/
COPY shared/Mimas.Core.Build/ shared/Mimas.Core.Build/
COPY server/Mimas.Server/ server/Mimas.Server/
RUN dotnet publish server/Mimas.Server/Mimas.Server.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV MIMAS_PORT=7777
EXPOSE 7777
VOLUME ["/data"]
ENTRYPOINT ["dotnet", "Mimas.Server.dll"]
