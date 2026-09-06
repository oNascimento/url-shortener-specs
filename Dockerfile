FROM mcr.microsoft.com/dotnet/sdk:10.0.400 AS build
ARG SERVICE=Api
WORKDIR /repo
COPY global.json Directory.Build.props ./
COPY src ./src
RUN dotnet restore src/Shortener.${SERVICE}/Shortener.${SERVICE}.csproj --locked-mode && dotnet publish src/Shortener.${SERVICE}/Shortener.${SERVICE}.csproj --no-restore -c Release -o /out
FROM mcr.microsoft.com/dotnet/aspnet:10.0.11
ARG SERVICE=Api
ENV SERVICE_DLL=Shortener.${SERVICE}.dll ASPNETCORE_HTTP_PORTS=8080
WORKDIR /app
COPY --from=build /out .
USER $APP_UID
ENTRYPOINT ["sh", "-c", "exec dotnet $SERVICE_DLL \"$@\"", "--"]
