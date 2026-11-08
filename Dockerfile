# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY nuget.config OkulumYeryuzu.slnx ./
COPY src/Oy.Api/Oy.Api.csproj src/Oy.Api/
RUN dotnet restore src/Oy.Api/Oy.Api.csproj
COPY src/ src/
RUN dotnet publish src/Oy.Api/Oy.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
RUN mkdir -p /app/storage/media /app/storage/keys && chown -R app:app /app/storage
USER app
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
ENTRYPOINT ["dotnet", "Oy.Api.dll"]
