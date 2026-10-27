#!/usr/bin/env bash
set -euo pipefail

echo "USE \`smartstay_accommodations\`;" > deploy/mysql/init-scripts/02-accommodations.sql
cat artifacts/migrations/accommodations.sql >> deploy/mysql/init-scripts/02-accommodations.sql

echo "USE \`smartstay_bookings\`;" > deploy/mysql/init-scripts/03-bookings.sql
cat artifacts/migrations/bookings.sql >> deploy/mysql/init-scripts/03-bookings.sql

echo "USE \`smartstay_payments\`;" > deploy/mysql/init-scripts/04-payments.sql
cat artifacts/migrations/payments.sql >> deploy/mysql/init-scripts/04-payments.sql

echo "USE \`smartstay_profiles\`;" > deploy/mysql/init-scripts/05-profiles.sql
cat artifacts/migrations/profiles.sql >> deploy/mysql/init-scripts/05-profiles.sql

echo "USE \`smartstay_iam\`;" > deploy/mysql/init-scripts/06-iam.sql
cat artifacts/migrations/iam.sql >> deploy/mysql/init-scripts/06-iam.sql

echo "USE \`smartstay_analytics\`;" > deploy/mysql/init-scripts/07-analytics.sql
cat artifacts/migrations/analytics.sql >> deploy/mysql/init-scripts/07-analytics.sql

echo "Init scripts prepared in deploy/mysql/init-scripts/"
