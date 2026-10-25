# ==============================================================================
# Stage 1: Build & Test (.NET 9 SDK)
# ==============================================================================
FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
WORKDIR /src

# Copiar arquivos de solução e csproj para cache de camadas do NuGet
COPY LegalProcessMonitor.sln ./
COPY src/LegalProcessMonitor.Domain/LegalProcessMonitor.Domain.csproj src/LegalProcessMonitor.Domain/
COPY src/LegalProcessMonitor.Infrastructure/LegalProcessMonitor.Infrastructure.csproj src/LegalProcessMonitor.Infrastructure/
COPY src/LegalProcessMonitor.Api/LegalProcessMonitor.Api.csproj src/LegalProcessMonitor.Api/
COPY src/LegalProcessMonitor.Worker/LegalProcessMonitor.Worker.csproj src/LegalProcessMonitor.Worker/
COPY tests/LegalProcessMonitor.Tests/LegalProcessMonitor.Tests.csproj tests/LegalProcessMonitor.Tests/

RUN dotnet restore LegalProcessMonitor.sln

# Copiar todo o código-fonte do backend e publicar em modo Release
COPY src/ src/
RUN dotnet publish src/LegalProcessMonitor.Api/LegalProcessMonitor.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# ==============================================================================
# Stage 2: Production Runtime (ASP.NET Core 9 Alpine Non-Root + SQLite WAL)
# ==============================================================================
FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS runtime
WORKDIR /app

# Instalar tzdata, icu-libs (cultura pt-BR) e curl para healthcheck
RUN apk add --no-cache icu-libs tzdata curl && \
    mkdir -p /app/data && \
    chown -R $APP_UID:$APP_UID /app

ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    TZ=America/Sao_Paulo \
    ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Sqlite__DatabasePath=/app/data/legalmonitor.db

COPY --from=build --chown=$APP_UID:$APP_UID /app/publish .

# Volume persistente para o arquivo SQLite (.db, .db-wal, .db-shm)
VOLUME ["/app/data"]

USER $APP_UID
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD curl -fsS http://localhost:8080/healthz || exit 1

ENTRYPOINT ["dotnet", "LegalProcessMonitor.Api.dll"]
