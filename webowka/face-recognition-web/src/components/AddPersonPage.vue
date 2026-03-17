<template>
  <div class="page">
    <header class="header">
      <h1>dodaj osobę</h1>
      <p>dodaj nową osobę do bazy</p>
    </header>

    <div class="content">
      <div class="input-field">
        <input v-model="name" type="text" placeholder="imię i nazwisko" @input="result = null" />
      </div>

      <div class="preview" :class="{ placeholder: !photoPreview }" @click="triggerFilePicker">
        <img v-if="photoPreview" :src="photoPreview" alt="preview" />
        <span v-else>kliknij aby wybrać zdjęcie</span>
      </div>

      <div v-if="result" class="result">
        <div :class="['result-header', result.success ? 'success' : 'error']">
          {{ result.message }}
        </div>
        <div v-if="result.success" style="text-align: center">{{ result.name }}</div>
      </div>
    </div>

    <div class="bottom-bar">
      <div v-if="loading" class="loading">zapisywanie</div>
      <div v-else class="button-group">
        <input type="file" ref="fileInput" accept="image/*" style="display: none" @change="onFileSelected" />
        <button class="btn btn-secondary" @click="triggerFilePicker">wybierz zdjęcie</button>
      </div>
      <button class="btn btn-primary" :disabled="!photoBytes || !name.trim() || loading" @click="addPerson">
        dodaj
      </button>
    </div>
  </div>
</template>

<script setup>
import { ref } from 'vue'

const API_BASE = '/api'

const fileInput = ref(null)
const name = ref('')
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

async function addPerson() {
  if (!photoBytes.value || !name.value.trim()) return
  loading.value = true
  try {
    const formData = new FormData()
    formData.append('name', name.value.trim())
    formData.append('image', photoBytes.value)
    const res = await fetch(`${API_BASE}/persons`, { method: 'POST', body: formData })
    const person = await res.json()
    result.value = { success: true, message: 'dodano', name: person.name }
    name.value = ''
    photoBytes.value = null
    photoPreview.value = null
  } catch {
    result.value = { success: false, message: 'błąd' }
  } finally {
    loading.value = false
  }
}
</script>