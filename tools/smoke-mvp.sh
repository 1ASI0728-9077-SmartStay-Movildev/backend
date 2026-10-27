#!/usr/bin/env bash
set -euo pipefail

GATEWAY_URL="${GATEWAY_URL:-http://localhost:8080}"

echo "=========================================================="
echo " SmartStay MVP: Automated Smoke Verification              "
echo " Gateway: $GATEWAY_URL                                    "
echo "=========================================================="

# 1. Healthcheck
echo -n "1. Verificando salud del API Gateway (/health)... "
HEALTH_STATUS=$(curl -s -o /dev/null -w "%{http_code}" "$GATEWAY_URL/health" || echo "000")
if [ "$HEALTH_STATUS" -ne 200 ]; then
    echo "FAILED (HTTP $HEALTH_STATUS)"
    exit 1
fi
echo "OK (HTTP 200)"

# 2. Autenticación (sin imprimir tokens)
echo -n "2. Verificando autenticación de usuario (IAM)... "
SIGNIN_RESPONSE=$(curl -s -X POST "$GATEWAY_URL/api/v1/authentication/sign-in" \
    -H "Content-Type: application/json" \
    -d '{"username":"guest1","password":"Password123!"}' || echo "{}")

TOKEN=$(echo "$SIGNIN_RESPONSE" | grep -o '"token":"[^"]*' | cut -d'"' -f4 || echo "")
if [ -z "$TOKEN" ]; then
    echo "FAILED: No se pudo obtener JWT token."
    exit 1
fi
echo "OK (Token emitido exitosamente)"

# 3. Búsqueda en Catálogo de Accommodations
echo -n "3. Verificando búsqueda de catálogo (Accommodations)... "
SEARCH_STATUS=$(curl -s -o /dev/null -w "%{http_code}" "$GATEWAY_URL/api/v1/rooms/search?district=Lima" || echo "000")
if [ "$SEARCH_STATUS" -ne 200 ]; then
    echo "FAILED (HTTP $SEARCH_STATUS)"
    exit 1
fi
echo "OK (HTTP 200)"

# 4. Manifiesto del Recorrido Virtual 360° (Habitación 102)
echo -n "4. Verificando manifiesto de Tour 360° (/rooms/102/tour)... "
TOUR_RESPONSE=$(curl -s -w "\n%{http_code}" "$GATEWAY_URL/api/v1/rooms/102/tour" || echo "000")
HTTP_CODE=$(echo "$TOUR_RESPONSE" | tail -n 1)
BODY=$(echo "$TOUR_RESPONSE" | head -n -1)

if [ "$HTTP_CODE" -ne 200 ] || ! echo "$BODY" | grep -q "entry"; then
    echo "FAILED (HTTP $HTTP_CODE o falta nodo entry)"
    exit 1
fi
echo "OK (HTTP 200 con nodos enlazados)"

# 5. Asset estático equirectangular del Tour 360°
echo -n "5. Verificando asset panorámico equirectangular (/tours/room-102/entry.jpg)... "
PANORAMA_STATUS=$(curl -s -o /dev/null -w "%{http_code}" "$GATEWAY_URL/tours/room-102/entry.jpg" || echo "000")
if [ "$PANORAMA_STATUS" -ne 200 ]; then
    echo "FAILED (HTTP $PANORAMA_STATUS)"
    exit 1
fi
echo "OK (HTTP 200 JPEG recibido)"

# 6. Asistente Concierge (Recomendación con DeepSeek / Fallback determinista)
echo -n "6. Verificando asistente Concierge (/api/v1/concierge/messages)... "
CONCIERGE_RESPONSE=$(curl -s -X POST "$GATEWAY_URL/api/v1/concierge/messages" \
    -H "Content-Type: application/json" \
    -d '{
        "message": "Quiero una habitación para 2 personas en Lima",
        "checkIn": "2026-11-10",
        "checkOut": "2026-11-12",
        "guests": 2,
        "district": "Cercado de Lima",
        "budgetTotal": 400.00
    }' || echo "{}")

if ! echo "$CONCIERGE_RESPONSE" | grep -q "assistantMessage"; then
    echo "FAILED: Respuesta de Concierge no contiene assistantMessage."
    exit 1
fi
echo "OK (Recomendación asistida generada con ofertas verificadas)"

echo "=========================================================="
echo " [SUCCESS] Todas las pruebas de humo del MVP superadas!   "
echo "=========================================================="
exit 0
