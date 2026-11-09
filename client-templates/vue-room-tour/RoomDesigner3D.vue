<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue';
import * as THREE from 'three';
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js';

interface BedPlacementDto {
  type: string;
  x: number;
  z: number;
  rotationDeg: number;
}

interface OpeningDto {
  wall: string;
  offset: number;
  width: number;
}

interface RoomLayoutDto {
  widthM: number;
  lengthM: number;
  heightM: number;
  floor: string;
  wallColor: string;
  beds: BedPlacementDto[];
  windows: OpeningDto[];
  door: OpeningDto;
}

interface RoomLayoutCatalog {
  beds: Array<{ type: string; label: string; widthM: number; lengthM: number; guests: number }>;
  floors: string[];
  minSideM: number;
  maxSideM: number;
  minHeightM: number;
  maxHeightM: number;
  maxBeds: number;
  maxWindows: number;
}

interface PreviewResult {
  isValid: boolean;
  areaM2: number;
  guestCapacity: number;
  bedSummary: string;
  errors: string[];
}

const props = withDefaults(defineProps<{ roomId?: number; authToken?: string; apiBase?: string; readonly?: boolean }>(), {
  roomId: undefined,
  authToken: undefined,
  apiBase: '',
  readonly: false
});

const emit = defineEmits<{ (e: 'change', layout: RoomLayoutDto): void; (e: 'saved', payload: any): void; }>();

const catalog = ref<RoomLayoutCatalog>({
  beds: [
    { type: 'OnePlaza', label: '1 plaza', widthM: 0.9, lengthM: 1.9, guests: 1 },
    { type: 'OneAndHalfPlaza', label: '1½ plaza', widthM: 1.05, lengthM: 1.9, guests: 1 },
    { type: 'TwoPlazas', label: '2 plazas', widthM: 1.4, lengthM: 1.9, guests: 2 },
    { type: 'Queen', label: 'Queen', widthM: 1.6, lengthM: 2.0, guests: 2 },
    { type: 'King', label: 'King', widthM: 1.8, lengthM: 2.0, guests: 2 }
  ],
  floors: ['Wood', 'Laminate', 'Tile', 'Carpet', 'Concrete', 'Porcelain'],
  minSideM: 2,
  maxSideM: 12,
  minHeightM: 2.2,
  maxHeightM: 4,
  maxBeds: 4,
  maxWindows: 6
});

const defaultLayout = (): RoomLayoutDto => ({
  widthM: 4,
  lengthM: 5,
  heightM: 2.7,
  floor: 'Wood',
  wallColor: '#efe9df',
  beds: [{ type: 'TwoPlazas', x: 2, z: 1.3, rotationDeg: 0 }],
  windows: [{ wall: 'N', offset: 1, width: 2 }],
  door: { wall: 'S', offset: 0.4, width: 0.9 }
});

const layout = reactive<RoomLayoutDto>(defaultLayout());
const preview = ref<PreviewResult | null>(null);
const loading = ref(false);
const saving = ref(false);
const errorMessage = ref<string | null>(null);
const statusMessage = ref<string | null>(null);
const host = ref<HTMLElement | null>(null);
const newBedType = ref('OnePlaza');

let renderer: THREE.WebGLRenderer | undefined;
let scene: THREE.Scene | undefined;
let camera: THREE.PerspectiveCamera | undefined;
let controls: OrbitControls | undefined;
let roomGroup: THREE.Group | undefined;
let resizeObserver: ResizeObserver | undefined;
let animationFrame = 0;

const canSave = computed(() => !!props.roomId && !props.readonly && preview.value?.isValid === true && !saving.value);

function objectUrl(path: string) {
  const base = props.apiBase ? props.apiBase.replace(/\/$/, '') : '';
  return `${base}${path}`;
}

function loadCatalog() {
  fetch(objectUrl('/api/v1/rooms/layout/catalog'))
    .then(async res => {
      if (!res.ok) throw new Error('catalog');
      catalog.value = await res.json();
    })
    .catch(() => undefined);
}

async function queryPreview() {
  loading.value = true;
  try {
    const res = await fetch(objectUrl('/api/v1/rooms/layout/preview'), {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(layout)
    });
    if (!res.ok) {
      throw new Error(`HTTP ${res.status}`);
    }
    preview.value = await res.json();
    emit('change', JSON.parse(JSON.stringify(layout)));
  } catch (err) {
    preview.value = null;
    errorMessage.value = 'No se pudo validar el layout con el servidor.';
  } finally {
    loading.value = false;
  }
}

async function saveLayout() {
  if (!props.roomId || props.readonly) return;
  saving.value = true;
  statusMessage.value = null;
  try {
    const headers: Record<string, string> = { 'Content-Type': 'application/json' };
    if (props.authToken) headers.Authorization = `Bearer ${props.authToken}`;

    const res = await fetch(objectUrl(`/api/v1/rooms/${props.roomId}/layout`), {
      method: 'PUT',
      headers,
      body: JSON.stringify(layout)
    });

    if (!res.ok) {
      const body = await res.json().catch(() => ({}));
      throw new Error(body.errors?.[0] || 'No se pudo guardar el diseño.');
    }

    const payload = await res.json();
    emit('saved', payload);
    statusMessage.value = 'Diseño guardado correctamente.';
  } catch (err: any) {
    statusMessage.value = err.message || 'Error al guardar el diseño.';
  } finally {
    saving.value = false;
  }
}

function addBed() {
  if (layout.beds.length >= catalog.value.maxBeds) return;
  layout.beds.push({ type: newBedType.value, x: 1, z: 1, rotationDeg: 0 });
}

function removeBed(index: number) {
  layout.beds.splice(index, 1);
}

function addWindow() {
  if (layout.windows.length >= catalog.value.maxWindows) return;
  layout.windows.push({ wall: 'E', offset: 1, width: 1.2 });
}

function removeWindow(index: number) {
  layout.windows.splice(index, 1);
}

function buildBedGeometry(bedType: string) {
  const spec = catalog.value.beds.find(b => b.type === bedType) ?? catalog.value.beds[0];
  const width = spec.widthM;
  const length = spec.lengthM;
  const group = new THREE.Group();

  const frame = new THREE.Mesh(
    new THREE.BoxGeometry(width, 0.35, length),
    new THREE.MeshStandardMaterial({ color: '#6f4d33', roughness: 0.9 })
  );
  frame.position.y = 0.18;
  group.add(frame);

  const mattress = new THREE.Mesh(
    new THREE.BoxGeometry(width - 0.08, 0.2, length - 0.1),
    new THREE.MeshStandardMaterial({ color: '#f6f6f4', roughness: 0.95 })
  );
  mattress.position.y = 0.46;
  group.add(mattress);

  return group;
}

function drawScene() {
  if (!host.value) return;
  if (!scene) {
    scene = new THREE.Scene();
    scene.background = new THREE.Color('#f3f4f6');
  }

  const width = host.value.clientWidth || 640;
  const height = host.value.clientHeight || 420;

  if (!renderer) {
    renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true });
    renderer.setPixelRatio(window.devicePixelRatio || 1);
    renderer.setSize(width, height);
    host.value.appendChild(renderer.domElement);
  }

  if (!camera) {
    camera = new THREE.PerspectiveCamera(45, width / height, 0.1, 1000);
    camera.position.set(6, 5, 8);
  }

  if (!controls) {
    controls = new OrbitControls(camera, renderer.domElement);
    controls.enableDamping = true;
    controls.enablePan = true;
    controls.target.set(0, 0.5, 0);
  }

  if (!roomGroup) {
    roomGroup = new THREE.Group();
    scene.add(roomGroup);
  }

  roomGroup.clear();

  const floor = new THREE.Mesh(
    new THREE.BoxGeometry(layout.widthM, 0.1, layout.lengthM),
    new THREE.MeshStandardMaterial({ color: '#d1d5db', roughness: 0.93 })
  );
  floor.position.y = -0.05;
  roomGroup.add(floor);

  const wallMat = new THREE.MeshStandardMaterial({ color: layout.wallColor || '#efe9df', roughness: 0.9 });
  const walls = [
    { wall: 'N', size: [layout.widthM, layout.heightM, 0.12], pos: [0, layout.heightM / 2, -layout.lengthM / 2] },
    { wall: 'S', size: [layout.widthM, layout.heightM, 0.12], pos: [0, layout.heightM / 2, layout.lengthM / 2] },
    { wall: 'E', size: [0.12, layout.heightM, layout.lengthM], pos: [layout.widthM / 2, layout.heightM / 2, 0] },
    { wall: 'W', size: [0.12, layout.heightM, layout.lengthM], pos: [-layout.widthM / 2, layout.heightM / 2, 0] }
  ];

  walls.forEach(({ size, pos }) => {
    const mesh = new THREE.Mesh(new THREE.BoxGeometry(...size as [number, number, number]), wallMat);
    mesh.position.set(pos[0], pos[1], pos[2]);
    roomGroup.add(mesh);
  });

  layout.beds.forEach((bed, i) => {
    const mesh = buildBedGeometry(bed.type);
    mesh.position.set(bed.x, 0.2, bed.z);
    mesh.rotation.y = THREE.MathUtils.degToRad(bed.rotationDeg);
    roomGroup.add(mesh);
  });

  const doorMat = new THREE.MeshStandardMaterial({ color: '#7c5b46' });
  const doorMesh = new THREE.Mesh(new THREE.BoxGeometry(layout.door.width, 1.9, 0.12), doorMat);
  if (layout.door.wall === 'N') doorMesh.position.set(layout.door.offset, 0.95, -layout.lengthM / 2 + 0.06);
  if (layout.door.wall === 'S') doorMesh.position.set(layout.door.offset, 0.95, layout.lengthM / 2 - 0.06);
  if (layout.door.wall === 'E') doorMesh.position.set(layout.widthM / 2 - 0.06, 0.95, layout.door.offset);
  if (layout.door.wall === 'W') doorMesh.position.set(-layout.widthM / 2 + 0.06, 0.95, layout.door.offset);
  roomGroup.add(doorMesh);

  layout.windows.forEach((windowDef, idx) => {
    const w = new THREE.Mesh(
      new THREE.BoxGeometry(windowDef.width, 0.7, 0.08),
      new THREE.MeshStandardMaterial({ color: '#93c5fd', emissive: '#93c5fd', emissiveIntensity: 0.2 })
    );
    if (windowDef.wall === 'N') w.position.set(idx * 0.5, 1.2, -layout.lengthM / 2 + 0.08);
    if (windowDef.wall === 'S') w.position.set(idx * 0.5, 1.2, layout.lengthM / 2 - 0.08);
    if (windowDef.wall === 'E') w.position.set(layout.widthM / 2 - 0.08, 1.2, idx * 0.5);
    if (windowDef.wall === 'W') w.position.set(-layout.widthM / 2 + 0.08, 1.2, idx * 0.5);
    roomGroup.add(w);
  });

  const ambient = new THREE.AmbientLight('#ffffff', 1.1);
  const key = new THREE.DirectionalLight('#fff', 1.2);
  key.position.set(4, 6, 4);
  scene.add(ambient, key);

  renderer.setSize(width, height);
  camera.aspect = width / height;
  camera.updateProjectionMatrix();
}

function animate() {
  if (scene && camera && renderer && controls) {
    controls.update();
    renderer.render(scene, camera);
  }
  animationFrame = requestAnimationFrame(animate);
}

watch(() => layout, () => {
  emit('change', JSON.parse(JSON.stringify(layout)));
  queryPreview();
}, { deep: true });

onMounted(() => {
  loadCatalog();
  drawScene();
  animate();
  if (host.value) {
    resizeObserver = new ResizeObserver(() => drawScene());
    resizeObserver.observe(host.value);
  }
});

onBeforeUnmount(() => {
  cancelAnimationFrame(animationFrame);
  renderer?.dispose();
  controls?.dispose();
  resizeObserver?.disconnect();
});
</script>

<template>
  <div class="designer-shell">
    <div class="panel">
      <div class="header">
        <h3>Diseñador 3D de cuarto</h3>
        <button v-if="!readonly" class="primary" @click="saveLayout">Guardar</button>
      </div>

      <div class="controls-grid">
        <label>
          Ancho (m)
          <input v-model.number="layout.widthM" type="number" min="2" max="12" step="0.1" />
        </label>
        <label>
          Largo (m)
          <input v-model.number="layout.lengthM" type="number" min="2" max="12" step="0.1" />
        </label>
        <label>
          Alto (m)
          <input v-model.number="layout.heightM" type="number" min="2.2" max="4" step="0.1" />
        </label>
        <label>
          Piso
          <select v-model="layout.floor">
            <option v-for="floor in catalog.floors" :key="floor" :value="floor">{{ floor }}</option>
          </select>
        </label>
        <label class="full">
          Color pared
          <input v-model="layout.wallColor" type="color" />
        </label>
      </div>

      <div class="section">
        <div class="section-title">Camas</div>
        <div class="inline-actions">
          <select v-model="newBedType">
            <option v-for="bed in catalog.beds" :key="bed.type" :value="bed.type">{{ bed.label }}</option>
          </select>
          <button v-if="!readonly" class="secondary" @click="addBed">Agregar</button>
        </div>

        <div v-for="(bed, index) in layout.beds" :key="`${bed.type}-${index}`" class="bed-row">
          <select v-model="bed.type">
            <option v-for="option in catalog.beds" :key="option.type" :value="option.type">{{ option.label }}</option>
          </select>
          <input v-model.number="bed.x" type="number" step="0.1" />
          <input v-model.number="bed.z" type="number" step="0.1" />
          <button v-if="!readonly" class="ghost" @click="removeBed(index)">Quitar</button>
        </div>
      </div>

      <div class="section">
        <div class="section-title">Ventanas</div>
        <button v-if="!readonly" class="secondary" @click="addWindow">Agregar ventana</button>
        <div v-for="(windowDef, index) in layout.windows" :key="`${windowDef.wall}-${index}`" class="bed-row">
          <select v-model="windowDef.wall">
            <option value="N">N</option>
            <option value="S">S</option>
            <option value="E">E</option>
            <option value="W">W</option>
          </select>
          <input v-model.number="windowDef.offset" type="number" step="0.1" />
          <input v-model.number="windowDef.width" type="number" step="0.1" />
          <button v-if="!readonly" class="ghost" @click="removeWindow(index)">Quitar</button>
        </div>
      </div>

      <div class="section">
        <div class="section-title">Puerta</div>
        <select v-model="layout.door.wall">
          <option value="N">N</option>
          <option value="S">S</option>
          <option value="E">E</option>
          <option value="W">W</option>
        </select>
        <input v-model.number="layout.door.offset" type="number" step="0.1" />
        <input v-model.number="layout.door.width" type="number" step="0.1" />
      </div>

      <div v-if="statusMessage" class="notice ok">{{ statusMessage }}</div>
      <div v-if="errorMessage" class="notice error">{{ errorMessage }}</div>
      <div v-if="preview" class="result">
        <div><strong>Área:</strong> {{ preview.areaM2.toFixed(2) }} m²</div>
        <div><strong>Capacidad:</strong> {{ preview.guestCapacity }} huésped(es)</div>
        <div><strong>Resumen:</strong> {{ preview.bedSummary }}</div>
        <ul v-if="preview.errors.length">
          <li v-for="(err, idx) in preview.errors" :key="idx">{{ err }}</li>
        </ul>
      </div>
    </div>

    <div class="viewport" ref="host"></div>
  </div>
</template>

<style scoped>
.designer-shell {
  display: grid;
  grid-template-columns: 420px minmax(300px, 1fr);
  gap: 16px;
  min-height: 480px;
  font-family: sans-serif;
}
.panel, .viewport {
  background: #fff;
  border: 1px solid #e5e7eb;
  border-radius: 14px;
  box-shadow: 0 8px 24px rgba(0, 0, 0, 0.05);
}
.panel {
  padding: 16px;
}
.header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 14px;
}
.controls-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 10px;
}
label {
  display: flex;
  flex-direction: column;
  gap: 6px;
  font-size: 0.82rem;
  color: #374151;
}
input, select, button {
  border-radius: 8px;
  border: 1px solid #d1d5db;
  padding: 8px 10px;
  font-size: 0.9rem;
}
.full { grid-column: 1 / -1; }
.section {
  margin-top: 16px;
  padding-top: 12px;
  border-top: 1px solid #e5e7eb;
}
.section-title {
  font-weight: 700;
  margin-bottom: 8px;
}
.inline-actions, .bed-row {
  display: flex;
  gap: 8px;
  align-items: center;
  margin-top: 8px;
}
.bed-row > * { flex: 1; }
.primary, .secondary, .ghost {
  cursor: pointer;
}
.primary {
  background: #2563eb;
  color: white;
  border: none;
}
.secondary {
  background: #f3f4f6;
}
.ghost {
  background: #fee2e2;
  color: #991b1b;
}
.viewport {
  min-height: 480px;
  position: relative;
  overflow: hidden;
}
.notice {
  margin-top: 12px;
  padding: 8px 10px;
  border-radius: 8px;
  font-size: 0.85rem;
}
.notice.ok { background: #dcfce7; color: #166534; }
.notice.error { background: #fee2e2; color: #991b1b; }
.result {
  margin-top: 12px;
  background: #f8fafc;
  border-radius: 8px;
  padding: 10px 12px;
  font-size: 0.85rem;
  line-height: 1.6;
}
.result ul { margin: 6px 0 0 18px; }
@media (max-width: 900px) {
  .designer-shell { grid-template-columns: 1fr; }
}
</style>
