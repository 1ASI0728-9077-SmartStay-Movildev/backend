USE `smartstay_bookings`;
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
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027013523_InitialBookings') THEN

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
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027013523_InitialBookings') THEN

    CREATE TABLE `bookings` (
        `id` int NOT NULL AUTO_INCREMENT,
        `room_id` int NOT NULL,
        `hotel_id` int NOT NULL,
        `guest_name` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
        `guest_email` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
        `check_in_date` datetime(6) NOT NULL,
        `check_out_date` datetime(6) NOT NULL,
        `nightly_price` decimal(18,2) NOT NULL,
        `total_amount` decimal(18,2) NOT NULL,
        `currency` varchar(10) CHARACTER SET utf8mb4 NOT NULL DEFAULT 'USD',
        `status` int NOT NULL,
        `expires_at_utc` datetime(6) NULL,
        `created_at_utc` datetime(6) NOT NULL,
        CONSTRAINT `pk_bookings` PRIMARY KEY (`id`)
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
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027013523_InitialBookings') THEN

    CREATE TABLE `room_night_locks` (
        `room_id` int NOT NULL,
        `stay_date` date NOT NULL,
        `booking_id` int NOT NULL,
        CONSTRAINT `pk_room_night_locks` PRIMARY KEY (`room_id`, `stay_date`),
        CONSTRAINT `fk_room_night_locks_bookings_booking_id` FOREIGN KEY (`booking_id`) REFERENCES `bookings` (`id`) ON DELETE CASCADE
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
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027013523_InitialBookings') THEN

    CREATE INDEX `ix_room_night_booking` ON `room_night_locks` (`booking_id`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027013523_InitialBookings') THEN

    INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
    VALUES ('20261027013523_InitialBookings', '9.0.10');

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027014510_AddBookingsInbox') THEN

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
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027014510_AddBookingsInbox') THEN

    INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
    VALUES ('20261027014510_AddBookingsInbox', '9.0.10');

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027162538_AddGuestUserIdToBookings') THEN

    ALTER TABLE `bookings` ADD `guest_user_id` int NOT NULL DEFAULT 0;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027162538_AddGuestUserIdToBookings') THEN

    CREATE INDEX `ix_bookings_guest_user_id` ON `bookings` (`guest_user_id`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027162538_AddGuestUserIdToBookings') THEN

    INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
    VALUES ('20261027162538_AddGuestUserIdToBookings', '9.0.10');

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027172349_AddConcurrencyTokenToBookings') THEN

    ALTER TABLE `bookings` ADD `concurrency_token` char(36) COLLATE ascii_general_ci NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027172349_AddConcurrencyTokenToBookings') THEN

    INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
    VALUES ('20261027172349_AddConcurrencyTokenToBookings', '9.0.10');

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

COMMIT;

