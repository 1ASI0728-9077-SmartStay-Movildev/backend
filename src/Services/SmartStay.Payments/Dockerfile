FROM mcr.microsoft.com/dotnet/sdk:9.0-bookworm-slim AS build
WORKDIR /src

# Copiar proyectos y contratos requeridos
COPY contracts/SmartStay.Contracts.csproj contracts/
COPY src/Services/SmartStay.Payments/SmartStay.Payments.csproj src/Services/SmartStay.Payments/

# Restaurar dependencias
RUN dotnet restore src/Services/SmartStay.Payments/SmartStay.Payments.csproj

# Copiar código fuente
COPY contracts/ contracts/
COPY src/Services/SmartStay.Payments/ src/Services/SmartStay.Payments/

# Compilar y publicar
WORKDIR /src/src/Services/SmartStay.Payments
RUN dotnet publish -c Release -o /app/publish --no-restore

# Etapa final de runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0-bookworm-slim AS runtime
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "SmartStay.Payments.dll"]
