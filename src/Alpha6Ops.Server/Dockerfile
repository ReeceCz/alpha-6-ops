# Alpha 6 OPS account server. Build from the repository root:
#   docker build -f src/Alpha6Ops.Server/Dockerfile -t alpha6ops-server .
# Runtime configuration comes entirely from environment variables (see docs/hosting.md).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props NuGet.Config ./
COPY src/Alpha6Ops.Identity/Alpha6Ops.Identity.csproj src/Alpha6Ops.Identity/
COPY src/Alpha6Ops.Accounts/Alpha6Ops.Accounts.csproj src/Alpha6Ops.Accounts/
COPY src/Alpha6Ops.Server/Alpha6Ops.Server.csproj src/Alpha6Ops.Server/
RUN dotnet restore src/Alpha6Ops.Server/Alpha6Ops.Server.csproj
COPY src/Alpha6Ops.Identity/ src/Alpha6Ops.Identity/
COPY src/Alpha6Ops.Accounts/ src/Alpha6Ops.Accounts/
COPY src/Alpha6Ops.Server/ src/Alpha6Ops.Server/
RUN dotnet publish src/Alpha6Ops.Server/Alpha6Ops.Server.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
COPY src/Alpha6Ops.Server/entrypoint.sh ./entrypoint.sh
RUN sed -i 's/\r$//' ./entrypoint.sh && chmod +x ./entrypoint.sh
ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=0
EXPOSE 8080
ENTRYPOINT ["./entrypoint.sh"]
