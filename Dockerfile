# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restaurar primero (aprovecha la caché de capas de Docker)
COPY Api/RealEstate.Api.csproj Api/
RUN dotnet restore Api/RealEstate.Api.csproj

COPY Api/ Api/
RUN dotnet publish Api/RealEstate.Api.csproj -c Release -o /app --no-restore

# ---------- runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Render termina el HTTPS y reenvía por un proxy: este switch procesa X-Forwarded-For / -Proto
# (necesario para ver la IP real del cliente en el límite de intentos de login).
ENV ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=0

COPY --from=build /app .

# Usuario sin privilegios (definido por las imágenes oficiales de .NET)
USER $APP_UID

# El puerto lo fija Render con la variable PORT (ver Program.cs)
ENTRYPOINT ["dotnet", "RealEstate.Api.dll"]
