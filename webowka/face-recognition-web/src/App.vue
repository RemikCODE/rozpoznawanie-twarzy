<template>
  <div class="page">
    <router-view />
    <nav class="tab-bar">
      <router-link to="/" class="tab" :class="{ active: $route.path === '/' }">rozpoznaj</router-link>
      <router-link to="/history" class="tab" :class="{ active: $route.path === '/history' }">historia</router-link>
      <router-link to="/add" class="tab" :class="{ active: $route.path === '/add' }">dodaj</router-link>
    </nav>
  </div>
</template>