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
COPY ["global.json", "Directory.Build.props", "Directory.Packages.props", "dotnet-tools.json", "./"]
COPY ["eng/tools/Explore.GeneratedContracts/GeneratedContractTool.targets", "eng/tools/Explore.GeneratedContracts/"]
COPY ["src/Explore.Blazor/Explore.Blazor.csproj", "src/Explore.Blazor/packages.lock.json", "src/Explore.Blazor/"]
COPY ["src/Explore.Blazor.Client/Explore.Blazor.Client.csproj", "src/Explore.Blazor.Client/packages.lock.json", "src/Explore.Blazor.Client/"]
COPY ["src/Explore.ServiceDefaults/Explore.ServiceDefaults.csproj", "src/Explore.ServiceDefaults/packages.lock.json", "src/Explore.ServiceDefaults/"]
COPY ["src/Event.Web.BffHosting/Event.Web.BffHosting.csproj", "src/Event.Web.BffHosting/packages.lock.json", "src/Event.Web.BffHosting/"]

# Restore using locked-mode for determinism
RUN dotnet restore "./src/Explore.Blazor/Explore.Blazor.csproj" -a $TARGETARCH --locked-mode

RUN dotnet tool restore
COPY . .
WORKDIR "/src/src/Explore.Blazor"
RUN dotnet build "./Explore.Blazor.csproj" -c $BUILD_CONFIGURATION -o /app/build \
      -a $TARGETARCH \
      --no-restore

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
ARG TARGETARCH
RUN dotnet publish "./Explore.Blazor.csproj" -c $BUILD_CONFIGURATION -o /app/publish \
      -a $TARGETARCH \
      /p:UseAppHost=false \
      --no-restore

FROM base AS final
WORKDIR /app

# Copy published application
COPY --from=publish /app/publish .

# Secret management is handled by the application via AddSecretManagement()
# Configure via environment variables:
#   SECRET_PROVIDER=infisical (or "none" for env vars only)
#   SecretProvider__Infisical__ProjectId/ClientId/ClientSecret, etc.
# See docs/CONFIGURATION.md for full list of options

ENTRYPOINT ["dotnet", "Explore.Blazor.dll"]
