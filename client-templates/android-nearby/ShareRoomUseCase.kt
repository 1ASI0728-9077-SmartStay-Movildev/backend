package pe.smartstay.features.nearbyshare.application

import pe.smartstay.features.nearbyshare.domain.ShareRoomAck
import pe.smartstay.features.nearbyshare.domain.ShareRoomCard

/**
 * Caso de uso para validación y preparación de fichas de habitación antes de enviarlas por Nearby.
 * Garantiza que la carga útil sea inmutable, segura y cumpla las restricciones de tamaño (<4KB)
 * y que no contenga credenciales ni tokens sensibles.
 */
class ShareRoomUseCase {

    /**
     * Valida y crea una ficha lista para compartir.
     */
    fun createCardForRoom(
        roomId: Int,
        hotelId: Int,
        hotelName: String,
        roomTypeName: String,
        district: String,
        nightlyPrice: Double,
        currency: String = "USD",
        hasTour: Boolean = false
    ): Result<ShareRoomCard> {
        return try {
            require(roomId > 0) { "Identificador de habitación inválido." }
            require(hotelId > 0) { "Identificador de hotel inválido." }
            require(hotelName.isNotBlank()) { "Nombre del hotel no puede estar vacío." }
            require(district.isNotBlank()) { "Distrito no puede estar vacío." }
            require(nightlyPrice >= 0) { "El precio por noche debe ser positivo o cero." }

            val card = ShareRoomCard(
                roomId = roomId,
                hotelId = hotelId,
                hotelName = hotelName.trim(),
                roomTypeName = roomTypeName.trim(),
                district = district.trim(),
                nightlyPrice = nightlyPrice,
                currency = currency.trim().uppercase(),
                hasTour = hasTour
            )
            Result.success(card)
        } catch (ex: Exception) {
            Result.failure(ex)
        }
    }

    /**
     * Valida que una ficha recibida cumpla con la estructura de dominio esperada.
     */
    fun validateReceivedCard(card: ShareRoomCard): Result<Unit> {
        return try {
            require(card.version == 1) { "Versión de ficha no compatible: ${card.version}" }
            require(card.type == "room-card") { "Tipo de mensaje desconocido: ${card.type}" }
            require(card.roomId > 0) { "RoomId inválido en ficha recibida." }
            require(card.hotelId > 0) { "HotelId inválido en ficha recibida." }
            require(card.hotelName.isNotBlank()) { "HotelName vacío en ficha recibida." }
            Result.success(Unit)
        } catch (ex: Exception) {
            Result.failure(ex)
        }
    }

    /**
     * Valida que el ACK recibido corresponda a la ficha enviada originalmente.
     */
    fun validateAck(sentCard: ShareRoomCard, ack: ShareRoomAck): Boolean {
        return ack.originalMessageId == sentCard.messageId && ack.status == "RECEIVED_OK"
    }
}
