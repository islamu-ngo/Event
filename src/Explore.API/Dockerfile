# Secrets are acquired asynchronously by the selected host-owned SecretKit authority.
# Supports zero-downtime deployments with Coolify

FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:de0ee358cdd672f7017a003bed21e9ac9ed48a27c922a59b18175b6e82fa7fee AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.403@sha256:de4fe90a98845d75910b9769662465c84de1a934665c431279d1382b2bddc02b AS build
ARG BUILD_CONFIGURATION=Release
ARG TARGETARCH
WORKDIR /src

# Copy restore inputs and project graph first for deterministic locked restore caching
COPY ["global.json", "Directory.Build.props", "Directory.Packages.props", "./"]
COPY ["NuGet.Config", "./"]
COPY ["src/Directory.Build.props", "src/"]
COPY ["vendor/secretkit/", "vendor/secretkit/"]
COPY ["eng/tools/Explore.GeneratedContracts/GeneratedContractTool.targets", "eng/tools/Explore.GeneratedContracts/"]
COPY ["src/Explore.API/Explore.API.csproj", "src/Explore.API/packages.lock.json", "src/Explore.API/"]
COPY ["src/Explore.Application/Explore.Application.csproj", "src/Explore.Application/packages.lock.json", "src/Explore.Application/"]
COPY ["src/Explore.Atproto.Transport/Explore.Atproto.Transport.csproj", "src/Explore.Atproto.Transport/packages.lock.json", "src/Explore.Atproto.Transport/"]
COPY ["src/Explore.Domain/Explore.Domain.csproj", "src/Explore.Domain/packages.lock.json", "src/Explore.Domain/"]
COPY ["src/Event.Wire.Contracts/Event.Wire.Contracts.csproj", "src/Event.Wire.Contracts/packages.lock.json", "src/Event.Wire.Contracts/"]
COPY ["src/Explore.Infrastructure/Explore.Infrastructure.csproj", "src/Explore.Infrastructure/packages.lock.json", "src/Explore.Infrastructure/"]
COPY ["src/Explore.Persistence/Explore.Persistence.csproj", "src/Explore.Persistence/packages.lock.json", "src/Explore.Persistence/"]
COPY ["src/Explore.ServiceDefaults/Explore.ServiceDefaults.csproj", "src/Explore.ServiceDefaults/packages.lock.json", "src/Explore.ServiceDefaults/"]
COPY ["src/ISLAMU.Moderation.Application/ISLAMU.Moderation.Application.csproj", "src/ISLAMU.Moderation.Application/packages.lock.json", "src/ISLAMU.Moderation.Application/"]
COPY ["src/ISLAMU.Moderation.Domain/ISLAMU.Moderation.Domain.csproj", "src/ISLAMU.Moderation.Domain/packages.lock.json", "src/ISLAMU.Moderation.Domain/"]

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
COPY --from=build /src/vendor/secretkit /app/vendor/secretkit

# Bundle Cerbos policy package assets for manual download and boot sync fallback.
# Compose deployments may still mount /app/cerbos/policies read-only to override this copy.
COPY --from=build /src/cerbos ./cerbos

# The application prepares its selected authority before registering consumers.
# Configure via environment variables:
#   SecretProvider__Provider=Environment or Infisical (required; no fallback)
#   SecretProvider__Infisical__ProjectId/ClientId/ClientSecret, etc.
# See docs/CONFIGURATION.md for full list of options

USER $APP_UID
ENTRYPOINT ["dotnet", "Explore.API.dll"]
