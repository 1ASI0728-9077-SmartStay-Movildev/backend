package pe.smartstay.features.nearbyshare.presentation

import kotlinx.coroutines.*
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import pe.smartstay.features.nearbyshare.application.ShareRoomUseCase
import pe.smartstay.features.nearbyshare.data.NearbyRoomTransport
import pe.smartstay.features.nearbyshare.data.NearbyTransportListener
import pe.smartstay.features.nearbyshare.domain.ShareRoomAck
import pe.smartstay.features.nearbyshare.domain.ShareRoomCard

/**
 * Representa los estados reactivos del flujo P2P Nearby Connections.
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
    data class Received(val card: ShareRoomCard, val isPendingOnlineSync: Boolean = true) : NearbyShareUiState()
    data class Error(val message: String) : NearbyShareUiState()
}

data class DiscoveredDevice(val endpointId: String, val name: String)

/**
 * ViewModel reactivo para orquestar la compartición P2P offline con timeout de 30s
 * y revalidación online al reconectar.
 */
class NearbyShareViewModel(
    private val transportFactory: (NearbyTransportListener) -> NearbyRoomTransport,
    private val useCase: ShareRoomUseCase = ShareRoomUseCase(),
    private val coroutineScope: CoroutineScope = CoroutineScope(Dispatchers.Main + SupervisorJob())
) : NearbyTransportListener {

    private var transport: NearbyRoomTransport? = null
    private val _uiState = MutableStateFlow<NearbyShareUiState>(NearbyShareUiState.Idle)
    val uiState: StateFlow<NearbyShareUiState> = _uiState.asStateFlow()

    private var currentCardToSend: ShareRoomCard? = null
    private var activeEndpointId: String? = null
    private val discoveredDevices = mutableListOf<DiscoveredDevice>()
    private var timeoutJob: Job? = null

    init {
        transport = transportFactory(this)
    }

    private fun startTimeoutTimer(timeoutMs: Long = 30_000L, operationDescription: String) {
        timeoutJob?.cancel()
        timeoutJob = coroutineScope.launch {
            delay(timeoutMs)
            if (_uiState.value !is NearbyShareUiState.Delivered && _uiState.value !is NearbyShareUiState.Received) {
                transport?.stopAll()
                _uiState.value = NearbyShareUiState.Error("Tiempo de espera agotado ($operationDescription). Operación cancelada.")
            }
        }
    }

    private fun cancelTimeout() {
        timeoutJob?.cancel()
        timeoutJob = null
    }

    /**
     * Inicia modo emisor (Sender): busca dispositivos cercanos para enviar una habitación.
     */
    fun startSharing(card: ShareRoomCard) {
        currentCardToSend = card
        discoveredDevices.clear()
        _uiState.value = NearbyShareUiState.Discovering(emptyList())
        transport?.startDiscovery()
        startTimeoutTimer(30_000L, "descubrimiento de dispositivos")
    }

    /**
     * Inicia modo receptor (Receiver): se hace visible a los emisores cercanos.
     */
    fun startReceiving(deviceName: String = "SmartStay Guest") {
        currentCardToSend = null
        _uiState.value = NearbyShareUiState.Advertising
        transport?.startAdvertising(deviceName)
        startTimeoutTimer(30_000L, "espera de conexión entrante")
    }

    /**
     * El usuario selecciona un dispositivo encontrado para solicitar conexión.
     */
    fun selectDeviceToConnect(endpointId: String, localName: String = "SmartStay User") {
        activeEndpointId = endpointId
        transport?.requestConnection(endpointId, localName)
        startTimeoutTimer(30_000L, "confirmación de conexión")
    }

    /**
     * Confirma la igualdad del PIN bilateral (authenticationDigits) en ambos dispositivos.
     */
    fun confirmVerification() {
        activeEndpointId?.let { endpointId ->
            transport?.acceptConnection(endpointId)
            startTimeoutTimer(30_000L, "transferencia de datos")
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
        cancelTimeout()
        transport?.stopAll()
        activeEndpointId = null
        currentCardToSend = null
        discoveredDevices.clear()
        _uiState.value = NearbyShareUiState.Idle
    }

    // Callbacks de NearbyTransportListener

    override fun onDeviceDiscovered(endpointId: String, deviceName: String) {
        if (discoveredDevices.none { it.endpointId == endpointId }) {
            discoveredDevices.add(DiscoveredDevice(endpointId, deviceName))
            _uiState.value = NearbyShareUiState.Discovering(discoveredDevices.toList())
        }
    }

    override fun onConnectionInitiated(endpointId: String, authDigits: String) {
        activeEndpointId = endpointId
        _uiState.value = NearbyShareUiState.AwaitingVerification(
            endpointId = endpointId,
            authDigits = authDigits,
            isSender = currentCardToSend != null
        )
    }

    override fun onConnected(endpointId: String) {
        activeEndpointId = endpointId
        _uiState.value = NearbyShareUiState.Connected(endpointId)

        currentCardToSend?.let { card ->
            _uiState.value = NearbyShareUiState.Sending
            transport?.sendCard(endpointId, card)
        }
    }

    override fun onCardReceived(endpointId: String, card: ShareRoomCard) {
        cancelTimeout()

        // Validar integridad de dominio antes de emitir ACK de aplicación
        useCase.validateReceivedCard(card).fold(
            onSuccess = {
                // Solo enviar ACK tras validación exitosa
                transport?.sendAck(endpointId, card.messageId)
                _uiState.value = NearbyShareUiState.Received(card, isPendingOnlineSync = true)
                transport?.stopAll()
            },
            onFailure = { error ->
                _uiState.value = NearbyShareUiState.Error("Ficha recibida corrupta o inválida: ${error.message}")
            }
        )
    }

    override fun onAckReceived(ack: ShareRoomAck) {
        cancelTimeout()
        val sentCard = currentCardToSend
        if (sentCard != null && useCase.validateAck(sentCard, ack)) {
            _uiState.value = NearbyShareUiState.Delivered(sentCard)
            transport?.stopAll()
        }
    }

    override fun onDisconnected(endpointId: String) {
        cancelTimeout()
        if (_uiState.value !is NearbyShareUiState.Delivered && _uiState.value !is NearbyShareUiState.Received) {
            _uiState.value = NearbyShareUiState.Error("Conexión interrumpida con el dispositivo.")
        }
    }

    override fun onError(message: String) {
        cancelTimeout()
        _uiState.value = NearbyShareUiState.Error(message)
    }

    /**
     * Sincronización online al recuperar conectividad a Internet.
     * Consulta Accommodations (tarifa actualizada) y Bookings (disponibilidad temporal).
     */
    fun revalidateOnline(card: ShareRoomCard, onSyncCompleted: (isAvailable: Boolean, updatedPrice: Double) -> Unit) {
        // En implementación real de Android: Retrofit / Ktor Client contra Gateway:
        // 1. GET /api/v1/rooms/{card.roomId}
        // 2. POST /internal/v1/availability/check
        // Actualiza el flag isPendingOnlineSync a false
    }
}
