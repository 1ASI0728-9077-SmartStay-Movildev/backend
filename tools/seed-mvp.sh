#!/usr/bin/env bash
set -euo pipefail

GATEWAY_URL="${GATEWAY_URL:-http://localhost:8080}"
MYSQL_HOST="${MYSQL_HOST:-localhost}"
MYSQL_PORT="${MYSQL_PORT:-3306}"
MYSQL_ROOT_PASSWORD="${MYSQL_ROOT_PASSWORD:-rootpassword}"

echo "=========================================================="
echo " SmartStay MVP: Idempotent Demo Seeder                     "
echo " Gateway: $GATEWAY_URL                                    "
echo "=========================================================="

SQL_PAYLOAD=$(cat << 'EOF'
USE `smartstay_iam`;

-- Hash bcrypt para 'Password123!'
SET @pwd_hash = '$2a$11$qB/7uM9QG5E0oPcfFp6oZONp59Y6K51817vCq2k8tZ0eQ0E6V6aO6';

INSERT INTO `users` (`Id`, `Username`, `PasswordHash`, `Role`, `Status`, `hotel_id`, `chain_id`, `token_version`, `created_at`, `updated_at`)
VALUES
  (1, 'admin', @pwd_hash, 'admin', 'Active', 1, 1, 1, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
  (2, 'guest1', @pwd_hash, 'guest', 'Active', NULL, NULL, 1, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
  (3, 'guest2', @pwd_hash, 'guest', 'Active', NULL, NULL, 1, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
  (4, 'admin_cusco', @pwd_hash, 'admin', 'Active', 2, 1, 1, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
  (5, 'chain_admin', @pwd_hash, 'chain_admin', 'Active', NULL, 1, 1, UTC_TIMESTAMP(), UTC_TIMESTAMP())
ON DUPLICATE KEY UPDATE
  `PasswordHash` = VALUES(`PasswordHash`),
  `Role` = VALUES(`Role`),
  `Status` = VALUES(`Status`),
  `hotel_id` = VALUES(`hotel_id`),
  `chain_id` = VALUES(`chain_id`),
  `token_version` = 1,
  `updated_at` = UTC_TIMESTAMP();
EOF
)

# 1. Intentar sembrado directo con cliente mysql o a través de Docker
echo "Insertando o actualizando usuarios de prueba en smartstay_iam..."
SEEDED=0

if command -v mysql >/dev/null 2>&1; then
    if echo "$SQL_PAYLOAD" | mysql -h "$MYSQL_HOST" -P "$MYSQL_PORT" -u root -p"$MYSQL_ROOT_PASSWORD" 2>/dev/null; then
        echo " -> Sembrado ejecutado exitosamente vía cliente mysql local."
        SEEDED=1
    fi
fi

if [ "$SEEDED" -eq 0 ] && command -v docker >/dev/null 2>&1; then
    if docker compose -f compose.microservices.yml ps --status running | grep -q "mysql"; then
        echo "$SQL_PAYLOAD" | docker compose -f compose.microservices.yml exec -T mysql mysql -u root -p"$MYSQL_ROOT_PASSWORD" 2>/dev/null
        echo " -> Sembrado ejecutado exitosamente dentro del contenedor mysql de Docker."
        SEEDED=1
    fi
fi

# 2. Verificar activamente que el Gateway y el servicio IAM respondan con autenticación válida
echo -n "Verificando validez del fixture mediante autenticación HTTP (/sign-in)... "
HTTP_RESPONSE=$(curl -s -X POST "$GATEWAY_URL/api/v1/authentication/sign-in" \
    -H "Content-Type: application/json" \
    -d '{"username":"guest1","password":"Password123!"}' || echo "{}")

if echo "$HTTP_RESPONSE" | grep -q "token"; then
    echo "OK (Usuario guest1 autenticado con éxito)"
else
    echo "FAILED"
    echo "Error: No se pudo verificar la existencia ni autenticación de los usuarios del fixture."
    echo "Respuesta del servidor: $HTTP_RESPONSE"
    exit 1
fi

echo "=========================================================="
echo " [SUCCESS] Fixture del MVP verificado y garantizado!      "
echo "=========================================================="
exit 0
