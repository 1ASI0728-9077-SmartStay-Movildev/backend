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

onMounted(async () => {
  if (!host.value) return;

  try {
    loading.value = true;
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
        VirtualTourPlugin.withConfig({
          positionMode: 'manual',
          renderMode: '3d',
          startNodeId: manifest.startNodeId,
          nodes: formattedNodes
        })
      ]
    });
  } catch (err: any) {
    errorMessage.value = err.message || 'No se pudo cargar el recorrido 360°.';
  } finally {
    loading.value = false;
  }
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
      <button @click="emit('close')">Volver a fotos estándar</button>
    </div>

    <div ref="host" class="viewer-host"></div>
  </div>
</template>

<style scoped>
.tour-container {
  position: relative;
  width: 100%;
  height: 480px;
  background-color: #1a1a1a;
  border-radius: 8px;
  overflow: hidden;
}

.tour-header {
  position: absolute;
  top: 12px;
  left: 12px;
  right: 12px;
  z-index: 10;
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.badge {
  background: rgba(0, 0, 0, 0.7);
  color: #fff;
  padding: 6px 12px;
  border-radius: 20px;
  font-size: 13px;
  font-weight: 500;
}

.close-btn {
  background: rgba(0, 0, 0, 0.7);
  color: #fff;
  border: none;
  padding: 6px 14px;
  border-radius: 20px;
  cursor: pointer;
  font-size: 13px;
  transition: background 0.2s;
}

.close-btn:hover {
  background: rgba(231, 76, 60, 0.9);
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
  color: #fff;
  background: rgba(0, 0, 0, 0.85);
  z-index: 5;
}

.spinner {
  width: 40px;
  height: 40px;
  border: 4px solid rgba(255, 255, 255, 0.2);
  border-top-color: #3498db;
  border-radius: 50%;
  animation: spin 1s linear infinite;
  margin-bottom: 12px;
}

@keyframes spin {
  to { transform: rotate(360deg); }
}
</style>
