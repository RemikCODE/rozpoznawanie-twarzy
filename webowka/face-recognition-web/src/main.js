import { createApp } from 'vue'
import { createRouter, createWebHistory } from 'vue-router'
import App from './App.vue'
import './assets/style.css'

import RecognizePage from './components/RecognizePage.vue'
import HistoryPage from './components/HistoryPage.vue'
import AddPersonPage from './components/AddPersonPage.vue'

const routes = [
  { path: '/', component: RecognizePage },
  { path: '/history', component: HistoryPage },
  { path: '/add', component: AddPersonPage }
]

const router = createRouter({
  history: createWebHistory(),
  routes
})

createApp(App).use(router).mount('#app')