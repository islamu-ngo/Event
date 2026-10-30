# The selected host-owned SecretKit authority prepares frontend secrets before startup.
# Supports zero-downtime deployments with Coolify

FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:d5633778c754e89a386882eddf8dbee6942447a8a4b8267c19cf1e1158d0b0ac AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.302@sha256:72dd743782f2ae7e5476fd64f6a460045e3998dc862218b80e6944cba79a01b0 AS build
ARG BUILD_CONFIGURATION=Release
ARG TARGETARCH
WORKDIR /src

# Copy restore inputs and project graph first for deterministic locked restore caching
COPY ["global.json", "Directory.Build.props", "Directory.Packages.props", "dotnet-tools.json", "./"]
COPY ["NuGet.Config", "./"]
COPY ["src/Directory.Build.props", "src/"]
COPY ["vendor/secretkit/", "vendor/secretkit/"]
COPY ["eng/tools/Explore.GeneratedContracts/GeneratedContractTool.targets", "eng/tools/Explore.GeneratedContracts/"]
COPY ["src/Explore.Blazor/Explore.Blazor.csproj", "src/Explore.Blazor/packages.lock.json", "src/Explore.Blazor/"]
COPY ["src/Explore.Blazor/Components/App.razor", "src/Explore.Blazor/Components/"]
COPY ["src/Explore.Blazor.Client/Explore.Blazor.Client.csproj", "src/Explore.Blazor.Client/packages.lock.json", "src/Explore.Blazor.Client/"]
COPY ["src/Explore.Blazor.Client/_Imports.razor", "src/Explore.Blazor.Client/"]
COPY ["src/Explore.Atproto.Transport/Explore.Atproto.Transport.csproj", "src/Explore.Atproto.Transport/packages.lock.json", "src/Explore.Atproto.Transport/"]
COPY ["src/Explore.ServiceDefaults/Explore.ServiceDefaults.csproj", "src/Explore.ServiceDefaults/packages.lock.json", "src/Explore.ServiceDefaults/"]
COPY ["src/ISLAMU.Moderation.Application/ISLAMU.Moderation.Application.csproj", "src/ISLAMU.Moderation.Application/packages.lock.json", "src/ISLAMU.Moderation.Application/"]
COPY ["src/ISLAMU.Moderation.Domain/ISLAMU.Moderation.Domain.csproj", "src/ISLAMU.Moderation.Domain/packages.lock.json", "src/ISLAMU.Moderation.Domain/"]
COPY ["src/Event.Web.BffHosting/Event.Web.BffHosting.csproj", "src/Event.Web.BffHosting/packages.lock.json", "src/Event.Web.BffHosting/"]
COPY ["src/Event.Wire.Contracts/Event.Wire.Contracts.csproj", "src/Event.Wire.Contracts/packages.lock.json", "src/Event.Wire.Contracts/"]

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
COPY --from=build /src/vendor/secretkit /app/vendor/secretkit

# Startup requires one explicit secret authority with no fallback.
# Configure via environment variables:
#   SECRET_PROVIDER=Infisical or Environment
#   SecretProvider__Infisical__ProjectId/ClientId/ClientSecret, etc.
# See docs/internal/CONFIGURATION.md for the complete input contract.

ENTRYPOINT ["dotnet", "Explore.Blazor.dll"]
