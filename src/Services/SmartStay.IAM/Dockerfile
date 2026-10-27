FROM mcr.microsoft.com/dotnet/sdk:9.0-bookworm-slim AS build
WORKDIR /src

# Copiar proyectos y contratos requeridos
COPY contracts/SmartStay.Contracts.csproj contracts/
COPY src/Services/SmartStay.IAM/SmartStay.IAM.csproj src/Services/SmartStay.IAM/

# Restaurar dependencias
RUN dotnet restore src/Services/SmartStay.IAM/SmartStay.IAM.csproj

# Copiar código fuente
COPY contracts/ contracts/
COPY src/Services/SmartStay.IAM/ src/Services/SmartStay.IAM/

# Compilar y publicar
WORKDIR /src/src/Services/SmartStay.IAM
RUN dotnet publish -c Release -o /app/publish --no-restore

# Etapa final de runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0-bookworm-slim AS runtime
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "SmartStay.IAM.dll"]
