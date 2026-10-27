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
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027015215_InitialPayments') THEN

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
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027015215_InitialPayments') THEN

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
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027015215_InitialPayments') THEN

    CREATE TABLE `outbox_messages` (
        `id` char(36) COLLATE ascii_general_ci NOT NULL,
        `event_type` varchar(150) CHARACTER SET utf8mb4 NOT NULL,
        `schema_version` int NOT NULL,
        `aggregate_id` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
        `aggregate_version` bigint NOT NULL,
        `payload` longtext CHARACTER SET utf8mb4 NOT NULL,
        `occurred_at_utc` datetime(6) NOT NULL,
        `published_at_utc` datetime(6) NULL,
        `attempts` int NOT NULL,
        `next_attempt_at_utc` datetime(6) NULL,
        `lease_owner` varchar(100) CHARACTER SET utf8mb4 NULL,
        `lease_until_utc` datetime(6) NULL,
        `last_error` varchar(2000) CHARACTER SET utf8mb4 NULL,
        CONSTRAINT `pk_outbox_messages` PRIMARY KEY (`id`)
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
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027015215_InitialPayments') THEN

    CREATE TABLE `payment_idempotency_records` (
        `idempotency_key` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
        `actor_id` int NOT NULL,
        `request_hash` varchar(128) CHARACTER SET utf8mb4 NOT NULL,
        `status` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
        `response_payload` longtext CHARACTER SET utf8mb4 NULL,
        `created_at_utc` datetime(6) NOT NULL,
        CONSTRAINT `pk_payment_idempotency_records` PRIMARY KEY (`idempotency_key`)
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
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027015215_InitialPayments') THEN

    CREATE TABLE `payments` (
        `id` int NOT NULL AUTO_INCREMENT,
        `booking_id` int NOT NULL,
        `transaction_id` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
        `amount` decimal(18,2) NOT NULL,
        `payment_method` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
        `card_holder_name` varchar(150) CHARACTER SET utf8mb4 NOT NULL,
        `card_number_masked` varchar(30) CHARACTER SET utf8mb4 NOT NULL,
        `payment_date` datetime(6) NOT NULL,
        `status` int NOT NULL,
        CONSTRAINT `pk_payments` PRIMARY KEY (`id`)
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
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027015215_InitialPayments') THEN

    CREATE INDEX `ix_outbox_polling` ON `outbox_messages` (`published_at_utc`, `next_attempt_at_utc`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027015215_InitialPayments') THEN

    CREATE INDEX `ix_outbox_published_at` ON `outbox_messages` (`published_at_utc`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027015215_InitialPayments') THEN

    CREATE INDEX `ix_payments_booking_id` ON `payments` (`booking_id`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027015215_InitialPayments') THEN

    CREATE UNIQUE INDEX `ix_payments_transaction_id` ON `payments` (`transaction_id`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027015215_InitialPayments') THEN

    INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
    VALUES ('20261027015215_InitialPayments', '9.0.10');

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

COMMIT;

