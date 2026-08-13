<template>
  <div class="card">
    <div class="settings-folder-header">
      <h3 class="settings-card-title m-0">
        <span class="material-symbols-outlined title-icon">folder_open</span>
        {{ t('settings.configure_folders') }}
      </h3>
      <span v-if="saving" class="settings-saving-text">
         <span class="material-symbols-outlined spin icon-14">progress_activity</span>
         {{ t('settings.saving') }}
      </span>
      <span v-else-if="saved" class="settings-saved-text">
         <span class="material-symbols-outlined icon-14">check</span>
         {{ t('settings.saved') }}
      </span>
    </div>

    <div class="settings-form-flex" style="flex-direction: column; gap: 1rem;">
      <!-- Document Base Dir -->
      <div class="form-group">
        <label>{{ t('settings.doc_dir') }}</label>
        <div style="display: flex; gap: 0.5rem;">
          <input
            type="text"
            :value="documentBaseDir"
            @input="$emit('update:documentBaseDir', ($event.target as HTMLInputElement).value)"
            class="form-control"
            style="flex: 1;"
            @blur="$emit('blur-base-dir')"
          />
          <button class="btn btn-secondary" style="padding: 0 1rem;" @click="$emit('browse-base-dir')">{{ t('settings.browse') }}</button>
        </div>
        <div class="form-text">{{ t('settings.doc_dir_desc') }}</div>
      </div>

      <!-- Game Files Dir -->
      <div class="form-group">
        <label>{{ t('settings.game_dir') }}</label>
        <div style="display: flex; gap: 0.5rem;">
          <input
            type="text"
            :value="gameFilesDir"
            @input="$emit('update:gameFilesDir', ($event.target as HTMLInputElement).value)"
            class="form-control"
            style="flex: 1;"
            @blur="$emit('blur-game-dir')"
            placeholder="e.g. C:\Program Files\EA Games\The Sims 3"
          />
          <button class="btn btn-secondary" style="padding: 0 1rem;" @click="$emit('browse-game-dir')">{{ t('settings.browse') }}</button>
        </div>
        <div class="form-text">{{ t('settings.game_dir_desc') }}</div>
      </div>

      <!-- Auto Import Box -->
      <div class="auto-import-box">
        <div style="display: flex; align-items: center; justify-content: space-between; margin-bottom: 0.5rem;">
          <div style="font-weight: 600; font-size: 0.95rem;">{{ t('settings.auto_import_title') }}</div>
          <label style="display: inline-flex; align-items: center; cursor: pointer; gap: 0.5rem;">
            <span style="font-size: 0.8rem; color: var(--text-muted); font-weight: 500;">
              {{ enableAutoScan ? t('settings.auto_scan_on') : t('settings.auto_scan_off') }}
            </span>
            <input
              type="checkbox"
              :checked="enableAutoScan"
              @change="$emit('update:enableAutoScan', ($event.target as HTMLInputElement).checked)"
              style="width: 18px; height: 18px; cursor: pointer; accent-color: var(--primary);"
            />
          </label>
        </div>
        <div style="color: var(--text-muted); font-size: 0.85rem; margin-bottom: 0.75rem; line-height: 1.4;">
          {{ t('settings.auto_import_desc') }}
        </div>
        <button class="btn btn-info-outline" id="btn-import-downloads" style="width: 100%; justify-content: center;" @click="$emit('run-import-downloads')">
          <span class="material-symbols-outlined icon-16-mr">download</span>
          {{ t('settings.import_downloads_btn') }}
        </button>
      </div>

      <!-- Observed Folders Section -->
      <div class="observed-folders-section" style="border-top: 1px solid var(--border-subtle, rgba(255,255,255,0.08)); padding-top: 1rem; margin-top: 0.5rem;">
        <div style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 0.4rem;">
          <label style="font-weight: 600; font-size: 0.95rem; margin: 0;">{{ t('settings.observed_folders_title') }}</label>
          <button class="btn btn-secondary btn-sm" style="font-size: 0.8rem; padding: 2px 10px; display: flex; align-items: center;" @click="$emit('browse-observed-folder')">
            <span class="material-symbols-outlined icon-14" style="margin-right: 4px;">add</span>
            {{ t('settings.add_observed_folder') }}
          </button>
        </div>
        <div class="form-text" style="margin-bottom: 0.75rem;">{{ t('settings.observed_folders_desc') }}</div>

        <div v-if="observedFolders && observedFolders.length > 0" class="observed-folders-list" style="display: flex; flex-direction: column; gap: 0.5rem;">
          <div v-for="(folderPath, idx) in observedFolders" :key="idx" class="observed-folder-item" style="display: flex; align-items: center; justify-content: space-between; background: var(--bg-secondary, rgba(255,255,255,0.03)); border: 1px solid var(--border-subtle, rgba(255,255,255,0.08)); padding: 0.4rem 0.75rem; border-radius: 6px;">
            <div style="display: flex; align-items: center; gap: 0.5rem; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; flex: 1;">
              <span class="material-symbols-outlined" style="font-size: 18px; color: var(--primary, #6366f1);">folder</span>
              <span style="font-size: 0.85rem; color: var(--text-main, #f8fafc); overflow: hidden; text-overflow: ellipsis;">{{ folderPath }}</span>
            </div>
            <button class="btn-icon" style="background: none; border: none; color: var(--text-muted, #94a3b8); cursor: pointer; padding: 2px; border-radius: 4px; display: flex; align-items: center;" title="Remove folder" @click="$emit('remove-observed-folder', idx)">
              <span class="material-symbols-outlined" style="font-size: 18px;">close</span>
            </button>
          </div>
        </div>
        <div v-else class="observed-folder-default-badge" style="font-size: 0.82rem; font-style: italic; color: var(--text-muted, #94a3b8); background: var(--bg-secondary, rgba(255,255,255,0.02)); border: 1px dashed var(--border-subtle, rgba(255,255,255,0.1)); padding: 0.5rem 0.75rem; border-radius: 6px;">
          <span class="material-symbols-outlined" style="font-size: 16px; vertical-align: text-bottom; margin-right: 4px; color: var(--primary, #6366f1);">info</span>
          {{ t('settings.default_observed_folder_hint') }}
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { useI18n } from '@/composables/useI18n';

const { t } = useI18n();

const props = defineProps<{
  documentBaseDir: string;
  gameFilesDir: string;
  enableAutoScan: boolean;
  observedFolders: string[];
  saving: boolean;
  saved: boolean;
}>();

const emit = defineEmits<{
  (e: 'update:documentBaseDir', val: string): void;
  (e: 'update:gameFilesDir', val: string): void;
  (e: 'update:enableAutoScan', val: boolean): void;
  (e: 'blur-base-dir'): void;
  (e: 'blur-game-dir'): void;
  (e: 'browse-base-dir'): void;
  (e: 'browse-game-dir'): void;
  (e: 'run-import-downloads'): void;
  (e: 'browse-observed-folder'): void;
  (e: 'remove-observed-folder', index: number): void;
}>();
</script>
