# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
ENV GITHUB_ACTIONS=false
COPY nuget.config .
COPY .dependencies/ .dependencies/
COPY Legacy.Maliev.AccountingService.Api/Legacy.Maliev.AccountingService.Api.csproj Legacy.Maliev.AccountingService.Api/
COPY Legacy.Maliev.AccountingService.Application/Legacy.Maliev.AccountingService.Application.csproj Legacy.Maliev.AccountingService.Application/
COPY Legacy.Maliev.AccountingService.Domain/Legacy.Maliev.AccountingService.Domain.csproj Legacy.Maliev.AccountingService.Domain/
COPY Legacy.Maliev.AccountingService.Data/Legacy.Maliev.AccountingService.Data.csproj Legacy.Maliev.AccountingService.Data/
RUN dotnet restore Legacy.Maliev.AccountingService.Api/Legacy.Maliev.AccountingService.Api.csproj \
    -p:MalievWorkspaceRoot=/src/.dependencies
COPY . .
RUN dotnet publish Legacy.Maliev.AccountingService.Api/Legacy.Maliev.AccountingService.Api.csproj \
    --configuration Release --no-restore --output /app /p:UseAppHost=false \
    -p:MalievWorkspaceRoot=/src/.dependencies

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build --chown=$APP_UID:$APP_UID /app .
USER $APP_UID
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "Legacy.Maliev.AccountingService.Api.dll"]
