<template>
  <div class="card">
    <h3 class="settings-card-title">
      <span class="material-symbols-outlined title-icon">build</span>
      {{ t('settings.maintenance_utilities') }}
    </h3>
    <p class="settings-migrate-text" style="margin-bottom: 0.75rem;">
      {{ t('settings.troubleshooting_desc') }}
    </p>

    <div class="maintenance-buttons-grid">
      <button class="btn btn-purple-outline" id="btn-recheck-types" @click="$emit('recheck-types')">
        <span class="material-symbols-outlined icon-16-mr">analytics</span>
        {{ t('settings.recheck_types') }}
      </button>
      <button class="btn btn-success-outline" id="btn-autofix" @click="$emit('autofix')">
        <span class="material-symbols-outlined icon-16-mr">build</span>
        {{ t('settings.autofix') }}
      </button>
      <button class="btn btn-migrate" id="btn-migrate" @click="$emit('migrate')">
        <span class="material-symbols-outlined icon-16-mr">upgrade</span>
        {{ t('settings.migrate_btn') }}
      </button>
    </div>
  </div>
</template>

<script setup lang="ts">
import { useI18n } from '@/composables/useI18n';

const { t } = useI18n();

defineEmits<{
  (e: 'recheck-types'): void;
  (e: 'autofix'): void;
  (e: 'migrate'): void;
}>();
</script>
