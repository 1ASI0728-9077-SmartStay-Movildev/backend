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

FLUSH PRIVILEGES;
