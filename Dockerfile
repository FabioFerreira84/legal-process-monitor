# ==============================================================================
# Stage 1: Build & Publish (.NET 9 SDK)
# ==============================================================================
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copiar arquivos de solução e csproj para cache de camadas do NuGet
COPY LegalProcessMonitor.sln ./
COPY src/LegalProcessMonitor.Domain/LegalProcessMonitor.Domain.csproj src/LegalProcessMonitor.Domain/
COPY src/LegalProcessMonitor.Infrastructure/LegalProcessMonitor.Infrastructure.csproj src/LegalProcessMonitor.Infrastructure/
COPY src/LegalProcessMonitor.Api/LegalProcessMonitor.Api.csproj src/LegalProcessMonitor.Api/
COPY src/LegalProcessMonitor.Worker/LegalProcessMonitor.Worker.csproj src/LegalProcessMonitor.Worker/
COPY tests/LegalProcessMonitor.Tests/LegalProcessMonitor.Tests.csproj tests/LegalProcessMonitor.Tests/

RUN dotnet restore LegalProcessMonitor.sln

# Copiar código-fonte e publicar em modo Release
COPY src/ src/
RUN dotnet publish src/LegalProcessMonitor.Api/LegalProcessMonitor.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# ==============================================================================
# Stage 2: Production Runtime (ASP.NET Core 9 Debian Slim - Compatível com Render Free & Docker Compose)
# ==============================================================================
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Criar diretório de dados com permissão para o usuário da aplicação (sem instrução VOLUME, que bloqueia o Render Free)
RUN mkdir -p /app/data && chmod 777 /app/data

ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    TZ=America/Sao_Paulo \
    ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Sqlite__DatabasePath=/app/data/legalmonitor.db

COPY --from=build /app/publish .

EXPOSE 8080

ENTRYPOINT ["dotnet", "LegalProcessMonitor.Api.dll"]
