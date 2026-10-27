# SmartStay — Plantilla P2P Google Nearby Connections (Android)

Este módulo proporciona la integración oficial para compartir fichas de habitaciones punto a punto (1:1) sin conexión a Internet entre dos dispositivos Android que tengan instalada la aplicación móvil de **SmartStay**.

---

## 1. Arquitectura y Principios de Diseño

1. **Estrategia P2P**: Emplea `Strategy.P2P_POINT_TO_POINT` (alta tasa de transferencia, topología 1 a 1 de proximidad Bluetooth/Wi-Fi Direct).
2. **Carga Útil Segura (< 4 KB)**:
   - Solo transfiere metadatos inmutables de catálogo (`roomId`, `hotelId`, `hotelName`, `roomTypeName`, `district`, `nightlyPrice`, `currency`, `hasTour`).
   - **Prohibido**: No contiene credenciales de usuario, JWT, tokens de sesión, datos de tarjetas de crédito ni reservas vivas.
   - Si el receptor está offline, visualiza la ficha en modo lectura («datos pendientes de sincronización»). Al reconectar a Internet, la app revalida precios y disponibilidad con el backend `SmartStay.Accommodations` y `SmartStay.Bookings`.
3. **Autenticación Bilateral**:
   - Uso de `authenticationDigits` en `ConnectionLifecycleCallback.onConnectionInitiated`.
   - Ambos usuarios deben comparar y aprobar los dígitos antes de invocar `acceptConnection()`.
4. **ACK de Aplicación**:
   - La entrega solo se considera exitosa (`Delivered`) cuando el receptor procesa y devuelve un payload `room-card-ack` con el `messageId` correspondiente.
5. **Aislamiento de Dominio**:
   - `ShareRoomCard.kt` y `ShareRoomUseCase.kt` no dependen de bibliotecas de Google.
   - Las dependencias de Google Play Services se confinan a `NearbyRoomTransport.kt`.

---

## 2. Dependencias de Gradle

En el archivo `build.gradle.kts` del módulo Android (usualmente `:app`):

```kotlin
dependencies {
    // Google Nearby Connections API
    implementation("com.google.android.gms:play-services-nearby:19.5.1")

    // Coroutines / Lifecycle (si aplica en la app)
    implementation("androidx.lifecycle:lifecycle-viewmodel-ktx:2.8.6")
}
```

---

## 3. Permisos en `AndroidManifest.xml`

Nearby Connections requiere permisos de Bluetooth, Wi-Fi y ubicación según la versión de Android:

```xml
<manifest xmlns:android="http://schemas.android.com/apk/res/android"
    package="pe.smartstay">

    <!-- Permisos para Android 12 (API 31) en adelante -->
    <uses-permission
        android:name="android.permission.BLUETOOTH_SCAN"
        android:usesPermissionFlags="neverForLocation" />
    <uses-permission android:name="android.permission.BLUETOOTH_ADVERTISE" />
    <uses-permission android:name="android.permission.BLUETOOTH_CONNECT" />

    <!-- Permisos para Android 13 (API 33) en adelante -->
    <uses-permission
        android:name="android.permission.NEARBY_WIFI_DEVICES"
        android:usesPermissionFlags="neverForLocation" />

    <!-- Permisos para versiones anteriores a Android 12 -->
    <uses-permission
        android:name="android.permission.BLUETOOTH"
        android:maxSdkVersion="30" />
    <uses-permission
        android:name="android.permission.BLUETOOTH_ADMIN"
        android:maxSdkVersion="30" />
    <uses-permission
        android:name="android.permission.ACCESS_FINE_LOCATION"
        android:maxSdkVersion="32" />

    <!-- Estado de red y Wi-Fi -->
    <uses-permission android:name="android.permission.ACCESS_WIFI_STATE" />
    <uses-permission android:name="android.permission.CHANGE_WIFI_STATE" />

    <application ...>
        ...
    </application>
</manifest>
```

> [!NOTE]
> En tiempo de ejecución (runtime), solicitar al usuario la concesión de permisos usando la `ActivityResultContracts.RequestMultiplePermissions` API antes de invocar `startDiscovery()` o `startAdvertising()`. Asimismo, comprobar que Bluetooth y Wi-Fi se encuentren encendidos físicamente.

---

## 4. Estructura de Clases

| Componente | Archivo | Responsabilidad |
|---|---|---|
| **Domain** | [`ShareRoomCard.kt`](file:///media/okami/Universidad/ArquitecturaDeSoftwareEmergentes/backend/client-templates/android-nearby/ShareRoomCard.kt) | Ficha inmutable <4KB y estructura de confirmación (`ShareRoomAck`). |
| **Application** | [`ShareRoomUseCase.kt`](file:///media/okami/Universidad/ArquitecturaDeSoftwareEmergentes/backend/client-templates/android-nearby/ShareRoomUseCase.kt) | Validación de contrato, deduplicación y emparejamiento de ACK. |
| **Data** | [`NearbyRoomTransport.kt`](file:///media/okami/Universidad/ArquitecturaDeSoftwareEmergentes/backend/client-templates/android-nearby/NearbyRoomTransport.kt) | Adaptador con `Nearby.getConnectionsClient(context)`, `P2P_POINT_TO_POINT`, `SERVICE_ID`. |
| **Presentation** | [`NearbyShareViewModel.kt`](file:///media/okami/Universidad/ArquitecturaDeSoftwareEmergentes/backend/client-templates/android-nearby/NearbyShareViewModel.kt) | Máquina de estados UI (`Idle` → `Discovering`/`Advertising` → `AwaitingVerification` → `Sending` → `Delivered`). |

---

## 5. Protocolo de Prueba Física (2 Dispositivos Reales)

1. **Dispositivo A (Emisor)**:
   - Abre la habitación #102.
   - Presiona «Compartir con viajero cercano».
   - Invoca `viewModel.startSharing(roomCard)`. La pantalla pasa a `Discovering`.
2. **Dispositivo B (Receptor)**:
   - Abre la app SmartStay y selecciona «Recibir habitación cercana».
   - Invoca `viewModel.startReceiving()`. La pantalla pasa a `Advertising`.
3. **Emparejamiento y Verificación**:
   - El Dispositivo A detecta el Dispositivo B en la lista y presiona «Conectar».
   - Ambos teléfonos muestran el cuadro de diálogo con los mismos dígitos (`authenticationDigits`).
   - Ambos usuarios presionan «Aceptar / Confirmar dígitos».
4. **Intercambio y ACK**:
   - El Dispositivo A serializa la ficha en JSON y la transmite vía `Payload.fromBytes()`.
   - El Dispositivo B recibe los bytes, valida el esquema con `ShareRoomUseCase` y emite de vuelta un payload `room-card-ack`.
   - El Dispositivo A recibe el ACK y muestra «¡Habitación compartida con éxito!». Ambos apagan los radios de escaneo.
