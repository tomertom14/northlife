FROM node:24-bookworm-slim AS web-build
WORKDIR /src/frontend
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api-build
WORKDIR /src
COPY NorthLife.slnx global.json ./
COPY backend/NorthLife.Api/NorthLife.Api.csproj backend/NorthLife.Api/
RUN dotnet restore backend/NorthLife.Api/NorthLife.Api.csproj
COPY backend/NorthLife.Api/ backend/NorthLife.Api/
RUN dotnet publish backend/NorthLife.Api/NorthLife.Api.csproj --configuration Release --no-restore --output /app/publish
COPY --from=web-build /src/frontend/dist/northlife-web/browser/ /app/publish/wwwroot/

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=10000 \
    ASPNETCORE_ENVIRONMENT=Production \
    ImageStorage__RootPath=/var/data/images
RUN apt-get update && apt-get install --yes --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /var/data/images \
    && chown -R $APP_UID:$APP_UID /var/data
COPY --from=api-build --chown=$APP_UID:$APP_UID /app/publish/ ./
USER $APP_UID
EXPOSE 10000
ENTRYPOINT ["dotnet", "NorthLife.Api.dll"]
