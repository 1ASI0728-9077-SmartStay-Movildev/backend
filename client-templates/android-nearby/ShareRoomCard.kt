package pe.smartstay.features.nearbyshare.domain

import java.util.UUID

/**
 * Ficha inmutable de habitación para transporte offline 1:1 vía Google Nearby Connections.
 * Tamaño máximo garantizado: < 4 KB. No contiene JWT, secretos ni credenciales.
 */
data class ShareRoomCard(
    val version: Int = 1,
    val messageId: String = UUID.randomUUID().toString(),
    val type: String = "room-card",
    val roomId: Int,
    val hotelId: Int,
    val hotelName: String,
    val roomTypeName: String,
    val district: String,
    val nightlyPrice: Double,
    val currency: String = "USD",
    val hasTour: Boolean = false
) {
    init {
        require(roomId > 0) { "RoomId must be positive." }
        require(hotelId > 0) { "HotelId must be positive." }
        require(hotelName.isNotBlank()) { "HotelName cannot be blank." }
    }
}

/**
 * Confirmación de recepción (ACK) devuelta por el receptor para validar la entrega real en la app.
 */
data class ShareRoomAck(
    val version: Int = 1,
    val originalMessageId: String,
    val status: String = "RECEIVED_OK",
    val timestampUtc: Long = System.currentTimeMillis()
)
