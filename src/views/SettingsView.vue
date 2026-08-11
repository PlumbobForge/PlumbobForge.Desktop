<template>
  <div id="view-settings" class="view settings-view-container">

    <!-- Version & Updates Hero Card -->
    <SettingsUpdateHero
      @check-updates="checkUpdates"
      @download-update="downloadUpdate"
      @install-update="installUpdate"
      @open-changelog="openChangelog"
    />

    <!-- 2-Column Responsive Layout -->
    <div class="settings-2col-layout">

      <!-- LEFT COLUMN: Preferences & Optimization -->
      <div class="settings-col">
        
        <!-- Appearance & Preferences -->
        <SettingsPreferencesCard
          :language="settings.language"
          :theme="settings.theme"
          :setsCount="setsCount"
          :itemsCount="itemsCount"
          @select-language="selectLanguage"
          @select-theme="selectTheme"
        />

        <!-- Cache & Game Optimization -->
        <SettingsOptimizationCard
          :cacheMethod="settings.cacheMethod"
          :compressionLevel="settings.compressionLevel"
          @select-cache-method="setCacheMethod"
          @select-compression-level="setCompressionLevel"
        />

        <!-- Special Thanks Card -->
        <SettingsSpecialThanksCard />

      </div>

      <!-- RIGHT COLUMN: Folder Configuration & Maintenance -->
      <div class="settings-col">

        <!-- Folder Configurations & Downloads -->
        <SettingsFolderConfigCard
          v-model:documentBaseDir="settings.documentBaseDir"
          v-model:gameFilesDir="settings.gameFilesDir"
          :observedFolders="settings.observedFolders"
          :saving="saving"
          :saved="saved"
          @blur-base-dir="onBaseDirBlur"
          @blur-game-dir="onGameFilesDirBlur"
          @browse-base-dir="browseDocumentBaseDir"
          @browse-game-dir="browseGameFilesDir"
          @run-import-downloads="runImportDownloads"
          @browse-observed-folder="browseObservedFolder"
          @remove-observed-folder="removeObservedFolder"
        />

        <!-- Maintenance & Tools -->
        <SettingsMaintenanceCard
          @recheck-types="confirmRecheckTypes"
          @autofix="confirmAutoFix"
          @migrate="onMigrate"
        />

      </div>

    </div>

  </div>
</template>

<script setup lang="ts">
import { ref, watch, onMounted, onUnmounted } from 'vue'
import { fetchSettings, saveSettings as saveSettingsApi, autodetectSettings, validateGameFiles, migrate, startScan, autoFixDatabase, startRecheckTypes, fetchSets, fetchItems, importDownloads, checkDownloadsDuplicates } from '@/api/client'
import { useModal } from '@/composables/useModal'
import { useToast } from '@/composables/useToast'
import { useAppStore } from '@/stores/app'
import { useI18n } from '@/composables/useI18n'
import { useTheme } from '@/composables/useTheme'
import SettingsUpdateHero from '@/components/settings/SettingsUpdateHero.vue'
import SettingsPreferencesCard from '@/components/settings/SettingsPreferencesCard.vue'
import SettingsOptimizationCard from '@/components/settings/SettingsOptimizationCard.vue'
import SettingsFolderConfigCard from '@/components/settings/SettingsFolderConfigCard.vue'
import SettingsMaintenanceCard from '@/components/settings/SettingsMaintenanceCard.vue'
import SettingsSpecialThanksCard from '@/components/settings/SettingsSpecialThanksCard.vue'

const { showConfirm, showProgress, showRecheckConfirm, showDuplicateImportModal } = useModal()
const { showToast } = useToast()
const store = useAppStore()
const { t, setLanguage } = useI18n()
const { setTheme } = useTheme()

const settings = ref({
  documentBaseDir: '',
  managedPackageFolderName: 'Library',
  setCacheFolderName: 'Builds',
  compressionLevel: 1,
  cacheMethod: 'Dynamic',
  gameFilesDir: '',
  language: 'auto',
  theme: 'auto',
  observedFolders: [] as string[]
})

let initialBaseDir = ''
let initialGameFilesDir = ''

const saving = ref(false)
const saved = ref(false)
const isInitialLoad = ref(true)

const cacheMethodDropdownOpen = ref(false)
const compressionDropdownOpen = ref(false)
const languageDropdownOpen = ref(false)
const themeDropdownOpen = ref(false)

const closeDropdowns = () => {
  if (cacheMethodDropdownOpen.value) cacheMethodDropdownOpen.value = false
  if (compressionDropdownOpen.value) compressionDropdownOpen.value = false
  if (languageDropdownOpen.value) languageDropdownOpen.value = false
  if (themeDropdownOpen.value) themeDropdownOpen.value = false
}

const getCacheMethodLabel = (method: string) => {
  return method === 'Static' ? t('settings.cache_method_static') : t('settings.cache_method_dynamic')
}

const setCacheMethod = (method: string) => {
  const isChanged = settings.value.cacheMethod !== method
  settings.value.cacheMethod = method
  cacheMethodDropdownOpen.value = false
  store.cacheMethod = method
  if (isChanged) {
    store.isDirty = true
  }
  onSettingsChange()
}

const getLanguageLabel = (lang: string) => {
  switch (lang) {
    case 'auto': return t('settings.language_auto')
    case 'en': return 'English'
    case 'pl': return 'Polski'
    case 'uk': return 'Українська'
    case 'el': return 'Ελληνικά'
    default: return t('settings.language_auto')
  }
}

const selectLanguage = (lang: string) => {
  settings.value.language = lang
  languageDropdownOpen.value = false
  setLanguage(lang)
  onSettingsChange()
}

const getThemeLabel = (theme: string) => {
  switch (theme) {
    case 'auto': return t('settings.theme_auto')
    case 'dark': return t('settings.theme_dark')
    case 'light': return t('settings.theme_light')
    default: return t('settings.theme_auto')
  }
}

const selectTheme = (theme: string) => {
  settings.value.theme = theme
  themeDropdownOpen.value = false
  setTheme(theme)
  onSettingsChange()
}

onMounted(() => {
  document.addEventListener('click', closeDropdowns)
})

onUnmounted(() => {
  document.removeEventListener('click', closeDropdowns)
})

const getCompressionLabel = (level: number) => {
  switch (level) {
    case 0: return t('settings.no_compression')
    case 1: return t('settings.low_compression')
    case 2: return t('settings.medium_compression')
    case 3: return t('settings.high_compression')
    case 4: return t('settings.very_high_compression')
    default: return t('settings.low_compression')
  }
}

const setCompressionLevel = (level: number) => {
  settings.value.compressionLevel = level
  compressionDropdownOpen.value = false
  onSettingsChange()
}

const setsCount = ref(0)
const itemsCount = ref(0)

const onBaseDirBlur = async () => {
  if (settings.value.documentBaseDir === initialBaseDir) return; // No change

  let moveFolder = false;
  if (initialBaseDir) {
    moveFolder = await showConfirm(
      t('settings.move_folder_title'),
      t('settings.move_folder_msg', { oldPath: initialBaseDir, newPath: settings.value.documentBaseDir })
    );
  }

  saving.value = true;
  saved.value = false;

  try {
    await saveSettingsApi({
      DocumentBaseDir: settings.value.documentBaseDir,
      GameFilesDir: settings.value.gameFilesDir,
      ManagedPackageFolderName: 'Library',
      SetCacheFolderName: 'Builds',
      DownloadFolderName: '',
      ArchiveFolderName: '',
      TS3PackFolderName: '',
      LegacyPackageFolderName: '',
      TS3PackStoreFolderName: '',
      CompressionLevel: settings.value.compressionLevel,
      ObservedFolders: settings.value.observedFolders
    }, moveFolder)

    saving.value = false;
    saved.value = true;
    initialBaseDir = settings.value.documentBaseDir; // Update the reference point
    setTimeout(() => { saved.value = false }, 3000);
  } catch (e: any) {
    saving.value = false;
    showToast(e.message || t('cm.failed_toast'), 'error');
    // Revert visually on error
    settings.value.documentBaseDir = initialBaseDir;
  }
}

const onGameFilesDirBlur = async () => {
  if (settings.value.gameFilesDir === initialGameFilesDir) return;

  if (settings.value.gameFilesDir.trim() !== '') {
    try {
      const res = await validateGameFiles(settings.value.gameFilesDir)
      if (!res.valid) {
        const proceed = await showConfirm(t('settings.warning_title'), t('settings.invalid_game_dir_msg'));
        if (!proceed) {
          settings.value.gameFilesDir = initialGameFilesDir;
          return;
        }
      } else if (res.normalizedPath) {
        settings.value.gameFilesDir = res.normalizedPath;
      }
    } catch (e) {
      const proceed = await showConfirm(t('settings.warning_title'), t('settings.game_dir_error_msg'));
      if (!proceed) {
        settings.value.gameFilesDir = initialGameFilesDir;
        return;
      }
    }
  }

  saving.value = true;
  saved.value = false;

  try {
    await saveSettingsApi({
      DocumentBaseDir: settings.value.documentBaseDir,
      GameFilesDir: settings.value.gameFilesDir,
      ManagedPackageFolderName: 'Library',
      SetCacheFolderName: 'Builds',
      DownloadFolderName: '',
      ArchiveFolderName: '',
      TS3PackFolderName: '',
      LegacyPackageFolderName: '',
      TS3PackStoreFolderName: '',
      CompressionLevel: settings.value.compressionLevel,
      ObservedFolders: settings.value.observedFolders
    }, false)

    saving.value = false;
    saved.value = true;
    initialGameFilesDir = settings.value.gameFilesDir;
    setTimeout(() => { saved.value = false }, 3000);
  } catch (e: any) {
    saving.value = false;
    showToast(e.message || t('cm.failed_toast'), 'error');
    settings.value.gameFilesDir = initialGameFilesDir;
  }
}

const onSettingsChange = async () => {
  if (isInitialLoad.value) return;
  saving.value = true;
  saved.value = false;

  try {
    await saveSettingsApi({
      DocumentBaseDir: settings.value.documentBaseDir,
      GameFilesDir: settings.value.gameFilesDir,
      ManagedPackageFolderName: 'Library',
      SetCacheFolderName: 'Builds',
      DownloadFolderName: '',
      ArchiveFolderName: '',
      TS3PackFolderName: '',
      LegacyPackageFolderName: '',
      TS3PackStoreFolderName: '',
      CompressionLevel: settings.value.compressionLevel,
      CacheMethod: settings.value.cacheMethod,
      Language: settings.value.language,
      Theme: settings.value.theme,
      ObservedFolders: settings.value.observedFolders
    }, false)

    saving.value = false;
    saved.value = true;
    setTimeout(() => { saved.value = false }, 3000);
  } catch (e: any) {
    saving.value = false;
    showToast(e.message || t('cm.failed_toast'), 'error');
  }
}

const browseObservedFolder = async () => {
  if ((window as any).electronAPI) {
    const selectedPath = await (window as any).electronAPI.selectDirectory();
    if (selectedPath) {
      if (!settings.value.observedFolders) settings.value.observedFolders = [];
      if (!settings.value.observedFolders.includes(selectedPath)) {
        settings.value.observedFolders.push(selectedPath);
        await onSettingsChange();
      }
    }
  }
}

const removeObservedFolder = async (index: number) => {
  if (settings.value.observedFolders && index >= 0 && index < settings.value.observedFolders.length) {
    settings.value.observedFolders.splice(index, 1);
    await onSettingsChange();
  }
}

onMounted(async () => {
  try {
    const data = await fetchSettings()
    settings.value.documentBaseDir = data.documentBaseDir || data.DocumentBaseDir || ''
    settings.value.managedPackageFolderName = 'Library'
    settings.value.setCacheFolderName = 'Builds'
    settings.value.compressionLevel = data.compressionLevel ?? data.CompressionLevel ?? 1
    settings.value.cacheMethod = data.cacheMethod || data.CacheMethod || 'Dynamic'
    store.cacheMethod = settings.value.cacheMethod
    settings.value.gameFilesDir = data.gameFilesDir || data.GameFilesDir || ''
    settings.value.language = data.language || data.Language || 'auto'
    settings.value.theme = data.theme || data.Theme || 'auto'
    settings.value.observedFolders = data.observedFolders || data.ObservedFolders || []
    setLanguage(settings.value.language)
    setTheme(settings.value.theme)

    initialBaseDir = settings.value.documentBaseDir
    initialGameFilesDir = settings.value.gameFilesDir

    const [sets, items] = await Promise.all([
      fetchSets().catch(() => []),
      fetchItems().catch(() => [])
    ])
    setsCount.value = sets.length
    itemsCount.value = items.length

    setTimeout(() => { isInitialLoad.value = false }, 500)
  } catch (err: any) {
    console.error('Failed to load settings:', err)
    isInitialLoad.value = false
  }
})

const onMigrate = async () => {
  if (await showConfirm(
    t('settings.migrate_confirm_title'),
    t('settings.migrate_confirm_msg')
  )) {
    try {
      const data = await migrate()
      if (data && data.copied > 0) {
        showToast(t('settings.migrate_success', { count: data.copied }), 'success')
      } else {
        showToast(t('settings.migrate_no_files'), 'info')
      }
    } catch (e: any) {
      showToast(e.message || t('settings.migrate_error'), 'error')
    }
  }
}

const runImportDownloads = async () => {
  try {
    const dupCheck = await checkDownloadsDuplicates()
    let duplicateAction = 'rename'

    if (dupCheck.hasDuplicates) {
      const choice = await showDuplicateImportModal(dupCheck.duplicates)
      if (!choice) return // User cancelled import
      duplicateAction = choice
    }

    const progress = showProgress(t('settings.import_downloads_btn'))
    const res = await importDownloads(duplicateAction)
    const reader = res.body?.getReader()
    if (!reader) throw new Error('No reader available')

    const decoder = new TextDecoder()
    while (true) {
      const { done, value } = await reader.read()
      if (done) break
      const text = decoder.decode(value)
      const lines = text.split('\n')
      for (const line of lines) {
        if (line.startsWith('data: ')) {
          const msg = line.substring(6).trim()
          if (msg === 'DONE') {
            progress.finish(true)
            const [sets, items] = await Promise.all([
              fetchSets().catch(() => []),
              fetchItems().catch(() => [])
            ])
            setsCount.value = sets.length
            itemsCount.value = items.length
            store.lastImportedAt = Date.now()
            showToast('Imported items from Downloads successfully!', 'success')
            window.dispatchEvent(new CustomEvent('items-updated'))
            return
          }
          progress.appendLog(msg)
        }
      }
    }
  } catch (err: any) {
    showToast(err.message || 'Failed to import downloads', 'error')
  }
}

const confirmRunScan = async () => {
  if (await showConfirm(
    t('settings.rebuild_cache_title'),
    t('settings.recheck_confirm_msg')
  )) {
    runScan()
  }
}

const runScan = async () => {
  const progress = showProgress(t('settings.rebuild_cache_title'))
  try {
    const res = await startScan()
    const reader = res.body?.getReader()
    if (!reader) throw new Error('No reader available')

    const decoder = new TextDecoder()
    while (true) {
      const { done, value } = await reader.read()
      if (done) break
      const text = decoder.decode(value)
      const lines = text.split('\n')
      for (const line of lines) {
        if (line.startsWith('data: ')) {
          const msg = line.substring(6).trim()
          if (msg === 'DONE') {
            progress.finish(true)
            store.isDirty = false
            return
          }
          progress.appendLog(msg)
        }
      }
    }
  } catch (err) {
    progress.appendLog('Error: ' + err)
    progress.finish(false)
  }
}

const confirmRecheckTypes = async () => {
  const result = await showRecheckConfirm()
  if (result && result.confirmed) {
    runRecheckTypes(result.skipUserTagged)
  }
}

const runRecheckTypes = async (skipUserTagged: boolean = true) => {
  const progress = showProgress(t('settings.recheck_types_title'))
  try {
    const res = await startRecheckTypes(skipUserTagged)
    const reader = res.body?.getReader()
    if (!reader) throw new Error('No reader available')

    const decoder = new TextDecoder()
    while (true) {
      const { done, value } = await reader.read()
      if (done) break
      const text = decoder.decode(value)
      const lines = text.split('\n')
      for (const line of lines) {
        if (line.startsWith('data: ')) {
          const msg = line.substring(6).trim()
          if (msg === 'DONE') {
            progress.finish(true)
            showToast(t('cm.items_toggled_toast'), 'success')
            return
          }
          progress.appendLog(msg)
        }
      }
    }
  } catch (err) {
    progress.appendLog('Error: ' + err)
    progress.finish(false)
  }
}

const confirmAutoFix = async () => {
  if (await showConfirm(
    t('settings.autofix_confirm_title'),
    t('settings.autofix_confirm_msg')
  )) {
    runAutoFix()
  }
}

const runAutoFix = async () => {
  const progress = showProgress(t('settings.autofix_title'))
  try {
    const res = await autoFixDatabase()
    const reader = res.body?.getReader()
    if (!reader) throw new Error('No reader available')

    const decoder = new TextDecoder()
    while (true) {
      const { done, value } = await reader.read()
      if (done) break
      const text = decoder.decode(value)
      const lines = text.split('\n')
      for (const line of lines) {
        if (line.startsWith('data: ')) {
          const msg = line.substring(6).trim()
          if (msg === 'DONE') {
            progress.finish(true)
            store.isDirty = false
            return
          }
          progress.appendLog(msg)
        }
      }
    }
  } catch (err) {
    progress.appendLog('Error: ' + err)
    progress.finish(false)
  }
}

const browseDocumentBaseDir = async () => {
  if ((window as any).electronAPI) {
    const selectedPath = await (window as any).electronAPI.selectDirectory();
    if (selectedPath) {
      settings.value.documentBaseDir = selectedPath;
      await onBaseDirBlur();
    }
  }
}

const browseGameFilesDir = async () => {
  if ((window as any).electronAPI) {
    const selectedPath = await (window as any).electronAPI.selectDirectory();
    if (selectedPath) {
      settings.value.gameFilesDir = selectedPath;
      await onGameFilesDirBlur();
    }
  }
}

const checkUpdates = () => {
  if ((window as any).electronAPI) {
    store.updateStatus = 'checking'
    ;(window as any).electronAPI.checkForUpdates()
  }
}

const downloadUpdate = () => {
  if ((window as any).electronAPI) {
    store.updateStatus = 'downloading'
    store.downloadPercent = 0
    ;(window as any).electronAPI.downloadUpdate()
  }
}

const installUpdate = () => {
  if ((window as any).electronAPI) {
    ;(window as any).electronAPI.installUpdate()
  }
}

const openChangelog = () => {
  window.dispatchEvent(new CustomEvent('open-changelog'))
}
</script>
