<template>
  <div id="view-configurations" class="view">
    <div class="content-manager-layout">

      <!-- Sidebar: Configurations Tree -->
      <ConfigSidebar
        :configs="sortedConfigs"
        :currentConfigId="currentConfig?.id"
        :loading="loading"
        v-model:configSortMode="configSortMode"
        @create-config="createConfig"
        @select-config="selectConfig"
        @config-context-menu="onConfigContextMenu"
      />

      <!-- Main Area: Sets Checkboxes -->
      <div class="cm-main">
        <ConfigHeader
          :currentConfig="currentConfig"
          :totalSetsCount="allSets.length"
          v-model:setSortMode="setSortMode"
          @rename-config="handleRenameConfig"
          @edit-description="handleEditDescription"
        />

        <ConfigSetsPanel
          :enabledSets="enabledSets"
          :disabledSets="disabledSets"
          :selectedSetIds="selectedSetIds"
          :getAncestors="getSetAncestors"
          :getDescendantCount="getDescendantCount"
          @drag-over="onDragOver"
          @drag-leave="onDragLeave"
          @drop-sets="onDrop"
          @click-set="onClickSet"
          @drag-start="onDragStart"
          @drag-end="onDragEnd"
          @dblclick-set="onDblClickSet"
        />
      </div>

    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { fetchConfigurations, fetchSets, createConfiguration, deleteConfiguration, activateConfiguration, updateConfigurationSets, renameConfiguration, duplicateConfiguration, updateConfigurationDescription } from '@/api/client'
import { useModal } from '@/composables/useModal'
import { useToast } from '@/composables/useToast'
import { useContextMenu } from '@/composables/useContextMenu'
import { useI18n } from '@/composables/useI18n'
import { useSelection } from '@/composables/useSelection'
import { useAppStore } from '@/stores/app'
import ConfigSidebar from '@/components/config/ConfigSidebar.vue'
import ConfigHeader from '@/components/config/ConfigHeader.vue'
import ConfigSetsPanel from '@/components/config/ConfigSetsPanel.vue'

const { showPrompt, showConfirm } = useModal()
const { showToast } = useToast()
const { showContextMenu } = useContextMenu()
const { t } = useI18n()
const store = useAppStore()

// Multi-selection state for set cards
const selectedSetIds = ref(new Set<number>())
const { initSelection, destroySelection, isDraggingSelection, wasJustDragging } = useSelection(selectedSetIds.value)

const allConfigs = ref<Configuration[]>([])
const allSets = ref<SetEntity[]>([])
const currentConfig = ref<Configuration | null>(null)
const loading = ref(true)

// Sorting modes & dropdown open states
const configSortMode = ref<'date' | 'alpha_asc' | 'alpha_desc'>('date')
const setSortMode = ref<'date' | 'alpha_asc' | 'alpha_desc' | 'subsets_desc'>('date')

const configSortDropdownOpen = ref(false)
const setSortDropdownOpen = ref(false)

const handleDocumentClick = (e: MouseEvent) => {
  if (configSortDropdownOpen.value) configSortDropdownOpen.value = false
  if (setSortDropdownOpen.value) setSortDropdownOpen.value = false

  if (isDraggingSelection.value || wasJustDragging.value) return

  // Clear selection if clicking empty space in config view
  const target = e.target as HTMLElement
  if (!target.closest('.config-set-card') && !target.closest('.selection-area')) {
    if (!e.ctrlKey && !e.metaKey && !e.shiftKey) {
      selectedSetIds.value.clear()
    }
  }
}

const handleKeyDown = (e: KeyboardEvent) => {
  if (e.key === 'Escape') {
    selectedSetIds.value.clear()
  }
}

onMounted(() => {
  loadData()
  document.addEventListener('click', handleDocumentClick)
  window.addEventListener('keydown', handleKeyDown)
  initSelection('.config-main-flex')
})

onUnmounted(() => {
  document.removeEventListener('click', handleDocumentClick)
  window.removeEventListener('keydown', handleKeyDown)
  destroySelection()
})

const getConfigSortLabel = () => {
  switch (configSortMode.value) {
    case 'alpha_asc': return t('config.sort_alpha_asc')
    case 'alpha_desc': return t('config.sort_alpha_desc')
    default: return t('config.sort_date')
  }
}

const getSetSortLabel = () => {
  switch (setSortMode.value) {
    case 'alpha_asc': return t('config.sort_alpha_asc')
    case 'alpha_desc': return t('config.sort_alpha_desc')
    case 'subsets_desc': return t('config.sort_subsets')
    default: return t('config.sort_date')
  }
}

// Fast set lookup map
const setsById = computed(() => {
  const map = new Map<number, SetEntity>()
  allSets.value.forEach(s => map.set(s.id, s))
  return map
})

// Get parent ancestors from root to immediate parent
const getSetAncestors = (set: SetEntity): SetEntity[] => {
  const ancestors: SetEntity[] = []
  let currId = set.parentSetsEntityId
  const visited = new Set<number>()
  while (currId != null && !visited.has(currId)) {
    visited.add(currId)
    const parent = setsById.value.get(currId)
    if (!parent) break
    ancestors.unshift(parent)
    currId = parent.parentSetsEntityId
  }
  return ancestors
}

// Get all descendant sub-set IDs recursively
const getAllDescendantIds = (setId: number): number[] => {
  const descendants: number[] = []
  const queue = [setId]
  const visited = new Set<number>([setId])

  while (queue.length > 0) {
    const parentId = queue.shift()!
    const children = allSets.value.filter(s => s.parentSetsEntityId === parentId)
    for (const child of children) {
      if (!visited.has(child.id)) {
        visited.add(child.id)
        descendants.push(child.id)
        queue.push(child.id)
      }
    }
  }
  return descendants
}

const getDescendantCount = (set: SetEntity): number => {
  return getAllDescendantIds(set.id).length
}

// Sorted Configurations list
const sortedConfigs = computed(() => {
  const list = [...allConfigs.value]
  if (configSortMode.value === 'alpha_asc') {
    return list.sort((a, b) => a.name.localeCompare(b.name))
  } else if (configSortMode.value === 'alpha_desc') {
    return list.sort((a, b) => b.name.localeCompare(a.name))
  }
  return list.sort((a, b) => a.id - b.id)
})

const sortSetList = (list: SetEntity[]) => {
  const arr = [...list]
  if (setSortMode.value === 'alpha_asc') {
    return arr.sort((a, b) => a.name.localeCompare(b.name))
  } else if (setSortMode.value === 'alpha_desc') {
    return arr.sort((a, b) => b.name.localeCompare(a.name))
  } else if (setSortMode.value === 'subsets_desc') {
    return arr.sort((a, b) => getDescendantCount(b) - getDescendantCount(a))
  }
  return arr.sort((a, b) => a.id - b.id)
}

const enabledSets = computed(() => {
  if (!currentConfig.value) return []
  const list = allSets.value.filter(s => currentConfig.value!.setIds.includes(s.id))
  return sortSetList(list)
})

const disabledSets = computed(() => {
  if (!currentConfig.value) return []
  const list = allSets.value.filter(s => !currentConfig.value!.setIds.includes(s.id))
  return sortSetList(list)
})

const loadData = async () => {
  loading.value = true
  try {
    const [configsRes, setsRes] = await Promise.all([fetchConfigurations(), fetchSets()])
    allConfigs.value = configsRes
    allSets.value = setsRes
    store.configs = configsRes

    if (currentConfig.value) {
      const updated = allConfigs.value.find(c => c.id === currentConfig.value!.id)
      selectConfig(updated || allConfigs.value[0] || null)
    } else if (allConfigs.value.length > 0) {
      const active = allConfigs.value.find(c => c.active)
      selectConfig(active || allConfigs.value[0])
    }
  } catch (err) {
    showToast('Failed to load configurations', 'error')
  } finally {
    loading.value = false
  }
}

const selectConfig = async (config: Configuration | null) => {
  const isChanging = config && (!currentConfig.value || currentConfig.value.id !== config.id)
  currentConfig.value = config ? { ...config, setIds: [...config.setIds] } : null
  selectedSetIds.value.clear()

  if (isChanging && currentConfig.value && !currentConfig.value.active) {
    await activateConfig()
  }
}

const handleActivateConfig = async (config: Configuration) => {
  if (!config) return
  await selectConfig(config)
}

const createConfig = async () => {
  const name = await showPrompt(t('config.new_config_title'), t('config.config_name_prompt'))
  if (!name || name.trim() === '') return
  try {
    const newConfig = await createConfiguration()
    await renameConfiguration(newConfig.id, name.trim())
    await loadData()
    selectConfig(allConfigs.value.find(c => c.id === newConfig.id) || null)
    showToast(t('config.created_toast'), 'success')
  } catch (err) {
    showToast(t('cm.failed_toast'), 'error')
  }
}

const onConfigContextMenu = (e: MouseEvent, config: Configuration) => {
  const menuItems: any[] = [];
  if (!config.active) {
    menuItems.push({
      label: t('config.set_as_active'),
      icon: 'check_circle',
      action: () => handleActivateConfig(config)
    });
  }

  menuItems.push(
    {
      label: t('config.duplicate'),
      icon: 'content_copy',
      action: () => handleDuplicateConfig(config)
    },
    {
      label: t('config.edit_description'),
      icon: 'notes',
      action: () => handleEditDescription(config)
    },
    {
      label: t('context.rename'),
      icon: 'edit',
      action: () => handleRenameConfig(config),
      disabled: config.default
    },
    {
      label: t('context.delete'),
      icon: 'delete',
      action: () => handleDeleteConfig(config),
      disabled: config.default || config.active,
      danger: true
    }
  );
  showContextMenu(e.pageX, e.pageY, menuItems)
}

const handleEditDescription = async (config: Configuration | null) => {
  if (!config) return
  const newDesc = await showPrompt(
    t('config.edit_description'),
    t('config.edit_description_prompt'),
    config.description || ''
  )
  if (newDesc === null) return
  try {
    await updateConfigurationDescription(config.id, newDesc.trim())
    if (currentConfig.value && currentConfig.value.id === config.id) {
      currentConfig.value.description = newDesc.trim()
    }
    const match = allConfigs.value.find(c => c.id === config.id)
    if (match) match.description = newDesc.trim()
    showToast(t('config.description_updated_toast'), 'success')
  } catch (err) {
    showToast(t('cm.failed_toast'), 'error')
  }
}

const handleDuplicateConfig = async (config: Configuration) => {
  if (!config) return
  try {
    const duplicated = await duplicateConfiguration(config.id)
    await loadData()
    selectConfig(allConfigs.value.find(c => c.id === duplicated.id) || null)
    showToast(t('config.duplicated_toast'), 'success')
  } catch (err) {
    showToast(t('cm.failed_toast'), 'error')
  }
}

const handleRenameConfig = async (config: Configuration) => {
  if (!config || config.default) return
  const newName = await showPrompt(t('config.rename_title'), t('config.config_name_prompt'), config.name)
  if (!newName || !newName.trim()) return
  try {
    await renameConfiguration(config.id, newName.trim())
    await loadData()
  } catch (err) {
    showToast(t('cm.failed_toast'), 'error')
  }
}

const handleDeleteConfig = async (config: Configuration) => {
  if (!config || config.default || config.active) return
  if (await showConfirm(t('config.delete_title'), t('config.delete_msg', { name: config.name }))) {
    try {
      await deleteConfiguration(config.id)
      if (currentConfig.value && currentConfig.value.id === config.id) {
        currentConfig.value = null
      }
      await loadData()
      showToast(t('config.deleted_toast'), 'success')
    } catch (err) {
      showToast(t('cm.failed_toast'), 'error')
    }
  }
}

const activateConfig = async () => {
  if (!currentConfig.value || currentConfig.value.active) return
  const targetId = currentConfig.value.id
  try {
    allConfigs.value.forEach(c => {
      c.active = (c.id === targetId)
    })
    currentConfig.value.active = true

    await activateConfiguration(targetId)

    showToast(`${currentConfig.value.name} is now the active configuration. Game files updated!`, 'success')

    const [configsRes, setsRes] = await Promise.all([fetchConfigurations(), fetchSets()])
    allConfigs.value = configsRes
    allSets.value = setsRes
    store.configs = configsRes
    store.currentConfig = configsRes.find(c => c.id === targetId) || null
    store.isDirty = true
  } catch (err) {
    showToast('Failed to activate configuration', 'error')
    await loadData()
  }
}

// Click selection on set cards
const onClickSet = (e: MouseEvent, set: SetEntity) => {
  if (isDraggingSelection.value || wasJustDragging.value) return
  if (e.ctrlKey || e.metaKey) {
    if (selectedSetIds.value.has(set.id)) {
      selectedSetIds.value.delete(set.id)
    } else {
      selectedSetIds.value.add(set.id)
    }
  } else {
    if (!selectedSetIds.value.has(set.id)) {
      selectedSetIds.value.clear()
      selectedSetIds.value.add(set.id)
    }
  }
}

// Drag & drop handlers for bulk sets
const onDragStart = (e: DragEvent, set: SetEntity) => {
  if (!selectedSetIds.value.has(set.id)) {
    selectedSetIds.value.clear()
    selectedSetIds.value.add(set.id)
  }
  const dragIds = Array.from(selectedSetIds.value)
  e.dataTransfer!.setData('application/json', JSON.stringify({ type: 'config-sets', setIds: dragIds }))

  // Style opacity for all selected cards being dragged
  dragIds.forEach(id => {
    const el = document.querySelector(`.selectable-card[data-id="${id}"]`) as HTMLElement
    if (el) el.style.opacity = '0.5'
  })
}

const onDragEnd = (_e: DragEvent) => {
  document.querySelectorAll('.selectable-card').forEach(el => {
    (el as HTMLElement).style.opacity = '1.0'
  })
}

const onDragOver = (e: DragEvent) => {
  ;(e.currentTarget as HTMLElement).style.background = 'rgba(255, 255, 255, 0.04)'
}

const onDragLeave = (e: DragEvent) => {
  ;(e.currentTarget as HTMLElement).style.background = 'transparent'
}

// Double click shortcut handler
const onDblClickSet = async (set: SetEntity) => {
  if (!currentConfig.value) return
  const isCurrentlyEnabled = currentConfig.value.setIds.includes(set.id)
  const targetIds = selectedSetIds.value.has(set.id) && selectedSetIds.value.size > 1
    ? Array.from(selectedSetIds.value)
    : [set.id]
  await moveMultipleSetsWithSubsets(targetIds, !isCurrentlyEnabled)
}

// Helper to move multiple sets and optionally their sub-sets
const moveMultipleSetsWithSubsets = async (targetSetIds: number[], enable: boolean) => {
  if (!currentConfig.value || targetSetIds.length === 0) return

  const targetSetIdSet = new Set(targetSetIds)
  const allDescendants = new Set<number>()

  targetSetIds.forEach(id => {
    const desc = getAllDescendantIds(id)
    desc.forEach(dId => {
      if (!targetSetIdSet.has(dId)) {
        allDescendants.add(dId)
      }
    })
  })

  let includeSubsets = false
  if (allDescendants.size > 0) {
    if (targetSetIds.length === 1) {
      const singleSet = setsById.value.get(targetSetIds[0])
      const answer = await showConfirm(
        t('config.move_subsets_title'),
        t('config.move_subsets_msg', { name: singleSet?.name || '', count: allDescendants.size }),
        {
          confirmText: t('config.move_subsets_yes'),
          cancelText: t('config.move_subsets_no')
        }
      )
      includeSubsets = answer
    } else {
      const answer = await showConfirm(
        t('config.move_subsets_title'),
        t('config.move_subsets_msg_plural', { setCount: targetSetIds.length, subsetCount: allDescendants.size }),
        {
          confirmText: t('config.move_subsets_yes_plural'),
          cancelText: t('config.move_subsets_no_plural')
        }
      )
      includeSubsets = answer
    }
  }

  const idsToModify = new Set(targetSetIds)
  if (includeSubsets) {
    allDescendants.forEach(dId => idsToModify.add(dId))
  }

  let newSetIds = [...currentConfig.value.setIds]

  if (enable) {
    idsToModify.forEach(id => {
      if (!newSetIds.includes(id)) newSetIds.push(id)
    })
  } else {
    newSetIds = newSetIds.filter(id => !idsToModify.has(id))
  }

  const currentSetIds = currentConfig.value.setIds
  const hasChanged = newSetIds.length !== currentSetIds.length ||
    !newSetIds.every(id => currentSetIds.includes(id))

  if (hasChanged) {
    currentConfig.value.setIds = newSetIds
    await updateConfigurationSets(currentConfig.value.id, newSetIds)

    if (currentConfig.value.active) {
      store.isDirty = true
    }

    const setsRes = await fetchSets()
    allSets.value = setsRes
    if (currentConfig.value.active || setsRes.some(s => s.dirty)) {
      store.isDirty = true
    }
  }
}

const onDrop = async (e: DragEvent, enable: boolean) => {
  ;(e.currentTarget as HTMLElement).style.background = 'transparent'
  const data = e.dataTransfer?.getData('application/json')
  if (!data || !currentConfig.value) return
  try {
    const parsed = JSON.parse(data)
    if (parsed.type === 'config-sets' && Array.isArray(parsed.setIds)) {
      await moveMultipleSetsWithSubsets(parsed.setIds, enable)
    } else if (parsed.type === 'config-set' && parsed.setId) {
      await moveMultipleSetsWithSubsets([parsed.setId], enable)
    }
  } catch (err) {
    showToast('Failed to update sets', 'error')
    loadData()
  }
}
</script>
