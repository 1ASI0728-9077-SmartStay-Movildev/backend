# SmartStay — Plataforma de Hospedaje y Descubrimiento Asistido (Microservicios & MVP 7 Días)

SmartStay es una plataforma hotelera moderna construida sobre una arquitectura de **microservicios autónomos en .NET 9**, con persistencia segregada en **MySQL 8**, mensajería asíncrona mediante **Apache ActiveMQ Artemis**, pasarela perimetral con **Nginx**, asistencia conversacional basada en **DeepSeek con garantías estrictas de precio de servidor**, recorridos virtuales **360° interactivos** y compartición P2P descentralizada mediante **Google Nearby Connections (Android)**.

---

## 1. Arquitectura del Sistema

```
                                  [ Clientes: Web (Vue 3) / Móvil (Android) ]
                                                       │
                                                       ▼
                                         ┌───────────────────────────┐
                                         │   Nginx API Gateway       │
                                         │   (Rate Limiting & Proxy) │
                                         │   Puerto 8080             │
                                         └─────────────┬─────────────┘
                                                       │
         ┌──────────────────┬──────────────────┬───────┴──────────┬──────────────────┬──────────────────┐
         ▼                  ▼                  ▼                  ▼                  ▼                  ▼
┌─────────────────┐┌─────────────────┐┌─────────────────┐┌─────────────────┐┌─────────────────┐┌─────────────────┐
│ SmartStay.IAM   ││ Accommodations  ││   Bookings      ││ SmartStay.      ││    Payments     ││   Analytics     │
│ Autenticación   ││ Catálogo &      ││ Reservas,       ││   Concierge     ││  Idempotencia   ││  Proyecciones   │
│ & Roles JWT     ││ Tour 360°       ││ Concurrencia    ││ IA Grounding &  ││ & Outbox        ││ & Métricas      │
│                 ││                 ││ & TTL Worker    ││ Precios Server  ││                 ││                 │
└────────┬────────┘└────────┬────────┘└────────┬────────┘└────────┬────────┘└────────┬────────┘└────────┬────────┘
         │                  │                  │                  │                  │                  │
         │                  │                  │                  │                  ▼                  ▼
         │                  │                  │                  │          ┌────────────────────────────┐
         │                  │                  │                  │          │   ActiveMQ Artemis (AMQP)  │
         │                  │                  │                  │          └────────────────────────────┘
         ▼                  ▼                  ▼                  ▼                         │
   smartstay_iam      accommodations        bookings           (Stateless)                  ▼
         │                  │                  │                                    payments / analytics
         └──────────────────┴──────────────────┴────────────────────────────────────────────┘
                                        MySQL 8 (Esquemas Lógicos Aislados)
```

### Microservicios Desarrollados

| Microservicio | Responsabilidad | Base de Datos | Rutas Perimetrales |
|---|---|---|---|
| **SmartStay.IAM** | Autenticación de usuarios, emisión de JWT y validación interna. | `smartstay_iam` | `/api/v1/authentication`, `/api/v1/users` |
| **SmartStay.Accommodations** | Catálogo de hoteles, habitaciones y manifiestos de Recorrido 360°. | `smartstay_accommodations` | `/api/v1/rooms`, `/api/v1/hotels`, `/tours` |
| **SmartStay.Bookings** | Creación de reservas, exclusión física de noches (`room_night_locks`), expiración TTL y control de propiedad. | `smartstay_bookings` | `/api/v1/bookings` |
| **SmartStay.Concierge** | Asistente inteligente con DeepSeek; valida presupuestos y precios autoritativos de servidor. | Stateless / Memoria | `/api/v1/concierge` |
| **SmartStay.Payments** | Pagos simulados con clave de idempotencia, outbox y compensación de reembolsos. | `smartstay_payments` | `/api/v1/payments` |
| **SmartStay.Profiles** | Gestión de perfiles de huéspedes y administradores. | `smartstay_profiles` | `/api/v1/profiles` |
| **SmartStay.Analytics** | Proyecciones autónomas y métricas de negocio. | `smartstay_analytics` | `/api/v1/analytics` |

---

## 2. Requisitos Previos

- **Docker** 24.0+ y **Docker Compose** v2
- **.NET SDK 9.0** (para desarrollo y pruebas locales)
- **curl** y **bash** (para scripts de automatización)

---

## 3. Puesta en Marcha en 1 Solo Paso

Desde la raíz del repositorio, ejecute:

```bash
# 1. Construir y levantar todos los microservicios, MySQL, ActiveMQ y Nginx Gateway
docker compose -f compose.microservices.yml up --build -d

# 2. Verificar que todos los contenedores reporten estado 'healthy'
docker compose -f compose.microservices.yml ps
```

---

## 4. Inicialización de Datos y Pruebas End-to-End

### Sembrado Automatizado del Fixture (Idempotente)
Configura usuarios de prueba en IAM (Admin Hotel 1, Admin Hotel 2, Guest 1, Guest 2) y verifica la autenticación HTTP:

```bash
bash tools/seed-mvp.sh
```

### Ejecución de Pruebas de Humo (End-to-End)
Ejecuta la suite integral de 13 pasos validando:
1. Salud de Gateway (`/health`).
2. Autenticación de identidades.
3. Catálogo de habitaciones en Accommodations (`/api/v1/rooms/search`).
4. Manifiesto del recorrido virtual 360° (`/api/v1/rooms/102/tour`).
5. Carga de assets panorámicos equirectangulares (`/tours/room-102/entry.jpg`).
6. Recomendación asistida de Concierge dentro del presupuesto.
7. Discriminación honesta de Concierge cuando el presupuesto no alcanza.
8. Creación de reserva autoritativa.
9. Consulta de reservas propias (`/api/v1/bookings/mine`).
10. **Aislamiento Estricto:** Huésped 2 no puede leer la reserva del Huésped 1 (HTTP 403 Forbidden).
11. **Aislamiento Estricto:** Huésped 2 no puede cancelar la reserva del Huésped 1 (HTTP 403 Forbidden).
12. **Alcance de Administradores:** Administrador de Cusco no puede gestionar reservas de Hotel Bolívar en Lima (HTTP 403 Forbidden).
13. Cancelación legítima por el huésped propietario y liberación física de inventario.

```bash
bash tools/smoke-mvp.sh
```

---

## 5. Pruebas Unitarias y Fronteras de Aislamiento

```bash
# Ejecutar toda la suite de pruebas unitarias (.NET 9)
dotnet test backend-aw-smartstay.sln -c Release

# Verificar que NINGÚN microservicio viole las fronteras arquitectónicas
bash tools/check-boundaries.sh
```

---

## 6. Características Clave del MVP (7 Días)

### Recorrido Virtual 360° (Habitación 102)
- Panoramas equirectangulares 2:1 almacenados en `src/Services/SmartStay.Accommodations/wwwroot/tours/room-102/`.
- Manifiesto dinámico (`GET /api/v1/rooms/102/tour`) enlazando nodos interactivos (`entry` <-> `window`).
- Plantilla Vue 3 en [`client-templates/vue-room-tour/RoomTour.vue`](file:///media/okami/Universidad/ArquitecturaDeSoftwareEmergentes/backend/client-templates/vue-room-tour/RoomTour.vue) con Photo Sphere Viewer y captura de fallos de textura.

### Asistente Concierge Inteligente (`SmartStay.Concierge`)
- Encapsula la API de DeepSeek mediante modelo DTO estructurado JSON (`roomId` y `reasonCode`).
- **Garantía total de precio:** El servidor calcula y autoriza los importes (`StayTotal`); el modelo LLM tiene prohibido inventar tarifas o descuentos.
- **Protección contra inyección:** Mensajes de usuario tratados como datos inertes; se purgan identificadores alucinados.
- **Resiliencia:** Fallback determinista en < 12 segundos si la API externa no responde o si no hay clave configurada.
- **Rate Limiting:** Límite perimetral en Nginx y en middleware ASP.NET Core (10 req/min).

### Compartición P2P Google Nearby Connections (Android)
- Código Kotlin modularizado en [`client-templates/android-nearby/`](file:///media/okami/Universidad/ArquitecturaDeSoftwareEmergentes/backend/client-templates/android-nearby/).
- Estrategia 1:1 `Strategy.P2P_POINT_TO_POINT` sin necesidad de Internet.
- Carga útil `< 4 KB` libre de tokens, secretos o datos personales.
- Verificación bilateral con PIN de 4 dígitos (`authenticationDigits`).
- Confirmación de entrega basada en ACK de aplicación (`room-card-ack`) solo tras validar el esquema de dominio.
- Timeout automático de 30 segundos y reenvío transparente de ACK ante mensajes duplicados.

---

## 7. Usuarios Preconfigurados para Pruebas

| Usuario | Contraseña | Rol | Ámbito Asignado |
|---|---|---|---|
| `admin` | `Password123!` | `admin` | Hotel Bolívar (Lima, ID 1) |
| `admin_cusco` | `Password123!` | `admin` | Cusco Andean Lodge (Cusco, ID 2) |
| `chain_admin` | `Password123!` | `chain_admin` | Toda la cadena hotelera |
| `guest1` | `Password123!` | `guest` | Huésped regular |
| `guest2` | `Password123!` | `guest` | Huésped regular |
