<template>
  <div class="card settings-update-hero">
    <div class="update-hero-left">
      <div class="update-hero-icon-bg">
        <span class="material-symbols-outlined update-hero-icon">rocket_launch</span>
      </div>
      <div class="update-hero-info">
        <div class="update-hero-version-row">
          <span class="update-hero-title">{{ t('settings.version') }}</span>
          <span class="badge update-version-badge">v{{ store.appVersion }}</span>
        </div>
        
        <!-- Status Indicators -->
        <div class="update-hero-status-text">
          <span v-if="store.updateStatus === 'checking'" class="status-checking">
            <span class="material-symbols-outlined spin icon-14">sync</span> {{ t('settings.checking_updates') }}
          </span>
          <span v-else-if="store.updateStatus === 'not-available'" class="status-up-to-date">
            <span class="material-symbols-outlined icon-14">check_circle</span> {{ t('settings.up_to_date') }}
          </span>
          <span v-else-if="store.updateStatus === 'available'" class="status-available">
            <span class="material-symbols-outlined icon-14">new_releases</span> {{ t('settings.update_available') }}
          </span>
          <span v-else-if="store.updateStatus === 'downloading'" class="status-downloading">
            <span class="material-symbols-outlined spin icon-14">downloading</span> {{ t('settings.downloading_update', { percent: Math.round(store.downloadPercent) }) }}
          </span>
          <span v-else-if="store.updateStatus === 'downloaded'" class="status-downloaded">
            <span class="material-symbols-outlined icon-14">verified</span> {{ t('settings.update_ready') }}
          </span>
          <span v-else-if="store.updateStatus === 'error'" class="status-error">
            <span class="material-symbols-outlined icon-14">error</span> {{ t('settings.update_error') }}
          </span>
          <span v-else class="status-idle">
            {{ t('settings.latest_installed') }}
          </span>
        </div>

        <!-- Download Progress Bar -->
        <div v-if="store.updateStatus === 'downloading'" class="update-progress-bar-bg">
          <div class="update-progress-bar-fill" :style="{ width: store.downloadPercent + '%' }"></div>
        </div>
      </div>
    </div>

    <div class="update-hero-actions">
      <button v-if="store.updateStatus !== 'downloading' && store.updateStatus !== 'downloaded'" class="btn btn-primary" @click="$emit('check-updates')" :disabled="store.updateStatus === 'checking'">
        <span class="material-symbols-outlined icon-16-mr" :class="{ spin: store.updateStatus === 'checking' }">sync</span>
        {{ store.updateStatus === 'checking' ? t('settings.checking_updates') : t('settings.check_updates') }}
      </button>

      <button v-if="store.updateStatus === 'available'" class="btn btn-success" @click="$emit('download-update')">
        <span class="material-symbols-outlined icon-16-mr">download</span>
        {{ t('settings.download_update') }}
      </button>

      <button v-if="store.updateStatus === 'downloaded'" class="btn btn-success" @click="$emit('install-update')">
        <span class="material-symbols-outlined icon-16-mr">restart_alt</span>
        {{ t('settings.install_restart') }}
      </button>

      <button class="btn btn-secondary" @click="$emit('open-changelog')">
        <span class="material-symbols-outlined icon-16-mr">rocket_launch</span>
        {{ t('settings.view_changelog') }}
      </button>
    </div>
  </div>
</template>

<script setup lang="ts">
import { useAppStore } from '@/stores/app';
import { useI18n } from '@/composables/useI18n';

const { t } = useI18n();
const store = useAppStore();

defineEmits<{
  (e: 'check-updates'): void;
  (e: 'download-update'): void;
  (e: 'install-update'): void;
  (e: 'open-changelog'): void;
}>();
</script>
