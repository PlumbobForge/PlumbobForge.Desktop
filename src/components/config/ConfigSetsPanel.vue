<template>
  <div class="config-main-flex">
    <!-- Enabled Sets Column -->
    <div class="config-panel">
      <div class="config-panel-header-primary">
        {{ t('config.enabled_sets_title') }} ({{ enabledSets.length }})
      </div>
      <div
        class="drop-zone config-drop-zone"
        id="config-enabled-sets"
        @dragover.prevent="$emit('drag-over', $event)"
        @dragleave="$emit('drag-leave', $event)"
        @drop="$emit('drop-sets', $event, true)"
      >
        <ConfigSetCard
          v-for="set in enabledSets"
          :key="set.id"
          :set="set"
          :ancestors="getAncestors(set)"
          :descendantCount="getDescendantCount(set)"
          :isSelected="selectedSetIds.has(set.id)"
          :enabled="true"
          @click-set="(e, s) => $emit('click-set', e, s)"
          @drag-start="(e, s) => $emit('drag-start', e, s)"
          @drag-end="(e) => $emit('drag-end', e)"
          @dblclick-set="(s) => $emit('dblclick-set', s)"
        />
      </div>
    </div>

    <!-- Disabled Sets Column -->
    <div class="config-panel">
      <div class="config-panel-header-muted">
        {{ t('config.disabled_sets_title') }} ({{ disabledSets.length }})
      </div>
      <div
        class="drop-zone config-drop-zone"
        id="config-disabled-sets"
        @dragover.prevent="$emit('drag-over', $event)"
        @dragleave="$emit('drag-leave', $event)"
        @drop="$emit('drop-sets', $event, false)"
      >
        <ConfigSetCard
          v-for="set in disabledSets"
          :key="set.id"
          :set="set"
          :ancestors="getAncestors(set)"
          :descendantCount="getDescendantCount(set)"
          :isSelected="selectedSetIds.has(set.id)"
          :enabled="false"
          @click-set="(e, s) => $emit('click-set', e, s)"
          @drag-start="(e, s) => $emit('drag-start', e, s)"
          @drag-end="(e) => $emit('drag-end', e)"
          @dblclick-set="(s) => $emit('dblclick-set', s)"
        />
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import type { SetEntity } from '@/types';
import { useI18n } from '@/composables/useI18n';
import ConfigSetCard from './ConfigSetCard.vue';

const { t } = useI18n();

const props = defineProps<{
  enabledSets: SetEntity[];
  disabledSets: SetEntity[];
  selectedSetIds: Set<number>;
  getAncestors: (set: SetEntity) => SetEntity[];
  getDescendantCount: (set: SetEntity) => number;
}>();

defineEmits<{
  (e: 'drag-over', event: DragEvent): void;
  (e: 'drag-leave', event: DragEvent): void;
  (e: 'drop-sets', event: DragEvent, targetEnabledState: boolean): void;
  (e: 'click-set', event: MouseEvent, set: SetEntity): void;
  (e: 'drag-start', event: DragEvent, set: SetEntity): void;
  (e: 'drag-end', event: DragEvent): void;
  (e: 'dblclick-set', set: SetEntity): void;
}>();
</script>
