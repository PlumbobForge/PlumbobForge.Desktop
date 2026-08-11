<template>
  <div class="cm-sidebar">
    <div class="cm-sidebar-header">
      <span>{{ t('config.title') }}</span>
      <div style="display: flex; align-items: center; gap: 0.35rem;">
        <!-- Custom Config Sort Dropdown -->
        <div class="sort-trigger-wrapper" @click.stop="configSortDropdownOpen = !configSortDropdownOpen">
          <button class="btn sort-trigger" style="padding: 4px 8px; font-size: 0.8rem; display: flex; align-items: center; gap: 4px;">
            <span class="material-symbols-outlined" style="font-size: 16px;">sort</span>
          </button>
          <div v-if="configSortDropdownOpen" class="context-menu dropdown-menu-left" style="top: 15%;left: 54px;">
            <div
              class="context-menu-item"
              :style="{ color: configSortMode === 'date' ? 'var(--primary)' : 'var(--text-main)' }"
              @click.stop="setSortMode('date')"
            >
              {{ t('config.sort_date') }}
            </div>
            <div class="context-menu-divider"></div>
            <div
              class="context-menu-item"
              :style="{ color: configSortMode === 'alpha_asc' ? 'var(--primary)' : 'var(--text-main)' }"
              @click.stop="setSortMode('alpha_asc')"
            >
              {{ t('config.sort_alpha_asc') }}
            </div>
            <div
              class="context-menu-item"
              :style="{ color: configSortMode === 'alpha_desc' ? 'var(--primary)' : 'var(--text-main)' }"
              @click.stop="setSortMode('alpha_desc')"
            >
              {{ t('config.sort_alpha_desc') }}
            </div>
          </div>
        </div>
        <button id="btn-create-config" class="btn btn-create-config" @click="$emit('create-config')">+</button>
      </div>
    </div>
    <div class="cm-tree-view" id="config-tree-view">
      <div v-if="loading && configs.length === 0" class="config-loading">Loading...</div>
      <div
        v-for="config in configs"
        :key="config.id"
        class="tree-item config-tree-item-flex"
        :class="{ active: currentConfigId === config.id }"
        :title="config.description || config.name"
        @click="$emit('select-config', config)"
        @contextmenu.prevent="$emit('config-context-menu', $event, config)"
      >
        <span class="tree-label">
          <span class="tree-icon material-symbols-outlined">tune</span>
          {{ config.name }}
        </span>
        <span v-if="config.active" class="badge-active">{{ t('config.active_badge') }}</span>
        <span v-else-if="config.default" class="badge-default">{{ t('config.default_badge') }}</span>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import type { Configuration } from '@/types';
import { useI18n } from '@/composables/useI18n';

const { t } = useI18n();

const props = defineProps<{
  configs: Configuration[];
  currentConfigId: number | undefined;
  loading: boolean;
  configSortMode: 'date' | 'alpha_asc' | 'alpha_desc';
}>();

const emit = defineEmits<{
  (e: 'update:configSortMode', mode: 'date' | 'alpha_asc' | 'alpha_desc'): void;
  (e: 'create-config'): void;
  (e: 'select-config', config: Configuration): void;
  (e: 'config-context-menu', event: MouseEvent, config: Configuration): void;
}>();

const configSortDropdownOpen = ref(false);

function setSortMode(mode: 'date' | 'alpha_asc' | 'alpha_desc') {
  configSortDropdownOpen.value = false;
  emit('update:configSortMode', mode);
}
</script>
