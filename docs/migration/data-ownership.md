# Propiedad de Datos, Esquemas y Relaciones (Fase 0)

Fecha: 26 de octubre de 2026  
Base de datos actual del monolito: `backend-smartstay-db` (MySQL 8.0)  
Convención de nombres: Snake_case (`ModelBuilderExtensions.UseSnakeCaseNamingConvention`)

---

## 1. Matriz de Propiedad de Entidades

| Tabla Actual | Servicio Propietario Futuro | Base Lógica Objetivo | Clave Primaria | Claves Foráneas / Relaciones Actuales |
|---|---|---|---|---|
| `hotels` | **Accommodations** | `smartstay_accommodations` | `id` (INT AUTO) | `host_id` conceptual hacia IAM (sin FK en BD) |
| `rooms` | **Accommodations** | `smartstay_accommodations` | `id` (INT AUTO) | `hotel_id` -> `hotels.id` (CASCADE)<br>`room_type_id` -> `room_types.id` (RESTRICT) |
| `room_types` | **Accommodations** | `smartstay_accommodations` | `id` (INT AUTO) | Ninguna (Catálogo local) |
| `hotel_categories` | **Accommodations** | `smartstay_accommodations` | `id` (INT) | Ninguna (Catálogo maestro) |
| `amenities_catalog` | **Accommodations** | `smartstay_accommodations` | `id` (INT) | Ninguna (Catálogo maestro) |
| `bookings` | **Bookings** | `smartstay_bookings` | `id` (INT AUTO) | `room_id` (INT sin FK en EF actual hacia `rooms`) |
| `payments` | **Payments** | `smartstay_payments` | `id` (INT AUTO) | `booking_id` (INT sin FK en EF actual hacia `bookings`) |
| `users` | **IAM** | `smartstay_iam` | `id` (INT AUTO) | `hotel_id`, `chain_id` como atributos tenant desacoplados |
| `profiles` | **Profiles** | `smartstay_profiles` | `id` (INT AUTO) | Value Objects integrados (`Name`, `Email`, `Address`) |
| *(Sin tablas)* | **Analytics** | `smartstay_analytics` | N/A | Consulta en caliente `payments`, `bookings` y `rooms` |

---

## 2. Detalle de Esquemas por Bounded Context

### 2.1. Accommodations (`smartstay_accommodations`)
- **`hotels`**:
  - `id` (INT PK AUTO_INCREMENT)
  - `host_id` (INT, no null)
  - `name` (VARCHAR(100), no null)
  - `address` (VARCHAR(200), no null)
  - `city` (VARCHAR(100), no null)
  - `country` (VARCHAR(100), no null)
  - `description` (VARCHAR(1000), null)
  - `image_url` (LONGTEXT, null)
  - `type` (VARCHAR(50), no null)
  - `amenities` (JSON, no null)
- **`rooms`**:
  - `id` (INT PK AUTO_INCREMENT)
  - `hotel_id` (INT FK -> `hotels.id` ON DELETE CASCADE)
  - `room_type_id` (INT FK -> `room_types.id` ON DELETE RESTRICT)
  - `price` (DECIMAL(18,2), no null)
  - `description` (VARCHAR(1000), no null)
  - `amenities` (JSON, no null)
- **`room_types`**:
  - `id` (INT PK AUTO_INCREMENT)
  - `name` (VARCHAR(50), no null)
  - `description` (VARCHAR(500), no null)
- **`hotel_categories`**:
  - `id` (INT PK)
  - `name` (VARCHAR(50), no null)
- **`amenities_catalog`**:
  - `id` (INT PK)
  - `name` (VARCHAR(50), no null)

### 2.2. Bookings (`smartstay_bookings`)
- **`bookings`**:
  - `id` (INT PK AUTO_INCREMENT)
  - `room_id` (INT, no null) — Referencia lógica a Accommodations
  - `guest_name` (VARCHAR(100), no null)
  - `guest_email` (VARCHAR(200), no null)
  - `check_in_date` (DATETIME, no null)
  - `check_out_date` (DATETIME, no null)
  - `status` (INT, no null: `0=Pending`, `1=Confirmed`, `2=Cancelled`)
- **Nueva tabla en Fase 4 (`room_night_locks`)**:
  - `room_id` (INT, no null)
  - `stay_date` (DATE, no null)
  - `booking_id` (INT, no null)
  - `PRIMARY KEY (room_id, stay_date)`
  - `INDEX ix_room_night_booking (booking_id)`
- **Nueva tabla Outbox / Inbox en Fase 5**:
  - `outbox_messages` y `inbox_messages` para sincronización eventual.

### 2.3. Payments (`smartstay_payments`)
- **`payments`**:
  - `id` (INT PK AUTO_INCREMENT)
  - `booking_id` (INT, no null) — Referencia lógica a Bookings
  - `transaction_id` (VARCHAR(64), no null)
  - `amount` (DECIMAL(18,2), no null)
  - `payment_method` (VARCHAR(50), no null)
  - `card_holder_name` (VARCHAR(100), no null)
  - `card_number_masked` (VARCHAR(20), no null)
  - `status` (INT, no null: `0=Pending`, `1=Completed`, `2=Failed`)
  - `payment_date` (DATETIME, no null)
- **Nueva tabla en Fase 5**:
  - `idempotency_keys` (`idempotency_key` PK, `actor_id`, `request_hash`, `response_payload`, `created_at`)
  - `outbox_messages`.

### 2.4. IAM (`smartstay_iam`)
- **`users`**:
  - `id` (INT PK AUTO_INCREMENT)
  - `username` (VARCHAR(100), no null, UNIQUE INDEX `i_x_users_username`)
  - `password_hash` (VARCHAR(255), no null)
  - `role` (VARCHAR(20), no null)
  - `status` (VARCHAR(20), no null, INDEX `i_x_users_status`)
  - `hotel_id` (INT, null)
  - `chain_id` (INT, null)
  - `token_version` (INT, default 0, no null)
  - `created_at` (DATETIME(6), default CURRENT_TIMESTAMP(6))
  - `updated_at` (DATETIME(6))

### 2.5. Profiles (`smartstay_profiles`)
- **`profiles`**:
  - `id` (INT PK AUTO_INCREMENT)
  - `first_name` (VARCHAR, no null)
  - `last_name` (VARCHAR, no null)
  - `email_address` (VARCHAR, no null)
  - `address_street` (VARCHAR, null)
  - `address_number` (VARCHAR, null)
  - `address_city` (VARCHAR, null)
  - `address_postal_code` (VARCHAR, null)
  - `address_country` (VARCHAR, null)
- **Extensión en Fase 6**: Incorporar `user_id` con índice único para alta idempotente desde IAM.

### 2.6. Analytics (`smartstay_analytics`)
- **Nuevas tablas en Fase 8 (Proyecciones de Hechos)**:
  - `payment_facts` (`payment_id` PK, `booking_id`, `hotel_id`, `amount`, `currency`, `occurred_at`)
  - `booking_facts` (`booking_id` PK, `room_id`, `hotel_id`, `nights_count`, `status`, `check_in`, `check_out`)
  - `room_inventory_facts` (`room_id` PK, `hotel_id`, `is_active`)
  - `inbox_messages` (`consumer_name`, `event_id` PK)

---

## 3. Reglas de Aislamiento y Migración
1. Ningún servicio podrá realizar `JOIN` o `SELECT` sobre bases de datos de otros servicios.
2. Cada microservicio contará con un usuario MySQL con permisos exclusivamente acotados a su propia base de datos (e.g. `GRANT ALL ON smartstay_accommodations.* TO 'accommodations_user'@'%'`).
3. La consistencia entre `Payment` y `Booking` se resolverá exclusivamente a través de mensajería asíncrona y transacciones locales desacopladas.
