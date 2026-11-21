# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props InvoiceHub.sln ./
COPY src/InvoiceHub.Domain/*.csproj src/InvoiceHub.Domain/
COPY src/InvoiceHub.Application/*.csproj src/InvoiceHub.Application/
COPY src/InvoiceHub.Infrastructure/*.csproj src/InvoiceHub.Infrastructure/
COPY src/InvoiceHub.Api/*.csproj src/InvoiceHub.Api/
RUN dotnet restore src/InvoiceHub.Api/InvoiceHub.Api.csproj
COPY src/ src/
RUN dotnet publish src/InvoiceHub.Api/InvoiceHub.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "InvoiceHub.Api.dll"]
