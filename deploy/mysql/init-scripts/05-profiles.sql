USE `smartstay_profiles`;
﻿CREATE TABLE IF NOT EXISTS `__EFMigrationsHistory` (
    `MigrationId` varchar(150) CHARACTER SET utf8mb4 NOT NULL,
    `ProductVersion` varchar(32) CHARACTER SET utf8mb4 NOT NULL,
    CONSTRAINT `PK___EFMigrationsHistory` PRIMARY KEY (`MigrationId`)
) CHARACTER SET=utf8mb4;

START TRANSACTION;
DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027020144_InitialProfiles') THEN

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
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027020144_InitialProfiles') THEN

    CREATE TABLE `profiles` (
        `id` int NOT NULL AUTO_INCREMENT,
        `user_id` int NULL,
        `first_name` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
        `last_name` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
        `email_address` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
        `address_street` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
        `address_number` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
        `address_city` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
        `address_postal_code` varchar(20) CHARACTER SET utf8mb4 NOT NULL,
        `address_country` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
        `created_at_utc` datetime(6) NOT NULL,
        `updated_at_utc` datetime(6) NULL,
        CONSTRAINT `pk_profiles` PRIMARY KEY (`id`)
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
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027020144_InitialProfiles') THEN

    CREATE INDEX `ix_profiles_user_id` ON `profiles` (`user_id`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027020144_InitialProfiles') THEN

    INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
    VALUES ('20261027020144_InitialProfiles', '9.0.10');

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

COMMIT;

