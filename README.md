# SmartStay — Plataforma de Hospedaje y Descubrimiento Asistido (Microservicios & MVP 7 Días)

SmartStay es una plataforma hotelera moderna construida sobre una arquitectura de **microservicios autónomos en .NET 9**, con persistencia en una **base MySQL compartida en filess.io para la demo académica**, mensajería asíncrona mediante **Apache ActiveMQ Artemis**, pasarela perimetral con **Nginx**, asistencia conversacional basada en **DeepSeek con garantías estrictas de precio de servidor**, recorridos virtuales **360° interactivos** y compartición P2P descentralizada mediante **Google Nearby Connections (Android)**.

---

## 1. Arquitectura del Sistema

```text
Clientes -> Nginx Gateway -> 7 microservicios .NET
                              |          |
                     MySQL filess.io   ActiveMQ
                     (base compartida)
```

Los servicios se ejecutan en Docker Compose local. Solo MySQL está alojado remotamente. Cada servicio usa sus propias tablas e historial de migraciones; Concierge no usa base de datos. Esta configuración comparte credenciales y almacenamiento como simplificación académica.

### Microservicios Desarrollados

| Microservicio | Responsabilidad | Base de Datos | Rutas Perimetrales |
|---|---|---|---|
| **SmartStay.IAM** | Autenticación de usuarios, emisión de JWT y validación interna. | Base compartida filess.io | `/api/v1/authentication`, `/api/v1/users` |
| **SmartStay.Accommodations** | Catálogo de hoteles, habitaciones y manifiestos de Recorrido 360°. | Base compartida filess.io | `/api/v1/rooms`, `/api/v1/hotels`, `/tours` |
| **SmartStay.Bookings** | Creación de reservas, exclusión física de noches (`room_night_locks`), expiración TTL y control de propiedad. | Base compartida filess.io | `/api/v1/bookings` |
| **SmartStay.Concierge** | Asistente inteligente con DeepSeek; valida presupuestos y precios autoritativos de servidor. | Stateless / Memoria | `/api/v1/concierge` |
| **SmartStay.Payments** | Pagos simulados con clave de idempotencia, outbox y compensación de reembolsos. | Base compartida filess.io | `/api/v1/payments` |
| **SmartStay.Profiles** | Gestión de perfiles de huéspedes y administradores. | Base compartida filess.io | `/api/v1/profiles` |
| **SmartStay.Analytics** | Proyecciones autónomas y métricas de negocio. | Base compartida filess.io | `/api/v1/analytics` |

---

## 2. Requisitos Previos

- Docker Desktop con contenedores Linux y Docker Compose v2.
- Acceso a una base MySQL de filess.io y conexión a Internet.
- Para scripts opcionales: Bash y curl; no necesitas instalar MySQL ni .NET en tu equipo.

## 3. Configuración y arranque

### Encender SmartStay para la demo desde Rider

El Compose actual ya conecta las seis APIs con persistencia a filess.io mediante `MYSQL_CONNECTION_STRING` en `.env`; Concierge no usa base de datos. No hace falta un Compose adicional. Si falta esa variable en otra instalación, complétala únicamente en el `.env` local con los datos del proveedor, sin compartir contraseñas ni subir el archivo a Git.

Sigue estos pasos cada día que quieras mostrar la demo. Las imágenes locales deben estar construidas y la base remota ya inicializada.

1. Abre **Docker Desktop**, usa contenedores Linux y espera a que el motor esté listo.

2. Abre el proyecto `backend` en Rider y entra en **View → Tool Windows → Terminal**. Selecciona **PowerShell** y comprueba que estás en la raíz de `backend`, donde está `compose.microservices.yml`.

3. Valida la configuración y enciende los siete microservicios, ActiveMQ y Nginx:

    ```powershell
    docker compose -f compose.microservices.yml config --quiet
    docker compose -f compose.microservices.yml up -d --no-deps --no-build --wait activemq iam accommodations bookings payments profiles analytics concierge
    docker compose -f compose.microservices.yml up -d --no-deps --no-build gateway
    docker compose -f compose.microservices.yml restart gateway
    docker compose -f compose.microservices.yml ps
    ```

    Enumerar los servicios con `--no-deps` omite `migrate`; `--no-build` reutiliza las imágenes existentes. Reiniciar el gateway renueva las direcciones internas de las APIs que Nginx mantiene en memoria. Estos comandos conservan el volumen de ActiveMQ y no ejecutan migraciones ni sembrado. No uses el arranque genérico de la sección de instalación para esta demo, porque incluye `migrate`.

4. Comprueba el backend local y la lectura del catálogo de filess.io:

    ```powershell
    Invoke-RestMethod 'http://localhost:8080/health'
    Invoke-RestMethod 'http://localhost:8080/api/v1/rooms/search?district=Lima&guests=2'
    ```

    La primera consulta debe devolver `Healthy`; la segunda debe mostrar habitaciones del catálogo remoto. Si falla alguna, revisa `docker compose -f compose.microservices.yml logs --tail 50 gateway iam accommodations` y agrega el servicio afectado si corresponde.

5. Abre el túnel HTTPS desde la terminal de Rider. Esta ruta corresponde a la instalación de Windows y funciona aunque `cloudflared` no esté en el PATH:

    ```powershell
    & 'C:\Program Files (x86)\cloudflared\cloudflared.exe' tunnel --url http://localhost:8080
    ```

    El túnel apunta únicamente al gateway. No abras túneles para MySQL, ActiveMQ ni las APIs individuales.

6. Copia la dirección `https://<nombre-aleatorio>.trycloudflare.com` que aparezca en la terminal. En otra terminal PowerShell, sustituye el ejemplo por la dirección real y comprueba:

    ```powershell
    $baseUrl = 'https://<nombre-aleatorio>.trycloudflare.com'
    Invoke-RestMethod "$baseUrl/health"
    ```

    Debe responder `Healthy`. También puedes abrir la dirección pública con `/health` en un navegador.

7. Usa esa dirección HTTPS **sin `/health`** como URL base del frontend en WebStorm. Conserva las rutas de la API, por ejemplo `$baseUrl/api/v1/rooms/search?district=Lima&guests=2`. La dirección cambia cada vez que creas un túnel nuevo: actualiza la URL base del frontend y no guardes el ejemplo como dirección permanente. No necesitas cambiar el código de los microservicios para cada URL temporal.

8. Durante la demo, mantén la PC encendida, conectada a Internet y sin suspender. Deja Docker Desktop activo y la terminal del túnel abierta. Quick Tunnels no requiere cuenta ni dominio de Cloudflare.

9. Para apagar la demo, pulsa **Ctrl+C** en la terminal del túnel y luego ejecuta en PowerShell:

    ```powershell
    docker compose -f compose.microservices.yml stop
    ```

    La URL pública dejará de funcionar. Los contenedores quedarán detenidos y se conservarán el volumen de ActiveMQ y los datos remotos de filess.io. Para volver a encender, repite los pasos anteriores.

### Instalar Cloudflare Tunnel una sola vez

En esta PC `cloudflared` ya está instalado. En otro equipo, si no existe `C:\Program Files (x86)\cloudflared\cloudflared.exe` ni se reconoce `Get-Command cloudflared`, instala:

```powershell
winget install --id Cloudflare.cloudflared --exact --source winget
```

Comprueba la instalación con `& 'C:\Program Files (x86)\cloudflared\cloudflared.exe' --version`. Si el instalador usa otra ubicación, utiliza la ruta real del ejecutable. Si prefieres usar `cloudflared` sin la ruta completa, abre otra terminal después de instalar y reinicia Rider si todavía no reconoce el PATH. No necesitas reinstalarlo cada día.

Referencias: [Quick Tunnels de Cloudflare](https://developers.cloudflare.com/tunnel/get-started/quick-tunnels/), [paquete cloudflared de WinGet](https://github.com/microsoft/winget-pkgs/tree/master/manifests/c/Cloudflare/cloudflared).

### Instalación inicial y cambios de esquema

Esta sección se usa al preparar una base nueva o aplicar cambios de esquema; no es el procedimiento del día de clase con la base ya configurada.

La configuración real está en `.env`, ignorado por Git y excluido de los contextos Docker. No publiques ese archivo ni la salida completa de `docker compose config`, que contiene secretos. Si ya tienes `.env`, no lo sobrescribas. En una nueva instalación, copia `.env.example` a `.env` y completa `MYSQL_CONNECTION_STRING` con host, puerto, usuario, contraseña y base proporcionados por filess.io. Conserva `SslMode=Required;Pooling=false;`.

Desde la raíz del repositorio, en PowerShell o Bash:

```sh
# Validar la configuración sin mostrar secretos
docker compose -f compose.microservices.yml config --quiet

# Construir el administrador de la base y comprobar conexión, TLS y tablas
docker compose -f compose.microservices.yml build migrate
docker compose -f compose.microservices.yml run --rm migrate check

# Crear/actualizar los esquemas secuencialmente
docker compose -f compose.microservices.yml run --rm migrate migrate

# Opcional: agregar usuarios de demostración sin borrar datos existentes
docker compose -f compose.microservices.yml run --rm migrate seed

# Iniciar servicios y gateway (las migraciones también se ejecutan antes de las APIs)
docker compose -f compose.microservices.yml up --build -d
# Al actualizar APIs, renovar las direcciones internas que Nginx mantiene en memoria
docker compose -f compose.microservices.yml restart gateway
docker compose -f compose.microservices.yml ps
docker compose -f compose.microservices.yml logs --tail 50 iam bookings
```

El gateway responde en `http://localhost:8080` (salud: `/health`). Para detenerlo: `docker compose -f compose.microservices.yml down`. No se elimina ninguna base remota. Los volúmenes MySQL antiguos no se usan ni se borran automáticamente.

El migrador rechaza bases con estructuras ajenas. Completa todas las migraciones de un servicio antes del siguiente porque las migraciones históricas crean temporalmente `inbox_messages` y luego la renombran para cada propietario. No ejecutes migraciones de varios servicios en paralelo. Las tres tablas finales son `bookings_inbox_messages`, `payments_inbox_messages` y `analytics_inbox_messages`; cada servicio tiene `__EFMigrationsHistory_<servicio>`.

El plan gratuito tiene límites de espacio y conexiones: usa pocos datos y una sola instancia por API. Desactivar pooling evita conexiones ociosas, pero no elimina el límite de concurrencia. La configuración deja de usar MySQL local; no publica automáticamente los contenedores en Internet. El alojamiento del backend, dominio y HTTPS se configuran en el siguiente paso.

## 4. Datos de demostración y pruebas End-to-End

Las migraciones incluyen el catálogo inicial. El comando `seed` agrega usuarios faltantes con contraseña `Password123!`, sin restablecer contraseñas ni eliminar reservas o pagos. Repetirlo conserva los registros existentes. Como alternativa desde Bash: `bash tools/seed-mvp.sh`. Para aplicar migraciones: `bash tools/migrate-all.sh`.

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
# backend
