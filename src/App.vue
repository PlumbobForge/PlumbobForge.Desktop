<template>
  <template v-if="appReady">
    <AppNav @rebuild="handleRebuild" />
    <RouterView v-slot="{ Component }">
      <Transition name="fade" mode="out-in">
        <component :is="Component" />
      </Transition>
    </RouterView>
  </template>
  <ToastContainer />
  <ContextMenu />
  <ConfirmModal />
  <PromptModal />
  <DeleteSetModal />
  <DeleteItemsModal />
  <SelectSetModal />
  <RetagModal />
  <RecheckConfirmModal />
  <UserTagsModal />
  <DuplicateImportModal />
  <ProgressModal />
  <WalkthroughModal :visible="showWalkthrough" @close="closeWalkthrough" />
  <ChangelogModal :visible="showChangelog" @close="closeChangelog" />
</template>

<script setup lang="ts">
import { useModal } from '@/composables/useModal'
import { useToast } from '@/composables/useToast'
import { API_BASE, startScan, importDownloads, importFiles, checkImportDuplicates, fetchSets, fetchSettings, validateGameFiles, autodetectSettings, saveSettings as saveSettingsApi } from '@/api/client'
import { useAppStore } from '@/stores/app'
import { onMounted } from 'vue'
import AppNav from '@/components/AppNav.vue'
import ToastContainer from '@/components/ToastContainer.vue'
import ContextMenu from '@/components/ContextMenu.vue'
import ConfirmModal from '@/components/ConfirmModal.vue'
import PromptModal from '@/components/PromptModal.vue'
import DeleteSetModal from '@/components/DeleteSetModal.vue'
import DeleteItemsModal from '@/components/DeleteItemsModal.vue'
import SelectSetModal from '@/components/SelectSetModal.vue'
import RetagModal from '@/components/RetagModal.vue'
import RecheckConfirmModal from '@/components/RecheckConfirmModal.vue'
import UserTagsModal from '@/components/UserTagsModal.vue'
import DuplicateImportModal from '@/components/DuplicateImportModal.vue'
import ProgressModal from '@/components/ProgressModal.vue'
import WalkthroughModal from '@/components/WalkthroughModal.vue'
import ChangelogModal from '@/components/ChangelogModal.vue'
import { useI18n } from '@/composables/useI18n'
import { useTheme } from '@/composables/useTheme'
import { ref } from 'vue'

const store = useAppStore()
const { showProgress, showDuplicateImportModal } = useModal()
const { showToast } = useToast()
const { initI18n, t } = useI18n()
const { initTheme } = useTheme()

const showWalkthrough = ref(false)
const showChangelog = ref(false)
const appReady = ref(false)

const closeChangelog = () => {
  showChangelog.value = false
}

window.addEventListener('open-changelog', () => {
  showChangelog.value = true
})

function isVersionGreater(newVer: string, oldVer: string): boolean {
  const n = newVer.split('.').map(Number)
  const o = oldVer.split('.').map(Number)
  for (let i = 0; i < Math.max(n.length, o.length); i++) {
    const nVal = n[i] || 0
    const oVal = o[i] || 0
    if (nVal > oVal) return true
    if (nVal < oVal) return false
  }
  return false
}

const initializeDirtyState = async () => {
  try {
    const sets = await fetchSets()
    store.isDirty = sets.some(s => s.dirty)
  } catch (err) {
    console.error('Failed to initialize dirty state:', err)
  }
}

const closeWalkthrough = async () => {
  showWalkthrough.value = false
  localStorage.setItem('plumbobforge_has_seen_walkthrough', 'true')
  try {
    const settings = await fetchSettings()
    settings.hasSeenWalkthrough = true
    settings.HasSeenWalkthrough = true
    await saveSettingsApi(settings, false)
  } catch (e) {
    console.error('Failed to save walkthrough state', e)
  }
  appReady.value = true
  initializeDirtyState()
}

const initNotificationStream = () => {
  try {
    const url = `${API_BASE}/notifications/stream`
    const es = new EventSource(url)
    es.onmessage = async (event) => {
      try {
        const payload = JSON.parse(event.data)
        if (payload.eventName === 'items_imported') {
          const count = payload.data?.count || 1
          store.lastImportedAt = Date.now()
          store.isDirty = true
          showToast(`Imported ${count} item(s) from Downloads!`, 'success')
          window.dispatchEvent(new CustomEvent('items-updated'))
        } else if (payload.eventName === 'library_changed') {
          store.isDirty = true
          window.dispatchEvent(new CustomEvent('items-updated'))
        } else if (payload.eventName === 'auto_import_duplicates') {
          const duplicates = payload.data || []
          if (duplicates.length > 0) {
            const choice = await showDuplicateImportModal(duplicates)
            if (choice) {
              await importDownloads(choice)
            }
          }
        }
      } catch (err) {
        console.error('Error handling notification payload:', err)
      }
    }
  } catch (err) {
    console.error('Failed to connect to notification stream:', err)
  }
}

onMounted(async () => {
  initTheme() // initialize default theme early
  initNotificationStream()

  if ((window as any).electronAPI) {
    store.appVersion = await (window as any).electronAPI.getAppVersion()

    ;(window as any).electronAPI.onUpdateEvent('update-available', (info: any) => {
      store.updateStatus = 'available'
      store.updateInfo = info
      showToast('An update is available! Check the settings to download it.', 'info')
    })
    ;(window as any).electronAPI.onUpdateEvent('update-not-available', () => {
      store.updateStatus = 'not-available'
      setTimeout(() => store.updateStatus = 'idle', 5000)
    })
    ;(window as any).electronAPI.onUpdateEvent('update-error', (err: string) => {
      store.updateStatus = 'error'
      console.error('Update error:', err)
      setTimeout(() => store.updateStatus = 'idle', 5000)
    })
    ;(window as any).electronAPI.onUpdateEvent('download-progress', (progressObj: any) => {
      store.updateStatus = 'downloading'
      store.downloadPercent = progressObj.percent
    })
    ;(window as any).electronAPI.onUpdateEvent('update-downloaded', () => {
      store.updateStatus = 'downloaded'
      showToast('Update downloaded! You can now install it from settings.', 'success')
    })

    // Check for updates silently on startup
    ;(window as any).electronAPI.checkForUpdates()
  }

  try {
    const [settings, sets] = await Promise.all([
      fetchSettings().catch(() => null),
      fetchSets().catch(() => [])
    ])

    const localSeen = localStorage.getItem('plumbobforge_has_seen_walkthrough') === 'true'
    const settingsSeen = !!(settings?.hasSeenWalkthrough || settings?.HasSeenWalkthrough)
    const hasSeenWalkthrough = localSeen || settingsSeen

    if (settings) {
      const lang = settings.language || settings.Language || 'auto'
      const theme = settings.theme || settings.Theme || 'auto'
      store.cacheMethod = settings.cacheMethod || settings.CacheMethod || 'Dynamic'
      await initI18n(lang)
      initTheme(theme)

      if (localSeen && !settingsSeen) {
        settings.hasSeenWalkthrough = true
        settings.HasSeenWalkthrough = true
        saveSettingsApi(settings, false).catch(() => {})
      }
    } else {
      await initI18n('en')
      initTheme('auto')
    }

    if (sets && sets.length > 0) {
      store.isDirty = sets.some(s => s.dirty)
    }

    const currentVersion = store.appVersion || '0.2.8'
    const lastSeenVer = localStorage.getItem('plumbobforge_last_seen_version')

    if (!hasSeenWalkthrough) {
      showWalkthrough.value = true
      localStorage.setItem('plumbobforge_last_seen_version', currentVersion)
    } else {
      showWalkthrough.value = false
      if (lastSeenVer && isVersionGreater(currentVersion, lastSeenVer)) {
        if (isVersionGreater('0.2.0', lastSeenVer)) {
          showChangelog.value = true
        }
      } else if (!lastSeenVer) {
        // Initial setup with walkthrough completed
      }
      localStorage.setItem('plumbobforge_last_seen_version', currentVersion)
    }
  } catch (err) {
    console.error('Failed to initialize app settings:', err)
    if (localStorage.getItem('plumbobforge_has_seen_walkthrough') !== 'true') {
      showWalkthrough.value = true
    }
  } finally {
    appReady.value = true
  }
})

const handleRebuild = async () => {
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

const handleImport = async () => {
  if ((window as any).electronAPI?.showOpenDialog) {
    try {
      const filePaths = await (window as any).electronAPI.showOpenDialog({
        title: t('nav.import_cc'),
        filters: [{ name: 'Sims 3 Package & Sims3Pack', extensions: ['package', 'sims3pack'] }],
        properties: ['openFile', 'multiSelections']
      })
      if (!filePaths || filePaths.length === 0) return

      const fileNames = filePaths.map((p: string) => p.split(/[/\\]/).pop() || p)
      const dupCheck = await checkImportDuplicates(fileNames)
      let duplicateAction = 'rename'

      if (dupCheck.hasDuplicates) {
        const choice = await showDuplicateImportModal(dupCheck.duplicates)
        if (!choice) return // User cancelled import
        duplicateAction = choice
      }

      const progress = showProgress(t('nav.import_cc'))
      const res = await importFiles(filePaths, duplicateAction)
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
              window.location.reload()
              return
            }
            progress.appendLog(msg)
          }
        }
      }
    } catch (err: any) {
      showToast(err.message || 'Failed to import files', 'error')
    }
  } else {
    // Fallback: import from Downloads folder
    const progress = showProgress(t('nav.import_cc'))
    try {
      const res = await importDownloads()
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
              window.location.reload()
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
}
</script>
