# The API on its own, for a container host such as Render. The frontend is
# deployed separately (Vercel), which proxies /api here so the browser sees a
# single origin.
#
#   docker build -t datamind-api backend/AnalystAI.Api
#
# On a host whose disk does not survive a restart, set ConnectionStrings__Default
# to a PostgreSQL connection string: accounts, data and session keys then live
# in the database. Without one, SQLite writes to /data, which must then be a
# persistent volume. Also set:
#   Security__TrustedOrigins__0      https://<your-app>.vercel.app
#   Security__TrustForwardedHeaders  true
#   Security__ForwardLimit           the number of proxies in front of the API

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY AnalystAI.Api.csproj ./
# ReadyToRun compiles the app ahead of time, so a start on a small, throttled
# instance (Render's free plan has 0.1 CPU) spends far less time in the JIT.
# The restore has to know about it too: that is what fetches the compiler the
# publish below uses, and the publish does not restore again.
RUN dotnet restore -r linux-x64 -p:PublishReadyToRun=true
COPY . ./
RUN dotnet publish -c Release -o /app --no-restore -r linux-x64 --self-contained false \
    -p:PublishReadyToRun=true

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app ./

RUN mkdir -p /data && chown -R "$APP_UID" /data

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    ConnectionStrings__Default="Data Source=/data/analystai.db"

EXPOSE 8080

# The non-root user the .NET images ship with.
USER $APP_UID

ENTRYPOINT ["dotnet", "AnalystAI.Api.dll"]
