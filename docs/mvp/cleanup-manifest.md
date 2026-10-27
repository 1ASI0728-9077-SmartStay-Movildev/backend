# Manifiesto de Retirada y Limpieza de Código Muerto / Legado

**Fecha:** 27 de octubre de 2026  
**Rama:** `chore/mvp-week-20261027`  
**Objetivo:** Registrar formalmente la justificación, dependientes y sustitutos de los componentes heredados del monolito para su desmantelamiento seguro.

---

## Matriz de Retirada

| Ruta | Motivo de Retirada | Dependientes Actuales | Sustituto Acreditado | Condición para Eliminación |
|---|---|---|---|---|
| `BackendAwSmartstay.API/` | Código del monolito original. Todas sus capacidades de negocio fueron extraídas a los 6 microservicios autónomos en `src/Services/`. | `tests/SmartStay.DomainTests/` (referencia de proyecto), `Dockerfile` raíz. | Los 6 microservicios en `src/Services/` (`SmartStay.Accommodations`, `SmartStay.Bookings`, `SmartStay.Payments`, `SmartStay.Profiles`, `SmartStay.IAM`, `SmartStay.Analytics`). | Portar invariantes críticas de `SmartStay.DomainTests` a los suites de microservicios o tests independientes. |
| `tests/SmartStay.DomainTests/` | Proyecto de pruebas de Fase 0 que contiene `ProjectReference` directa al monolito (`BackendAwSmartstay.API.csproj`). | `backend-aw-smartstay.sln`. | `tests/SmartStay.Accommodations.Tests/`, `tests/SmartStay.Bookings.Tests/`, `tests/SmartStay.Payments.Tests/`, etc. | Asegurar que los invariantes de modelo estén cubiertos en los proyectos de test por microservicio. |
| `Dockerfile` (raíz) | Genera imagen del monolito (`BackendAwSmartstay.API`). | `docker-compose.yml` (raíz). | `src/Services/*/Dockerfile` (un Dockerfile por cada microservicio). | Eliminación del monolito de la solución. |
| `docker-compose.yml` (raíz) | Archivo de Compose inicial del monolito con 2 réplicas y base única. | Ninguno en producción. | `compose.microservices.yml` (orquesta los 6 microservicios, MySQL 8 con esquemas lógicos y ActiveMQ). | Aprobación del manifiesto. |
| `nginx.conf` (raíz) | Configuración de Nginx para balancear entre `app1:80` y `app2:80` del monolito. | `docker-compose.yml` (raíz). | `deploy/nginx/nginx.conf` (Gateway perimetral que enruta a los 6 microservicios y bloquea `/internal/`). | Aprobación del manifiesto. |
| `BackendAwSmartstay.API/docker-compose.yml` | Configuración duplicada dentro de la carpeta del monolito. | Ninguno. | `compose.microservices.yml`. | Eliminación de la carpeta `BackendAwSmartstay.API/`. |
| `compose.microservices.yml` -> servicio `legacy` | Servicio monolítico mantenido con profile `legacy` como contingencia. | Ninguno (gateway ya no tiene fallback a legacy). | 6 microservicios autónomos en Compose. | Retirar del compose cuando el stack opere 100% independiente. |
