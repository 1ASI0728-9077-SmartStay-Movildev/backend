package pe.smartstay.features.nearbyshare.presentation

import pe.smartstay.features.nearbyshare.application.ShareRoomUseCase
import pe.smartstay.features.nearbyshare.data.NearbyRoomTransport
import pe.smartstay.features.nearbyshare.data.NearbyTransportListener
import pe.smartstay.features.nearbyshare.domain.ShareRoomAck
import pe.smartstay.features.nearbyshare.domain.ShareRoomCard

/**
 * Representa los estados del flujo de compartición P2P mediante Google Nearby Connections.
 */
sealed class NearbyShareUiState {
    object Idle : NearbyShareUiState()
    object Advertising : NearbyShareUiState()
    data class Discovering(val devices: List<DiscoveredDevice>) : NearbyShareUiState()
    data class AwaitingVerification(
        val endpointId: String,
        val authDigits: String,
        val isSender: Boolean
    ) : NearbyShareUiState()
    data class Connected(val endpointId: String) : NearbyShareUiState()
    object Sending : NearbyShareUiState()
    data class Delivered(val card: ShareRoomCard) : NearbyShareUiState()
    data class Received(val card: ShareRoomCard) : NearbyShareUiState()
    data class Error(val message: String) : NearbyShareUiState()
}

data class DiscoveredDevice(val endpointId: String, val name: String)

/**
 * ViewModel / Presenter para orquestar el flujo UI de Nearby Share 1:1.
 */
class NearbyShareViewModel(
    private val transportFactory: (NearbyTransportListener) -> NearbyRoomTransport,
    private val useCase: ShareRoomUseCase = ShareRoomUseCase()
) : NearbyTransportListener {

    private var transport: NearbyRoomTransport? = null
    var uiState: NearbyShareUiState = NearbyShareUiState.Idle
        private set

    private var currentCardToSend: ShareRoomCard? = null
    private var activeEndpointId: String? = null
    private val discoveredDevices = mutableListOf<DiscoveredDevice>()

    init {
        transport = transportFactory(this)
    }

    /**
     * Inicia modo emisor (Sender): busca dispositivos cercanos para enviar una habitación.
     */
    fun startSharing(card: ShareRoomCard) {
        currentCardToSend = card
        discoveredDevices.clear()
        uiState = NearbyShareUiState.Discovering(emptyList())
        transport?.startDiscovery()
    }

    /**
     * Inicia modo receptor (Receiver): se hace visible a los emisores cercanos.
     */
    fun startReceiving(deviceName: String = "SmartStay Guest") {
        currentCardToSend = null
        uiState = NearbyShareUiState.Advertising
        transport?.startAdvertising(deviceName)
    }

    /**
     * El usuario selecciona un dispositivo encontrado para solicitar conexión.
     */
    fun selectDeviceToConnect(endpointId: String, localName: String = "SmartStay User") {
        activeEndpointId = endpointId
        transport?.requestConnection(endpointId, localName)
    }

    /**
     * Confirma la igualdad del PIN bilateral (authenticationDigits) en ambos dispositivos.
     */
    fun confirmVerification() {
        activeEndpointId?.let { endpointId ->
            transport?.acceptConnection(endpointId)
        }
    }

    /**
     * Rechaza la solicitud de conexión.
     */
    fun rejectVerification() {
        activeEndpointId?.let { endpointId ->
            transport?.rejectConnection(endpointId)
            cancel()
        }
    }

    fun cancel() {
        transport?.stopAll()
        activeEndpointId = null
        currentCardToSend = null
        discoveredDevices.clear()
        uiState = NearbyShareUiState.Idle
    }

    // Callbacks de NearbyTransportListener

    override fun onDeviceDiscovered(endpointId: String, deviceName: String) {
        if (discoveredDevices.none { it.endpointId == endpointId }) {
            discoveredDevices.add(DiscoveredDevice(endpointId, deviceName))
            uiState = NearbyShareUiState.Discovering(discoveredDevices.toList())
        }
    }

    override fun onConnectionInitiated(endpointId: String, authDigits: String) {
        activeEndpointId = endpointId
        uiState = NearbyShareUiState.AwaitingVerification(
            endpointId = endpointId,
            authDigits = authDigits,
            isSender = currentCardToSend != null
        )
    }

    override fun onConnected(endpointId: String) {
        activeEndpointId = endpointId
        uiState = NearbyShareUiState.Connected(endpointId)

        // Si es el emisor, envía el payload automáticamente al conectar
        currentCardToSend?.let { card ->
            uiState = NearbyShareUiState.Sending
            transport?.sendCard(endpointId, card)
        }
    }

    override fun onCardReceived(card: ShareRoomCard) {
        useCase.validateReceivedCard(card).fold(
            onSuccess = {
                uiState = NearbyShareUiState.Received(card)
            },
            onFailure = { error ->
                uiState = NearbyShareUiState.Error("Ficha recibida corrupta o inválida: ${error.message}")
            }
        )
    }

    override fun onAckReceived(ack: ShareRoomAck) {
        val sentCard = currentCardToSend
        if (sentCard != null && useCase.validateAck(sentCard, ack)) {
            uiState = NearbyShareUiState.Delivered(sentCard)
            // Detener radios para evitar consumo de batería tras entrega exitosa
            transport?.stopAll()
        }
    }

    override fun onDisconnected(endpointId: String) {
        if (uiState !is NearbyShareUiState.Delivered && uiState !is NearbyShareUiState.Received) {
            uiState = NearbyShareUiState.Error("Conexión interrumpida con el dispositivo.")
        }
    }

    override fun onError(message: String) {
        uiState = NearbyShareUiState.Error(message)
    }
}
