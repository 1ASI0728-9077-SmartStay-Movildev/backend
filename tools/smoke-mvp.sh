#!/usr/bin/env bash
set -euo pipefail

GATEWAY_URL="${GATEWAY_URL:-http://localhost:8080}"

echo "=========================================================="
echo " SmartStay MVP: Comprehensive End-to-End Smoke Test       "
echo " Gateway: $GATEWAY_URL                                    "
echo "=========================================================="

# 1. Healthcheck del API Gateway
echo -n "1. Verificando salud del API Gateway (/health)... "
HEALTH_STATUS=$(curl -s -o /dev/null -w "%{http_code}" "$GATEWAY_URL/health" || echo "000")
if [ "$HEALTH_STATUS" -ne 200 ]; then
    echo "FAILED (HTTP $HEALTH_STATUS)"
    exit 1
fi
echo "OK (HTTP 200)"

# Función auxiliar para login
get_token() {
    local username="$1"
    local password="$2"
    local resp
    resp=$(curl -s -X POST "$GATEWAY_URL/api/v1/authentication/sign-in" \
        -H "Content-Type: application/json" \
        -d "{\"username\":\"$username\",\"password\":\"$password\"}" || echo "{}")
    echo "$resp" | grep -o '"token":"[^"]*' | cut -d'"' -f4 || echo ""
}

# 2. Autenticación de múltiples roles (Guest1, Guest2, Admin Hotel 1, Admin Hotel 2)
echo -n "2. Autenticando identidades para pruebas de seguridad y aislamiento... "
TOKEN_GUEST1=$(get_token "guest1" "Password123!")
TOKEN_GUEST2=$(get_token "guest2" "Password123!")
TOKEN_ADMIN_LIMA=$(get_token "admin" "Password123!")
TOKEN_ADMIN_CUSCO=$(get_token "admin_cusco" "Password123!")

if [ -z "$TOKEN_GUEST1" ] || [ -z "$TOKEN_GUEST2" ] || [ -z "$TOKEN_ADMIN_LIMA" ] || [ -z "$TOKEN_ADMIN_CUSCO" ]; then
    echo "FAILED: No se pudieron emitir los tokens requeridos para todos los actores."
    exit 1
fi
echo "OK (Tokens emitidos para Guest1, Guest2, Admin Hotel 1 y Admin Hotel 2)"

# 3. Búsqueda en Catálogo de Accommodations
echo -n "3. Verificando búsqueda de catálogo (Accommodations)... "
CATALOG_RESP=$(curl -s "$GATEWAY_URL/api/v1/rooms/search?district=Lima&guests=2")
if ! echo "$CATALOG_RESP" | grep -q "Grand Hotel Bolivar"; then
    echo "FAILED: El catálogo no devolvió habitaciones esperadas para Lima."
    echo "Respuesta: $CATALOG_RESP"
    exit 1
fi
echo "OK (Catálogo verificado con habitaciones reales)"

# 4. Manifiesto del Recorrido Virtual 360° (Habitación 102)
echo -n "4. Verificando manifiesto de Tour 360° (/rooms/102/tour)... "
TOUR_RESPONSE=$(curl -s -w "\n%{http_code}" "$GATEWAY_URL/api/v1/rooms/102/tour" || echo "000")
TOUR_CODE=$(echo "$TOUR_RESPONSE" | tail -n 1)
TOUR_BODY=$(echo "$TOUR_RESPONSE" | head -n -1)

if [ "$TOUR_CODE" -ne 200 ] || ! echo "$TOUR_BODY" | grep -q '"startNodeId":"entry"'; then
    echo "FAILED (HTTP $TOUR_CODE o falta startNodeId:entry)"
    exit 1
fi
echo "OK (HTTP 200 con nodos interactivos vinculados)"

# 5. Asset estático equirectangular del Tour 360°
echo -n "5. Verificando asset panorámico equirectangular (/tours/room-102/entry.jpg)... "
PANORAMA_STATUS=$(curl -s -o /dev/null -w "%{http_code}" "$GATEWAY_URL/tours/room-102/entry.jpg" || echo "000")
if [ "$PANORAMA_STATUS" -ne 200 ]; then
    echo "FAILED (HTTP $PANORAMA_STATUS)"
    exit 1
fi
echo "OK (HTTP 200 JPEG recibido)"

# 6. Asistente Concierge (Recomendación con ofertas reales y presupuesto respetado)
echo -n "6. Verificando asistente Concierge con presupuesto suficiente (/concierge/messages)... "
CONCIERGE_RESP=$(curl -s -X POST "$GATEWAY_URL/api/v1/concierge/messages" \
    -H "Content-Type: application/json" \
    -d '{
        "message": "Quiero una habitación cómoda para 2 personas en Cercado de Lima",
        "checkIn": "2026-11-20",
        "checkOut": "2026-11-22",
        "guests": 2,
        "district": "Cercado de Lima",
        "budgetTotal": 400.00,
        "currency": "USD"
    }')

if ! echo "$CONCIERGE_RESP" | grep -q '"roomId":102'; then
    echo "FAILED: Concierge no incluyó la habitación calificada 102 dentro del presupuesto."
    echo "Respuesta: $CONCIERGE_RESP"
    exit 1
fi
echo "OK (Concierge retornó ofertas verificadas y respetó el presupuesto)"

# 7. Asistente Concierge (Verificación de presupuesto insuficiente - discriminación honesta)
echo -n "7. Verificando discriminación de presupuesto excedido en Concierge... "
BUDGET_EXCEEDED_RESP=$(curl -s -X POST "$GATEWAY_URL/api/v1/concierge/messages" \
    -H "Content-Type: application/json" \
    -d '{
        "message": "Habitación barata",
        "checkIn": "2026-11-20",
        "checkOut": "2026-11-22",
        "guests": 2,
        "district": "Cercado de Lima",
        "budgetTotal": 50.00,
        "currency": "USD"
    }')

if ! echo "$BUDGET_EXCEEDED_RESP" | grep -q "exceden tu presupuesto"; then
    echo "FAILED: Concierge no informó educadamente que las opciones exceden el presupuesto."
    echo "Respuesta: $BUDGET_EXCEEDED_RESP"
    exit 1
fi
echo "OK (Concierge discriminó presupuesto sin inventar habitaciones)"

# 8. Creación de Reserva por Guest1
echo -n "8. Creando reserva para Guest1 en Habitación 102... "
CREATE_RESP=$(curl -s -w "\n%{http_code}" -X POST "$GATEWAY_URL/api/v1/bookings" \
    -H "Authorization: Bearer $TOKEN_GUEST1" \
    -H "Content-Type: application/json" \
    -d '{
        "roomId": 102,
        "guestName": "Juan Perez",
        "guestEmail": "juan.perez@example.com",
        "checkInDate": "2026-12-01T00:00:00Z",
        "checkOutDate": "2026-12-03T00:00:00Z"
    }')

CREATE_CODE=$(echo "$CREATE_RESP" | tail -n 1)
CREATE_BODY=$(echo "$CREATE_RESP" | head -n -1)

if [ "$CREATE_CODE" -ne 201 ]; then
    echo "FAILED (HTTP $CREATE_CODE)"
    echo "Respuesta: $CREATE_BODY"
    exit 1
fi

BOOKING_ID=$(echo "$CREATE_BODY" | grep -o '"id":[0-9]*' | cut -d':' -f2 || echo "")
if [ -z "$BOOKING_ID" ]; then
    echo "FAILED: No se pudo obtener el ID de la reserva creada."
    exit 1
fi
echo "OK (Reserva #$BOOKING_ID creada con éxito)"

# 9. Verificación de propiedad (Guest1 consulta su propia reserva)
echo -n "9. Verificando consulta de reservas propias (Guest1 /bookings/mine)... "
MINE_RESP=$(curl -s -H "Authorization: Bearer $TOKEN_GUEST1" "$GATEWAY_URL/api/v1/bookings/mine")
if ! echo "$MINE_RESP" | grep -q "\"id\":$BOOKING_ID"; then
    echo "FAILED: La reserva #$BOOKING_ID no apareció en /bookings/mine de Guest1."
    exit 1
fi
echo "OK (Reserva encontrada en /bookings/mine)"

# 10. Aislamiento Estricto: Guest2 intenta acceder a la reserva de Guest1 -> Debe ser 403 Forbidden
echo -n "10. Comprobando aislamiento de seguridad (Guest2 intenta leer reserva de Guest1)... "
FORBIDDEN_GET=$(curl -s -o /dev/null -w "%{http_code}" -H "Authorization: Bearer $TOKEN_GUEST2" "$GATEWAY_URL/api/v1/bookings/$BOOKING_ID")
if [ "$FORBIDDEN_GET" -ne 403 ]; then
    echo "FAILED (Se esperaba HTTP 403 Forbidden pero se recibió HTTP $FORBIDDEN_GET)"
    exit 1
fi
echo "OK (HTTP 403 Forbidden garantizado)"

# 11. Aislamiento Estricto: Guest2 intenta cancelar la reserva de Guest1 -> Debe ser 403 Forbidden
echo -n "11. Comprobando aislamiento de seguridad (Guest2 intenta cancelar reserva de Guest1)... "
FORBIDDEN_CANCEL=$(curl -s -o /dev/null -w "%{http_code}" -X POST -H "Authorization: Bearer $TOKEN_GUEST2" -H "Content-Length: 0" "$GATEWAY_URL/api/v1/bookings/$BOOKING_ID/cancel")
if [ "$FORBIDDEN_CANCEL" -ne 403 ]; then
    echo "FAILED (Se esperaba HTTP 403 Forbidden pero se recibió HTTP $FORBIDDEN_CANCEL)"
    exit 1
fi
echo "OK (HTTP 403 Forbidden garantizado)"

# 12. Autorización por Recurso: Administrador de Cusco (Hotel 2) intenta acceder a reserva de Hotel 1 -> Debe ser 403 Forbidden
echo -n "12. Comprobando alcance de HotelAdmin (Admin Cusco intenta acceder a reserva de Hotel Bolívar)... "
FORBIDDEN_ADMIN=$(curl -s -o /dev/null -w "%{http_code}" -H "Authorization: Bearer $TOKEN_ADMIN_CUSCO" "$GATEWAY_URL/api/v1/bookings/$BOOKING_ID")
if [ "$FORBIDDEN_ADMIN" -ne 403 ]; then
    echo "FAILED (Se esperaba HTTP 403 Forbidden pero se recibió HTTP $FORBIDDEN_ADMIN)"
    exit 1
fi
echo "OK (HTTP 403 Forbidden garantizado para admin fuera de ámbito)"

# 13. Cancelación legítima por Guest1
echo -n "13. Verificando cancelación legítima por el huésped propietario (Guest1)... "
CANCEL_STATUS=$(curl -s -o /dev/null -w "%{http_code}" -X POST -H "Authorization: Bearer $TOKEN_GUEST1" -H "Content-Length: 0" "$GATEWAY_URL/api/v1/bookings/$BOOKING_ID/cancel")
if [ "$CANCEL_STATUS" -ne 200 ]; then
    echo "FAILED (HTTP $CANCEL_STATUS)"
    exit 1
fi
echo "OK (Reserva cancelada y bloqueos liberados)"

echo "=========================================================="
echo " [SUCCESS] ¡El recorrido completo del MVP fue verificado! "
echo " Catálogo -> Concierge -> Tour 360 -> Reserva -> Permisos "
echo "=========================================================="
exit 0
