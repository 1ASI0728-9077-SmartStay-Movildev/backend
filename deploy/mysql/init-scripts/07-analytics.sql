USE `smartstay_analytics`;
CREATE TABLE IF NOT EXISTS `__EFMigrationsHistory` (
    `MigrationId` varchar(150) CHARACTER SET utf8mb4 NOT NULL,
    `ProductVersion` varchar(32) CHARACTER SET utf8mb4 NOT NULL,
    CONSTRAINT `PK___EFMigrationsHistory` PRIMARY KEY (`MigrationId`)
) CHARACTER SET=utf8mb4;

START TRANSACTION;
DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027022034_InitialAnalytics') THEN

    ALTER DATABASE CHARACTER SET utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027022034_InitialAnalytics') THEN

    CREATE TABLE `analytics_booking_facts` (
        `booking_id` int NOT NULL,
        `room_id` int NOT NULL,
        `hotel_id` int NOT NULL,
        `status` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
        `check_in_date` datetime(6) NOT NULL,
        `check_out_date` datetime(6) NOT NULL,
        `nights_count` int NOT NULL,
        `occurred_at_utc` datetime(6) NOT NULL,
        CONSTRAINT `pk_analytics_booking_facts` PRIMARY KEY (`booking_id`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027022034_InitialAnalytics') THEN

    CREATE TABLE `analytics_catalog_snapshots` (
        `room_id` int NOT NULL,
        `hotel_id` int NOT NULL,
        `is_bookable` tinyint(1) NOT NULL,
        `nightly_price` decimal(18,2) NOT NULL,
        `updated_at_utc` datetime(6) NOT NULL,
        CONSTRAINT `pk_analytics_catalog_snapshots` PRIMARY KEY (`room_id`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027022034_InitialAnalytics') THEN

    CREATE TABLE `analytics_revenue_facts` (
        `payment_id` int NOT NULL,
        `booking_id` int NOT NULL,
        `hotel_id` int NOT NULL,
        `amount` decimal(18,2) NOT NULL,
        `currency` varchar(10) CHARACTER SET utf8mb4 NOT NULL DEFAULT 'USD',
        `is_refunded` tinyint(1) NOT NULL,
        `refund_amount` decimal(18,2) NOT NULL,
        `occurred_at_utc` datetime(6) NOT NULL,
        CONSTRAINT `pk_analytics_revenue_facts` PRIMARY KEY (`payment_id`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027022034_InitialAnalytics') THEN

    CREATE TABLE `inbox_messages` (
        `consumer_name` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
        `event_id` char(36) COLLATE ascii_general_ci NOT NULL,
        `event_type` varchar(150) CHARACTER SET utf8mb4 NOT NULL,
        `received_at_utc` datetime(6) NOT NULL,
        `processed_at_utc` datetime(6) NULL,
        `status` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
        `last_error` varchar(1000) CHARACTER SET utf8mb4 NULL,
        CONSTRAINT `pk_inbox_messages` PRIMARY KEY (`consumer_name`, `event_id`)
    ) CHARACTER SET=utf8mb4;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027022034_InitialAnalytics') THEN

    CREATE INDEX `ix_bookings_occurred_at` ON `analytics_booking_facts` (`occurred_at_utc`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027022034_InitialAnalytics') THEN

    CREATE INDEX `ix_bookings_status` ON `analytics_booking_facts` (`status`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027022034_InitialAnalytics') THEN

    CREATE INDEX `ix_revenue_booking_id` ON `analytics_revenue_facts` (`booking_id`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027022034_InitialAnalytics') THEN

    CREATE INDEX `ix_revenue_occurred_at` ON `analytics_revenue_facts` (`occurred_at_utc`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027022034_InitialAnalytics') THEN

    INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
    VALUES ('20261027022034_InitialAnalytics', '9.0.10');

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

COMMIT;

