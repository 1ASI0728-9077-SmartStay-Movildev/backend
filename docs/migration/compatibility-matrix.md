# Matriz de Compatibilidad y Cambios de Comportamiento (Fase 0)

Fecha: 26 de octubre de 2026  
Objetivo: Registrar las diferencias entre el comportamiento del monolito legado y la arquitectura distribuida por fases.

---

## 1. Matriz General de Capacidades

| Capacidad | Comportamiento Legado (Monolito) | Comportamiento Nuevo (Microservicios) | Impacto / Compatibilidad |
|---|---|---|---|
| **Catálogo de Alojamientos** | Endpoints en monolito (`/api/v1/hotels`, `/api/v1/rooms`, `/api/v1/room-types`). | Desplegado en `SmartStay.Accommodations` detrás del Gateway Nginx. | **100% Compatible**: Contratos, rutas, DTOs y respuestas idénticas. |
| **Disponibilidad de Habitaciones** | Creación de reserva sin bloqueo atómico de noches; susceptible a reservas dobles concurrentes. | Validado exclusivamente en `SmartStay.Bookings` con tabla `room_night_locks` `(room_id, stay_date)` en SQL. | **Mejora de Concurrencia**: Respuestas 409 Conflict explícitas ante colisiones de fecha en lugar de corrupción de datos. |
| **Flujo de Pago y Confirmación** | `POST /api/v1/payments` ejecuta pago y en la misma transacción SQL muta `Booking.Confirm()` mediante `IBookingRepository` y `IUnitOfWork`. | `POST /api/v1/payments` registra pago en `SmartStay.Payments` y emite evento `PaymentCompleted` vía Outbox. `SmartStay.Bookings` consume el evento y confirma la reserva de forma asíncrona. | **Cambio de Sincronía**: La respuesta HTTP del pago confirma el procesamiento del pago; el estado de la reserva pasa de `Pending` a `Confirmed` con consistencia eventual (< 2 s). |
| **Idempotencia de Pagos** | No existe validación de idempotencia; solicitudes repetidas procesan cobros múltiples. | Cabecera `Idempotency-Key` requerida o deducida; tabla `idempotency_keys` para rechazo de pagos duplicados. | **Protección Financiera**: Peticiones con misma clave retornan resultado previo sin reejecutar cobro. |
| **Métricas de Analytics** | `AnalyticsRepository` consulta directamente tablas `payments`, `bookings` y `rooms` en caliente en la misma BD. | En transición (Fase 1-7): Adaptador HTTP interno. En Fase 8: Proyecciones locales en `smartstay_analytics` alimentadas por eventos de ActiveMQ. | **Aislamiento**: Se corrige el intervalo de fin de mes y el cálculo de tasa de ocupación basado en noches-habitación. |
| **Autenticación y Revocación** | Middleware consulta `User` y `token_version` en base de datos en cada petición HTTP. | Microservicios validan firma JWT y consultan endpoint interno de IAM (`POST /internal/v1/identity/validate`) para comprobar revocación. | **Seguridad Equivalente**: Se mantiene la revocación inmediata de sesiones sin acoplamiento a la base de datos de IAM. |
| **Emulador IoT** | `IoTEmulatorController` mantiene estado en memoria estática no distribuida (`IoTEmulatorStore`). | Mantenido en el monolito durante las fases intermedias; instancia única para pruebas de telemetría y actuadores. | **Sin cambios**: Aislado del flujo crítico de reservas y pagos. |

---

## 2. Detalle del Cambio de Sincronía en el Flujo de Pagos

### Flujo Antiguo (Monolito)
```
Cliente HTTP
    │
    ▼ POST /api/v1/payments
PaymentsController
    │
    ▼
PaymentCommandService
    │── 1. Crea Payment
    │── 2. Simula validación bancaria
    │── 3. Llama a bookingRepository.FindByIdAsync(bookingId)
    │── 4. booking.Confirm()
    │── 5. bookingRepository.Update(booking)
    │── 6. unitOfWork.CompleteAsync() (Commit atómico en BD compartida)
    ▼
Retorna 201 Created con Payment y Booking confirmado en el mismo instante.
```

### Flujo Nuevo (Microservicios con Outbox)
```
Cliente HTTP
    │
    ▼ POST /api/v1/payments (con Idempotency-Key)
SmartStay.Payments API
    │── 1. Verifica elegibilidad con SmartStay.Bookings (HTTP interno GET)
    │── 2. Simula validación de pago
    │── 3. Transacción SQL Local:
    │       - Inserta Payment (Status: Completed)
    │       - Inserta OutboxMessage (Evento: PaymentCompletedV1)
    │       - Commit SQL local
    │── 4. Retorna 201 Created (Pago procesado exitosamente)
    ▼
Outbox Publisher Worker (Background)
    │── Reclama mensajes pendientes
    │── Publica a ActiveMQ: VirtualTopic.SmartStay.Payments.V1
    ▼
Broker ActiveMQ
    │── Encola a Consumer.Bookings.VirtualTopic.SmartStay.Payments.V1
    │── Encola a Consumer.Analytics.VirtualTopic.SmartStay.Payments.V1
    ▼
SmartStay.Bookings Consumer (Background)
    │── Recibe mensaje
    │── Transacción SQL Local:
    │       - Inserta InboxMessage (consumer, event_id) [Clave única]
    │       - Muta Booking: PendingPayment ──> Confirmed
    │       - Emite BookingConfirmedV1 a Outbox
    │       - Commit SQL local
    │── ACK al broker
```

### Gestión de Fallos y Compensación
- Si la reserva expiró o fue cancelada antes de que el evento `PaymentCompleted` sea procesado por Bookings:
  1. Bookings no resucita la reserva.
  2. Bookings emite evento `PaymentRefundRequested`.
  3. Payments consume el evento, marca el reembolso simulado y emite `PaymentRefunded`.
- Si el broker ActiveMQ se encuentra temporalmente no disponible:
  1. El pago se registra exitosamente en MySQL de Payments y queda encolado en su tabla `outbox_messages`.
  2. Una vez restablecido el broker, el worker reanuda la publicación sin pérdida de datos.

---

## 3. Inventario Final de Endpoints por Microservicio

Todos los endpoints se exponen a través del API Gateway Nginx (`:8080`). Las rutas `/internal/` están bloqueadas hacia el exterior:

| Microservicio | Endpoint Público | Método | Propósito |
|---|---|---|---|
| **SmartStay.IAM** | `/api/v1/authentication/sign-in` | `POST` | Autenticación y emisión de JWT token |
| | `/api/v1/authentication/sign-up` | `POST` | Registro de nuevo usuario |
| | `/api/v1/users` | `GET` | Listado de usuarios con filtro por rol |
| | `/api/v1/users/{id}` | `GET` | Detalle de usuario |
| | `/internal/v1/identity/validate` | `POST` | *(Interno)* Validación de token, revocación y scopes |
| **SmartStay.Accommodations** | `/api/v1/hotels` | `GET`, `POST` | Catálogo de hoteles y registro |
| | `/api/v1/hotels/{id}` | `GET`, `PUT`, `DELETE` | Detalle, actualización y borrado |
| | `/api/v1/hotels/{hotelId}/rooms` | `GET`, `POST` | Habitaciones por hotel |
| | `/api/v1/rooms` | `GET`, `POST` | Consulta general de habitaciones |
| | `/api/v1/rooms/{id}` | `GET` | Detalle de habitación |
| | `/api/v1/room-types` | `GET` | Catálogo de tipos de habitación |
| | `/internal/v1/rooms/{id}/snapshot` | `GET` | *(Interno)* Snapshot de habitación para Bookings |
| **SmartStay.Bookings** | `/api/v1/bookings` | `GET`, `POST` | Consulta de reservas y creación con cerrojos de noches |
| | `/api/v1/bookings/{id}` | `GET` | Detalle de reserva |
| | `/api/v1/bookings/{id}/confirm` | `POST` | Confirmación manual o administrativa |
| | `/api/v1/bookings/{id}/cancel` | `POST` | Cancelación de reserva y liberación de cerrojos |
| | `/internal/v1/bookings/{id}/summary` | `GET` | *(Interno)* Resumen de elegibilidad para Payments |
| **SmartStay.Payments** | `/api/v1/payments` | `POST` | Procesamiento idempotente de pago y encolado en Outbox |
| | `/api/v1/payments/{id}` | `GET` | Consulta de transacción de pago |
| **SmartStay.Profiles** | `/api/v1/profiles` | `GET`, `POST` | Consulta y alta/upsert idempotente de perfiles |
| | `/api/v1/profiles/{id}` | `GET`, `PUT` | Detalle y actualización de perfil de usuario |
| **SmartStay.Analytics** | `/api/v1/analytics/performance` | `GET` | Métricas operativas mensuales calculadas sobre hechos locales |

---

## 4. Topología de Mensajería Asíncrona (ActiveMQ Classic)

- **Broker**: `apache/activemq-classic:5.18.3` (protocolo OpenWire en `:61616`).
- **Patrón de Publicación**: Virtual Topics (`VirtualTopic.<NombreDominio>.<Version>`).
- **Garantía**: Al menos una vez (At-least-once delivery) mediante Transactional Outbox en emisor y Transactional Inbox con deduplicación por `(consumer, event_id)` en receptor.

| Evento | Virtual Topic | Consumidores / Colas Virtuales | Propósito |
|---|---|---|---|
| `PaymentCompletedV1` | `VirtualTopic.SmartStay.Payments.V1` | `Consumer.Bookings.VirtualTopic.SmartStay.Payments.V1` | Bookings: confirma reserva y libera o consolida cerrojos. |
| | | `Consumer.Analytics.VirtualTopic.SmartStay.Payments.V1` | Analytics: proyecta `RevenueFact` para cálculo de ingresos netos. |
| `BookingConfirmedV1` | `VirtualTopic.SmartStay.Bookings.V1` | `Consumer.Analytics.VirtualTopic.SmartStay.Bookings.V1` | Analytics: proyecta `BookingFact` para cálculo de ocupación. |
| `PaymentRefundRequestedV1` | `VirtualTopic.SmartStay.Bookings.V1` | `Consumer.Payments.VirtualTopic.SmartStay.Bookings.V1` | Payments: ejecuta compensación (reembolso simulado). |
| `PaymentRefundedV1` | `VirtualTopic.SmartStay.Payments.V1` | `Consumer.Analytics.VirtualTopic.SmartStay.Payments.V1` | Analytics: ajusta monto reembolsado en `RevenueFact`. |

---

## 5. Aislamiento de Almacenamiento (Database-per-Service)

| Microservicio | Esquema Lógico | Usuario Dedicado | Migraciones EF Core |
|---|---|---|---|
| SmartStay.IAM | `smartstay_iam` | `iam_user` | `artifacts/migrations/iam.sql` |
| SmartStay.Accommodations | `smartstay_accommodations` | `accommodations_user` | `artifacts/migrations/accommodations.sql` |
| SmartStay.Bookings | `smartstay_bookings` | `bookings_user` | `artifacts/migrations/bookings.sql` |
| SmartStay.Payments | `smartstay_payments` | `payments_user` | `artifacts/migrations/payments.sql` |
| SmartStay.Profiles | `smartstay_profiles` | `profiles_user` | `artifacts/migrations/profiles.sql` |
| SmartStay.Analytics | `smartstay_analytics` | `analytics_user` | `artifacts/migrations/analytics.sql` |

---

## 6. Estado de Desmantelamiento del Monolito Legado

- **Tráfico en Producción**: 0% dirigido al monolito.
- **API Gateway**: Nginx desvía el 100% de las rutas hacia los 6 microservices autónomos.
- **Base de Datos Monolítica (`backend-smartstay-db`)**: Cerrada para nuevas escrituras de producción; reemplazada por los 6 esquemas dedicados.
- **Contenedor Monolito**: Desactivado del perfil por defecto en `compose.microservices.yml` (movido al profile de emergencia `legacy`).

