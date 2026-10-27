# Recorrido de Prueba Focalizado de Línea Base (Fase 0)

Fecha: 26 de octubre de 2026  
Entorno de prueba: Local / Mock reproducible  
Objetivo: Validar los contratos de solicitud/respuesta del flujo crítico: autenticación, creación/consulta de habitación, reserva, pago aprobado, pago rechazado y aislamiento de hotel.

---

## 1. Paso 1 — Autenticación (`POST /api/v1/authentication/sign-in`)

### Solicitud
```http
POST /api/v1/authentication/sign-in HTTP/1.1
Host: localhost:8080
Content-Type: application/json

{
  "username": "chain_admin_user",
  "password": "SecurePassword123!"
}
```

### Respuesta Esperada (200 OK)
```json
{
  "id": 1,
  "username": "chain_admin_user",
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.sanitized.signature",
  "role": "ChainAdmin"
}
```

---

## 2. Paso 2 — Crear y Consultar Habitación (`Accommodations`)

### 2.1. Crear Habitación (`POST /api/v1/rooms`)
```http
POST /api/v1/rooms HTTP/1.1
Host: localhost:8080
Authorization: Bearer <TOKEN>
Content-Type: application/json

{
  "hotelId": 1,
  "roomTypeId": 1,
  "price": 120.00,
  "description": "Habitación ejecutiva con vista exterior",
  "amenities": ["Wifi", "TV", "AireAcondicionado"]
}
```

### Respuesta Esperada (201 Created)
```json
{
  "id": 105,
  "hotelId": 1,
  "roomTypeId": 1,
  "price": 120.00,
  "description": "Habitación ejecutiva con vista exterior",
  "amenities": ["Wifi", "TV", "AireAcondicionado"]
}
```

### 2.2. Consultar Habitación Creada (`GET /api/v1/rooms/105`)
```http
GET /api/v1/rooms/105 HTTP/1.1
Host: localhost:8080
Authorization: Bearer <TOKEN>
```

### Respuesta Esperada (200 OK)
```json
{
  "id": 105,
  "hotelId": 1,
  "roomTypeId": 1,
  "price": 120.00,
  "description": "Habitación ejecutiva con vista exterior",
  "amenities": ["Wifi", "TV", "AireAcondicionado"]
}
```

---

## 3. Paso 3 — Crear Reserva (`POST /api/v1/bookings`)

### Solicitud
```http
POST /api/v1/bookings HTTP/1.1
Host: localhost:8080
Authorization: Bearer <TOKEN>
Content-Type: application/json

{
  "roomId": 105,
  "guestName": "Carlos Mendoza",
  "guestEmail": "carlos.mendoza@example.com",
  "checkInDate": "2026-11-01T14:00:00Z",
  "checkOutDate": "2026-11-05T11:00:00Z"
}
```

### Respuesta Esperada (201 Created)
```json
{
  "id": 15,
  "roomId": 105,
  "guestName": "Carlos Mendoza",
  "guestEmail": "carlos.mendoza@example.com",
  "checkInDate": "2026-11-01T14:00:00Z",
  "checkOutDate": "2026-11-05T11:00:00Z",
  "status": 0
}
```
*(Nota: Estado 0 = `Pending`)*

---

## 4. Paso 4 — Pago Aprobado y Confirmación de Reserva (`POST /api/v1/payments`)

### Solicitud (Tarjeta Válida)
```http
POST /api/v1/payments HTTP/1.1
Host: localhost:8080
Authorization: Bearer <TOKEN>
Content-Type: application/json

{
  "bookingId": 15,
  "amount": 480.00,
  "paymentMethod": "CreditCard",
  "cardNumber": "4532015098761234",
  "cardHolderName": "Carlos Mendoza",
  "expirationDate": "12/28",
  "cvv": "987"
}
```

### Respuesta Esperada (201 Created)
```json
{
  "id": 8,
  "bookingId": 15,
  "transactionId": "d3b07384-d113-4011-85b3-3a137e163d42",
  "amount": 480.00,
  "paymentMethod": "CreditCard",
  "cardNumberMasked": "**** **** **** 1234",
  "status": 1,
  "paymentDate": "2026-10-26T20:00:00Z"
}
```
*(Nota: Estado 1 = `Completed`)*

### Verificación de la Reserva tras Pago (`GET /api/v1/bookings/15`)
```http
GET /api/v1/bookings/15 HTTP/1.1
Host: localhost:8080
Authorization: Bearer <TOKEN>
```

### Respuesta Esperada (200 OK)
```json
{
  "id": 15,
  "roomId": 105,
  "guestName": "Carlos Mendoza",
  "guestEmail": "carlos.mendoza@example.com",
  "checkInDate": "2026-11-01T14:00:00Z",
  "checkOutDate": "2026-11-05T11:00:00Z",
  "status": 1
}
```
*(Nota: En el monolito, el estado pasa a 1 = `Confirmed` de forma síncrona. En microservicios ocurrirá asíncronamente mediante el evento `PaymentCompleted` emitido por el Outbox).*

---

## 5. Paso 5 — Pago Rechazado (Tarjeta Terminada en "0000")

### Solicitud (Simulación de Tarjeta Declinada)
```http
POST /api/v1/payments HTTP/1.1
Host: localhost:8080
Authorization: Bearer <TOKEN>
Content-Type: application/json

{
  "bookingId": 16,
  "amount": 250.00,
  "paymentMethod": "CreditCard",
  "cardNumber": "4111222233330000",
  "cardHolderName": "Elena Torres",
  "expirationDate": "05/27",
  "cvv": "456"
}
```

### Respuesta Esperada (201 Created con Estado Fallido)
```json
{
  "id": 9,
  "bookingId": 16,
  "transactionId": "7f8b9a12-c2e4-4d89-91a5-8e123456789a",
  "amount": 250.00,
  "paymentMethod": "CreditCard",
  "cardNumberMasked": "**** **** **** 0000",
  "status": 2,
  "paymentDate": "2026-10-26T20:05:00Z"
}
```
*(Nota: Estado 2 = `Failed`. La reserva `16` permanece en estado 0 = `Pending` sin confirmar).*

---

## 6. Paso 6 — Acceso a Recursos de Hotel Ajeno (Control de Acceso Multitenant)

### Contexto
Usuario Administrador autenticado asignado a `hotel_id = 1` intenta modificar una habitación perteneciente a `hotel_id = 2`.

### Solicitud
```http
PUT /api/v1/rooms/201 HTTP/1.1
Host: localhost:8080
Authorization: Bearer <TOKEN_ADMIN_HOTEL_1>
Content-Type: application/json

{
  "hotelId": 2,
  "roomTypeId": 3,
  "price": 350.00,
  "description": "Intento de modificación no autorizada",
  "amenities": ["Jacuzzi"]
}
```

### Respuesta Esperada (403 Forbidden)
```json
{
  "statusCode": 403,
  "message": "Access denied. You do not have permission to modify resources for Hotel 2."
}
```
