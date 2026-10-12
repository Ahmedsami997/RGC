# RGC Server container image (used for Azure Container Apps or any Docker host).
# Build context: the announcement-system folder.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY Directory.Build.props .
COPY src/RGC.Shared/ src/RGC.Shared/
COPY src/RGC.Server/ src/RGC.Server/
RUN dotnet publish src/RGC.Server/RGC.Server.csproj -c Release -o /app -p:DebugType=none

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
# The base image already listens on 8080 (ASPNETCORE_HTTP_PORTS); point the container app's ingress there.
EXPOSE 8080
ENTRYPOINT ["dotnet", "RGC.Server.dll"]
