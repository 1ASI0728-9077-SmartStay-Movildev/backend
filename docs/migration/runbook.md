# Manual de Operaciones y Despliegue de Microservicios SmartStay

Este runbook describe los procedimientos de arranque, migración de esquemas, verificación de salud, ejecución de pruebas y resolución de problemas para la arquitectura distribuida de microservicios SmartStay.

---

## 1. Requisitos Previos

- **Docker y Docker Compose**: Docker Engine v24+ con soporte para `compose v2`.
- **.NET SDK**: .NET 9.0 (para compilación y ejecución de pruebas locales).
- **Herramientas de Cliente**: `curl`, `jq`, `mysql-client` (opcional).

---

## 2. Estructura de Servicios

| Contenedor | Imagen / Contexto | Puerto Interno | Puerto Host | Descripción |
|---|---|---|---|---|
| `smartstay-mysql` | `mysql:8.0.36` | 3306 | 3306 | Servidor MySQL 8 con esquemas lógicos aislados |
| `smartstay-activemq` | `apache/activemq-classic:5.18.3` | 61616, 8161 | 61616, 8161 | Message Broker (OpenWire + Consola Web) |
| `smartstay-gateway` | `nginx:1.25-alpine` | 80 | 8080 | API Gateway perimetral con enrutamiento inverso |
| `smartstay-iam` | `src/Services/SmartStay.IAM` | 8080 | - | Identidad, autenticación, JWT y revocación |
| `smartstay-accommodations`| `src/Services/SmartStay.Accommodations` | 8080 | - | Hoteles, habitaciones y amenidades |
| `smartstay-bookings` | `src/Services/SmartStay.Bookings` | 8080 | - | Reservas con cerrojos de noches `room_night_locks` |
| `smartstay-payments` | `src/Services/SmartStay.Payments` | 8080 | - | Pagos con Outbox/Inbox e Idempotencia |
| `smartstay-profiles` | `src/Services/SmartStay.Profiles` | 8080 | - | Perfiles de usuario y direcciones |
| `smartstay-analytics` | `src/Services/SmartStay.Analytics` | 8080 | - | Proyecciones analíticas y métricas de rendimiento |

---

## 3. Procedimiento de Inicialización y Arranque

### 3.1. Arranque Limpio (Automático con Docker)
El directorio `deploy/mysql/init-scripts` incluye todos los scripts de inicialización de esquemas (`01-create-databases.sql` hasta `07-analytics.sql`). Al levantar el stack por primera vez, MySQL ejecuta estos scripts en orden alfanumérico.

```bash
# 1. Validar la configuración de Docker Compose
docker compose -f compose.microservices.yml config --quiet

# 2. Iniciar el stack completo de microservicios
docker compose -f compose.microservices.yml up -d --build

# 3. Comprobar estado de los contenedores
docker compose -f compose.microservices.yml ps
```

### 3.2. Ejecución Explícita de Migraciones (Jobs Independientes)
Si se desea aplicar migraciones de manera explícita en entornos de integración continua (CI/CD) sin depender de los scripts de inicialización del contenedor MySQL:

```bash
# Iniciar servicios de infraestructura
docker compose -f compose.microservices.yml up -d mysql activemq

# Esperar que MySQL pase su healthcheck
docker compose -f compose.microservices.yml exec mysql mysqladmin ping -h localhost -u root -prootpassword

# Ejecutar jobs de migración en orden
docker compose -f compose.microservices.yml run --rm iam-migrate
docker compose -f compose.microservices.yml run --rm accommodations-migrate
docker compose -f compose.microservices.yml run --rm bookings-migrate
docker compose -f compose.microservices.yml run --rm payments-migrate
docker compose -f compose.microservices.yml run --rm profiles-migrate
docker compose -f compose.microservices.yml run --rm analytics-migrate

# Alternativa mediante script Bash local:
./tools/migrate-all.sh
```

---

## 4. Verificación de Salud (Health Checks)

Cada microservicio expone un endpoint de liveness/readiness `/health` que responde HTTP `200 OK` con contenido `Healthy`.

A través del Gateway:
```bash
curl -i http://localhost:8080/health
```

Verificación directa por contenedor (usando Docker healthcheck):
```bash
docker compose -f compose.microservices.yml ps
```
Todos los contenedores deben reportar estado `(healthy)`.

---

## 5. Pruebas de Integración y Flujo E2E

### 5.1. Autenticación y Emisión de Token JWT
```bash
# Iniciar sesión con credenciales por defecto
TOKEN=$(curl -s -X POST http://localhost:8080/api/v1/authentication/sign-in \
  -H "Content-Type: application/json" \
  -d '{"username": "admin", "password": "Password123!"}' | jq -r .token)

echo "JWT: $TOKEN"
```

### 5.2. Crear Hotel y Habitación (Accommodations)
```bash
# Crear Hotel
HOTEL_ID=$(curl -s -X POST http://localhost:8080/api/v1/hotels \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{
    "name": "Grand Miraflores",
    "address": "Av. Larco 123",
    "city": "Lima",
    "country": "Peru",
    "categoryId": 1
  }' | jq -r .id)

# Crear Habitación
ROOM_ID=$(curl -s -X POST http://localhost:8080/api/v1/hotels/$HOTEL_ID/rooms \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{
    "roomNumber": "101",
    "roomTypeId": 1,
    "pricePerNight": 100.00,
    "capacity": 2
  }' | jq -r .id)
```

### 5.3. Crear Reserva con Cerrojo de Noches (Bookings)
```bash
BOOKING_ID=$(curl -s -X POST http://localhost:8080/api/v1/bookings \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d "{
    \"hotelId\": $HOTEL_ID,
    \"roomId\": $ROOM_ID,
    \"checkIn\": \"2026-11-01\",
    \"checkOut\": \"2026-11-05\",
    \"guestName\": \"Carlos Gomez\"
  }" | jq -r .id)
```

### 5.4. Pagar Reserva con Idempotencia y Outbox (Payments)
```bash
IDEMPOTENCY_KEY=$(uuidgen)

curl -s -X POST http://localhost:8080/api/v1/payments \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Idempotency-Key: $IDEMPOTENCY_KEY" \
  -d "{
    \"bookingId\": $BOOKING_ID,
    \"amount\": 400.00,
    \"currency\": \"USD\",
    \"paymentMethod\": \"CreditCard\"
  }"
```

El evento `PaymentCompletedV1` se publica en ActiveMQ, Bookings transiciona a `Confirmed`, y Analytics proyecta el ingreso en `RevenueFact`.

### 5.5. Consultar Métricas Proyectadas (Analytics)
```bash
curl -s -X GET "http://localhost:8080/api/v1/analytics/performance?year=2026&month=11" \
  -H "Authorization: Bearer $TOKEN"
```

---

## 6. Verificación de Aislamiento y Reglas de Dependencias

Para auditar que ningún microservicio tenga acoplamiento directo o referencias cruzadas de proyecto:

```bash
bash tools/check-boundaries.sh
```

Salida esperada:
```
==========================================================
 SmartStay Boundary Checker: Validating Service Isolation 
==========================================================
Inspeccionando dependencias de: SmartStay.IAM
Inspeccionando dependencias de: SmartStay.Accommodations
Inspeccionando dependencias de: SmartStay.Profiles
Inspeccionando dependencias de: SmartStay.Analytics
Inspeccionando dependencias de: SmartStay.Bookings
Inspeccionando dependencias de: SmartStay.Payments
 [SUCCESS] Todas las fronteras arquitectónicas respetan el aislamiento estricto (0 referencias cruzadas).
```

---

## 7. Plan de Contingencia y Rollback

Si se detecta un incidente crítico en producción:

1. **Reversión del Gateway**: Modificar `deploy/nginx/nginx.conf` para reactivar el enrutamiento hacia `legacy:8080`.
2. **Reactivación del Monolito**: Iniciar el perfil legado:
   ```bash
   docker compose -f compose.microservices.yml --profile legacy up -d legacy
   ```
3. **Consistencia de Datos**: Los datos creados en los esquemas aislados durante la operación del microservicio se mantienen en sus bases de datos correspondientes para conciliación posterior.
