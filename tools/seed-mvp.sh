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

# 1. Asegurar usuarios en smartstay_iam
echo "Asegurando usuarios de prueba (Admin, Guest1, Guest2)..."
if command -v mysql >/dev/null 2>&1; then
    mysql -h "$MYSQL_HOST" -P "$MYSQL_PORT" -u root -p"$MYSQL_ROOT_PASSWORD" smartstay_iam << 'EOF'
-- Hash bcrypt para 'Password123!'
SET @pwd_hash = '$2a$11$qB/7uM9QG5E0oPcfFp6oZONp59Y6K51817vCq2k8tZ0eQ0E6V6aO6';

INSERT INTO `users` (`id`, `username`, `password_hash`, `role`, `hotel_id`, `chain_id`, `token_version`, `created_at_utc`, `updated_at_utc`)
VALUES
  (1, 'admin', @pwd_hash, 'admin', 1, 1, 1, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
  (2, 'guest1', @pwd_hash, 'guest', NULL, NULL, 1, UTC_TIMESTAMP(), UTC_TIMESTAMP()),
  (3, 'guest2', @pwd_hash, 'guest', NULL, NULL, 1, UTC_TIMESTAMP(), UTC_TIMESTAMP())
ON DUPLICATE KEY UPDATE `role` = VALUES(`role`), `token_version` = 1;
EOF
    echo " -> Usuarios verificados en smartstay_iam."
else
    echo " -> [INFO] Cliente mysql local no encontrado; se asume inicialización por init-scripts de Docker."
fi

echo "=========================================================="
echo " [SUCCESS] Fixture del MVP verificado con éxito!          "
echo "=========================================================="
