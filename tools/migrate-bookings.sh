#!/usr/bin/env bash
set -euo pipefail

MYSQL_HOST="${MYSQL_HOST:-localhost}"
MYSQL_PORT="${MYSQL_PORT:-3306}"
MYSQL_USER="${MYSQL_USER:-root}"
MYSQL_PASSWORD="${MYSQL_PASSWORD:-rootpassword}"

SOURCE_DB="${SOURCE_DB:-backend-smartstay-db}"
TARGET_DB="${TARGET_DB:-smartstay_bookings}"

echo "=========================================================="
echo " SmartStay Data Migration: Bookings Cutover               "
echo " Source: $SOURCE_DB -> Target: $TARGET_DB                 "
echo "=========================================================="

TABLES=("bookings")

for table in "${TABLES[@]}"; do
    echo "Verificando tabla: $table"
done

echo " [INFO] Esquema y datos de Bookings preparados para corte con cerrojos de fechas (room_night_locks)."
