<template>
  <Teleport to="body">
    <div class="modal-overlay" v-if="selectSetState.visible" style="display: flex;">
      <div class="modal modal-centered">
        <span class="material-symbols-outlined modal-icon primary">drive_file_move</span>
        <h2 class="modal-title">{{ t('modal.select_target_set') }}</h2>
        <select v-model="selectedSetId" class="form-control" style="margin-bottom: 1.5rem; width: 100%;">
          <option v-for="opt in flattenedSets" :key="opt.id" :value="opt.id" v-html="opt.label"></option>
        </select>
        <div class="modal-actions-spaced">
          <button class="btn btn-secondary" @click="cancel">{{ t('modal.cancel') }}</button>
          <button class="btn" @click="confirm">{{ t('modal.move') }}</button>
        </div>
      </div>
    </div>
  </Teleport>
</template>

<script setup lang="ts">
import { ref, computed, watch } from 'vue';
import { useModal } from '@/composables/useModal';
import { useI18n } from '@/composables/useI18n';

const { selectSetState } = useModal();
const { t } = useI18n();
const selectedSetId = ref<number | null>(null);

// Reset selected set when modal opens
watch(() => selectSetState.visible, (newVal) => {
  if (newVal) {
    if (flattenedSets.value.length > 0) {
      selectedSetId.value = flattenedSets.value[0].id;
    } else {
      selectedSetId.value = null;
    }
  }
});

const flattenedSets = computed(() => {
  const allSets = selectSetState.sets;
  const result: { id: number; label: string }[] = [];
  const visited = new Set<number>();
  
  const buildAndFlatten = (parentId: number | null, depth = 0) => {
    const children = allSets.filter(s => s.parentSetsEntityId === parentId);
    children.forEach(c => {
      if (!visited.has(c.id)) {
        visited.add(c.id);
        result.push({
          id: c.id,
          label: '&nbsp;'.repeat(depth * 4) + c.name
        });
        buildAndFlatten(c.id, depth + 1);
      }
    });
  };
  
  buildAndFlatten(null, 0);
  return result;
});

function cancel() {
  selectSetState.visible = false;
  if (selectSetState.resolve) selectSetState.resolve('cancelled');
}

function confirm() {
  selectSetState.visible = false;
  if (selectSetState.resolve) selectSetState.resolve(selectedSetId.value);
}
</script>
