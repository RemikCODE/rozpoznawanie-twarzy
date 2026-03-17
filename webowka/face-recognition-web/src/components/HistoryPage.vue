<template>
  <div class="page">
    <header class="header">
      <h1>historia</h1>
      <p>ostatnie rozpoznania</p>
    </header>

    <div class="content">
      <div v-if="loading" class="loading">ładowanie</div>
      <div v-else-if="logs.length === 0" class="empty-state">
        <p>brak historii</p>
      </div>
      <div v-else>
        <div v-for="log in logs" :key="log.id" class="history-item">
          <div class="history-row">
            <span class="history-name">{{ log.personName || 'nieznana' }}</span>
            <span :class="['history-badge', log.found ? 'success' : 'error']">
              {{ log.found ? 'tak' : 'nie' }}
            </span>
          </div>
          <div class="history-details">
            <span>{{ formatDate(log.recognizedAt) }}</span>
            <span>{{ log.found ? Math.round(log.confidence * 100) + '%' : '—' }}</span>
          </div>
          <div class="history-message">{{ log.message }}</div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue'

const API_BASE = '/api'
const logs = ref([])
const loading = ref(true)

function formatDate(dateStr) {
  const date = new Date(dateStr + 'Z')
  return date.toLocaleString('pl-PL')
}

async function fetchHistory() {
  try {
    const res = await fetch(`${API_BASE}/recognitions?limit=20`)
    logs.value = await res.json()
  } catch (err) {
    console.error(err)
  } finally {
    loading.value = false
  }
}

onMounted(() => {
  fetchHistory()
  setInterval(fetchHistory, 10000)
})
</script>