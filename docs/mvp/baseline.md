# SmartStay MVP — Línea Base y Localización (Fase 0)

**Fecha:** 27 de octubre de 2026  
**Rama activa:** `chore/mvp-week-20261027`  
**Commit base:** `0d0883bd5040dec5ce50232872739bfdc18d8e2a`  

---

## 1. Estado del Árbol de Trabajo y Git
```
HEAD: 0d0883bd5040dec5ce50232872739bfdc18d8e2a
Branch: chore/mvp-week-20261027
```

---

## 2. Proyectos en la Solución (`backend-aw-smartstay.sln`)
```
- BackendAwSmartstay.API/BackendAwSmartstay.API.csproj (Monolito legado)
- contracts/SmartStay.Contracts.csproj (Contratos compartidos)
- src/Services/SmartStay.Accommodations/SmartStay.Accommodations.csproj
- src/Services/SmartStay.Analytics/SmartStay.Analytics.csproj
- src/Services/SmartStay.Bookings/SmartStay.Bookings.csproj
- src/Services/SmartStay.IAM/SmartStay.IAM.csproj
- src/Services/SmartStay.Payments/SmartStay.Payments.csproj
- src/Services/SmartStay.Profiles/SmartStay.Profiles.csproj
- tests/SmartStay.Analytics.Tests/SmartStay.Analytics.Tests.csproj
- tests/SmartStay.Bookings.Tests/SmartStay.Bookings.Tests.csproj
- tests/SmartStay.DomainTests/SmartStay.DomainTests.csproj (Tests que referencian el monolito)
- tests/SmartStay.IAM.Tests/SmartStay.IAM.Tests.csproj
- tests/SmartStay.Payments.Tests/SmartStay.Payments.Tests.csproj
- tests/SmartStay.Profiles.Tests/SmartStay.Profiles.Tests.csproj
```

---

## 3. Configuración de Orquestación y Despliegue
- `compose.microservices.yml`: Validado (`docker compose config --quiet` exit code 0).
  - Servicios principales activos: `mysql`, `activemq`, `gateway` (Nginx), `iam`, `accommodations`, `bookings`, `payments`, `profiles`, `analytics`.
  - Servicio `legacy` marcado bajo profile `["legacy"]` (apagado por defecto).
  - Migradores declarados bajo profile `["migration"]`.
- `deploy/nginx/nginx.conf`: Enruta el 100% del tráfico funcional a los 6 microservicios.

---

## 4. Archivos Legados Identificados para Manifiesto de Retirada
- `BackendAwSmartstay.API/` (Monolito)
- `tests/SmartStay.DomainTests/` (Tests de dominio acoplados a `BackendAwSmartstay.API.csproj`)
- `Dockerfile` (raíz: construye monolito)
- `docker-compose.yml` (raíz: orquesta monolito y nginx legado)
- `nginx.conf` (raíz: proxy legado de réplicas monolíticas)
- `BackendAwSmartstay.API/docker-compose.yml`
