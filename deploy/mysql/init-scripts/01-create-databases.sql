-- Creación de bases de datos lógicas para SmartStay Microservicios

CREATE DATABASE IF NOT EXISTS `smartstay_accommodations` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE DATABASE IF NOT EXISTS `smartstay_bookings` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE DATABASE IF NOT EXISTS `smartstay_payments` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE DATABASE IF NOT EXISTS `smartstay_iam` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE DATABASE IF NOT EXISTS `smartstay_profiles` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE DATABASE IF NOT EXISTS `smartstay_analytics` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

-- Usuario de aplicación para Accommodations
CREATE USER IF NOT EXISTS 'accommodations_user'@'%' IDENTIFIED BY 'accommodations_pass';
GRANT SELECT, INSERT, UPDATE, DELETE ON `smartstay_accommodations`.* TO 'accommodations_user'@'%';

-- Usuario de aplicación para Bookings
CREATE USER IF NOT EXISTS 'bookings_user'@'%' IDENTIFIED BY 'bookings_pass';
GRANT SELECT, INSERT, UPDATE, DELETE ON `smartstay_bookings`.* TO 'bookings_user'@'%';

-- Usuario de aplicación para Payments
CREATE USER IF NOT EXISTS 'payments_user'@'%' IDENTIFIED BY 'payments_pass';
GRANT SELECT, INSERT, UPDATE, DELETE ON `smartstay_payments`.* TO 'payments_user'@'%';

-- Usuario de aplicación para IAM
CREATE USER IF NOT EXISTS 'iam_user'@'%' IDENTIFIED BY 'iam_pass';
GRANT SELECT, INSERT, UPDATE, DELETE ON `smartstay_iam`.* TO 'iam_user'@'%';

-- Usuario de aplicación para Profiles
CREATE USER IF NOT EXISTS 'profiles_user'@'%' IDENTIFIED BY 'profiles_pass';
GRANT SELECT, INSERT, UPDATE, DELETE ON `smartstay_profiles`.* TO 'profiles_user'@'%';

-- Usuario de aplicación para Analytics
CREATE USER IF NOT EXISTS 'analytics_user'@'%' IDENTIFIED BY 'analytics_pass';
GRANT SELECT, INSERT, UPDATE, DELETE ON `smartstay_analytics`.* TO 'analytics_user'@'%';

FLUSH PRIVILEGES;
