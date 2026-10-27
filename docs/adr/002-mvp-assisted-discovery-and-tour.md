# ADR 002: Alcance del MVP — Descubrimiento Asistido, Recorrido Virtual 360° y P2P

**Fecha:** 27 de octubre de 2026  
**Estado:** Aceptado  
**Autores:** Equipo de Arquitectura SmartStay  

---

## 1. Contexto

La arquitectura original de SmartStay ha sido exitosamente descompuesta desde un monolito hacia microservicios autónomos. Para la demostración del MVP académico, se requiere priorizar un flujo de valor de alta diferenciación y confiabilidad en un plazo acotado de una semana, evitando la complejidad excesiva de un PMS comercial completo (housekeeping, facturación tributaria, integración con OTAs externas o cobro bancario real).

---

## 2. Decisiones Arquitectónicas

### 2.1. Alcance del Producto: Portal de Descubrimiento y Reserva Asistida
1. **Catálogo Propietario con Veracidad**: El backend es la única fuente de verdad sobre inventario, tarifas base y disponibilidad. Se fija como moneda única inicial **USD**.
2. **Tres Habitaciones de Fixture en 2 Zonas**:
   - `Room 101`: Grand Hotel Bolivar (Cercado de Lima), Single Standard ($85.00/noche).
   - `Room 102`: Grand Hotel Bolivar (Cercado de Lima), Double Deluxe ($150.00/noche, Tour 360 habilitado).
   - `Room 201`: Cusco Andean Lodge (San Blas, Cusco), Suite Presidencial ($320.00/noche).
3. **Reserva y Bloqueo de Noches**: La exclusión de carreras se gestiona mediante la tabla `room_night_locks` con clave primaria compuesta `(room_id, stay_date)`.
4. **Expiración de Reservas**: Las reservas `PendingPayment` tienen un TTL de 15 minutos; el servicio `BookingExpirationWorker` libera automáticamente los cerrojos expirados.

### 2.2. Recorrido Virtual 360° (Street-View Estilo Habitacional)
- **Tecnología**: Panoramas equirectangulares 2:1 visualizados mediante `@photo-sphere-viewer/core` y `@photo-sphere-viewer/virtual-tour-plugin`.
- **Topología**: 1 habitación insignia (Room 102) con 2 puntos de vista enlazados (`entry` y `window`).
- **Entrega de Assets**: Manifiesto normalizado mediante `GET /api/v1/rooms/{id}/tour` y assets estáticos servidos en `/tours/room-102/` desde Nginx / Accommodations.

### 2.3. Asistente Inteligente Concierge (DeepSeek)
- **Microservicio `SmartStay.Concierge`**: Servicio ligero sin base de datos propia. Consume el catálogo de `Accommodations` y la disponibilidad orientativa de `Bookings`.
- **Garantía Estricta de Precios**: Los precios, fechas y condiciones son calculados determinísticamente por el backend; el modelo de lenguaje solo redacta explicaciones concisas y nunca inventa descuentos, tarifas ni reservas.
- **Resiliencia y Privacidad**: La clave `DeepSeek:ApiKey` se mantiene en el backend. Si el proveedor presenta timeout (12 s) o fallos, el sistema conmuta a un fallback determinista sin bloquear al usuario.

### 2.4. Compartición P2P Android mediante Google Nearby Connections
- **Estrategia**: `P2P_POINT_TO_POINT` (1:1) sin requerir conexión a Internet para la transmisión.
- **Payload Ligero**: Ficha estructurada en JSON (< 4 KB) conteniendo identificadores y metadatos visuales. No incluye credenciales, tokens JWT ni datos de tarjetas.
- **Consentimiento**: Autenticación bilateral obligatoria mediante comparación visual de `authenticationDigits`.

---

## 3. Consecuencias

### Positivas
- Se elimina el acoplamiento y el riesgo de inventar disponibilidad o precios.
- El usuario experimenta una demo fluida con innovación (360° + IA + P2P).
- Cero claves expuestas en clientes frontend o APKs móviles.
- Fronteras estrictas de microservicios preservadas (0 referencias cruzadas).

### Negativas / Mitigaciones
- La prueba física de P2P requiere dos terminales Android reales con Google Play services. En ausencia del segundo dispositivo, se provee el suite de pruebas unitarias y mocks de transporte.
