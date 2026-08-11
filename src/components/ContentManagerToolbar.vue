<template>
  <div class="cm-main-header">
    <div class="cm-header-flex gap-4">
      <h3 id="cm-items-title">
        {{ currentSetName === 'All Items' ? t('cm.all_items') : (currentSetName === 'Legacy' ? t('cm.legacy') : currentSetName) }}
      </h3>
      <div class="cm-items-stats" id="cm-items-stats" style="color: var(--text-muted); font-size: 0.9rem;">
        {{ t('cm.items_count', { count: filteredCount, size: formattedTotalSize }) }}
      </div>
    </div>
    <div class="cm-header-flex gap-2">
      <!-- Sort Dropdown -->
      <div class="sort-trigger-wrapper" @click.stop="sortDropdownOpen = !sortDropdownOpen">
        <button class="btn sort-trigger">
          <span class="material-symbols-outlined" style="font-size:20px;">sort</span>
          {{ t('cm.sort') }}
        </button>
        <div v-if="sortDropdownOpen" class="context-menu dropdown-menu-right">
          <div class="context-menu-item" :style="{ color: sortMode === 'date_desc' ? 'var(--primary)' : 'var(--text-main)' }" @click.stop="setSort('date_desc')">
            {{ t('cm.sort_latest') }}
          </div>
          <div class="context-menu-item" :style="{ color: sortMode === 'date_asc' ? 'var(--primary)' : 'var(--text-main)' }" @click.stop="setSort('date_asc')">
            {{ t('cm.sort_oldest') }}
          </div>
          <div class="context-menu-divider"></div>
          <div class="context-menu-item" :style="{ color: sortMode === 'alpha_asc' ? 'var(--primary)' : 'var(--text-main)' }" @click.stop="setSort('alpha_asc')">
            {{ t('cm.sort_alpha_asc') }}
          </div>
          <div class="context-menu-item" :style="{ color: sortMode === 'alpha_desc' ? 'var(--primary)' : 'var(--text-main)' }" @click.stop="setSort('alpha_desc')">
            {{ t('cm.sort_alpha_desc') }}
          </div>
        </div>
      </div>

      <!-- View Mode Toggles -->
      <div class="view-toggle-container">
        <div class="custom-tooltip-container" :data-tooltip="t('cm.comfy_view')">
          <button class="btn btn-view-toggle" :class="{ active: viewMode === 'comfy' }" @click="setViewMode('comfy')">
            <span class="material-symbols-outlined" style="font-size:20px;">grid_view</span>
          </button>
        </div>
        <div class="custom-tooltip-container" :data-tooltip="t('cm.compact_view')">
          <button class="btn btn-view-toggle" :class="{ active: viewMode === 'compact' }" @click="setViewMode('compact')">
            <span class="material-symbols-outlined" style="font-size:20px;">view_list</span>
          </button>
        </div>
      </div>

      <!-- Batch Action Bar -->
      <div id="cm-action-bar" v-if="selectionMode && selectedCount > 0" class="cm-action-bar">
        <span class="cm-action-bar-text">{{ t('cm.selected', { count: selectedCount }) }}</span>
        <button id="btn-enable-selected" class="btn btn-action custom-tooltip-container" :data-tooltip="t('context.enable')" @click="$emit('enable-selected', true)">
          <span class="material-symbols-outlined" style="font-size:18px;">check_circle</span>
        </button>
        <button id="btn-disable-selected" class="btn btn-action custom-tooltip-container" :data-tooltip="t('context.disable')" @click="$emit('enable-selected', false)">
          <span class="material-symbols-outlined" style="font-size:18px;">block</span>
        </button>
        <button id="btn-retag-selected" class="btn btn-action custom-tooltip-container" :data-tooltip="t('context.retag')" @click="$emit('retag-selected')">
          <span class="material-symbols-outlined" style="font-size:18px;">sell</span>
        </button>
        <button id="btn-tags-selected" class="btn btn-action custom-tooltip-container" :data-tooltip="t('context.user_tags')" @click="$emit('edit-tags-selected')">
          <span class="material-symbols-outlined" style="font-size:18px;">label</span>
        </button>
        <button id="btn-move-selected" class="btn btn-action custom-tooltip-container" :data-tooltip="t('modal.move')" @click="$emit('move-selected')">
          <span class="material-symbols-outlined" style="font-size:18px;">drive_file_move</span>
        </button>
        <button id="btn-delete-selected" class="btn btn-action btn-danger-outline custom-tooltip-container" :data-tooltip="t('modal.delete')" @click="$emit('delete-selected')">
          <span class="material-symbols-outlined" style="font-size:18px;">delete</span>
        </button>
      </div>

      <!-- Select All Toggle -->
      <button
        v-if="selectionMode"
        id="btn-select-all"
        class="btn btn-action custom-tooltip-container mr-2"
        :data-tooltip="selectedCount === filteredCount && filteredCount > 0 ? t('cm.deselect_all') : t('cm.select_all')"
        @click="$emit('toggle-select-all')"
      >
        <span class="material-symbols-outlined" style="font-size:18px;">
          {{ selectedCount === filteredCount && filteredCount > 0 ? 'deselect' : 'select_all' }}
        </span>
      </button>

      <!-- Manual Selection Mode Toggle -->
      <button id="btn-toggle-select" class="btn btn-select-toggle" :class="{ active: selectionMode }" @click="$emit('toggle-selection-mode')">
        {{ selectionMode ? t('cm.done') : t('cm.manual_select') }}
      </button>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import { useI18n } from '@/composables/useI18n';

const { t } = useI18n();

const props = defineProps<{
  currentSetName: string;
  filteredCount: number;
  formattedTotalSize: string;
  sortMode: string;
  viewMode: string;
  selectionMode: boolean;
  selectedCount: number;
}>();

const emit = defineEmits<{
  (e: 'update:sortMode', value: string): void;
  (e: 'update:viewMode', value: string): void;
  (e: 'enable-selected', enable: boolean): void;
  (e: 'retag-selected'): void;
  (e: 'edit-tags-selected'): void;
  (e: 'move-selected'): void;
  (e: 'delete-selected'): void;
  (e: 'toggle-select-all'): void;
  (e: 'toggle-selection-mode'): void;
}>();

const sortDropdownOpen = ref(false);

function setSort(mode: string) {
  emit('update:sortMode', mode);
  sortDropdownOpen.value = false;
}

function setViewMode(mode: string) {
  emit('update:viewMode', mode);
}
</script>
