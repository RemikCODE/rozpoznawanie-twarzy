<template>
  <div class="page">
    <header class="header">
      <h1>rozpoznawanie</h1>
      <p>wybierz zdjęcie aby rozpoznać osobę</p>
    </header>

    <div class="content">
      <div class="preview" :class="{ placeholder: !photoPreview }" @click="triggerFilePicker">
        <img v-if="photoPreview" :src="photoPreview" alt="preview" />
        <span v-else>kliknij aby wybrać zdjęcie</span>
      </div>

      <div v-if="result" class="result">
        <div :class="['result-header', result.found ? 'success' : 'error']">
          {{ result.found ? 'rozpoznano' : 'nie rozpoznano' }}
        </div>
        <div class="result-grid">
          <div>
            <div class="result-label">osoba</div>
            <div class="result-value">{{ result.person?.name || '—' }}</div>
          </div>
          <div>
            <div class="result-label">pewność</div>
            <div class="result-value">{{ result.found ? Math.round(result.confidence * 100) + '%' : '—' }}</div>
          </div>
        </div>
        <div class="result-message">{{ result.message }}</div>
      </div>
    </div>

    <div class="bottom-bar">
      <div v-if="loading" class="loading">przetwarzanie</div>
      <div v-else class="button-group">
        <input type="file" ref="fileInput" accept="image/*" style="display: none" @change="onFileSelected" />
        <button class="btn btn-secondary" @click="triggerFilePicker">wybierz zdjęcie</button>
      </div>
      <button class="btn btn-primary" :disabled="!photoBytes || loading" @click="recognize">
        rozpoznaj
      </button>
    </div>
  </div>
</template>

<script setup>
import { ref } from 'vue'

const API_BASE = '/api'

const fileInput = ref(null)
const photoBytes = ref(null)
const photoPreview = ref(null)
const loading = ref(false)
const result = ref(null)

function triggerFilePicker() {
  fileInput.value.click()
}

function onFileSelected(event) {
  const file = event.target.files[0]
  if (!file) return

  const reader = new FileReader()
  reader.onload = (e) => {
    photoPreview.value = e.target.result
    photoBytes.value = file
    result.value = null
  }
  reader.readAsDataURL(file)
}

async function recognize() {
  if (!photoBytes.value) return
  loading.value = true
  try {
    const formData = new FormData()
    formData.append('image', photoBytes.value)
    const res = await fetch(`${API_BASE}/faces/recognize`, { method: 'POST', body: formData })
    result.value = await res.json()
  } catch (err) {
    alert('Błąd: ' + err.message)
  } finally {
    loading.value = false
  }
}
</script>