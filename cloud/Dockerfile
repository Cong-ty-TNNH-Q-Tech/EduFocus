FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY EduFocus.Cloud.Api/EduFocus.Cloud.Api.csproj EduFocus.Cloud.Api/
COPY EduFocus.Cloud.Contracts/EduFocus.Cloud.Contracts.csproj EduFocus.Cloud.Contracts/
COPY EduFocus.Cloud.Data/EduFocus.Cloud.Data.csproj EduFocus.Cloud.Data/
RUN dotnet restore EduFocus.Cloud.Api/EduFocus.Cloud.Api.csproj

COPY . .
RUN dotnet publish EduFocus.Cloud.Api/EduFocus.Cloud.Api.csproj \
    --configuration Release \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app/publish .
RUN apt-get update \
    && apt-get install -y --no-install-recommends sqlite3 \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /data \
    && chown "$APP_UID:$APP_UID" /data
USER $APP_UID
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD ["dotnet", "EduFocus.Cloud.Api.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "EduFocus.Cloud.Api.dll"]
