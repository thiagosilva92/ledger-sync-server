# Multi-stage: the SDK image (large, has the full compiler toolchain)
# only exists to publish; the final image is the much smaller ASP.NET
# runtime, which is all a container running this API actually needs.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Ledger.SyncServer/Ledger.SyncServer.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
# ASPNETCORE_URLS defaults to http://+:8080 on this base image already;
# no explicit ENV needed. ENTRYPOINT is exec-form so a docker-compose
# `command:` (the "--migrate-only" one-shot mode) appends to it rather
# than replacing it.
ENTRYPOINT ["dotnet", "Ledger.SyncServer.dll"]
