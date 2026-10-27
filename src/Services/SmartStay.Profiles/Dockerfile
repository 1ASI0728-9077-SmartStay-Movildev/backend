FROM mcr.microsoft.com/dotnet/sdk:9.0-bookworm-slim AS build
WORKDIR /src

# Copiar proyectos y contratos requeridos
COPY contracts/SmartStay.Contracts.csproj contracts/
COPY src/Services/SmartStay.Profiles/SmartStay.Profiles.csproj src/Services/SmartStay.Profiles/

# Restaurar dependencias
RUN dotnet restore src/Services/SmartStay.Profiles/SmartStay.Profiles.csproj

# Copiar código fuente
COPY contracts/ contracts/
COPY src/Services/SmartStay.Profiles/ src/Services/SmartStay.Profiles/

# Compilar y publicar
WORKDIR /src/src/Services/SmartStay.Profiles
RUN dotnet publish -c Release -o /app/publish --no-restore

# Etapa final de runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0-bookworm-slim AS runtime
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "SmartStay.Profiles.dll"]
