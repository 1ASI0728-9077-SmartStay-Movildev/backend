<script setup lang="ts">
import { onMounted, onBeforeUnmount, ref } from 'vue';
import { Viewer } from '@photo-sphere-viewer/core';
import { VirtualTourPlugin } from '@photo-sphere-viewer/virtual-tour-plugin';
import '@photo-sphere-viewer/core/index.css';
import '@photo-sphere-viewer/virtual-tour-plugin/index.css';

interface TourLink {
  nodeId: string;
  yaw: number;
  pitch: number;
}

interface TourNode {
  id: string;
  panorama: string;
  name: string;
  links: TourLink[];
}

interface RoomTourManifest {
  roomId: number;
  startNodeId: string;
  nodes: TourNode[];
}

const props = defineProps<{
  roomId: number;
}>();

const emit = defineEmits<{
  (e: 'close'): void;
}>();

const host = ref<HTMLDivElement>();
const loading = ref(true);
const errorMessage = ref<string | null>(null);
let viewer: Viewer | undefined;

async function loadTour() {
  if (!host.value) return;

  try {
    loading.value = true;
    errorMessage.value = null;

    if (viewer) {
      viewer.destroy();
      viewer = undefined;
    }

    const response = await fetch(`/api/v1/rooms/${props.roomId}/tour`);
    if (!response.ok) {
      throw new Error(`El recorrido virtual no está disponible para esta habitación (HTTP ${response.status}).`);
    }

    const manifest: RoomTourManifest = await response.json();

    const formattedNodes = manifest.nodes.map(n => ({
      id: n.id,
      panorama: n.panorama,
      name: n.name,
      links: n.links.map(l => ({
        nodeId: l.nodeId,
        position: { yaw: l.yaw, pitch: l.pitch }
      }))
    }));

    viewer = new Viewer({
      container: host.value,
      plugins: [
        [VirtualTourPlugin, {
          positionMode: 'manual',
          renderMode: '3d',
          startNodeId: manifest.startNodeId,
          nodes: formattedNodes
        }]
      ]
    });

    // Capturar fallos asíncronos de renderizado y carga de texturas panorámicas
    viewer.addEventListener('panorama-load-error', (e: any) => {
      errorMessage.value = 'Fallo al descargar o renderizar la textura panorámica equirectangular.';
      loading.value = false;
    });

    viewer.addEventListener('ready', () => {
      loading.value = false;
    });

  } catch (err: any) {
    errorMessage.value = err.message || 'No se pudo inicializar el recorrido 360°.';
    loading.value = false;
  }
}

onMounted(() => {
  loadTour();
});

onBeforeUnmount(() => {
  viewer?.destroy();
  viewer = undefined;
});
</script>

<template>
  <div class="tour-container">
    <div class="tour-header">
      <span class="badge">Recorrido Virtual 360° Demostrativo</span>
      <button class="close-btn" @click="emit('close')">&times; Cerrar</button>
    </div>

    <div v-if="loading" class="status-overlay">
      <div class="spinner"></div>
      <p>Cargando panorama 360°...</p>
    </div>

    <div v-else-if="errorMessage" class="status-overlay error">
      <p>{{ errorMessage }}</p>
      <div class="error-actions">
        <button class="retry-btn" @click="loadTour">Reintentar</button>
        <button class="secondary-btn" @click="emit('close')">Volver a fotos estándar</button>
      </div>
    </div>

    <div ref="host" class="viewer-host"></div>
  </div>
</template>

<style scoped>
.tour-container {
  position: relative;
  width: 100%;
  height: 550px;
  border-radius: 12px;
  overflow: hidden;
  box-shadow: 0 4px 20px rgba(0, 0, 0, 0.15);
  background: #111;
}

.tour-header {
  position: absolute;
  top: 12px;
  left: 12px;
  right: 12px;
  display: flex;
  justify-content: space-between;
  align-items: center;
  z-index: 10;
  pointer-events: none;
}

.badge {
  background: rgba(0, 0, 0, 0.7);
  color: #fff;
  padding: 6px 12px;
  border-radius: 20px;
  font-size: 0.85rem;
  font-weight: 600;
  pointer-events: auto;
}

.close-btn {
  background: rgba(0, 0, 0, 0.7);
  color: #fff;
  border: 1px solid rgba(255, 255, 255, 0.3);
  padding: 6px 14px;
  border-radius: 20px;
  font-size: 0.9rem;
  cursor: pointer;
  pointer-events: auto;
  transition: background 0.2s;
}

.close-btn:hover {
  background: rgba(220, 38, 38, 0.8);
}

.viewer-host {
  width: 100%;
  height: 100%;
}

.status-overlay {
  position: absolute;
  inset: 0;
  display: flex;
  flex-direction: column;
  justify-content: center;
  align-items: center;
  background: rgba(18, 18, 18, 0.85);
  color: #fff;
  z-index: 5;
  gap: 16px;
}

.spinner {
  width: 44px;
  height: 44px;
  border: 4px solid rgba(255, 255, 255, 0.2);
  border-top-color: #3b82f6;
  border-radius: 50%;
  animation: spin 1s linear infinite;
}

@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}

.status-overlay.error {
  color: #f87171;
  text-align: center;
  padding: 24px;
}

.error-actions {
  display: flex;
  gap: 12px;
}

.retry-btn {
  background: #2563eb;
  color: #fff;
  border: none;
  padding: 8px 16px;
  border-radius: 6px;
  cursor: pointer;
}

.retry-btn:hover {
  background: #1d4ed8;
}

.secondary-btn {
  background: #374151;
  color: #fff;
  border: none;
  padding: 8px 16px;
  border-radius: 6px;
  cursor: pointer;
}
</style>
