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
