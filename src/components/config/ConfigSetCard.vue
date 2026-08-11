<template>
  <div
    :data-id="set.id"
    draggable="true"
    class="config-set-card selectable-card"
    :class="{ selected: isSelected }"
    @click="$emit('click-set', $event, set)"
    @dragstart="$emit('drag-start', $event, set)"
    @dragend="$emit('drag-end', $event)"
    @dblclick="$emit('dblclick-set', set)"
    :title="enabled ? 'Double-click to disable set' : 'Double-click to enable set'"
  >
    <div style="display: flex; align-items: center; justify-content: space-between; width: 100%;">
      <div style="display: flex; align-items: center; gap: 0.35rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; flex: 1;">
        <span class="material-symbols-outlined config-set-icon">sell</span>
        <template v-if="ancestors.length > 0">
          <span v-for="ancestor in ancestors" :key="ancestor.id" class="set-breadcrumb-inline">
            <span>{{ ancestor.name }}</span>
            <span class="material-symbols-outlined set-breadcrumb-icon">chevron_right</span>
          </span>
        </template>
        <span style="font-weight: 600;">{{ set.name }}</span>
      </div>
      <span v-if="descendantCount > 0" class="set-subset-count-badge">
        {{ t('config.subset_count', { count: descendantCount }) }}
      </span>
    </div>
  </div>
</template>

<script setup lang="ts">
import type { SetEntity } from '@/types';
import { useI18n } from '@/composables/useI18n';

const { t } = useI18n();

const props = defineProps<{
  set: SetEntity;
  ancestors: SetEntity[];
  descendantCount: number;
  isSelected: boolean;
  enabled: boolean;
}>();

defineEmits<{
  (e: 'click-set', event: MouseEvent, set: SetEntity): void;
  (e: 'drag-start', event: DragEvent, set: SetEntity): void;
  (e: 'drag-end', event: DragEvent): void;
  (e: 'dblclick-set', set: SetEntity): void;
}>();
</script>
