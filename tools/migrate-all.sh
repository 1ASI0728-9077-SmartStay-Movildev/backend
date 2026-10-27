#!/usr/bin/env bash
set -euo pipefail

MYSQL_HOST="${MYSQL_HOST:-localhost}"
MYSQL_PORT="${MYSQL_PORT:-3306}"
MYSQL_USER="${MYSQL_USER:-root}"
MYSQL_PASSWORD="${MYSQL_PASSWORD:-rootpassword}"

echo "=========================================================="
echo " SmartStay Schema Migrations: All Microservices           "
echo " Host: $MYSQL_HOST:$MYSQL_PORT                            "
echo "=========================================================="

apply_migration() {
    local service=$1
    local db=$2
    local sql_file=$3

    echo "Applying migrations for $service (Database: $db)..."
    mysql -h "$MYSQL_HOST" -P "$MYSQL_PORT" -u "$MYSQL_USER" -p"$MYSQL_PASSWORD" "$db" < "$sql_file"
    echo " -> $service migrations applied successfully."
}

# 1. IAM
apply_migration "IAM" "smartstay_iam" "artifacts/migrations/iam.sql"

# 2. Accommodations
apply_migration "Accommodations" "smartstay_accommodations" "artifacts/migrations/accommodations.sql"

# 3. Bookings
apply_migration "Bookings" "smartstay_bookings" "artifacts/migrations/bookings.sql"

# 4. Payments
apply_migration "Payments" "smartstay_payments" "artifacts/migrations/payments.sql"

# 5. Profiles
apply_migration "Profiles" "smartstay_profiles" "artifacts/migrations/profiles.sql"

# 6. Analytics
apply_migration "Analytics" "smartstay_analytics" "artifacts/migrations/analytics.sql"

echo "=========================================================="
echo " [SUCCESS] All 6 microservice databases migrated!        "
echo "=========================================================="
