package pe.smartstay.features.nearbyshare.data

import android.content.Context
import com.google.android.gms.nearby.Nearby
import com.google.android.gms.nearby.connection.*
import org.json.JSONObject
import pe.smartstay.features.nearbyshare.domain.ShareRoomAck
import pe.smartstay.features.nearbyshare.domain.ShareRoomCard

interface NearbyTransportListener {
    fun onDeviceDiscovered(endpointId: String, deviceName: String)
    fun onConnectionInitiated(endpointId: String, authDigits: String)
    fun onConnected(endpointId: String)
    fun onCardReceived(endpointId: String, card: ShareRoomCard)
    fun onAckReceived(ack: ShareRoomAck)
    fun onDisconnected(endpointId: String)
    fun onError(message: String)
}

/**
 * Adaptador de bajo nivel para Google Nearby Connections usando estrategia P2P_POINT_TO_POINT (1:1).
 * Implementa validación estricta de tamaño en entrada (<4 KB), reenvío de ACK a duplicados y desacople de confirmación.
 */
class NearbyRoomTransport(
    private val context: Context,
    private val listener: NearbyTransportListener
) {
    private val connectionsClient = Nearby.getConnectionsClient(context)
    private val strategy = Strategy.P2P_POINT_TO_POINT
    private val serviceId = "pe.smartstay.roomshare.v1"
    private val receivedMessageIds = mutableSetOf<String>()

    private val payloadCallback = object : PayloadCallback() {
        override fun onPayloadReceived(endpointId: String, payload: Payload) {
            if (payload.type != Payload.Type.BYTES) return
            val bytes = payload.asBytes() ?: return

            // Validación estricta de tamaño en recepción (<= 4 KB)
            if (bytes.size > 4096) {
                listener.onError("Payload recibido excede el límite máximo seguro de 4 KB (${bytes.size} bytes).")
                return
            }

            val jsonStr = String(bytes, Charsets.UTF_8)

            try {
                val json = JSONObject(jsonStr)
                val type = json.optString("type", "")

                if (type == "room-card") {
                    val messageId = json.getString("messageId")
                    if (receivedMessageIds.contains(messageId)) {
                        // Mensaje duplicado: reenviar ACK para desbloquear al emisor si el primer ACK se perdió en tránsito
                        sendAck(endpointId, messageId)
                        return
                    }
                    receivedMessageIds.add(messageId)

                    val card = ShareRoomCard(
                        version = json.getInt("version"),
                        messageId = messageId,
                        roomId = json.getInt("roomId"),
                        hotelId = json.getInt("hotelId"),
                        hotelName = json.getString("hotelName"),
                        roomTypeName = json.getString("roomTypeName"),
                        district = json.getString("district"),
                        nightlyPrice = json.getDouble("nightlyPrice"),
                        currency = json.optString("currency", "USD"),
                        hasTour = json.optBoolean("hasTour", false)
                    )

                    // Entregar al listener. El ACK de aplicación lo emitirá el caso de uso tras validar esquema.
                    listener.onCardReceived(endpointId, card)
                } else if (type == "room-card-ack") {
                    val ack = ShareRoomAck(
                        version = json.getInt("version"),
                        originalMessageId = json.getString("originalMessageId"),
                        status = json.getString("status")
                    )
                    listener.onAckReceived(ack)
                }
            } catch (ex: Exception) {
                listener.onError("Error deserializando payload recibido: ${ex.message}")
            }
        }

        override fun onPayloadTransferUpdate(endpointId: String, update: PayloadTransferUpdate) {}
    }

    private val connectionLifecycleCallback = object : ConnectionLifecycleCallback() {
        override fun onConnectionInitiated(endpointId: String, connectionInfo: ConnectionInfo) {
            // Requiere autenticación visual mediante token/dígitos
            val digits = connectionInfo.authenticationDigits
            listener.onConnectionInitiated(endpointId, digits)
        }

        override fun onConnectionResult(endpointId: String, resolution: ConnectionResolution) {
            if (resolution.status.isSuccess) {
                listener.onConnected(endpointId)
            } else {
                listener.onError("Conexión rechazada o fallida (Status: ${resolution.status.statusCode})")
            }
        }

        override fun onDisconnected(endpointId: String) {
            listener.onDisconnected(endpointId)
        }
    }

    fun startAdvertising(deviceName: String) {
        val options = AdvertisingOptions.Builder().setStrategy(strategy).build()
        connectionsClient.startAdvertising(deviceName, serviceId, connectionLifecycleCallback, options)
            .addOnFailureListener { listener.onError("Fallo al iniciar publicidad: ${it.message}") }
    }

    fun startDiscovery() {
        val options = DiscoveryOptions.Builder().setStrategy(strategy).build()
        connectionsClient.startDiscovery(serviceId, object : EndpointDiscoveryCallback() {
            override fun onEndpointFound(endpointId: String, info: DiscoveredEndpointInfo) {
                listener.onDeviceDiscovered(endpointId, info.endpointName)
            }

            override fun onEndpointLost(endpointId: String) {}
        }, options).addOnFailureListener { listener.onError("Fallo al iniciar descubrimiento: ${it.message}") }
    }

    fun requestConnection(endpointId: String, localName: String) {
        connectionsClient.requestConnection(localName, endpointId, connectionLifecycleCallback)
            .addOnFailureListener { listener.onError("Fallo en solicitud de conexión: ${it.message}") }
    }

    fun acceptConnection(endpointId: String) {
        connectionsClient.acceptConnection(endpointId, payloadCallback)
    }

    fun rejectConnection(endpointId: String) {
        connectionsClient.rejectConnection(endpointId)
    }

    fun sendCard(endpointId: String, card: ShareRoomCard) {
        val json = JSONObject().apply {
            put("version", card.version)
            put("messageId", card.messageId)
            put("type", "room-card")
            put("roomId", card.roomId)
            put("hotelId", card.hotelId)
            put("hotelName", card.hotelName)
            put("roomTypeName", card.roomTypeName)
            put("district", card.district)
            put("nightlyPrice", card.nightlyPrice)
            put("currency", card.currency)
            put("hasTour", card.hasTour)
        }

        val bytes = json.toString().toByteArray(Charsets.UTF_8)
        require(bytes.size <= 4096) { "Payload de habitación excede el límite seguro de 4 KB." }

        connectionsClient.sendPayload(endpointId, Payload.fromBytes(bytes))
            .addOnFailureListener { listener.onError("Fallo al enviar payload: ${it.message}") }
    }

    fun sendAck(endpointId: String, originalMessageId: String) {
        val json = JSONObject().apply {
            put("version", 1)
            put("type", "room-card-ack")
            put("originalMessageId", originalMessageId)
            put("status", "RECEIVED_OK")
        }
        val bytes = json.toString().toByteArray(Charsets.UTF_8)
        connectionsClient.sendPayload(endpointId, Payload.fromBytes(bytes))
    }

    fun stopAll() {
        connectionsClient.stopAdvertising()
        connectionsClient.stopDiscovery()
        connectionsClient.stopAllEndpoints()
    }
}
