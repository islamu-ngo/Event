# Secrets are loaded at runtime via AddSecretManagement() - no CLI needed
# Supports zero-downtime deployments with Coolify

FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:7644f992230d35cf230017189d4038c0ae0f7388b13f4f7ae1900a155bafb597 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:ea8bde36c11b6e7eec2656d0e59101d4462f6bd630730f2c8201ed0572b295d5 AS build
ARG BUILD_CONFIGURATION=Release
ARG USE_COMMERCIAL_LUCKYPENNY_LIBS=false
WORKDIR /src

# Copy restore inputs and project graph first for deterministic locked restore caching
COPY ["global.json", "Directory.Build.props", "Directory.Packages.props", "./"]
COPY ["src/Explore.API/Explore.API.csproj", "src/Explore.API/packages.lock.json", "src/Explore.API/"]
COPY ["src/Explore.Application/Explore.Application.csproj", "src/Explore.Application/packages.lock.json", "src/Explore.Application/"]
COPY ["src/Explore.Atproto.Transport/Explore.Atproto.Transport.csproj", "src/Explore.Atproto.Transport/packages.lock.json", "src/Explore.Atproto.Transport/"]
COPY ["src/Explore.Domain/Explore.Domain.csproj", "src/Explore.Domain/packages.lock.json", "src/Explore.Domain/"]
COPY ["src/Explore.Infrastructure/Explore.Infrastructure.csproj", "src/Explore.Infrastructure/packages.lock.json", "src/Explore.Infrastructure/"]
COPY ["src/Explore.Persistence/Explore.Persistence.csproj", "src/Explore.Persistence/packages.lock.json", "src/Explore.Persistence/"]
COPY ["src/Explore.Secrets/Explore.Secrets.csproj", "src/Explore.Secrets/packages.lock.json", "src/Explore.Secrets/"]
COPY ["src/Explore.ServiceDefaults/Explore.ServiceDefaults.csproj", "src/Explore.ServiceDefaults/packages.lock.json", "src/Explore.ServiceDefaults/"]

# Restore: FOSS uses locked-mode for determinism; commercial re-resolves
RUN if [ "$USE_COMMERCIAL_LUCKYPENNY_LIBS" = "true" ]; then \
      dotnet restore "./src/Explore.API/Explore.API.csproj" \
        -p:UseCommercialLuckyPennyLibraries=true; \
    else \
      dotnet restore "./src/Explore.API/Explore.API.csproj" \
        --locked-mode \
        -p:UseCommercialLuckyPennyLibraries=false; \
    fi

COPY . .
WORKDIR "/src/src/Explore.API"
RUN dotnet build "./Explore.API.csproj" -c $BUILD_CONFIGURATION -o /app/build \
      --no-restore \
      -p:UseCommercialLuckyPennyLibraries=$USE_COMMERCIAL_LUCKYPENNY_LIBS

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
ARG USE_COMMERCIAL_LUCKYPENNY_LIBS=false
RUN dotnet publish "./Explore.API.csproj" -c $BUILD_CONFIGURATION -o /app/publish \
      /p:UseAppHost=false \
      --no-restore \
      -p:UseCommercialLuckyPennyLibraries=$USE_COMMERCIAL_LUCKYPENNY_LIBS

FROM base AS final
USER root
WORKDIR /app
RUN mkdir -p /app/bootstrap && chown $APP_UID:$APP_UID /app/bootstrap

# Copy published application
COPY --from=publish /app/publish .

# Bundle Cerbos policy package assets for manual download and boot sync fallback.
# Compose deployments may still mount /app/cerbos/policies read-only to override this copy.
COPY --from=build /src/cerbos ./cerbos

# Secret management is handled by the application via AddSecretManagement()
# Configure via environment variables:
#   SecretProvider__Provider=Environment or Infisical (required; no fallback)
#   SecretProvider__Infisical__ProjectId/ClientId/ClientSecret, etc.
#   Licensing__AutoMapper__LicenseKey, Licensing__MediatR__LicenseKey (commercial builds only)
# See docs/CONFIGURATION.md for full list of options

USER $APP_UID
ENTRYPOINT ["dotnet", "Explore.API.dll"]
