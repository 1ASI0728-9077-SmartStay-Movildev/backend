<script setup lang="ts">
import { ref } from 'vue';

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

const checkIn = ref('2026-11-01');
const checkOut = ref('2026-11-04');
const guests = ref(2);
const district = ref('Cercado de Lima');
const budgetTotal = ref(500);
const userQuery = ref('');
const loading = ref(false);
const responseData = ref<ConciergeResponse | null>(null);

const emit = defineEmits<{
  (e: 'selectRoom', room: RoomOffer): void;
  (e: 'openTour', roomId: number): void;
}>();

async function consultConcierge() {
  loading.value = true;
  try {
    const res = await fetch('/api/v1/concierge/messages', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
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
    loading.value = false;
  }
}
</script>

<template>
  <div class="concierge-card">
    <div class="header">
      <h3>🛎️ Concierge Inteligente SmartStay</h3>
      <p>Orientación con Inteligencia Artificial acotada al catálogo verificado.</p>
    </div>

    <div class="search-form">
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
        <label>Presupuesto Total ($ USD):
          <input type="number" v-model.number="budgetTotal" min="50" step="10" />
        </label>
      </div>

      <div class="query-box">
        <input
          v-model="userQuery"
          type="text"
          placeholder="¿Qué buscas? Ej. Vista panorámica, balcón o la opción más económica..."
          @keyup.enter="consultConcierge"
        />
        <button :disabled="loading" @click="consultConcierge">
          {{ loading ? 'Consultando...' : 'Preguntar al Concierge' }}
        </button>
      </div>
    </div>

    <!-- Resultados del Asistente -->
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
              Reservar Habitación
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
.search-form { background: #f8f9fa; padding: 16px; border-radius: 8px; margin-bottom: 20px; }
.row { display: flex; gap: 16px; margin-bottom: 12px; }
.row label { flex: 1; display: flex; flex-direction: column; font-size: 13px; font-weight: 500; color: #34495e; }
.row input, .row select { margin-top: 4px; padding: 8px 12px; border: 1px solid #ccc; border-radius: 6px; font-size: 14px; }
.query-box { display: flex; gap: 12px; margin-top: 14px; }
.query-box input { flex: 1; padding: 10px 14px; border: 1px solid #3498db; border-radius: 6px; }
.query-box button { background: #3498db; color: #fff; border: none; padding: 10px 18px; border-radius: 6px; cursor: pointer; font-weight: 600; }
.query-box button:hover { background: #2980b9; }
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
