# Stage 1: Build Angular Dashboard
FROM node:20-alpine AS dashboard-build
WORKDIR /app
COPY src/platform-dashboard-web/package*.json ./
RUN npm ci
COPY src/platform-dashboard-web/ ./
RUN npm run build -- --configuration=production

# Stage 2: Build and Publish .NET Services
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS dotnet-build
WORKDIR /app
COPY . .
RUN dotnet restore src/Platform.Api/Platform.Api.csproj
RUN dotnet restore src/Platform.Ingestion/Platform.Ingestion.csproj
RUN dotnet publish src/Platform.Api/Platform.Api.csproj -c Release -o /app/publish/api
RUN dotnet publish src/Platform.Ingestion/Platform.Ingestion.csproj -c Release -o /app/publish/ingestion

# Stage 3: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Create non-root user
RUN adduser --disabled-password --gecos "" plaxion
RUN mkdir -p /app/wwwroot/assets && chown -R plaxion:plaxion /app
USER plaxion

# Copy .NET publish outputs
COPY --from=dotnet-build --chown=plaxion:plaxion /app/publish/api ./
COPY --from=dotnet-build --chown=plaxion:plaxion /app/publish/ingestion ./

# Copy Angular dist to Api's wwwroot
# Angular 17+ with application builder outputs to dist/platform-dashboard-web/browser
COPY --from=dashboard-build --chown=plaxion:plaxion /app/dist/platform-dashboard-web/browser ./wwwroot

# Copy entrypoint
COPY --chown=plaxion:plaxion docker/entrypoint.sh ./entrypoint.sh
RUN chmod +x ./entrypoint.sh

ENV ROLE=""
ENV API_BASE_URL=""
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["./entrypoint.sh"]
