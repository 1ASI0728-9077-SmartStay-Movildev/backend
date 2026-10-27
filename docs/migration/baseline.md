# Línea Base Técnica y Defectos Conocidos (Fase 0)

Fecha: 26 de octubre de 2026  
Rama de trabajo: `feat/microservices-foundation`  
Commit base: `a8178218e0e2f142199b9cd97e5c798a6023e31f`  
Commit de estabilización de compilación: `479813a` (`fix(shared): resolve pre-existing compilation error in UseOpenApiConfiguration`)

---

## 1. Entorno y Herramientas

| Componente | Versión detectada | Notas |
|---|---|---|
| .NET SDK | `9.0.318` | Ubuntu x64 |
| ASP.NET Core Runtime | `9.0.20` | `net9.0` |
| Docker Engine | `29.1.3` (build 29.1.3-0ubuntu4.1) | Runtime de contenedores |
| Docker Compose | `2.40.3+ds1-0ubuntu1` | Orquestación local |
| MySQL Server | `8.0` / MySQL compatible | Configurado en `appsettings.json` vía puerto 3306 |
| ActiveMQ Classic | OpenWire `tcp://activemq:61616` | Apache NMS 2.2.0 |

---

## 2. Dependencias del Monolito (`BackendAwSmartstay.API.csproj`)

```xml
<TargetFramework>net9.0</TargetFramework>
```

- **Persistencia y ORM**:
  - `Microsoft.EntityFrameworkCore` `9.0.10`
  - `Microsoft.EntityFrameworkCore.Relational` `9.0.10`
  - `Microsoft.EntityFrameworkCore.Design` `9.0.10`
  - `Pomelo.EntityFrameworkCore.MySql` `9.0.0`
  - `EntityFrameworkCore.CreatedUpdatedDate` `8.0.0`
- **Mensajería**:
  - `Apache.NMS` `2.2.0`
  - `Apache.NMS.ActiveMQ` `2.2.0`
- **Seguridad e Identidad**:
  - `Microsoft.AspNetCore.Authentication.JwtBearer` `9.0.10`
  - `System.IdentityModel.Tokens.Jwt` `8.14.0`
  - `Microsoft.IdentityModel.Tokens` `8.14.0`
  - `BCrypt.Net-Next` `4.0.3`
- **Resiliencia y Caché**:
  - `Polly` `8.7.0`
  - `StackExchange.Redis` `2.13.17`
- **Documentación y Convenciones**:
  - `Swashbuckle.AspNetCore` `9.0.6`
  - `Swashbuckle.AspNetCore.Annotations` `9.0.6`
  - `Microsoft.AspNetCore.OpenApi` `9.0.10`
  - `Humanizer` `2.14.1`
- **Mediador**:
  - `Cortex.Mediator` `2.1.0`
- **Health Checks**:
  - `AspNetCore.HealthChecks.MySql` `9.0.0`

---

## 3. Defectos Conocidos y Acoplamientos Preexistentes

1. **Error de sintaxis en `WebApplicationExtensions.cs` (Corregido)**:
   - *Ubicación*: `BackendAwSmartstay.API/Shared/Infrastructure/Documentation/OpenApi/Configuration/Extensions/WebApplicationExtensions.cs`
   - *Problema*: Método `public static void UseOpenApiConfiguration` contenía `return app;`, provocando error CS0127.
   - *Estado*: Corregido en commit `479813a`.

2. **Ausencia de Migraciones EF Core Versionadas**:
   - *Hallazgo*: No existía directorio `Migrations/` ni snapshots en el proyecto.
   - *Impacto*: `Program.cs` ejecutaba `context.Database.MigrateAsync()`, el cual no creaba esquemas sin migraciones previas. Existía un helper `EnsureDatabaseCreated()` sin integrar al arranque principal.

3. **Acoplamiento Síncrono Transaccional Payments → Bookings**:
   - *Ubicación*: `Payments/Application/Internal/CommandServices/PaymentCommandService.cs`
   - *Problema*: Inyectaba `IBookingRepository` y modificaba el estado de la reserva (`booking.Confirm()`) usando el `IUnitOfWork` compartido dentro de la misma transacción de base de datos del pago.

4. **Acoplamiento de Lectura Cruzada en Analytics**:
   - *Ubicación*: `Analytics/Infrastructure/Persistence/EFC/Repositories/AnalyticsRepository.cs`
   - *Problema*: Consultaba directamente las tablas `payments`, `bookings` y `rooms` mediante `context.Set<T>()`.
   - *Bugs de lógica detectados*:
     - Intervalo mensual `startOfMonth.AddMonths(1).AddDays(-1)` a medianoche omite las últimas 24 horas del mes.
     - Tasa de ocupación dividía `activeBookings / totalRooms * 100` en lugar de calcular noches-habitación sobre inventario disponible.

5. **Infraestructura de Mensajería Incompleta**:
   - *Ubicación*: `Shared/Infrastructure/Messaging/ActiveMqProducer.cs`
   - *Problema*: Creaba conexión, sesión y productor por cada mensaje enviado (sin pool ni ciclo de vida gestionado); utilizaba colas punto a punto (`Queue`) sin capacidad de fan-out hacia múltiples consumidores (Bookings y Analytics); no existía consumidor ni DLQ configurado.

6. **Acoplamiento de Autorización e Identidad**:
   - *Ubicación*: `IAM/Infrastructure/Pipeline/Middleware/Components/RequestAuthorizationMiddleware.cs`
   - *Problema*: El middleware resolvía al usuario en base de datos en cada petición, inyectando la entidad `User` en `HttpContext.Items["User"]`. Los controladores dependían de este objeto para verificar tenant (`hotel_id`, `chain_id`) y permisos.

7. **Configuración de Despliegue en `docker-compose.yml`**:
   - *Problema*: Contenedores dependientes de `host.docker.internal:3306`, credenciales fijas de `root:12345678` en texto plano, tags `:latest` en imágenes de Nginx y ActiveMQ, y ausencia de servicios de bases de datos aislados.

---

## 4. Comandos de Verificación de Línea Base

```bash
# Restaurar y compilar solución
dotnet restore backend-aw-smartstay.sln
dotnet build backend-aw-smartstay.sln --no-restore

# Inspección de esquemas y modelos
dotnet ef dbcontext info --project BackendAwSmartstay.API
```
