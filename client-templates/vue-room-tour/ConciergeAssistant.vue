<script setup lang="ts">
import { ref, onMounted, onBeforeUnmount } from 'vue';

interface DemoCard {
  id: string;
  label: string;
  status: string;
  balance: number;
  currency: string;
}

interface PurchaseIntentResponse {
  id: string;
  status: 'Pending' | 'Reserving' | 'AwaitingConfirmation' | 'Confirmed' | 'Cancelling' | 'Cancelled' | 'Failed' | 'Expired';
  bookingId?: number;
  roomId?: number;
  roomTypeName?: string;
  hotelName?: string;
  stayTotal?: number;
  totalAmountPaid?: number;
  receiptVoucher?: string;
  failureReason?: string;
  updatedCardBalance?: number;
}

interface RoomOffer {
  roomId: number;
  hotelId: number;
  hotelName: string;
  roomTypeName: string;
  district: string;
  nightlyPrice: number;
  stayTotal: number;
  currency: string;
  capacity: number;
  tourPath: string | null;
  amenities: string[];
}

interface ConciergeResponse {
  assistantMessage: string;
  offers: RoomOffer[];
  fallbackUsed: boolean;
}

const STORAGE_KEY = 'smartstay_active_intent_id';

const now = new Date();
const tomorrow = new Date(now.getTime() + 24 * 60 * 60 * 1000);
const threeDaysLater = new Date(now.getTime() + 4 * 24 * 60 * 60 * 1000);

const checkIn = ref(tomorrow.toISOString().split('T')[0]);
const checkOut = ref(threeDaysLater.toISOString().split('T')[0]);
const guests = ref(2);
const district = ref('Cercado de Lima');
const budgetTotal = ref(500);
const userQuery = ref('');
const loadingQuery = ref(false);
const responseData = ref<ConciergeResponse | null>(null);

const demoCard = ref<DemoCard | null>(null);
const loadingCard = ref(false);
const activatingCard = ref(false);
const cancellationPolicyAccepted = ref(false);
const authorizing = ref(false);
const failureBannerDismissed = ref(false);

const activeIntent = ref<PurchaseIntentResponse | null>(null);
let pollInterval: any = null;

const emit = defineEmits<{
  (e: 'selectRoom', room: RoomOffer): void;
  (e: 'openTour', roomId: number): void;
}>();

function getAuthHeader(): Record<string, string> {
  const token = localStorage.getItem('token') || '';
  return token ? { 'Authorization': `Bearer ${token}` } : {};
}

async function fetchDemoCard() {
  loadingCard.value = true;
  try {
    const res = await fetch('/api/v1/payments/demo-method', {
      headers: { ...getAuthHeader() }
    });
    if (res.ok) {
      demoCard.value = await res.json();
    } else if (res.status === 404) {
      demoCard.value = null;
    }
  } catch (err) {
    console.error('Error fetching demo card:', err);
  } finally {
    loadingCard.value = false;
  }
}

async function activateDemoCard() {
  activatingCard.value = true;
  try {
    const res = await fetch('/api/v1/payments/demo-method', {
      method: 'POST',
      headers: { ...getAuthHeader(), 'Content-Type': 'application/json' }
    });
    if (res.ok) {
      demoCard.value = await res.json();
    } else {
      alert('No se pudo activar la tarjeta de prueba.');
    }
  } catch (err) {
    alert('Error al conectar con el servidor.');
  } finally {
    activatingCard.value = false;
  }
}

async function authorizeAutonomousPurchase() {
  if (!demoCard.value || demoCard.value.status !== 'Active' || !cancellationPolicyAccepted.value) return;

  authorizing.value = true;
  failureBannerDismissed.value = false;
  try {
    const idempotencyKey = crypto.randomUUID();
    const res = await fetch('/api/v1/concierge/purchase-intents', {
      method: 'POST',
      headers: {
        ...getAuthHeader(),
        'Content-Type': 'application/json',
        'Idempotency-Key': idempotencyKey
      },
      body: JSON.stringify({
        checkIn: checkIn.value,
        checkOut: checkOut.value,
        guests: guests.value,
        district: district.value,
        budgetTotal: budgetTotal.value,
        currency: 'USD',
        demoPaymentMethodId: demoCard.value.id,
        cancellationPolicyAccepted: cancellationPolicyAccepted.value,
        contactName: 'SmartStay Guest',
        contactEmail: 'guest1@example.com',
        contactPhone: '+51999999999'
      })
    });

    if (res.status === 202 || res.status === 200 || res.status === 201) {
      const data = await res.json();
      activeIntent.value = data;
      localStorage.setItem(STORAGE_KEY, data.id);
      startPolling(data.id);
    } else if (res.status === 422) {
      const err = await res.json();
      alert(`Tarjeta rechazada: ${err.reason || err.message}`);
    } else {
      const err = await res.json();
      alert(`Error en autorización: ${err.message || 'Intente nuevamente'}`);
    }
  } catch (err: any) {
    alert(`Error al procesar: ${err.message}`);
  } finally {
    authorizing.value = false;
  }
}

function startPolling(intentId: string) {
  stopPolling();
  pollInterval = setInterval(async () => {
    try {
      const res = await fetch(`/api/v1/concierge/purchase-intents/${intentId}`, {
        headers: { ...getAuthHeader() }
      });
      if (res.ok) {
        const updated: PurchaseIntentResponse = await res.json();
        activeIntent.value = updated;

        if (updated.status === 'Confirmed') {
          stopPolling();
          await fetchDemoCard(); // Actualizar saldo disponible
        } else if (updated.status === 'Cancelled') {
          stopPolling();
          await fetchDemoCard(); // Actualizar saldo restaurado
        } else if (updated.status === 'Failed' || updated.status === 'Expired') {
          stopPolling();
          localStorage.removeItem(STORAGE_KEY);
        }
      }
    } catch (e) {
      console.error('Polling error:', e);
    }
  }, 2000);
}

function stopPolling() {
  if (pollInterval) {
    clearInterval(pollInterval);
    pollInterval = null;
  }
}

async function cancelActiveIntent() {
  if (!activeIntent.value) return;
  try {
    const res = await fetch(`/api/v1/concierge/purchase-intents/${activeIntent.value.id}/cancel`, {
      method: 'POST',
      headers: { ...getAuthHeader() }
    });
    if (res.ok) {
      activeIntent.value.status = 'Cancelling';
      startPolling(activeIntent.value.id);
    }
  } catch (err) {
    alert('Error al solicitar cancelación.');
  }
}

function clearReceiptAndReset() {
  localStorage.removeItem(STORAGE_KEY);
  activeIntent.value = null;
  failureBannerDismissed.value = true;
}

async function consultConcierge() {
  loadingQuery.value = true;
  try {
    const res = await fetch('/api/v1/concierge/messages', {
      method: 'POST',
      headers: { ...getAuthHeader(), 'Content-Type': 'application/json' },
      body: JSON.stringify({
        message: userQuery.value || 'Recomiéndame las mejores opciones para mi estadía.',
        checkIn: checkIn.value,
        checkOut: checkOut.value,
        guests: guests.value,
        district: district.value,
        budgetTotal: budgetTotal.value,
        currency: 'USD'
      })
    });

    if (!res.ok) throw new Error('Error al conectar con Concierge.');
    responseData.value = await res.json();
  } catch (err: any) {
    alert(err.message || 'Error en la consulta');
  } finally {
    loadingQuery.value = false;
  }
}

onMounted(async () => {
  await fetchDemoCard();
  const savedIntentId = localStorage.getItem(STORAGE_KEY);
  if (savedIntentId) {
    try {
      const res = await fetch(`/api/v1/concierge/purchase-intents/${savedIntentId}`, {
        headers: { ...getAuthHeader() }
      });
      if (res.ok) {
        const data = await res.json();
        activeIntent.value = data;
        if (data.status !== 'Confirmed' && data.status !== 'Failed' && data.status !== 'Expired' && data.status !== 'Cancelled') {
          startPolling(savedIntentId);
        } else if (data.status === 'Confirmed') {
          await fetchDemoCard();
        }
      } else {
        localStorage.removeItem(STORAGE_KEY);
      }
    } catch (e) {
      console.error('Error recovering saved intent:', e);
    }
  }
});

onBeforeUnmount(() => {
  stopPolling();
});
</script>

<template>
  <div class="concierge-card">
    <div class="header">
      <h3>🛎️ Concierge Autónomo SmartStay</h3>
      <p>Orquestación autónoma de reservas con tarjeta ficticia y tolerancia a fallos.</p>
    </div>

    <!-- WIDGET DE TARJETA FICTICIA -->
    <div class="demo-card-widget">
      <div v-if="demoCard" class="card-box active">
        <div class="card-top">
          <span class="badge">{{ demoCard.label }}</span>
          <span class="status-indicator">● {{ demoCard.status }}</span>
        </div>
        <div class="card-id">ID: <code>{{ demoCard.id }}</code></div>
        <div class="card-balance">
          Saldo Disponible: <strong>${{ demoCard.balance.toLocaleString('en-US', { minimumFractionDigits: 2 }) }} {{ demoCard.currency }}</strong>
        </div>
      </div>
      <div v-else class="card-box empty">
        <div class="empty-msg">
          <p>⚠️ No tienes una tarjeta de prueba activa.</p>
          <small>Para autorizar compras autónomas necesitas activar tu tarjeta SmartStay Demo.</small>
        </div>
        <button class="activate-btn" :disabled="activatingCard" @click="activateDemoCard">
          {{ activatingCard ? 'Activando...' : '💳 Activar tarjeta de prueba' }}
        </button>
      </div>
    </div>

    <!-- FORMULARIO DE MANDATO Y AUTORIZACIÓN -->
    <div v-if="!activeIntent || activeIntent.status === 'Failed' || activeIntent.status === 'Expired'" class="search-form">
      <h4>Configuración del Mandato de Compra</h4>
      <div class="row">
        <label>Distrito:
          <select v-model="district">
            <option value="Cercado de Lima">Cercado de Lima</option>
            <option value="San Blas">San Blas (Cusco)</option>
          </select>
        </label>
        <label>Huéspedes:
          <input type="number" v-model.number="guests" min="1" max="6" />
        </label>
      </div>

      <div class="row">
        <label>Check-In:
          <input type="date" v-model="checkIn" />
        </label>
        <label>Check-Out:
          <input type="date" v-model="checkOut" />
        </label>
      </div>

      <div class="row">
        <label>Presupuesto Total Máximo ($ USD):
          <input type="number" v-model.number="budgetTotal" min="50" step="10" />
        </label>
      </div>

      <!-- TÉRMINOS Y POLÍTICA DE CANCELACIÓN -->
      <div class="terms-box">
        <p><strong>Condiciones del Mandato:</strong></p>
        <ul>
          <li>Vigencia del mandato: 15 minutos de orquestación autónoma.</li>
          <li>Criterio de selección: Menor precio disponible con desempate por RoomId.</li>
          <li>Pago ficticio: Se debitará únicamente de tu tarjeta SmartStay Demo.</li>
        </ul>
        <label class="checkbox-label">
          <input type="checkbox" v-model="cancellationPolicyAccepted" />
          <span>He leído y acepto la política de cancelación (reembolso del 100% en caso de cancelación de reserva pagada).</span>
        </label>
      </div>

      <!-- BOTÓN AUTORIZAR COMPRA FICTICIA -->
      <div class="action-box">
        <button
          class="authorize-btn"
          :disabled="!demoCard || demoCard.status !== 'Active' || !cancellationPolicyAccepted || authorizing"
          @click="authorizeAutonomousPurchase"
        >
          {{ authorizing ? 'Autorizando compra...' : '⚡ Autorizar compra ficticia' }}
        </button>
      </div>

      <!-- ORIENTACIÓN CONVERSACIONAL (OPCIONAL) -->
      <div class="query-box">
        <input
          v-model="userQuery"
          type="text"
          placeholder="¿Deseas recomendaciones adicionales? Ej. Balcón, vista panorámica..."
          @keyup.enter="consultConcierge"
        />
        <button :disabled="loadingQuery" @click="consultConcierge">
          {{ loadingQuery ? 'Consultando...' : 'Preguntar al Asistente' }}
        </button>
      </div>
    </div>

    <!-- VISTA DE PROGRESO Y SEGUIMIENTO -->
    <div v-if="activeIntent && ['Pending', 'Reserving', 'AwaitingConfirmation', 'Cancelling'].includes(activeIntent.status)" class="progress-box">
      <h4>Orquestando Compra Autónoma...</h4>
      <div class="status-banner">
        <div class="spinner"></div>
        <div>
          <strong>Estado Actual:</strong> <span class="badge-status">{{ activeIntent.status }}</span>
          <p v-if="activeIntent.status === 'Pending'">Evaluando candidatos y solicitando cotización de 2 minutos...</p>
          <p v-if="activeIntent.status === 'Reserving'">Bloqueando noches en Bookings con cerrojo de 15 minutos...</p>
          <p v-if="activeIntent.status === 'AwaitingConfirmation'">Procesando débito atómico en Payments...</p>
          <p v-if="activeIntent.status === 'Cancelling'">Procesando reembolso y liberación de noches...</p>
        </div>
      </div>
      <button class="cancel-intent-btn" @click="cancelActiveIntent">Cancelar Compra</button>
    </div>

    <!-- COMPROBANTE DE COMPRA CONFIRMADA -->
    <div v-if="activeIntent && activeIntent.status === 'Confirmed'" class="receipt-card">
      <div class="receipt-header">
        <h3>🎉 ¡Compra Autónoma Confirmada!</h3>
        <p>Tu reserva ha sido asegurada y pagada exitosamente.</p>
      </div>
      <div class="receipt-body">
        <div class="receipt-row">
          <span>Comprobante:</span>
          <strong><code>{{ activeIntent.receiptVoucher }}</code></strong>
        </div>
        <div class="receipt-row">
          <span>Habitación Reservada:</span>
          <strong>Habitación #{{ activeIntent.roomId }} ({{ activeIntent.roomTypeName || 'Estándar' }})</strong>
        </div>
        <div class="receipt-row">
          <span>Total Pagado:</span>
          <strong class="paid-amount">${{ activeIntent.totalAmountPaid ?? activeIntent.stayTotal }} USD</strong>
        </div>
        <div class="receipt-row">
          <span>Nuevo Saldo SmartStay Demo:</span>
          <strong class="balance-amount">${{ (demoCard?.balance ?? activeIntent.updatedCardBalance ?? 0).toLocaleString('en-US', { minimumFractionDigits: 2 }) }} USD</strong>
        </div>
      </div>
      <div class="receipt-actions">
        <button class="cancel-booking-btn" @click="cancelActiveIntent">Solicitar Cancelación (Reembolso 100%)</button>
        <button class="new-search-btn" @click="clearReceiptAndReset">Nueva Búsqueda</button>
      </div>
    </div>

    <!-- BANNER DE FALLO / EXPIRACIÓN -->
    <div v-if="activeIntent && (activeIntent.status === 'Failed' || activeIntent.status === 'Expired') && !failureBannerDismissed" class="failure-box">
      <h4>❌ No se pudo completar la compra autónoma</h4>
      <p><strong>Motivo:</strong> {{ activeIntent.failureReason || 'El mandato no pudo resolverse dentro de los límites fijados.' }}</p>
      <div class="guarantee-note">
        🛡️ <strong>Garantía SmartStay:</strong> Tu saldo de tarjeta de prueba no fue debitado y no existen noches bloqueadas.
      </div>
      <button class="reset-btn" @click="clearReceiptAndReset">Volver a intentar</button>
    </div>

    <!-- BANNER DE CANCELACIÓN EXITOSA -->
    <div v-if="activeIntent && activeIntent.status === 'Cancelled'" class="cancelled-box">
      <h4>ℹ️ Compra Cancelada</h4>
      <p>La reserva fue cancelada y el importe fue reembolsado íntegramente a tu tarjeta SmartStay Demo.</p>
      <button class="reset-btn" @click="clearReceiptAndReset">Aceptar</button>
    </div>

    <!-- RESULTADOS DEL ASISTENTE CONVERSACIONAL (SI SE CONSULTÓ) -->
    <div v-if="responseData" class="results-container">
      <div class="assistant-bubble" :class="{ fallback: responseData.fallbackUsed }">
        <strong>Asistente SmartStay:</strong>
        <p>{{ responseData.assistantMessage }}</p>
      </div>

      <div class="offers-grid">
        <div v-for="offer in responseData.offers" :key="offer.roomId" class="offer-card">
          <div class="offer-header">
            <h4>{{ offer.hotelName }}</h4>
            <span class="room-type">{{ offer.roomTypeName }}</span>
          </div>

          <div class="offer-details">
            <p><strong>Ubicación:</strong> {{ offer.district }}</p>
            <p><strong>Capacidad:</strong> {{ offer.capacity }} personas</p>
            <div class="price-box">
              <span class="nightly">${{ offer.nightlyPrice }} / noche</span>
              <span class="total">Total: ${{ offer.stayTotal }} {{ offer.currency }}</span>
            </div>
          </div>

          <div class="amenities">
            <span v-for="a in offer.amenities" :key="a" class="tag">{{ a }}</span>
          </div>

          <div class="actions">
            <button v-if="offer.tourPath" class="tour-btn" @click="emit('openTour', offer.roomId)">
              🌐 Ver en 360°
            </button>
            <button class="reserve-btn" @click="emit('selectRoom', offer)">
              Ver Habitación
            </button>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.concierge-card {
  border: 1px solid #e0e0e0;
  border-radius: 12px;
  padding: 24px;
  background: #ffffff;
  max-width: 800px;
  margin: 0 auto;
}
.header h3 { margin: 0 0 4px; color: #2c3e50; }
.header p { margin: 0 0 18px; color: #7f8c8d; font-size: 14px; }

/* Demo Card */
.demo-card-widget { margin-bottom: 20px; }
.card-box { padding: 16px; border-radius: 10px; }
.card-box.active { background: linear-gradient(135deg, #1e3c72, #2a5298); color: #fff; }
.card-box.empty { background: #fff3cd; border: 1px solid #ffeeba; display: flex; justify-content: space-between; align-items: center; }
.card-top { display: flex; justify-content: space-between; align-items: center; margin-bottom: 8px; }
.badge { background: rgba(255, 255, 255, 0.2); padding: 4px 10px; border-radius: 16px; font-weight: 600; font-size: 13px; }
.status-indicator { font-size: 13px; color: #4ade80; }
.card-id { font-size: 13px; opacity: 0.9; margin-bottom: 6px; }
.card-balance { font-size: 16px; }
.activate-btn { background: #27ae60; color: #fff; border: none; padding: 10px 16px; border-radius: 6px; cursor: pointer; font-weight: 600; }
.activate-btn:disabled { opacity: 0.6; cursor: not-allowed; }

/* Search Form */
.search-form { background: #f8f9fa; padding: 18px; border-radius: 8px; margin-bottom: 20px; }
.row { display: flex; gap: 16px; margin-bottom: 12px; }
.row label { flex: 1; display: flex; flex-direction: column; font-size: 13px; font-weight: 500; color: #34495e; }
.row input, .row select { margin-top: 4px; padding: 8px 12px; border: 1px solid #ccc; border-radius: 6px; font-size: 14px; }
.terms-box { background: #ffffff; border: 1px solid #e2e8f0; border-radius: 6px; padding: 12px 16px; margin: 14px 0; font-size: 13px; color: #475569; }
.terms-box ul { margin: 6px 0 10px 20px; padding: 0; }
.checkbox-label { display: flex; align-items: center; gap: 8px; font-weight: 600; color: #1e293b; cursor: pointer; }
.action-box { margin-bottom: 14px; }
.authorize-btn { width: 100%; background: #2563eb; color: #fff; border: none; padding: 12px; border-radius: 6px; cursor: pointer; font-size: 15px; font-weight: bold; }
.authorize-btn:disabled { background: #94a3b8; cursor: not-allowed; }

.query-box { display: flex; gap: 12px; margin-top: 14px; }
.query-box input { flex: 1; padding: 10px 14px; border: 1px solid #cbd5e1; border-radius: 6px; }
.query-box button { background: #475569; color: #fff; border: none; padding: 10px 18px; border-radius: 6px; cursor: pointer; font-weight: 600; }

/* Progress */
.progress-box { background: #eff6ff; border: 1px solid #bfdbfe; border-radius: 8px; padding: 20px; text-align: center; }
.status-banner { display: flex; align-items: center; justify-content: center; gap: 16px; margin: 16px 0; }
.spinner { width: 32px; height: 32px; border: 4px solid #bfdbfe; border-top-color: #2563eb; border-radius: 50%; animation: spin 1s infinite linear; }
@keyframes spin { to { transform: rotate(360deg); } }
.badge-status { background: #2563eb; color: #fff; padding: 2px 8px; border-radius: 4px; font-size: 13px; }
.cancel-intent-btn { background: #ef4444; color: #fff; border: none; padding: 8px 16px; border-radius: 6px; cursor: pointer; font-size: 13px; }

/* Receipt */
.receipt-card { background: #f0fdf4; border: 2px solid #86efac; border-radius: 10px; padding: 24px; }
.receipt-header h3 { margin: 0; color: #166534; }
.receipt-header p { margin: 4px 0 16px; color: #15803d; font-size: 14px; }
.receipt-body { background: #fff; border: 1px solid #dcfce7; border-radius: 6px; padding: 16px; margin-bottom: 16px; }
.receipt-row { display: flex; justify-content: space-between; padding: 8px 0; border-bottom: 1px solid #f1f5f9; font-size: 14px; }
.paid-amount { color: #16a34a; font-size: 16px; }
.balance-amount { color: #0284c7; }
.receipt-actions { display: flex; gap: 12px; }
.cancel-booking-btn { background: #f87171; color: #fff; border: none; padding: 10px; border-radius: 6px; cursor: pointer; font-size: 13px; flex: 1; }
.new-search-btn { background: #3b82f6; color: #fff; border: none; padding: 10px; border-radius: 6px; cursor: pointer; font-size: 13px; flex: 1; }

/* Failure & Cancelled */
.failure-box { background: #fef2f2; border: 1px solid #fecaca; border-radius: 8px; padding: 18px; margin-top: 14px; color: #991b1b; }
.guarantee-note { margin: 12px 0; font-size: 13px; color: #065f46; background: #ecfdf5; padding: 8px 12px; border-radius: 6px; }
.cancelled-box { background: #f1f5f9; border: 1px solid #cbd5e1; border-radius: 8px; padding: 18px; margin-top: 14px; color: #334155; }
.reset-btn { background: #475569; color: #fff; border: none; padding: 8px 16px; border-radius: 6px; cursor: pointer; margin-top: 8px; }

/* Results */
.results-container { margin-top: 20px; }
.assistant-bubble { background: #e8f4fd; border-left: 4px solid #3498db; padding: 14px 18px; border-radius: 6px; margin-bottom: 18px; }
.assistant-bubble.fallback { background: #fef9e7; border-left-color: #f39c12; }
.offers-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(240px, 1fr)); gap: 16px; }
.offer-card { border: 1px solid #eaeaea; border-radius: 8px; padding: 16px; background: #fff; display: flex; flex-direction: column; }
.offer-header h4 { margin: 0; color: #2c3e50; font-size: 16px; }
.room-type { font-size: 13px; color: #7f8c8d; }
.price-box { margin-top: 8px; background: #f1f8ff; padding: 8px; border-radius: 6px; display: flex; justify-content: space-between; align-items: baseline; }
.price-box .total { font-weight: bold; color: #27ae60; font-size: 15px; }
.amenities { display: flex; flex-wrap: wrap; gap: 6px; margin: 12px 0; }
.tag { background: #eee; font-size: 11px; padding: 2px 8px; border-radius: 12px; color: #555; }
.actions { margin-top: auto; display: flex; flex-direction: column; gap: 8px; }
.tour-btn { background: #8e44ad; color: #fff; border: none; padding: 8px; border-radius: 6px; cursor: pointer; font-weight: 500; font-size: 13px; }
.reserve-btn { background: #27ae60; color: #fff; border: none; padding: 8px; border-radius: 6px; cursor: pointer; font-weight: 600; font-size: 14px; }
</style>
