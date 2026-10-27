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

COMMIT;

