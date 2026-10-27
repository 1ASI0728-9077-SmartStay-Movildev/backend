# Inventario de Endpoints y Matriz de Autorización (Fase 0)

Fecha: 26 de octubre de 2026  
Convención de ruteo global: Kebab-case (`KebabCaseRouteNamingConvention`)  
Esquema de autenticación: `Bearer` token validado por `RequestAuthorizationMiddleware`.

---

## 1. Módulo IAM (`/api/v1/...`)

| Endpoint | Método | Roles Permitidos | Request Body / Params | Respuestas | Notas / Tenant |
|---|---|---|---|---|---|
| `/api/v1/authentication/sign-in` | `POST` | `[AllowAnonymous]` | `SignInResource` (`Username`, `Password`) | `200 OK` (token, id, username, role), `400 BadRequest` | Genera JWT con `token_version` |
| `/api/v1/authentication/sign-up` | `POST` | `[AllowAnonymous]` | `SignUpResource` (`Username`, `Password`, `Role`) | `200 OK`, `400 BadRequest` | Registro inicial de usuarios |
| `/api/v1/users` | `POST` | `Admin`, `ChainAdmin` | `CreateUserResource` (`Username`, `Password`, `Role`, `HotelId`, `ChainId`) | `201 Created`, `400 BadRequest` | Crea usuario asignado a hotel/cadena |
| `/api/v1/users/{id}` | `GET` | `Admin`, `ChainAdmin` | Ruta: `id` (int) | `200 OK`, `404 NotFound` | Detalle de usuario |
| `/api/v1/users` | `GET` | `Admin`, `ChainAdmin` | - | `200 OK` (Lista de usuarios) | Inventario administrativo de usuarios |
| `/api/v1/users/change-password` | `POST` | Autenticado | `ChangePasswordResource` (`CurrentPassword`, `NewPassword`) | `200 OK`, `400 BadRequest` | Incrementa `token_version` para revocar sesiones |
| `/api/v1/users/{id}` | `PUT` | `Admin`, `ChainAdmin` | Ruta: `id`, Body: `UpdateUserResource` | `200 OK`, `404 NotFound` | Actualización de datos de usuario |
| `/api/v1/users/{id}/assign-role` | `POST` | `Admin`, `ChainAdmin` | Ruta: `id`, Body: `AssignRoleResource` | `200 OK`, `400 BadRequest` | Modificación de rol administrativo |
| `/api/v1/users/{id}` | `DELETE` | `Admin`, `ChainAdmin` | Ruta: `id` | `200 OK`, `404 NotFound` | Baja lógica / física de usuario |
| `/api/v1/users/{id}/activate` | `POST` | `Admin`, `ChainAdmin` | Ruta: `id` | `200 OK`, `400 BadRequest` | Reactiva usuario inactivo |

---

## 2. Módulo Accommodations (`/api/v1/...`)

| Endpoint | Método | Roles Permitidos | Request Body / Params | Respuestas | Notas / Tenant |
|---|---|---|---|---|---|
| `/api/v1/hotels` | `GET` | `Guest`, `Admin`, `ChainAdmin` | Query params opcionales | `200 OK` (Lista de hoteles) | Catálogo público/privado de hoteles |
| `/api/v1/hotels/{hotelId:int}` | `GET` | `Guest`, `Admin`, `ChainAdmin` | Ruta: `hotelId` | `200 OK`, `404 NotFound` | Detalle del hotel |
| `/api/v1/hotels` | `POST` | `Admin`, `ChainAdmin` | `CreateHotelResource` | `201 Created`, `400 BadRequest` | Alta de hotel (asigna `HostId`) |
| `/api/v1/hotels/{hotelId:int}` | `PUT` | `Admin`, `ChainAdmin` | Ruta: `hotelId`, Body: `UpdateHotelResource` | `200 OK`, `404 NotFound` | Modificación de hotel |
| `/api/v1/hotels/{hotelId:int}` | `DELETE` | `Admin`, `ChainAdmin` | Ruta: `hotelId` | `200 OK`, `404 NotFound` | Eliminación de hotel |
| `/api/v1/rooms` | `GET` | `Guest`, `Admin`, `ChainAdmin` | - | `200 OK` (Lista de habitaciones) | Catálogo de habitaciones |
| `/api/v1/rooms/{roomId:int}` | `GET` | `Guest`, `Admin`, `ChainAdmin` | Ruta: `roomId` | `200 OK`, `404 NotFound` | Detalle de habitación |
| `/api/v1/rooms/type/{roomTypeId:int}` | `GET` | `Guest`, `Admin`, `ChainAdmin` | Ruta: `roomTypeId` | `200 OK` | Filtro por tipo de habitación |
| `/api/v1/rooms` | `POST` | `Admin`, `ChainAdmin` | `CreateRoomResource` | `201 Created`, `400 BadRequest` | Alta de habitación asociada a Hotel |
| `/api/v1/rooms/{roomId:int}` | `PUT` | `Admin`, `ChainAdmin` | Ruta: `roomId`, Body: `UpdateRoomResource` | `200 OK`, `404 NotFound` | Edición de habitación |
| `/api/v1/rooms/{roomId:int}` | `DELETE` | `Admin`, `ChainAdmin` | Ruta: `roomId` | `200 OK`, `404 NotFound` | Eliminación de habitación |
| `/api/v1/room-types` | `GET` | `Guest`, `Admin`, `ChainAdmin` | - | `200 OK` | Catálogo de tipos de habitación |
| `/api/v1/room-types/{roomTypeId:int}` | `GET` | `Guest`, `Admin`, `ChainAdmin` | Ruta: `roomTypeId` | `200 OK`, `404 NotFound` | Detalle de tipo de habitación |
| `/api/v1/room-types` | `POST` | `Admin`, `ChainAdmin` | `CreateRoomTypeResource` | `201 Created`, `400 BadRequest` | Creación de tipo |
| `/api/v1/accommodations/options/categories` | `GET` | `Guest`, `Admin`, `ChainAdmin` | - | `200 OK` | Categorías maestras de alojamiento |
| `/api/v1/accommodations/options/categories` | `POST` | `ChainAdmin` | `CreateHotelCategoryResource` | `201 Created` | Alta de categoría maestra |
| `/api/v1/accommodations/options/amenities` | `GET` | `Guest`, `Admin`, `ChainAdmin` | - | `200 OK` | Amenities maestras |
| `/api/v1/accommodations/options/amenities` | `POST` | `ChainAdmin` | `CreateAmenityResource` | `201 Created` | Alta de amenity maestra |

---

## 3. Módulo Bookings (`/api/v1/bookings`)

| Endpoint | Método | Roles Permitidos | Request Body / Params | Respuestas | Notas / Tenant |
|---|---|---|---|---|---|
| `/api/v1/bookings` | `POST` | `Guest`, `Admin`, `ChainAdmin` | `CreateBookingResource` (`RoomId`, `GuestName`, `GuestEmail`, `CheckInDate`, `CheckOutDate`) | `201 Created`, `400 BadRequest` | Crea reserva en estado `Pending` |
| `/api/v1/bookings/{bookingId:int}` | `GET` | `Admin`, `ChainAdmin` | Ruta: `bookingId` | `200 OK`, `404 NotFound` | Detalle de reserva |
| `/api/v1/bookings` | `GET` | `Admin`, `ChainAdmin` | - | `200 OK` | Listado general |
| `/api/v1/bookings/room/{roomId:int}` | `GET` | `Admin`, `ChainAdmin` | Ruta: `roomId` | `200 OK` | Historial de reservas de la habitación |
| `/api/v1/bookings/{bookingId:int}/confirm` | `POST` | `Admin`, `ChainAdmin` | Ruta: `bookingId` | `200 OK`, `404 NotFound` | Confirmación manual administrativa |
| `/api/v1/bookings/{bookingId:int}/cancel` | `POST` | `Admin`, `ChainAdmin` | Ruta: `bookingId` | `200 OK`, `404 NotFound` | Cancelación de reserva |

---

## 4. Módulo Payments (`/api/v1/payments`)

| Endpoint | Método | Roles Permitidos | Request Body / Params | Respuestas | Notas / Tenant |
|---|---|---|---|---|---|
| `/api/v1/payments` | `POST` | `Guest`, `Admin`, `ChainAdmin` | `ProcessPaymentResource` (`BookingId`, `Amount`, `PaymentMethod`, `CardNumber`, `CardHolderName`, `ExpirationDate`, `Cvv`) | `201 Created`, `400 BadRequest` | Simula cobro y actualmente confirma Booking sincrónicamente |
| `/api/v1/payments/booking/{bookingId:int}` | `GET` | `Admin`, `ChainAdmin` | Ruta: `bookingId` | `200 OK`, `404 NotFound` | Auditoría de transacción vinculada a reserva |

---

## 5. Módulo Profiles (`/api/v1/profiles`)

| Endpoint | Método | Roles Permitidos | Request Body / Params | Respuestas | Notas / Tenant |
|---|---|---|---|---|---|
| `/api/v1/profiles/{profileId:int}` | `GET` | `Admin`, `ChainAdmin` | Ruta: `profileId` | `200 OK`, `404 NotFound` | Obtiene perfil |
| `/api/v1/profiles` | `POST` | `Guest`, `Admin`, `ChainAdmin` | `CreateProfileResource` (`FirstName`, `LastName`, `Email`, `Street`, `Number`, `City`, `PostalCode`, `Country`) | `201 Created` | Creación de perfil |
| `/api/v1/profiles` | `GET` | `Admin`, `ChainAdmin` | - | `200 OK` | Listado general de perfiles |

---

## 6. Módulo Analytics (`/api/v1/analytics`)

| Endpoint | Método | Roles Permitidos | Request Body / Params | Respuestas | Notas / Tenant |
|---|---|---|---|---|---|
| `/api/v1/analytics/monthly` | `GET` | `Admin`, `ChainAdmin` | - | `200 OK` (`PerformanceMetrics`) | Métricas agregadas (ingresos, reservas, cancelaciones, ocupación) |

---

## 7. Módulo IoT Emulator (`/api/v1/io-t-emulator`)

| Endpoint | Método | Roles Permitidos | Request Body / Params | Respuestas | Notas / Tenant |
|---|---|---|---|---|---|
| `/api/v1/io-t-emulator/rooms/{roomId:int}/inject-telemetry` | `POST` | Ninguno (abierto) | `TelemetryInjectionRequest` | `200 OK`, `400 BadRequest` | Inyección de estado térmico/presencia |
| `/api/v1/io-t-emulator/rooms/{roomId:int}/thermostat` | `POST` | Ninguno (abierto) | `ThermostatControlRequest` | `200 OK`, `400 BadRequest` | Control de termostato |
| `/api/v1/io-t-emulator/rooms/{roomId:int}/actuators-state` | `GET` | Ninguno (abierto) | Ruta: `roomId` | `200 OK`, `404 NotFound` | Estado actual de actuadores en memoria |
