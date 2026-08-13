<template>
  <div class="cm-sidebar" id="cm-sidebar" @contextmenu.prevent="$emit('sidebar-context-menu', $event)" @dragover.prevent @drop.prevent="onDropSidebar">
    <div class="cm-sidebar-header" style="position: relative; display: flex; align-items: center; justify-content: space-between;">
      <span>{{ t('cm.sets') }}</span>
      <div style="display: flex; align-items: center; gap: 0.25rem;">
        <div class="custom-tooltip-container" :data-tooltip="t('cm.sort_sets')">
          <button class="btn sort-trigger" style="padding: 4px 8px; font-size: 0.8rem; display: flex; align-items: center; gap: 4px;" @click.stop="setSortDropdownOpen = !setSortDropdownOpen">
            <span class="material-symbols-outlined" style="font-size: 16px;">sort</span>
          </button>
        </div>
        <div class="custom-tooltip-container" :data-tooltip="t('cm.add_set')">
          <button id="btn-create-set" class="btn btn-sm" @click.stop="$emit('create-set')">+</button>
        </div>
      </div>
      <div v-if="setSortDropdownOpen" class="context-menu dropdown-menu-left" style="top: 15%; left: 0; min-width: 150px; z-index: 100;">
        <div class="context-menu-item" :class="{ active: setSortBy === 'date' }" @click="updateSort('date')">
          {{ t('cm.sort_date') }}
        </div>
        <div class="context-menu-divider"></div>
        <div class="context-menu-item" :class="{ active: setSortBy === 'name_asc' }" @click="updateSort('name_asc')">
          {{ t('cm.sort_name_asc') }}
        </div>
        <div class="context-menu-item" :class="{ active: setSortBy === 'name_desc' }" @click="updateSort('name_desc')">
          {{ t('cm.sort_name_desc') }}
        </div>
      </div>
    </div>
    <div class="cm-tree-view" v-if="loadingSets">
      <div class="text-muted-padded">Loading Sets...</div>
    </div>
    <div class="cm-tree-view" id="cm-tree-view" v-else @wheel="onTreeWheel" @dragover.prevent @drop.prevent="onDropSidebar">
      <!-- All Items Node -->
      <div class="tree-item" :class="{ active: selectedSetId === null, 'drag-over-invalid': isDragOverAll }" @click="$emit('select-set', null)" @contextmenu.prevent.stop @dragover.prevent="onDragOverAll" @dragleave="onDragLeaveAll" @drop.prevent.stop="onDropAll">
        <span class="tree-label">
          <span class="tree-icon material-symbols-outlined">layers</span>
          {{ t('cm.all_items') }}
        </span>
      </div>

      <!-- Recursive Tree -->
      <SetTreeNode
        v-for="set in sortedRootSets"
        :key="set.id"
        :set="set"
        :allSets="sortedAllSets"
        :depth="0"
        @select="(id) => $emit('select-set', id)"
        @context-menu="(e, s) => $emit('set-context-menu', e, s)"
        @drop="(data, targetId) => $emit('drop-set', data, targetId)"
      />
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue';
import type { SetEntity } from '@/types';
import { useI18n } from '@/composables/useI18n';
import SetTreeNode from './SetTreeNode.vue';

const { t } = useI18n();

const props = defineProps<{
  allSets: SetEntity[];
  selectedSetId: number | null;
  loadingSets: boolean;
  setSortBy: 'date' | 'name_asc' | 'name_desc';
}>();

const emit = defineEmits<{
  (e: 'select-set', setId: number | null): void;
  (e: 'create-set'): void;
  (e: 'update:setSortBy', value: 'date' | 'name_asc' | 'name_desc'): void;
  (e: 'set-context-menu', event: MouseEvent, set: SetEntity): void;
  (e: 'sidebar-context-menu', event: MouseEvent): void;
  (e: 'drop-set', data: any, targetSetId: number): void;
  (e: 'drop-all', data: any): void;
}>();

const setSortDropdownOpen = ref(false);
const isDragOverAll = ref(false);

function updateSort(val: 'date' | 'name_asc' | 'name_desc') {
  emit('update:setSortBy', val);
  setSortDropdownOpen.value = false;
}

const sortedAllSets = computed(() => {
  const sets = [...props.allSets];
  if (props.setSortBy === 'name_asc') {
    sets.sort((a, b) => a.name.localeCompare(b.name));
  } else if (props.setSortBy === 'name_desc') {
    sets.sort((a, b) => b.name.localeCompare(a.name));
  }
  return sets;
});

const sortedRootSets = computed(() => {
  return sortedAllSets.value.filter(s => !s.parentSetsEntityId);
});

function onTreeWheel(e: WheelEvent) {
  const target = e.currentTarget as HTMLElement;
  if (target) {
    target.scrollTop += e.deltaY;
  }
}

function onDragOverAll(e: DragEvent) {
  if (e.dataTransfer) {
    e.dataTransfer.dropEffect = 'none';
    isDragOverAll.value = true;
  }
}

function onDragLeaveAll() {
  isDragOverAll.value = false;
}

function onDropAll(e: DragEvent) {
  isDragOverAll.value = false;
  if (!e.dataTransfer) return;
  const dataStr = e.dataTransfer.getData('application/json');
  if (!dataStr) return;
  try {
    const data = JSON.parse(dataStr);
    emit('drop-all', data);
  } catch {}
}

function onDropSidebar(e: DragEvent) {
  if (!e.dataTransfer) return;
  const dataStr = e.dataTransfer.getData('application/json');
  if (!dataStr) return;
  try {
    const data = JSON.parse(dataStr);
    emit('drop-all', data);
  } catch {}
}
</script>
