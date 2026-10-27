# Manifiesto de Retirada y Limpieza de Código Muerto / Legado (Estado Final)

**Fecha:** 27 de octubre de 2026  
**Rama:** `chore/mvp-week-20261027`  
**Estado:** **EJECUTADO Y VERIFICADO AL 100%**  
**Objetivo:** Registro formal del desmantelamiento total de los componentes del monolito original y la transición completa a la arquitectura de 7 microservicios autónomos.

---

## 1. Matriz de Retirada y Estado de Ejecución

| Componente | Motivo de Retirada | Estado Final | Sustituto Acreditado | Verificación |
|---|---|---|---|---|
| `BackendAwSmartstay.API/` | Código del monolito legado. Sus capacidades fueron distribuidas en los microservicios. | **ELIMINADO** (removido de git y del sistema de archivos). | 7 microservicios en `src/Services/` (`IAM`, `Accommodations`, `Bookings`, `Payments`, `Profiles`, `Analytics`, `Concierge`). | `dotnet sln list` y `tools/check-boundaries.sh` confirman 0 referencias al monolito. |
| `tests/SmartStay.DomainTests/` | Suite monolítica con acoplamiento directo a `BackendAwSmartstay.API.csproj`. | **ELIMINADO** (removido de git y del sistema de archivos). | Suites individuales: `SmartStay.Accommodations.Tests/`, `SmartStay.Bookings.Tests/`, `SmartStay.IAM.Tests/`, etc. | 33 pruebas unitarias autónomas ejecutándose en `Release` con 0 fallos. |
| `Dockerfile` (raíz) | Generación de imagen para el contenedor monolítico. | **ELIMINADO**. | `src/Services/*/Dockerfile` (un Dockerfile independiente por microservicio). | Cada microservicio se compila aisladamente. |
| `docker-compose.yml` (raíz) | Orquestación inicial del monolito con dos réplicas y base única. | **ELIMINADO**. | `compose.microservices.yml` (orquesta los 7 microservicios, MySQL 8 particionado por esquema lógico y ActiveMQ). | `docker compose -f compose.microservices.yml config` validado. |
| `nginx.conf` (raíz) | Balanceador de carga exclusivo para `app1:80` y `app2:80` monolítico. | **ELIMINADO**. | `deploy/nginx/nginx.conf` (Gateway perimetral que enruta por bounded context, aplica rate limiting y bloquea `/internal/`). | `curl /health` y rutas perimetrales validadas. |
| `compose.microservices.yml` -> servicio `legacy` | Servicio del monolito configurado con perfil `legacy`. | **ELIMINADO**. | El stack opera 100% desacoplado de dependencias legadas. | No existe contenedor `legacy` en el stack. |

---

## 2. Garantías de Aislamiento Acreditadas

1. **Cero referencias entre servicios**:
   - `tools/check-boundaries.sh` inspecciona los 7 proyectos en `src/Services/` y valida que ninguno contenga `ProjectReference` hacia otro microservicio ni hacia el monolito.
2. **Contratos compartidos exclusivamente vía DTOs inmutables**:
   - La biblioteca [`contracts/SmartStay.Contracts.csproj`](file:///media/okami/Universidad/ArquitecturaDeSoftwareEmergentes/backend/contracts/SmartStay.Contracts.csproj) únicamente expone records inmutables, constantes de roles y DTOs HTTP/mensajería sin dependencias de EF Core ni lógica de negocio.
3. **Persistencia segregada**:
   - Cada microservicio posee su propio `DbContext` y su propio esquema lógico en MySQL (`smartstay_iam`, `smartstay_accommodations`, `smartstay_bookings`, `smartstay_payments`, `smartstay_profiles`, `smartstay_analytics`).
