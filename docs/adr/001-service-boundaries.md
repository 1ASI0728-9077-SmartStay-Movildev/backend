# ADR 001: Definición de Límites de Microservicios y Alcance de Migración

- **Estado:** Aceptado
- **Fecha:** 2026-10-26
- **Autores:** Antigravity / RapiFast Engineering Team
- **Contexto:** Arquitectura de Software Emergentes

---

## Contexto y Declaración del Problema

El backend actual de SmartStay es un monolito modular implementado en ASP.NET Core (.NET 9) con una base de datos única MySQL (`backend-smartstay-db`). A pesar de contar con separación de carpetas por Bounded Contexts según principios DDD:
1. `AppDbContext` centraliza y registra entidades de todos los contextos (`User`, `Profile`, `Hotel`, `Room`, `Booking`, `Payment`).
2. `PaymentCommandService` modifica directamente el estado de `Booking` utilizando `IBookingRepository` y confirmando cambios mediante el `IUnitOfWork` compartido en la misma transacción física de base de datos.
3. `AnalyticsRepository` ejecuta lecturas directas cruzando las tablas `payments`, `bookings` y `rooms`.
4. El mecanismo de autenticación y autorización (`RequestAuthorizationMiddleware`) consulta el estado y la versión del token (`token_version`) directamente en la tabla `users` en cada petición HTTP protegida.
5. El empaquetamiento en Docker contiene dos réplicas acopladas a una única instancia MySQL no gestionada y a un broker ActiveMQ con configuración punto a punto sin garantías de fan-out durable.

Se requiere desacoplar el sistema en servicios independientes manteniendo funcionalidad continua en cada hito de extracción.

---

## Decisión de Arquitectura

Se acuerda una migración por capacidad de negocio siguiendo el patrón **Strangler Fig** con los siguientes límites y directrices:

1. **Topología de Servicios (Monorepositorio)**:
   - Se extraerán seis servicios desplegables autónomos:
     - `SmartStay.Accommodations` (Catálogo, inventario de hoteles y habitaciones).
     - `SmartStay.Bookings` (Gestión de reservas, autoridad única de disponibilidad temporal y bloqueos por noche).
     - `SmartStay.Payments` (Procesamiento y registro de pagos simulados, idempotencia de transacciones).
     - `SmartStay.IAM` (Identidad, emisión de tokens, roles y revocación).
     - `SmartStay.Profiles` (Información de contacto y perfil de usuarios).
     - `SmartStay.Analytics` (Métricas, agregaciones y proyecciones basadas en eventos de negocio).
   - Se mantiene un Gateway perimetral con **Nginx** que enrutará gradualmente las rutas del monolito hacia los microservicios correspondientes.

2. **Aislamiento de Persistencia (Database per Service)**:
   - Cada servicio dispondrá de su propia base de datos lógica (`smartstay_accommodations`, `smartstay_bookings`, etc.) y usuario MySQL con permisos restringidos exclusivamente a su esquema.
   - Quedan prohibidas las claves foráneas cruzadas entre bases de datos y los accesos de lectura/escritura directos entre servicios.

3. **Consistencia Transaccional y Comunicación Asíncrona**:
   - Se elimina la llamada transaccional sincrónica `Payment` → `Booking`.
   - Se adopta el patrón **Transactional Outbox** en Payments y **Inbox / Idempotent Consumer** en Bookings y Analytics sobre **ActiveMQ Classic (OpenWire)** con topics virtuales (`VirtualTopic`) para fan-out garantizado.
   - La confirmación de reservas tras el pago pasará a ser **eventualmente consistente**.

4. **Preservación de Autorización y Revocación**:
   - Mientras IAM permanezca en el monolito (Fases 1–6), los microservicios extraerán la identidad criptográfica vía JWT y consultarán un endpoint interno protegido para verificar `token_version` y estado activo, preservando la semántica de seguridad sin exponer credenciales a bypass.

5. **Disponibilidad y Concurrencia de Reservas**:
   - Bookings implementará una tabla dedicada `room_night_locks` con clave primaria compuesta `(room_id, stay_date)` para garantizar en el motor relacional que 20 peticiones concurrentes para la misma habitación y noche resulten en exactamente una reserva exitosa y el resto rechazadas con 409 Conflict.

---

## Alternativas Consideradas

1. **Migración Big Bang (Todo en un solo paso)**:
   - *Descartada*: Alto riesgo de regresiones incontroladas, imposibilidad de verificar el sistema en etapas intermedias y pérdida de trazabilidad sobre fallos de dominio.
2. **Event Sourcing Completo en todo el Backend**:
   - *Descartada*: Fuera del alcance y sobrediseño innecesario para un sistema de reservas hoteleras académicas; se limita el uso de eventos al Transactional Outbox para integración entre bounded contexts.
3. **Validación JWT sin comprobación de versión de token**:
   - *Descartada*: Provocaría pérdida del mecanismo de revocación inmediata implementado en el sistema original mediante `token_version`.

---

## Consecuencias

- **Positivas**:
  - Despliegue y escalado independiente de cada capacidad crítica (e.g. escalar réplicas de Bookings sin afectar Payments).
  - Eliminación de bloqueos de base de datos compartidos.
  - Modelo formal de eventos de integración trazables y reproducibles.
  - Reducción del blast radius ante caídas de servicios individuales.
- **Negativas / Desafíos**:
  - Requiere manejo de consistencia eventual: el cliente HTTP recibe respuesta de cobro aceptado y el estado de la reserva se confirma asíncronamente en milisegundos.
  - Requiere mecanismos de compensación (reembolsos simulados) ante carreras donde una reserva expire o se cancele mientras el pago estaba en tránsito.
  - Mayor sobrecarga de configuración de red y orquestación local (Docker Compose, Nginx, ActiveMQ).
