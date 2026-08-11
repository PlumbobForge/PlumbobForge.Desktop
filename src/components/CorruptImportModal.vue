<template>
  <Teleport to="body">
    <div class="modal-overlay" v-if="corruptImportState.visible" style="display: flex;">
      <div class="modal modal-centered" style="max-width: 560px; width: 100%;">
        <div style="display: flex; align-items: center; gap: 0.75rem; margin-bottom: 0.5rem;">
          <span class="material-symbols-outlined modal-icon warning" style="margin: 0; font-size: 28px; color: #ef4444;">warning</span>
          <h2 class="modal-title" style="margin: 0;">{{ t('modal.corrupt_title') }}</h2>
        </div>

        <p style="text-align: left; font-size: 0.88rem; color: var(--text-muted); margin-bottom: 0.75rem;">
          {{ t('modal.corrupt_desc', { count: corruptImportState.corruptFiles.length }) }}
        </p>

        <!-- Corrupt files scrollable list -->
        <div class="corrupt-file-list">
          <div v-for="(file, idx) in corruptImportState.corruptFiles" :key="idx" class="corrupt-file-item" :class="file.fixable ? 'fixable' : 'unfixable'">
            <div class="corrupt-file-left">
              <span class="material-symbols-outlined" :style="{ color: file.fixable ? 'var(--primary, #10b981)' : '#ef4444', fontSize: '20px' }">
                {{ file.fixable ? 'build_circle' : 'error' }}
              </span>
              <div class="corrupt-file-details">
                <span class="corrupt-file-name" :title="file.fileName">{{ file.fileName }}</span>
                <span class="corrupt-file-reason">{{ getCorruptLabel(file.packageType) }}</span>
              </div>
            </div>
            <span class="corrupt-status-badge" :class="file.fixable ? 'badge-fixable' : 'badge-unfixable'">
              {{ file.fixable ? t('modal.corrupt_fixable') : t('modal.corrupt_unfixable') }}
            </span>
          </div>
        </div>

        <div class="modal-actions" style="margin-top: 1.5rem; justify-content: flex-end; gap: 0.5rem;">
          <button v-if="hasFixableFiles" class="btn btn-primary" @click="choose('fix')">
            <span class="material-symbols-outlined" style="font-size: 18px; margin-right: 4px;">build</span>
            {{ t('modal.corrupt_attempt_repair') }}
          </button>
          <button class="btn btn-danger" @click="choose('remove')">
            <span class="material-symbols-outlined" style="font-size: 18px; margin-right: 4px;">delete</span>
            {{ t('modal.corrupt_remove_files') }}
          </button>
          <button class="btn btn-secondary" @click="choose('ignore')">
            {{ t('modal.corrupt_ignore_keep') }}
          </button>
        </div>
      </div>
    </div>
  </Teleport>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import { useModal } from '@/composables/useModal';
import { useI18n } from '@/composables/useI18n';

const { corruptImportState } = useModal();
const { t } = useI18n();

const hasFixableFiles = computed(() => corruptImportState.corruptFiles.some(f => f.fixable));

function getCorruptLabel(pt: string): string {
  if (pt === 'Corrupt (DollDressed)') return t('corrupt.dolldressed');
  if (pt === 'Corrupt (TXTC)') return t('corrupt.txtc');
  if (pt === 'Corrupt (Bad Download)') return t('corrupt.bad_download');
  if (pt === 'Corrupt (Not a DBPF)') return t('corrupt.not_dbpf');
  if (pt === 'Sims 2 Package') return t('corrupt.sims2');
  if (pt === 'Empty Package') return t('corrupt.empty');
  return t('corrupt.generic');
}

function choose(action: 'fix' | 'remove' | 'ignore') {
  corruptImportState.visible = false;
  if (corruptImportState.resolve) {
    corruptImportState.resolve(action);
    corruptImportState.resolve = null;
  }
}
</script>

<style scoped>
.corrupt-file-list {
  max-height: 160px;
  overflow-y: auto;
  background: var(--bg-surface, rgba(0, 0, 0, 0.2));
  border: 1px solid var(--border-color, rgba(255, 255, 255, 0.1));
  border-radius: var(--radius-sm, 6px);
  padding: 0.5rem;
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
}

.corrupt-file-item {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0.5rem 0.65rem;
  background: var(--bg-card, rgba(255, 255, 255, 0.03));
  border: 1px solid var(--border-color, rgba(255, 255, 255, 0.08));
  border-radius: var(--radius-sm, 6px);
}

.corrupt-file-item.fixable {
  border-left: 3px solid var(--primary, #10b981);
}

.corrupt-file-item.unfixable {
  border-left: 3px solid #ef4444;
}

.corrupt-file-left {
  display: flex;
  align-items: center;
  gap: 0.6rem;
  overflow: hidden;
  text-align: left;
}

.corrupt-file-details {
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.corrupt-file-name {
  color: var(--text-color, #e0e0e0);
  font-family: monospace;
  font-size: 0.85rem;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.corrupt-file-reason {
  font-size: 0.75rem;
  color: #fca5a5;
  margin-top: 1px;
}

.corrupt-status-badge {
  font-size: 0.7rem;
  font-weight: 600;
  padding: 2px 8px;
  border-radius: 12px;
  white-space: nowrap;
  margin-left: 0.5rem;
}

.badge-fixable {
  background: rgba(16, 185, 129, 0.15);
  color: #34d399;
  border: 1px solid rgba(16, 185, 129, 0.3);
}

.badge-unfixable {
  background: rgba(239, 68, 68, 0.15);
  color: #fca5a5;
  border: 1px solid rgba(239, 68, 68, 0.3);
}
</style>
