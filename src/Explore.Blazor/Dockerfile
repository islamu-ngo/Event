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
COPY ["global.json", "Directory.Build.props", "Directory.Packages.props", "dotnet-tools.json", "./"]
COPY ["src/Explore.Blazor/Explore.Blazor.csproj", "src/Explore.Blazor/packages.lock.json", "src/Explore.Blazor/"]
COPY ["src/Explore.Blazor.Client/Explore.Blazor.Client.csproj", "src/Explore.Blazor.Client/packages.lock.json", "src/Explore.Blazor.Client/"]
COPY ["src/Explore.ServiceDefaults/Explore.ServiceDefaults.csproj", "src/Explore.ServiceDefaults/packages.lock.json", "src/Explore.ServiceDefaults/"]
COPY ["src/Event.Web.BffHosting/Event.Web.BffHosting.csproj", "src/Event.Web.BffHosting/packages.lock.json", "src/Event.Web.BffHosting/"]

# Restore: FOSS uses locked-mode for determinism; commercial re-resolves
RUN if [ "$USE_COMMERCIAL_LUCKYPENNY_LIBS" = "true" ]; then \
      dotnet restore "./src/Explore.Blazor/Explore.Blazor.csproj" \
        -p:UseCommercialLuckyPennyLibraries=true; \
    else \
      dotnet restore "./src/Explore.Blazor/Explore.Blazor.csproj" \
        --locked-mode \
        -p:UseCommercialLuckyPennyLibraries=false; \
    fi

RUN dotnet tool restore
COPY . .
WORKDIR "/src/src/Explore.Blazor"
RUN dotnet build "./Explore.Blazor.csproj" -c $BUILD_CONFIGURATION -o /app/build \
      --no-restore \
      -p:UseCommercialLuckyPennyLibraries=$USE_COMMERCIAL_LUCKYPENNY_LIBS

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
ARG USE_COMMERCIAL_LUCKYPENNY_LIBS=false
RUN dotnet publish "./Explore.Blazor.csproj" -c $BUILD_CONFIGURATION -o /app/publish \
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
