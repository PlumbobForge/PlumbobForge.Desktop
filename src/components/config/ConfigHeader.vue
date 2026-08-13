<template>
  <div class="cm-main-header">
    <div>
      <h3
        id="config-title"
        :class="{ 'config-title-clickable': currentConfig && !currentConfig.default }"
        :title="currentConfig && !currentConfig.default ? 'Click to rename' : ''"
        @click="currentConfig && $emit('rename-config', currentConfig)"
      >
        {{ currentConfig ? currentConfig.name : t('config.title') }}
      </h3>
      <div v-if="currentConfig" class="config-description-row" @click="$emit('edit-description', currentConfig)" title="Click to edit description">
        <span v-if="currentConfig.description && currentConfig.description.trim()" class="config-description-text">
          {{ currentConfig.description }}
        </span>
        <span v-else class="config-description-placeholder">
          {{ t('config.add_description') }}
        </span>
        <span class="material-symbols-outlined edit-desc-icon">edit</span>
      </div>
    </div>
    <div style="display: flex; align-items: center; gap: 1rem;">
      <!-- Custom Sets Sort Dropdown -->
      <div class="sort-trigger-wrapper" @click.stop="setSortDropdownOpen = !setSortDropdownOpen">
        <div class="custom-tooltip-container" :data-tooltip="t('cm.sort_sets')">
          <button class="btn sort-trigger" style="padding: 4px 10px; font-size: 0.82rem; display: flex; align-items: center; gap: 4px;">
            <span class="material-symbols-outlined" style="font-size: 18px;">sort</span>
            {{ getSetSortLabel() }}
          </button>
        </div>
        <div v-if="setSortDropdownOpen" class="context-menu dropdown-menu-right" style="top: 100%; right: 0;">
          <div
            class="context-menu-item"
            :style="{ color: setSortMode === 'date' ? 'var(--primary)' : 'var(--text-main)' }"
            @click.stop="selectSortMode('date')"
          >
            {{ t('config.sort_date') }}
          </div>
          <div class="context-menu-divider"></div>
          <div
            class="context-menu-item"
            :style="{ color: setSortMode === 'alpha_asc' ? 'var(--primary)' : 'var(--text-main)' }"
            @click.stop="selectSortMode('alpha_asc')"
          >
            {{ t('config.sort_alpha_asc') }}
          </div>
          <div
            class="context-menu-item"
            :style="{ color: setSortMode === 'alpha_desc' ? 'var(--primary)' : 'var(--text-main)' }"
            @click.stop="selectSortMode('alpha_desc')"
          >
            {{ t('config.sort_alpha_desc') }}
          </div>
          <div class="context-menu-divider"></div>
          <div
            class="context-menu-item"
            :style="{ color: setSortMode === 'subsets_desc' ? 'var(--primary)' : 'var(--text-main)' }"
            @click.stop="selectSortMode('subsets_desc')"
          >
            {{ t('config.sort_subsets') }}
          </div>
        </div>
      </div>
      <div class="cm-items-stats config-stats" id="config-stats">
        <span v-if="currentConfig">{{ t('config.sets_enabled', { enabled: currentConfig.setIds.length, total: totalSetsCount }) }}</span>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import type { Configuration } from '@/types';
import { useI18n } from '@/composables/useI18n';

const { t } = useI18n();

type SetSortMode = 'date' | 'alpha_asc' | 'alpha_desc' | 'subsets_desc';

const props = defineProps<{
  currentConfig: Configuration | null;
  totalSetsCount: number;
  setSortMode: SetSortMode;
}>();

const emit = defineEmits<{
  (e: 'update:setSortMode', mode: SetSortMode): void;
  (e: 'rename-config', config: Configuration): void;
  (e: 'edit-description', config: Configuration): void;
}>();

const setSortDropdownOpen = ref(false);

function getSetSortLabel() {
  switch (props.setSortMode) {
    case 'alpha_asc': return t('config.sort_alpha_asc');
    case 'alpha_desc': return t('config.sort_alpha_desc');
    case 'subsets_desc': return t('config.sort_subsets');
    default: return t('config.sort_date');
  }
}

function selectSortMode(mode: SetSortMode) {
  setSortDropdownOpen.value = false;
  emit('update:setSortMode', mode);
}
</script>
