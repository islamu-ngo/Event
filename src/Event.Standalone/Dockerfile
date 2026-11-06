FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.403@sha256:212fe4cb74627d5adf9817a4088906ae3edff1bd853bb27565d4b46731efe61e AS build
ARG TARGETARCH
WORKDIR /src

COPY ["global.json", "Directory.Build.props", "Directory.Packages.props", "./"]
COPY ["NuGet.Config", "./"]
COPY ["src/Directory.Build.props", "src/"]
COPY ["vendor/secretkit/", "vendor/secretkit/"]
COPY ["eng/tools/Explore.GeneratedContracts/GeneratedContractTool.targets", "eng/tools/Explore.GeneratedContracts/"]
COPY ["dotnet-tools.json", "./"]
COPY ["src/Event.Standalone/Event.Standalone.csproj", "src/Event.Standalone/packages.lock.json", "src/Event.Standalone/"]
COPY ["src/Event.Standalone/container/packages.lock.json", "src/Event.Standalone/container/"]
COPY ["src/Event.MigrationService/Event.MigrationService.csproj", "src/Event.MigrationService/packages.lock.json", "src/Event.MigrationService/"]
COPY ["src/Explore.API/Explore.API.csproj", "src/Explore.API/packages.lock.json", "src/Explore.API/"]
COPY ["src/Explore.Application/Explore.Application.csproj", "src/Explore.Application/packages.lock.json", "src/Explore.Application/"]
COPY ["src/Explore.Atproto.Transport/Explore.Atproto.Transport.csproj", "src/Explore.Atproto.Transport/packages.lock.json", "src/Explore.Atproto.Transport/"]
COPY ["src/Explore.Blazor/Explore.Blazor.csproj", "src/Explore.Blazor/packages.lock.json", "src/Explore.Blazor/"]
COPY ["src/Explore.Blazor/Components/App.razor", "src/Explore.Blazor/Components/"]
COPY ["src/Explore.Blazor.Client/Explore.Blazor.Client.csproj", "src/Explore.Blazor.Client/packages.lock.json", "src/Explore.Blazor.Client/"]
COPY ["src/Explore.Blazor.Client/_Imports.razor", "src/Explore.Blazor.Client/"]
COPY ["src/Explore.Domain/Explore.Domain.csproj", "src/Explore.Domain/packages.lock.json", "src/Explore.Domain/"]
COPY ["src/Event.Wire.Contracts/Event.Wire.Contracts.csproj", "src/Event.Wire.Contracts/packages.lock.json", "src/Event.Wire.Contracts/"]
COPY ["src/Explore.Infrastructure/Explore.Infrastructure.csproj", "src/Explore.Infrastructure/packages.lock.json", "src/Explore.Infrastructure/"]
COPY ["src/Explore.Persistence/Explore.Persistence.csproj", "src/Explore.Persistence/packages.lock.json", "src/Explore.Persistence/"]
COPY ["src/Explore.Persistence.DataProtection.Migrations.MySql/Explore.Persistence.DataProtection.Migrations.MySql.csproj", "src/Explore.Persistence.DataProtection.Migrations.MySql/packages.lock.json", "src/Explore.Persistence.DataProtection.Migrations.MySql/"]
COPY ["src/Explore.Persistence.DataProtection.Migrations.Sqlite/Explore.Persistence.DataProtection.Migrations.Sqlite.csproj", "src/Explore.Persistence.DataProtection.Migrations.Sqlite/packages.lock.json", "src/Explore.Persistence.DataProtection.Migrations.Sqlite/"]
COPY ["src/Explore.Persistence.DataProtection.Migrations.SqlServer/Explore.Persistence.DataProtection.Migrations.SqlServer.csproj", "src/Explore.Persistence.DataProtection.Migrations.SqlServer/packages.lock.json", "src/Explore.Persistence.DataProtection.Migrations.SqlServer/"]
COPY ["src/Explore.Persistence.Migrations.MySql/Explore.Persistence.Migrations.MySql.csproj", "src/Explore.Persistence.Migrations.MySql/packages.lock.json", "src/Explore.Persistence.Migrations.MySql/"]
COPY ["src/Explore.Persistence.Migrations.Sqlite/Explore.Persistence.Migrations.Sqlite.csproj", "src/Explore.Persistence.Migrations.Sqlite/packages.lock.json", "src/Explore.Persistence.Migrations.Sqlite/"]
COPY ["src/Explore.Persistence.Migrations.SqlServer/Explore.Persistence.Migrations.SqlServer.csproj", "src/Explore.Persistence.Migrations.SqlServer/packages.lock.json", "src/Explore.Persistence.Migrations.SqlServer/"]
COPY ["src/Explore.Persistence.PrivacyErasureAuthority.Migrations.Sqlite/Explore.Persistence.PrivacyErasureAuthority.Migrations.Sqlite.csproj", "src/Explore.Persistence.PrivacyErasureAuthority.Migrations.Sqlite/packages.lock.json", "src/Explore.Persistence.PrivacyErasureAuthority.Migrations.Sqlite/"]
COPY ["src/Explore.ServiceDefaults/Explore.ServiceDefaults.csproj", "src/Explore.ServiceDefaults/packages.lock.json", "src/Explore.ServiceDefaults/"]
COPY ["src/ISLAMU.Moderation.Application/ISLAMU.Moderation.Application.csproj", "src/ISLAMU.Moderation.Application/packages.lock.json", "src/ISLAMU.Moderation.Application/"]
COPY ["src/ISLAMU.Moderation.Domain/ISLAMU.Moderation.Domain.csproj", "src/ISLAMU.Moderation.Domain/packages.lock.json", "src/ISLAMU.Moderation.Domain/"]
COPY ["src/Event.Web.BffHosting/Event.Web.BffHosting.csproj", "src/Event.Web.BffHosting/packages.lock.json", "src/Event.Web.BffHosting/"]
RUN dotnet restore "src/Event.MigrationService/Event.MigrationService.csproj" -a $TARGETARCH --locked-mode \
    && dotnet restore "src/Event.Standalone/Event.Standalone.csproj" -a $TARGETARCH --locked-mode /p:StandaloneContainerPublish=true \
    && dotnet tool restore

COPY . .
RUN dotnet build "src/Event.MigrationService/Event.MigrationService.csproj" -c Release -a $TARGETARCH --no-restore -maxcpucount:1 \
    && dotnet publish "src/Event.Standalone/Event.Standalone.csproj" -c Release -o /app/publish -a $TARGETARCH /p:StandaloneContainerPublish=true /p:DebugType=none /p:ErrorOnDuplicatePublishOutputFiles=false --no-restore -maxcpucount:1 \
    && find /app/publish/wwwroot -type f -name '*.gz' -delete \
    && cp src/Event.Standalone/appsettings.json /app/publish/appsettings.json \
    && mkdir /app/data

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0.14-noble-chiseled-extra@sha256:56b6c354a140e50e09d45a397b43f9f9d31012c325020239e602e06d1594f8ff AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
ENV HttpsRedirection__Enabled=false
ENV HttpsRedirection__HttpsPort=
ENV SETUP_SECRET_FILE=/app/data/setup-secret
EXPOSE 8080
COPY --from=build /app/publish .
COPY --from=build /src/vendor/secretkit /app/vendor/secretkit
COPY --chown=$APP_UID:$APP_UID --from=build /app/data /app/data
COPY --chown=$APP_UID:$APP_UID --from=build /src/deploy/bootstrap/README.md /etc/islamu-event/bootstrap/README.md
COPY --chown=$APP_UID:$APP_UID --from=build /src/schemas/configuration-manifest-v1alpha2.schema.json /app/schemas/configuration-manifest-v1alpha2.schema.json
COPY --chown=$APP_UID:$APP_UID --from=build /src/schemas/tenant-configuration-package-v1alpha2.schema.json /app/schemas/tenant-configuration-package-v1alpha2.schema.json
USER $APP_UID
ENTRYPOINT ["./Event.Standalone"]
