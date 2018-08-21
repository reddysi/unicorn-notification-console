FROM microsoft/dotnet:2.1-runtime AS base
WORKDIR /app

FROM microsoft/dotnet:2.1-sdk AS build
WORKDIR /src
COPY NotificationConsole/NotificationConsole.csproj NotificationConsole/
RUN dotnet restore NotificationConsole/NotificationConsole.csproj
COPY . .
WORKDIR /src/NotificationConsole
RUN dotnet build NotificationConsole.csproj -c Release -o /app

FROM build AS publish
RUN dotnet publish NotificationConsole.csproj -c Release -o /app

FROM base AS final
WORKDIR /app
COPY --from=publish /app .
ENTRYPOINT ["dotnet", "NotificationConsole.dll"]
