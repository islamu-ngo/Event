# Secrets are loaded at runtime via AddSecretManagement() - no CLI needed
# Supports zero-downtime deployments with Coolify

FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:6a94333d37514e385650a3c81a55e5350b67253dbe136e9cf17e499c35606a8c AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0@sha256:2fa828c68761b1b8c23d7662dc134421b9d3b59fe1425fdbc80804e390cdb24d AS build
ARG BUILD_CONFIGURATION=Release
ARG USE_COMMERCIAL_LUCKYPENNY_LIBS=false
ARG TARGETARCH
WORKDIR /src

# Copy restore inputs and project graph first for deterministic locked restore caching
COPY ["global.json", "Directory.Build.props", "Directory.Packages.props", "dotnet-tools.json", "./"]
COPY ["src/Explore.Blazor/Explore.Blazor.csproj", "src/Explore.Blazor/packages.lock.json", "src/Explore.Blazor/"]
COPY ["src/Explore.Blazor.Client/Explore.Blazor.Client.csproj", "src/Explore.Blazor.Client/packages.lock.json", "src/Explore.Blazor.Client/"]
COPY ["src/Explore.ServiceDefaults/Explore.ServiceDefaults.csproj", "src/Explore.ServiceDefaults/packages.lock.json", "src/Explore.ServiceDefaults/"]
COPY ["src/Event.Web.BffHosting/Event.Web.BffHosting.csproj", "src/Event.Web.BffHosting/packages.lock.json", "src/Event.Web.BffHosting/"]

# Restore: FOSS uses locked-mode for determinism; commercial re-resolves
RUN if [ "$USE_COMMERCIAL_LUCKYPENNY_LIBS" = "true" ]; then \
      dotnet restore "./src/Explore.Blazor/Explore.Blazor.csproj" \
        -a $TARGETARCH \
        -p:UseCommercialLuckyPennyLibraries=true; \
    else \
      dotnet restore "./src/Explore.Blazor/Explore.Blazor.csproj" \
        -a $TARGETARCH \
        --locked-mode \
        -p:UseCommercialLuckyPennyLibraries=false; \
    fi

RUN dotnet tool restore
COPY . .
WORKDIR "/src/src/Explore.Blazor"
RUN dotnet build "./Explore.Blazor.csproj" -c $BUILD_CONFIGURATION -o /app/build \
      -a $TARGETARCH \
      --no-restore \
      -p:UseCommercialLuckyPennyLibraries=$USE_COMMERCIAL_LUCKYPENNY_LIBS

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
ARG USE_COMMERCIAL_LUCKYPENNY_LIBS=false
ARG TARGETARCH
RUN dotnet publish "./Explore.Blazor.csproj" -c $BUILD_CONFIGURATION -o /app/publish \
      -a $TARGETARCH \
      /p:UseAppHost=false \
      --no-restore \
      -p:UseCommercialLuckyPennyLibraries=$USE_COMMERCIAL_LUCKYPENNY_LIBS

FROM base AS final
WORKDIR /app

# Copy published application
COPY --from=publish /app/publish .

# Secret management is handled by the application via AddSecretManagement()
# Configure via environment variables:
#   SECRET_PROVIDER=infisical (or "none" for env vars only)
#   SecretProvider__Infisical__ProjectId/ClientId/ClientSecret, etc.
#   Licensing__AutoMapper__LicenseKey, Licensing__MediatR__LicenseKey (commercial builds only)
# See docs/CONFIGURATION.md for full list of options

ENTRYPOINT ["dotnet", "Explore.Blazor.dll"]
