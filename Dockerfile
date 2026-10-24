# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY LegalProcessMonitor.sln .
COPY src/LegalProcessMonitor.Domain/LegalProcessMonitor.Domain.csproj src/LegalProcessMonitor.Domain/
COPY src/LegalProcessMonitor.Infrastructure/LegalProcessMonitor.Infrastructure.csproj src/LegalProcessMonitor.Infrastructure/
COPY src/LegalProcessMonitor.Api/LegalProcessMonitor.Api.csproj src/LegalProcessMonitor.Api/
COPY tests/LegalProcessMonitor.Tests/LegalProcessMonitor.Tests.csproj tests/LegalProcessMonitor.Tests/

RUN dotnet restore

COPY . .
WORKDIR /src/src/LegalProcessMonitor.Api
RUN dotnet publish -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "LegalProcessMonitor.Api.dll"]
