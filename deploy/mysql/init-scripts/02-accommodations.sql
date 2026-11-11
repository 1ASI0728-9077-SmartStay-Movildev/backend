USE `smartstay_accommodations`;
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
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027011242_InitialAccommodations') THEN

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
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027011242_InitialAccommodations') THEN

    CREATE TABLE `amenities_catalogs` (
        `id` int NOT NULL AUTO_INCREMENT,
        `name` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
        `category` longtext CHARACTER SET utf8mb4 NOT NULL,
        CONSTRAINT `pk_amenities_catalogs` PRIMARY KEY (`id`)
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
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027011242_InitialAccommodations') THEN

    CREATE TABLE `hotel_categories` (
        `id` int NOT NULL AUTO_INCREMENT,
        `name` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
        CONSTRAINT `pk_hotel_categories` PRIMARY KEY (`id`)
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
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027011242_InitialAccommodations') THEN

    CREATE TABLE `hotels` (
        `id` int NOT NULL AUTO_INCREMENT,
        `host_id` int NOT NULL,
        `name` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
        `address` varchar(200) CHARACTER SET utf8mb4 NOT NULL,
        `city` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
        `country` varchar(100) CHARACTER SET utf8mb4 NOT NULL,
        `image_url` longtext CHARACTER SET utf8mb4 NOT NULL,
        `description` varchar(1000) CHARACTER SET utf8mb4 NOT NULL,
        `type` longtext CHARACTER SET utf8mb4 NOT NULL,
        `amenities` json NOT NULL,
        CONSTRAINT `pk_hotels` PRIMARY KEY (`id`)
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
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027011242_InitialAccommodations') THEN

    CREATE TABLE `room_types` (
        `id` int NOT NULL AUTO_INCREMENT,
        `name` varchar(50) CHARACTER SET utf8mb4 NOT NULL,
        `description` varchar(500) CHARACTER SET utf8mb4 NOT NULL,
        CONSTRAINT `pk_room_types` PRIMARY KEY (`id`)
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
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027011242_InitialAccommodations') THEN

    CREATE TABLE `rooms` (
        `id` int NOT NULL AUTO_INCREMENT,
        `room_type_id` int NOT NULL,
        `hotel_id` int NOT NULL,
        `price` decimal(18,2) NOT NULL,
        `description` varchar(1000) CHARACTER SET utf8mb4 NOT NULL,
        `amenities` json NOT NULL,
        `layout` json NULL,
        CONSTRAINT `pk_rooms` PRIMARY KEY (`id`),
        CONSTRAINT `fk_rooms_hotels_hotel_id` FOREIGN KEY (`hotel_id`) REFERENCES `hotels` (`id`) ON DELETE CASCADE,
        CONSTRAINT `fk_rooms_room_types_room_type_id` FOREIGN KEY (`room_type_id`) REFERENCES `room_types` (`id`) ON DELETE RESTRICT
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
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027011242_InitialAccommodations') THEN

    INSERT INTO `amenities_catalogs` (`id`, `category`, `name`)
    VALUES (1, 'General', 'Wifi'),
    (2, 'General', 'Piscina'),
    (3, 'General', 'Gimnasio'),
    (4, 'General', 'Restaurante'),
    (5, 'General', 'Parking'),
    (6, 'General', 'Spa'),
    (7, 'General', 'Bar'),
    (8, 'General', 'Desayuno');

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027011242_InitialAccommodations') THEN

    INSERT INTO `hotel_categories` (`id`, `name`)
    VALUES (1, 'Hotel'),
    (2, 'Posada'),
    (3, 'Lodge'),
    (4, 'Hostal'),
    (5, 'Cabaña'),
    (6, 'Resort');

    INSERT INTO `hotel_categories` (`id`, `name`)
    SELECT 7, 'Departamento'
    WHERE NOT EXISTS (SELECT 1 FROM `hotel_categories` WHERE `id` = 7);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027011242_InitialAccommodations') THEN

    INSERT INTO `hotels` (`id`, `address`, `amenities`, `city`, `country`, `description`, `host_id`, `image_url`, `name`, `type`)
    VALUES (1, 'Jr. de la Unión 958', '["Wifi","Restaurante","Bar"]', 'Lima', 'Peru', 'Historic hotel in the center of Lima.', 1, 'https://placehold.co/600x400/3498DB/FFFFFF?text=Bolivar', 'Grand Hotel Bolivar', 'Hotel'),
    (2, 'San Blas 123', '["Desayuno","Wifi","Gimnasio"]', 'Cusco', 'Peru', 'Experience the mystic energy of the Andes.', 1, 'https://placehold.co/600x400/E67E22/FFFFFF?text=Andean', 'Cusco Andean Lodge', 'Lodge');

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027011242_InitialAccommodations') THEN

    INSERT INTO `room_types` (`id`, `description`, `name`)
    VALUES (1, 'Cozy room for solo travelers.', 'Single Standard'),
    (2, 'Spacious room for couples or business.', 'Double Deluxe'),
    (3, 'Luxury suite with best views.', 'Presidential Suite');

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027011242_InitialAccommodations') THEN

    INSERT INTO `rooms` (`id`, `amenities`, `description`, `hotel_id`, `price`, `room_type_id`)
    VALUES (101, '["Wifi","TV"]', 'Room 101 - Standard view.', 1, 85.0, 1),
    (102, '["Wifi","TV","Minibar"]', 'Room 102 - Plaza view with balcony.', 1, 150.0, 2),
    (201, '["Jacuzzi","Wifi","Desayuno","Chimenea"]', 'Suite 201 - Panoramic mountain view.', 2, 320.0, 3);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027011242_InitialAccommodations') THEN

    CREATE INDEX `ix_rooms_hotel_id` ON `rooms` (`hotel_id`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027011242_InitialAccommodations') THEN

    CREATE INDEX `ix_rooms_room_type_id` ON `rooms` (`room_type_id`);

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027011242_InitialAccommodations') THEN

    INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
    VALUES ('20261027011242_InitialAccommodations', '9.0.10');

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027162513_AddRoomMvpAttributes') THEN

    ALTER TABLE `rooms` ADD `capacity` int NOT NULL DEFAULT 2;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027162513_AddRoomMvpAttributes') THEN

    ALTER TABLE `rooms` ADD `currency` varchar(3) CHARACTER SET utf8mb4 NOT NULL DEFAULT 'USD';

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027162513_AddRoomMvpAttributes') THEN

    ALTER TABLE `rooms` ADD `is_bookable` tinyint(1) NOT NULL DEFAULT TRUE;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027162513_AddRoomMvpAttributes') THEN

    ALTER TABLE `rooms` ADD `tour_path` varchar(500) CHARACTER SET utf8mb4 NULL;

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027162513_AddRoomMvpAttributes') THEN

    ALTER TABLE `hotels` ADD `district` varchar(100) CHARACTER SET utf8mb4 NOT NULL DEFAULT '';

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027162513_AddRoomMvpAttributes') THEN

    UPDATE `hotels` SET `district` = 'Cercado de Lima'
    WHERE `id` = 1;
    SELECT ROW_COUNT();


    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027162513_AddRoomMvpAttributes') THEN

    UPDATE `hotels` SET `district` = 'San Blas'
    WHERE `id` = 2;
    SELECT ROW_COUNT();


    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027162513_AddRoomMvpAttributes') THEN

    UPDATE `rooms` SET `capacity` = 1, `currency` = 'USD', `description` = 'Room 101 - Standard cozy single room.', `is_bookable` = TRUE, `tour_path` = NULL
    WHERE `id` = 101;
    SELECT ROW_COUNT();


    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027162513_AddRoomMvpAttributes') THEN

    UPDATE `rooms` SET `capacity` = 2, `currency` = 'USD', `description` = 'Room 102 - Plaza view deluxe room with 360 virtual tour.', `is_bookable` = TRUE, `tour_path` = '/tours/room-102/manifest.json'
    WHERE `id` = 102;
    SELECT ROW_COUNT();


    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027162513_AddRoomMvpAttributes') THEN

    UPDATE `rooms` SET `capacity` = 4, `currency` = 'USD', `description` = 'Suite 201 - Panoramic mountain view presidential suite.', `is_bookable` = TRUE, `tour_path` = NULL
    WHERE `id` = 201;
    SELECT ROW_COUNT();


    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

DROP PROCEDURE IF EXISTS MigrationsScript;
DELIMITER //
CREATE PROCEDURE MigrationsScript()
BEGIN
    IF NOT EXISTS(SELECT 1 FROM `__EFMigrationsHistory` WHERE `MigrationId` = '20261027162513_AddRoomMvpAttributes') THEN

    INSERT INTO `__EFMigrationsHistory` (`MigrationId`, `ProductVersion`)
    VALUES ('20261027162513_AddRoomMvpAttributes', '9.0.10');

    END IF;
END //
DELIMITER ;
CALL MigrationsScript();
DROP PROCEDURE MigrationsScript;

COMMIT;

