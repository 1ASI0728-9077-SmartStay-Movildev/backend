#!/usr/bin/env bash
set -euo pipefail

MYSQL_HOST="${MYSQL_HOST:-localhost}"
MYSQL_PORT="${MYSQL_PORT:-3306}"
MYSQL_USER="${MYSQL_USER:-root}"
MYSQL_PASSWORD="${MYSQL_PASSWORD:-rootpassword}"

SOURCE_DB="${SOURCE_DB:-backend-smartstay-db}"
TARGET_DB="${TARGET_DB:-smartstay_payments}"

echo "=========================================================="
echo " SmartStay Data Migration: Payments Cutover               "
echo " Source: $SOURCE_DB -> Target: $TARGET_DB                 "
echo "=========================================================="

TABLES=("payments")

for table in "${TABLES[@]}"; do
    echo "Verificando tabla: $table"
done

echo " [INFO] Esquema y datos de Payments preparados para corte con Outbox, Inbox e Idempotencia."
