# Secrets are loaded at runtime via AddSecretManagement() - no CLI needed
# Supports zero-downtime deployments with Coolify

FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:2d584d8147faddb0d678c5748d47953e5b8e18621ed4fb7049a91381d9d7746f AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0@sha256:35d40304542c8689331f8cab17c65926cdf48fe711e289321d71924b230a7d29 AS build
ARG BUILD_CONFIGURATION=Release
ARG TARGETARCH
WORKDIR /src

# Copy restore inputs and project graph first for deterministic locked restore caching
COPY ["global.json", "Directory.Build.props", "Directory.Packages.props", "./"]
COPY ["eng/tools/Explore.GeneratedContracts/GeneratedContractTool.targets", "eng/tools/Explore.GeneratedContracts/"]
COPY ["src/Explore.API/Explore.API.csproj", "src/Explore.API/packages.lock.json", "src/Explore.API/"]
COPY ["src/Explore.Application/Explore.Application.csproj", "src/Explore.Application/packages.lock.json", "src/Explore.Application/"]
COPY ["src/Explore.Atproto.Transport/Explore.Atproto.Transport.csproj", "src/Explore.Atproto.Transport/packages.lock.json", "src/Explore.Atproto.Transport/"]
COPY ["src/Explore.Domain/Explore.Domain.csproj", "src/Explore.Domain/packages.lock.json", "src/Explore.Domain/"]
COPY ["src/Explore.Infrastructure/Explore.Infrastructure.csproj", "src/Explore.Infrastructure/packages.lock.json", "src/Explore.Infrastructure/"]
COPY ["src/Explore.Persistence/Explore.Persistence.csproj", "src/Explore.Persistence/packages.lock.json", "src/Explore.Persistence/"]
COPY ["src/Explore.Secrets/Explore.Secrets.csproj", "src/Explore.Secrets/packages.lock.json", "src/Explore.Secrets/"]
COPY ["src/Explore.ServiceDefaults/Explore.ServiceDefaults.csproj", "src/Explore.ServiceDefaults/packages.lock.json", "src/Explore.ServiceDefaults/"]

# Restore using locked-mode for determinism
RUN dotnet restore "./src/Explore.API/Explore.API.csproj" -a $TARGETARCH --locked-mode

COPY . .
WORKDIR "/src/src/Explore.API"
RUN dotnet build "./Explore.API.csproj" -c $BUILD_CONFIGURATION -o /app/build \
      -a $TARGETARCH \
      --no-restore

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
ARG TARGETARCH
RUN dotnet publish "./Explore.API.csproj" -c $BUILD_CONFIGURATION -o /app/publish \
      -a $TARGETARCH \
      /p:UseAppHost=false \
      --no-restore

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
# See docs/CONFIGURATION.md for full list of options

USER $APP_UID
ENTRYPOINT ["dotnet", "Explore.API.dll"]
