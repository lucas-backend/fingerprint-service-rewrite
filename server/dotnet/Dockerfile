# syntax=docker/dockerfile:1

# ---------------------------------------------------------
# Build stage
# ---------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["fingerprint-service.csproj", "./"]
RUN dotnet restore "./fingerprint-service.csproj"

COPY . .
RUN dotnet publish "./fingerprint-service.csproj" \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

# ---------------------------------------------------------
# Runtime stage
# ---------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# curl is only used for the container health check.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_EnableDiagnostics=0 \
    TZ=UTC

EXPOSE 8080

COPY --from=build /app/publish .

# Run as the built-in non-root user provided by the .NET runtime images.
USER $APP_UID

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD curl --fail --silent http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "fingerprint-service.dll"]
