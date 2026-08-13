<template>
  <div id="view-content" class="view">
    <div class="content-manager-layout">

      <!-- Sidebar: Sets Tree -->
      <SetTreeSidebar
        :allSets="allSets"
        :selectedSetId="store.selectedSetId"
        :loadingSets="loadingSets"
        v-model:setSortBy="setSortBy"
        @select-set="selectSet"
        @create-set="createSet"
        @set-context-menu="onSetContextMenu"
        @sidebar-context-menu="onSidebarContextMenu"
        @drop-set="onDropSet"
        @drop-all="onDropAll"
      />

      <!-- Main Area: Items Grid -->
      <div class="cm-main"
        @dragenter.prevent="onDragEnter"
        @dragover.prevent="onDragOver"
        @dragleave.prevent="onDragLeave"
        @drop.prevent="onDropFiles"
        style="position: relative;"
      >
        <div v-if="isDraggingFiles" class="drop-overlay">
          <div class="drop-message">
            <span class="material-symbols-outlined" style="font-size: 48px; margin-bottom: 1rem;">upload_file</span>
            <h2>{{ t('cm.drop_overlay_title') }}</h2>
          </div>
        </div>

        <!-- Toolbar -->
        <ContentManagerToolbar
          :currentSetName="currentSetName"
          :filteredCount="filteredItems.length"
          :formattedTotalSize="formatSize(totalSize)"
          v-model:sortMode="sortMode"
          v-model:viewMode="viewMode"
          :selectionMode="store.selectionMode"
          :selectedCount="store.selectedItemIds.size"
          :filterSidebarCollapsed="isFilterSidebarCollapsed"
          @enable-selected="enableSelected"
          @retag-selected="onRetagItem"
          @edit-tags-selected="onEditTags"
          @move-selected="moveSelected"
          @delete-selected="deleteSelected"
          @toggle-select-all="toggleSelectAll"
          @toggle-selection-mode="toggleSelectionMode"
          @toggle-filter-sidebar="isFilterSidebarCollapsed = !isFilterSidebarCollapsed"
        />

        <div :class="viewMode === 'comfy' ? 'cm-items-grid' : 'cm-items-list'" id="cm-items-grid" ref="gridRef" @click.self="clearSelection">
          <div v-if="loadingItems" class="text-muted-padded col-span-full">{{ t('cm.loading_items') }}</div>
          <div v-else-if="filteredItems.length === 0" class="text-muted-padded col-span-full">{{ t('cm.no_items') }}</div>
          <template v-else>
            <ItemCard
              v-for="item in displayedItems"
              :key="item.id"
              :item="item"
              :selected="store.selectedItemIds.has(item.id)"
              :selectionMode="store.selectionMode"
              :viewMode="viewMode"
              @select="onItemClick"
              @dragstart="onItemDragStart($event, item.id)"
              @dragend="onItemDragEnd"
              @contextmenu.prevent="onItemContextMenu($event, item)"
              @rename="onRenameItem"
              @retag="onRetagItem"
              @edit-tags="onEditTags"
            />
            <div ref="sentinelRef" style="height: 1px; width: 100%; grid-column: 1 / -1; pointer-events: none;"></div>
          </template>
        </div>
      </div>

      <!-- Filter Sidebar -->
      <ContentManagerFilterSidebar
        v-if="!isFilterSidebarCollapsed"
        v-model:searchQuery="searchQuery"
        :userTagsList="allUserTags"
        v-model:filterTypeCAS="filterTypeCAS"
        v-model:filterTypeBuildBuy="filterTypeBuildBuy"
        v-model:filterTypeOther="filterTypeOther"
        :activeCasCategories="activeCasCategories"
        :activeCasAges="activeCasAges"
        :activeCasGenders="activeCasGenders"
        :activeCasOutfits="activeCasOutfits"
        :activeOtherSubCategories="activeOtherSubCategories"
        v-model:filterModeEnabled="filterModeEnabled"
        v-model:filterModeDisabled="filterModeDisabled"
        @toggle-cas-category="toggleCasCategory"
        @toggle-cas-age="toggleCasAge"
        @toggle-cas-gender="toggleCasGender"
        @toggle-cas-outfit="toggleCasOutfit"
        @toggle-all-cas-categories="toggleAllCasCategories"
        @toggle-all-cas-ages="toggleAllCasAges"
        @toggle-all-cas-genders="toggleAllCasGenders"
        @toggle-all-cas-outfits="toggleAllCasOutfits"
        @toggle-other-subcategory="toggleOtherSubCategory"
        @toggle-collapse="isFilterSidebarCollapsed = true"
      />

      <!-- Floating Drag Warning Cursor Tooltip -->
      <div
        ref="dragWarningTooltipRef"
        v-show="showDragWarningTooltip"
        class="drag-warning-tooltip"
      >
        <span class="material-symbols-outlined" style="font-size: 16px; color: var(--danger);">block</span>
        <span>{{ t('cm.cannot_drop_all_items') }}</span>
      </div>

    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, shallowRef, triggerRef, computed, onMounted, onUnmounted, watch } from 'vue'
import { fetchSets, fetchItems, createSet as createSetApi, renameSet, deleteSet, moveItems, moveSet, deleteItems, setItemEnabled, renameItemApi, retagItems, updateUserTags, importFiles, uploadFiles, checkImportDuplicates, openItemFolderApi } from '@/api/client'
import { useModal } from '@/composables/useModal'
import { useToast } from '@/composables/useToast'
import { useSelection } from '@/composables/useSelection'
import { useContextMenu } from '@/composables/useContextMenu'
import { useAppStore } from '@/stores/app'
import type { SetEntity, ItemEntity } from '@/types'
import SetTreeSidebar from '@/components/SetTreeSidebar.vue'
import ContentManagerToolbar from '@/components/ContentManagerToolbar.vue'
import ContentManagerFilterSidebar from '@/components/ContentManagerFilterSidebar.vue'
import SetTreeNode from '@/components/SetTreeNode.vue'
import ItemCard from '@/components/ItemCard.vue'
import { useI18n } from '@/composables/useI18n'

// ===== Composables & Store =====
const { t } = useI18n()
const { showPrompt, showConfirm, showDeleteSet, showDeleteItems, showSelectSet, showRetag, showUserTagsModal, showDuplicateImportModal } = useModal()
const { showContextMenu } = useContextMenu()
const { showToast } = useToast()
const store = useAppStore()

// ===== Primary Data State =====
const allSets = ref<SetEntity[]>([])
const allItems = shallowRef<ItemEntity[]>([])
const loadingSets = ref(true)
const loadingItems = ref(true)
const searchQuery = ref('')

// ===== UI & View Mode State =====
const viewMode = ref(localStorage.getItem('viewMode') || 'comfy')
watch(viewMode, (v) => {
  localStorage.setItem('viewMode', v)
})

const sortMode = ref(localStorage.getItem('sortMode') || 'date_desc')
watch(sortMode, (v) => {
  localStorage.setItem('sortMode', v)
})
const sortDropdownOpen = ref(false)

// ===== Set Sorting State =====
const setSortBy = ref<'date' | 'name_asc' | 'name_desc'>((localStorage.getItem('plumbobforge_set_sort') as any) || 'date')
const setSortDropdownOpen = ref(false)

watch(setSortBy, (v) => {
  localStorage.setItem('plumbobforge_set_sort', v)
})

const closeSortDropdown = () => {
  if (sortDropdownOpen.value) sortDropdownOpen.value = false
  if (setSortDropdownOpen.value) setSortDropdownOpen.value = false
}

const toggleSetSortDropdown = () => {
  setSortDropdownOpen.value = !setSortDropdownOpen.value
}

const sortSetsList = (setsList: SetEntity[]) => {
  const cloned = [...setsList]
  if (setSortBy.value === 'name_asc') {
    return cloned.sort((a, b) => a.name.localeCompare(b.name))
  } else if (setSortBy.value === 'name_desc') {
    return cloned.sort((a, b) => b.name.localeCompare(a.name))
  }
  return cloned.sort((a, b) => a.id - b.id)
}

// ===== Computeds depending on allSets =====
const rootSets = computed(() => allSets.value.filter(s => !s.parentSetsEntityId))
const sortedAllSets = computed(() => sortSetsList(allSets.value))
const sortedRootSets = computed(() => sortSetsList(rootSets.value))

// ===== User Location Persistence =====
watch(() => store.selectedSetId, (newId) => {
  store.selectedItemIds.clear()
  lastClickedItemId.value = null
  if (newId === null) {
    localStorage.setItem('plumbobforge_last_selected_set_id', 'null')
  } else {
    localStorage.setItem('plumbobforge_last_selected_set_id', newId.toString())
  }
})

const restoreLastSelectedSet = () => {
  const saved = localStorage.getItem('plumbobforge_last_selected_set_id')
  if (saved && saved !== 'null') {
    const id = parseInt(saved, 10)
    if (!isNaN(id) && allSets.value.some(s => s.id === id)) {
      store.selectedSetId = id
      let cur = allSets.value.find(s => s.id === id)
      const visited = new Set<number>()
      while (cur && cur.parentSetsEntityId && !visited.has(cur.id)) {
        visited.add(cur.id)
        store.expandedSets.add(cur.parentSetsEntityId)
        cur = allSets.value.find(s => s.id === cur!.parentSetsEntityId)
      }
    }
  }
}

// ===== Scroll Wheel During Set Drag =====
const onTreeWheel = (e: WheelEvent) => {
  const target = e.currentTarget as HTMLElement
  if (target) {
    target.scrollTop += e.deltaY
  }
}

// ===== Search Autocomplete & History =====
const isSearchFocused = ref(false)
const searchHistory = ref<string[]>(JSON.parse(localStorage.getItem('plumbobforge_search_history') || '[]'))

const allUserTags = computed(() => {
  const set = new Set<string>()
  allItems.value.forEach(item => {
    if (item.userTags) {
      item.userTags.split(',').forEach(t => {
        const trimmed = t.trim()
        if (trimmed) set.add(trimmed)
      })
    }
  })
  return Array.from(set).sort()
})

const filteredTagSuggestions = computed(() => {
  if (!searchQuery.value) return allUserTags.value.slice(0, 8)
  const q = searchQuery.value.toLowerCase()
  return allUserTags.value.filter(t => t.toLowerCase().includes(q)).slice(0, 8)
})

const selectSearchSuggestion = (val: string) => {
  searchQuery.value = val
  addSearchHistory(val)
  isSearchFocused.value = false
}

const addSearchHistory = (query: string) => {
  const trimmed = query.trim()
  if (!trimmed) return
  searchHistory.value = [trimmed, ...searchHistory.value.filter(h => h !== trimmed)].slice(0, 10)
  localStorage.setItem('plumbobforge_search_history', JSON.stringify(searchHistory.value))
}

const removeSearchHistory = (query: string) => {
  searchHistory.value = searchHistory.value.filter(h => h !== query)
  localStorage.setItem('plumbobforge_search_history', JSON.stringify(searchHistory.value))
}

const onSearchBlur = () => {
  setTimeout(() => {
    isSearchFocused.value = false
  }, 150)
}

const isDragOverAll = ref(false)
const showDragWarningTooltip = ref(false)
const dragWarningTooltipRef = ref<HTMLElement | null>(null)
let dragRafId: number | null = null
let dragLastX = 0
let dragLastY = 0

const gridRef = ref<HTMLElement | null>(null)
const { initSelection, destroySelection, isDraggingSelection } = useSelection()
const { initSelection: initSetSelection, destroySelection: destroySetSelection } = useSelection(store.selectedSetIds, '.tree-item')
function loadSavedFilterSet(key: string, defaultList: string[]): Set<string> {
  try {
    const saved = localStorage.getItem(key)
    if (saved) {
      const arr = JSON.parse(saved)
      if (Array.isArray(arr)) return new Set(arr)
    }
  } catch (e) {}
  return new Set(defaultList)
}

function loadSavedBoolean(key: string, defaultValue: boolean): boolean {
  const saved = localStorage.getItem(key)
  if (saved === null) return defaultValue
  return saved === 'true'
}

const isFilterSidebarCollapsed = ref(loadSavedBoolean('pf_filter_sidebar_collapsed', false))
watch(isFilterSidebarCollapsed, (v) => localStorage.setItem('pf_filter_sidebar_collapsed', String(v)))

const filterTypeCAS = ref(loadSavedBoolean('pf_filter_type_cas', true))
watch(filterTypeCAS, v => localStorage.setItem('pf_filter_type_cas', String(v)))

const filterTypeBuildBuy = ref(loadSavedBoolean('pf_filter_type_bb', true))
watch(filterTypeBuildBuy, v => localStorage.setItem('pf_filter_type_bb', String(v)))

const filterTypeOther = ref(loadSavedBoolean('pf_filter_type_other', true))
watch(filterTypeOther, v => localStorage.setItem('pf_filter_type_other', String(v)))

const filterModeEnabled = ref(loadSavedBoolean('pf_filter_mode_enabled', true))
watch(filterModeEnabled, v => localStorage.setItem('pf_filter_mode_enabled', String(v)))

const filterModeDisabled = ref(loadSavedBoolean('pf_filter_mode_disabled', true))
watch(filterModeDisabled, v => localStorage.setItem('pf_filter_mode_disabled', String(v)))

// ===== Collapsible Filter Section States =====
const isTypeFilterCollapsed = ref(localStorage.getItem('pf_filter_type_collapsed') === 'true')
watch(isTypeFilterCollapsed, (v) => localStorage.setItem('pf_filter_type_collapsed', String(v)))

const isCasSectionCollapsed = ref(localStorage.getItem('pf_filter_cas_collapsed') === 'true')
watch(isCasSectionCollapsed, (v) => localStorage.setItem('pf_filter_cas_collapsed', String(v)))

const isCasCategoryCollapsed = ref(localStorage.getItem('pf_filter_cas_cat_collapsed') === 'true')
watch(isCasCategoryCollapsed, (v) => localStorage.setItem('pf_filter_cas_cat_collapsed', String(v)))

const isCasAgeCollapsed = ref(localStorage.getItem('pf_filter_cas_age_collapsed') === 'true')
watch(isCasAgeCollapsed, (v) => localStorage.setItem('pf_filter_cas_age_collapsed', String(v)))

const isCasGenderCollapsed = ref(localStorage.getItem('pf_filter_cas_gen_collapsed') === 'true')
watch(isCasGenderCollapsed, (v) => localStorage.setItem('pf_filter_cas_gen_collapsed', String(v)))

const isCasOutfitCollapsed = ref(localStorage.getItem('pf_filter_cas_outfit_collapsed') === 'true')
watch(isCasOutfitCollapsed, (v) => localStorage.setItem('pf_filter_cas_outfit_collapsed', String(v)))

const isOtherSectionCollapsed = ref(localStorage.getItem('pf_filter_other_collapsed') === 'true')
watch(isOtherSectionCollapsed, (v) => localStorage.setItem('pf_filter_other_collapsed', String(v)))

const isModeFilterCollapsed = ref(localStorage.getItem('pf_filter_mode_collapsed') === 'true')
watch(isModeFilterCollapsed, (v) => localStorage.setItem('pf_filter_mode_collapsed', String(v)))

const casCategoriesList = ['Hair', 'Full body', 'Tops', 'Bottoms', 'Shoes', 'Details', 'Skins', 'Accessories', 'Sliders', 'Presets', 'Other']
const casAgesList = ['Baby', 'Toddler', 'Child', 'Teen', 'YoungAdult', 'Adult', 'Elder']
const casGendersList = ['Male', 'Female']
const casOutfitsList = ['Everyday', 'Formal', 'Sleepwear', 'Swimwear', 'Athletic', 'Career', 'Outerwear']

const casCategoryIcons: Record<string, string> = {
  'Hair': 'face',
  'Full body': 'accessibility_new',
  'Tops': 'apparel',
  'Bottoms': 'airline_seat_legroom_extra',
  'Shoes': 'steps',
  'Details': 'health_and_beauty',
  'Skins': 'palette',
  'Accessories': 'diamond',
  'Sliders': 'tune',
  'Presets': 'auto_awesome',
  'Other': 'more_horiz'
}

const activeCasCategories = ref(loadSavedFilterSet('pf_filter_cas_cats', casCategoriesList))
watch(activeCasCategories, v => localStorage.setItem('pf_filter_cas_cats', JSON.stringify(Array.from(v))), { deep: true })

const activeCasAges = ref(loadSavedFilterSet('pf_filter_cas_ages', casAgesList))
watch(activeCasAges, v => localStorage.setItem('pf_filter_cas_ages', JSON.stringify(Array.from(v))), { deep: true })

const activeCasGenders = ref(loadSavedFilterSet('pf_filter_cas_genders', casGendersList))
watch(activeCasGenders, v => localStorage.setItem('pf_filter_cas_genders', JSON.stringify(Array.from(v))), { deep: true })

const activeCasOutfits = ref(loadSavedFilterSet('pf_filter_cas_outfits', casOutfitsList))
watch(activeCasOutfits, v => localStorage.setItem('pf_filter_cas_outfits', JSON.stringify(Array.from(v))), { deep: true })

const toggleCasCategory = (cat: string) => {
  if (activeCasCategories.value.has(cat)) {
    activeCasCategories.value.delete(cat)
  } else {
    activeCasCategories.value.add(cat)
  }
}

const toggleCasAge = (age: string) => {
  if (activeCasAges.value.has(age)) {
    activeCasAges.value.delete(age)
  } else {
    activeCasAges.value.add(age)
  }
}

const toggleCasGender = (gen: string) => {
  if (activeCasGenders.value.has(gen)) {
    activeCasGenders.value.delete(gen)
  } else {
    activeCasGenders.value.add(gen)
  }
}

const toggleCasOutfit = (outfit: string) => {
  if (activeCasOutfits.value.has(outfit)) {
    activeCasOutfits.value.delete(outfit)
  } else {
    activeCasOutfits.value.add(outfit)
  }
}

const toggleAllCasCategories = () => {
  if (activeCasCategories.value.size === casCategoriesList.length) {
    activeCasCategories.value.clear()
  } else {
    activeCasCategories.value = new Set(casCategoriesList)
  }
}

const toggleAllCasAges = () => {
  if (activeCasAges.value.size === casAgesList.length) {
    activeCasAges.value.clear()
  } else {
    activeCasAges.value = new Set(casAgesList)
  }
}

const toggleAllCasGenders = () => {
  if (activeCasGenders.value.size === casGendersList.length) {
    activeCasGenders.value.clear()
  } else {
    activeCasGenders.value = new Set(casGendersList)
  }
}

const toggleAllCasOutfits = () => {
  if (activeCasOutfits.value.size === casOutfitsList.length) {
    activeCasOutfits.value.clear()
  } else {
    activeCasOutfits.value = new Set(casOutfitsList)
  }
}

const otherSubCategoriesList = ['Worlds', 'Sims', 'Lots', 'Misc']

const otherSubCategoryIcons: Record<string, string> = {
  'Worlds': 'public',
  'Sims': 'person',
  'Lots': 'home',
  'Misc': 'category'
}

const activeOtherSubCategories = ref(loadSavedFilterSet('pf_filter_other_subs', otherSubCategoriesList))
watch(activeOtherSubCategories, v => localStorage.setItem('pf_filter_other_subs', JSON.stringify(Array.from(v))), { deep: true })

const toggleOtherSubCategory = (sub: string) => {
  if (activeOtherSubCategories.value.has(sub)) {
    activeOtherSubCategories.value.delete(sub)
  } else {
    activeOtherSubCategories.value.add(sub)
  }
}

const getOtherSubCategory = (item: ItemEntity): string => {
  const pt = item.packageType || ''
  const fn = item.fileName.toLowerCase()
  if (pt === 'World' || fn.endsWith('.world')) return 'Worlds'
  if (pt === 'Sim' || fn.endsWith('.sim')) return 'Sims'
  if (pt === 'Lot') return 'Lots'
  return 'Misc'
}


const filteredItems = computed(() => {
  let items = [...allItems.value]

  // Set filter
  if (store.selectedSetId !== null) {
    items = items.filter(i => i.setsEntityId === store.selectedSetId)
  }

  // Name & tag filter
  const q = searchQuery.value.trim().toLowerCase()
  if (q) {
    items = items.filter(i =>
      i.fileName.toLowerCase().includes(q) ||
      (i.userTags && i.userTags.toLowerCase().includes(q))
    )
  }

  // Mode filter
  if (!filterModeEnabled.value) items = items.filter(i => !i.enabled)
  if (!filterModeDisabled.value) items = items.filter(i => i.enabled)

  // Type filter
  items = items.filter(i => {
    const pt = i.packageType || ''
    const isCAS = pt === 'CAS'
    const isBuildBuy = pt === 'BuildBuy'
    const isOther = !isCAS && !isBuildBuy

    if (isCAS && filterTypeCAS.value) {
      // Category filter
      if (i.casCategories) {
        const cats = i.casCategories.split(',').map(c => c.trim())
        if (!cats.some(c => activeCasCategories.value.has(c))) return false
      }
      // Age filter
      if (i.casAge) {
        const ages = i.casAge.split(',').map(a => a.trim())
        if (!ages.some(a => activeCasAges.value.has(a))) return false
      }
      // Gender filter
      if (i.casGender) {
        const gens = i.casGender.split(',').map(g => g.trim())
        if (!gens.some(g => activeCasGenders.value.has(g))) return false
      }
      // Outfit Category filter
      if (i.casOutfitCategory) {
        const outfits = i.casOutfitCategory.split(',').map(o => o.trim())
        if (!outfits.some(o => activeCasOutfits.value.has(o))) return false
      }
      return true
    }
    if (isBuildBuy && filterTypeBuildBuy.value) return true
    if (isOther && filterTypeOther.value) {
      const subCat = getOtherSubCategory(i)
      return activeOtherSubCategories.value.has(subCat)
    }

    return false
  })

  // Sort logic
  items.sort((a, b) => {
    switch (sortMode.value) {
      case 'date_asc': return a.id - b.id
      case 'date_desc': return b.id - a.id
      case 'alpha_asc': return a.fileName.localeCompare(b.fileName)
      case 'alpha_desc': return b.fileName.localeCompare(a.fileName)
      default: return 0
    }
  })

  return items
})

const renderLimit = ref(60)

const displayedItems = computed(() => {
  return filteredItems.value.slice(0, renderLimit.value)
})

watch(filteredItems, () => {
  renderLimit.value = 60
  if (gridRef.value) gridRef.value.scrollTop = 0
})

const sentinelRef = ref<HTMLElement | null>(null)
let sentinelObserver: IntersectionObserver | null = null

onMounted(() => {
  sentinelObserver = new IntersectionObserver((entries) => {
    if (entries[0] && entries[0].isIntersecting) {
      if (renderLimit.value < filteredItems.value.length) {
        renderLimit.value = Math.min(renderLimit.value + 60, filteredItems.value.length)
      }
    }
  }, { root: gridRef.value, rootMargin: '400px' })

  if (sentinelRef.value) sentinelObserver.observe(sentinelRef.value)
})

onUnmounted(() => {
  if (sentinelObserver) sentinelObserver.disconnect()
})

const totalSize = computed(() => filteredItems.value.reduce((acc, curr) => acc + curr.fileSize, 0))

const formatSize = (kb: number) => {
  if (kb < 1024) return kb.toFixed(2) + ' KB'
  const mb = kb / 1024
  if (mb < 1024) return mb.toFixed(2) + ' MB'
  const gb = mb / 1024
  return gb.toFixed(2) + ' GB'
}

const currentSetName = computed(() => {
  if (store.selectedSetId === null) return 'All Items'
  const s = allSets.value.find(set => set.id === store.selectedSetId)
  return s ? s.name : 'Items'
})

const loadData = async () => {
  try {
    const setsPromise = fetchSets().then(setsRes => {
      allSets.value = setsRes
      if (allSets.value.some(s => s.dirty)) {
        store.isDirty = true
      }
      loadingSets.value = false
    })

    const itemsPromise = fetchItems().then(itemsRes => {
      allItems.value = itemsRes
      loadingItems.value = false
    })

    await Promise.all([setsPromise, itemsPromise])
    restoreLastSelectedSet()
  } catch (err) {
    showToast('Failed to load Content Manager data.', 'error')
    loadingSets.value = false
    loadingItems.value = false
  }
}

const handleKeydown = (e: KeyboardEvent) => {
  if (e.key === 'Escape') {
    store.selectedItemIds.clear()
    store.selectedSetIds.clear()
    if (store.selectionMode) {
      store.selectionMode = false
    }
  } else if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'a') {
    const tag = (e.target as HTMLElement)?.tagName?.toLowerCase()
    if (tag !== 'input' && tag !== 'textarea') {
      e.preventDefault()
      if (!store.selectionMode) {
        store.selectionMode = true
      }
      filteredItems.value.forEach(item => store.selectedItemIds.add(item.id))
    }
  }
}

const toggleSelectAll = () => {
  if (store.selectedItemIds.size === filteredItems.value.length && filteredItems.value.length > 0) {
    store.selectedItemIds.clear()
  } else {
    filteredItems.value.forEach(item => store.selectedItemIds.add(item.id))
  }
}

const handleGlobalDragEnd = () => {
  isDragOverAll.value = false
  showDragWarningTooltip.value = false
}

const handleItemsUpdated = () => {
  loadData()
}

watch(() => store.lastImportedAt, () => {
  loadData()
})

onMounted(() => {
  loadData()
  initSelection('#cm-items-grid')
  initSetSelection('#cm-sidebar')
  document.addEventListener('click', closeSortDropdown)
  document.addEventListener('keydown', handleKeydown)
  window.addEventListener('dragend', handleGlobalDragEnd)
  window.addEventListener('mouseup', handleGlobalDragEnd)
  window.addEventListener('items-updated', handleItemsUpdated)
})

onUnmounted(() => {
  destroySelection()
  destroySetSelection()
  document.removeEventListener('click', closeSortDropdown)
  document.removeEventListener('keydown', handleKeydown)
  window.removeEventListener('dragend', handleGlobalDragEnd)
  window.removeEventListener('mouseup', handleGlobalDragEnd)
  window.removeEventListener('items-updated', handleItemsUpdated)
})

const clearSelection = () => {
  if (isDraggingSelection.value || isDraggingItem.value) return;
  store.selectedItemIds.clear()
  store.selectedSetIds.clear()
}

const selectSet = (id: number | null) => {
  if (store.selectedSetId !== id) {
    store.selectedSetId = id
    store.selectedItemIds.clear()
    lastClickedItemId.value = null
  }
}

const createSet = async () => {
  const name = await showPrompt(t('cm.new_set_prompt'))
  if (!name || name.trim() === '') return
  try {
    await createSetApi(name.trim(), store.selectedSetId)
    showToast(t('cm.set_created_toast'), 'success')
    if (store.selectedSetId) store.expandedSets.add(store.selectedSetId)
    await loadData()
  } catch (e) {
    showToast(t('cm.failed_toast'), 'error')
  }
}

const toggleSelectionMode = () => {
  store.selectionMode = !store.selectionMode
  if (!store.selectionMode) store.selectedItemIds.clear()
}

const lastClickedItemId = ref<number | null>(null);
const isDraggingItem = ref(false);

const onItemClick = (e: MouseEvent, id: number) => {
  if (isDraggingSelection.value || isDraggingItem.value) return;

  if (e.shiftKey && lastClickedItemId.value !== null) {
    const items = filteredItems.value;
    const lastIdx = items.findIndex(i => i.id === lastClickedItemId.value);
    const currIdx = items.findIndex(i => i.id === id);

    if (lastIdx !== -1 && currIdx !== -1) {
      const start = Math.min(lastIdx, currIdx);
      const end = Math.max(lastIdx, currIdx);

      for (let i = start; i <= end; i++) {
        store.selectedItemIds.add(items[i].id);
      }
      return;
    }
  }

  if (store.selectionMode) {
    if (store.selectedItemIds.has(id)) store.selectedItemIds.delete(id)
    else store.selectedItemIds.add(id)
  } else {
    if (e.ctrlKey || e.metaKey) {
      if (store.selectedItemIds.has(id)) store.selectedItemIds.delete(id)
      else store.selectedItemIds.add(id)
    } else {
      store.selectedItemIds.clear()
      store.selectedItemIds.add(id)
    }
  }

  lastClickedItemId.value = id;
}

const onItemDragStart = (e: DragEvent, id: number) => {
  isDraggingItem.value = true;
  if (!store.selectedItemIds.has(id)) {
    store.selectedItemIds.clear()
    store.selectedItemIds.add(id)
  }
  e.dataTransfer!.setData('application/json', JSON.stringify({ type: 'items', ids: Array.from(store.selectedItemIds) }))
}

const onItemDragEnd = () => {
  setTimeout(() => {
    isDraggingItem.value = false;
  }, 150);
}

const moveSelected = async () => {
  if (store.selectedItemIds.size === 0) return
  const targetSetId = await showSelectSet(allSets.value)
  if (targetSetId === 'cancelled') return

  try {
    await moveItems(Array.from(store.selectedItemIds), targetSetId)
    showToast(t('cm.items_moved_toast', { count: store.selectedItemIds.size }), 'success')
    store.selectedItemIds.clear()
    store.selectionMode = false
    await loadData()
  } catch (e) {
    showToast(t('cm.failed_toast'), 'error')
  }
}

const deleteSelected = async () => {
  const result = await showDeleteItems(t('cm.delete_items_confirm', { count: store.selectedItemIds.size }))
  if (result && result.confirmed) {
    try {
      const ids = Array.from(store.selectedItemIds)
      await deleteItems(ids, result.permanent)
      showToast(t('cm.items_deleted_toast', { count: ids.length }), 'success')
      store.selectedItemIds.clear()
      await loadData()
    } catch (e) {
      showToast(t('cm.failed_toast'), 'error')
    }
  }
}

const onSidebarContextMenu = (e: MouseEvent) => {
  showContextMenu(e.pageX, e.pageY, [
    {
      label: t('cm.add_set'), icon: 'add', action: async () => {
        const name = await showPrompt(t('cm.new_set_prompt'))
        if (!name || name.trim() === '') return
        try {
          await createSetApi(name.trim(), null)
          await loadData()
        } catch (e) {
          showToast(t('cm.failed_toast'), 'error')
        }
      }
    }
  ])
}

const onSetContextMenu = (e: MouseEvent, set: SetEntity) => {
  const isBuiltIn = set.name === 'Default' || set.name === 'Legacy'
  const menuItems = [
    {
      label: t('cm.add_subset'), icon: 'add', action: async () => {
        const name = await showPrompt(t('cm.new_subset_prompt'))
        if (!name || name.trim() === '') return
        try {
          await createSetApi(name.trim(), set.id)
          store.expandedSets.add(set.id)
          await loadData()
        } catch (e) {
          showToast(t('cm.failed_toast'), 'error')
        }
      }
    }
  ]

  if (!isBuiltIn) {
    menuItems.push(
      {
        label: t('context.rename'), icon: 'edit', action: async () => {
          const newName = await showPrompt(t('cm.rename_set_title') || t('context.rename'), t('cm.rename_set_prompt', { name: set.name }), set.name)
          if (!newName || newName.trim() === '') return
          try {
            await renameSet(set.id, newName.trim())
            await loadData()
          } catch (e) {
            showToast(t('cm.failed_toast'), 'error')
          }
        }
      },
      { divider: true, label: '' },
      {
        label: t('cm.delete_set'), icon: 'delete', danger: true, action: async () => {
          const result = await showDeleteSet(set.name)
          if (result && result.confirmed) {
            try {
              await deleteSet(set.id, result.deletePhysical)
              if (store.selectedSetId === set.id) store.selectedSetId = null
              showToast(t('cm.set_deleted_toast'), 'success')
              await loadData()
            } catch (e: any) {
              showToast(e.message || t('cm.failed_toast'), 'error')
            }
          }
        }
      }
    )
  }
  showContextMenu(e.pageX, e.pageY, menuItems)
}

const onItemContextMenu = (e: MouseEvent, item: ItemEntity) => {
  const isSelected = store.selectedItemIds.has(item.id)
  const targetIds = isSelected ? Array.from(store.selectedItemIds) : [item.id]

  const menuItems = [
    {
      label: item.enabled ? t('context.disable') : t('context.enable'),
      icon: item.enabled ? 'block' : 'check_circle',
      action: async () => {
        try {
          const nextState = !item.enabled
          await setItemEnabled(targetIds, nextState)
          targetIds.forEach(id => {
            const target = allItems.value.find(i => i.id === id)
            if (target) target.enabled = nextState
          })
          triggerRef(allItems)
          store.isDirty = true
          showToast(targetIds.length > 1 ? `Toggled ${targetIds.length} items.` : (nextState ? 'Item enabled.' : 'Item disabled.'), 'success')
        } catch (e) {
          showToast('Failed to toggle item(s).', 'error')
        }
      }
    }
  ]

  if (targetIds.length === 1) {
    menuItems.push({
      label: t('context.rename'),
      icon: 'edit',
      action: () => onRenameItem(targetIds[0])
    })
    menuItems.push({
      label: t('context.show_in_folder') || 'Show in Folder',
      icon: 'folder_open',
      action: async () => {
        try {
          if ((window as any).electronAPI && (window as any).electronAPI.showItemInFolder) {
            await (window as any).electronAPI.showItemInFolder(item.completeFileName)
          } else {
            await openItemFolderApi(item.id)
          }
        } catch (e) {
          showToast('Failed to open file folder.', 'error')
        }
      }
    })
  }

  menuItems.push({
    label: t('context.retag'),
    icon: 'sell',
    action: () => onRetagItem(targetIds.length === 1 ? item : undefined)
  })

  menuItems.push({
    label: t('context.user_tags'),
    icon: 'label',
    action: () => onEditTags(targetIds.length === 1 ? item : undefined)
  })

  menuItems.push({
    label: t('context.delete'),
    icon: 'delete',
    danger: true,
    action: async () => {
      const result = await showDeleteItems(`Are you sure you want to delete ${targetIds.length > 1 ? `these ${targetIds.length} items` : 'this item'}?`)
      if (result && result.confirmed) {
        try {
          await deleteItems(targetIds, result.permanent)
          allItems.value = allItems.value.filter(i => !targetIds.includes(i.id))
          if (isSelected) store.selectedItemIds.clear()
          store.isDirty = true
          showToast(`Deleted ${targetIds.length} item(s).`, 'success')
          await loadData()
        } catch (e) {
          showToast('Failed to delete item(s).', 'error')
        }
      }
    }
  })

  showContextMenu(e.pageX, e.pageY, menuItems)
}

const onEditTags = async (target?: ItemEntity) => {
  let targetItems: ItemEntity[] = []

  if (target) {
    targetItems = [target]
  } else if (store.selectedItemIds.size > 0) {
    targetItems = allItems.value.filter(i => store.selectedItemIds.has(i.id))
  }

  if (targetItems.length === 0) return

  const itemIds = targetItems.map(i => i.id)

  const firstTags = (targetItems[0].userTags || '').split(',').map(s => s.trim()).filter(Boolean).sort().join(',')
  const hasDifferentTags = targetItems.some(i => {
    const iTags = (i.userTags || '').split(',').map(s => s.trim()).filter(Boolean).sort().join(',')
    return iTags !== firstTags
  })

  const initialTags = hasDifferentTags ? [] : (targetItems[0].userTags || '').split(',').map(s => s.trim()).filter(Boolean)

  const result = await showUserTagsModal(itemIds, initialTags, hasDifferentTags)
  if (result && result.confirmed) {
    try {
      await updateUserTags(itemIds, {
        setTags: result.setTags,
        addTags: result.addTags,
        removeAll: result.removeAll
      })
      showToast(`Updated tags for ${itemIds.length} item(s)`, 'success')
      await loadData()
    } catch (e) {
      showToast('Failed to update tags.', 'error')
    }
  }
}

const onRetagItem = async (target?: ItemEntity) => {
  let targetIds: number[] = []
  let initialPkgType = 'CAS'
  let initialCasCats = ''

  if (target) {
    targetIds = [target.id]
    initialPkgType = target.packageType || 'CAS'
    initialCasCats = target.casCategories || ''
  } else if (store.selectedItemIds.size > 0) {
    targetIds = Array.from(store.selectedItemIds)
    const first = allItems.value.find(i => targetIds.includes(i.id))
    if (first) {
      initialPkgType = first.packageType || 'CAS'
      initialCasCats = first.casCategories || ''
    }
  }

  if (targetIds.length === 0) return

  const result = await showRetag(initialPkgType, initialCasCats)
  if (result && result.confirmed) {
    try {
      await retagItems(targetIds, result.packageType, result.casCategories)
      showToast(t('modal.retag_success', { count: targetIds.length }) || `Retagged ${targetIds.length} item(s)`, 'success')
      await loadData()
    } catch (e) {
      showToast('Failed to retag items.', 'error')
    }
  }
}

const enableSelected = async (enabled: boolean) => {
  const ids = Array.from(store.selectedItemIds)
  if (ids.length === 0) return
  try {
    await setItemEnabled(ids, enabled)
    ids.forEach(id => {
      const item = allItems.value.find(i => i.id === id)
      if (item) item.enabled = enabled
    })
    triggerRef(allItems)
    store.isDirty = true
    showToast(`Successfully ${enabled ? 'enabled' : 'disabled'} ${ids.length} items.`, 'success')
    toggleSelectionMode()
  } catch (e) {
    showToast('Failed to toggle items.', 'error')
  }
}

const updateDragWarningPos = () => {
  dragRafId = null
  if (dragWarningTooltipRef.value) {
    dragWarningTooltipRef.value.style.transform = `translate3d(${dragLastX + 16}px, ${dragLastY + 16}px, 0)`
  }
}

const onDragOverAll = (e: DragEvent) => {
  if (e.dataTransfer) {
    e.dataTransfer.dropEffect = 'none'
  }
  isDragOverAll.value = true
  showDragWarningTooltip.value = true
  dragLastX = e.clientX
  dragLastY = e.clientY
  if (!dragRafId) {
    dragRafId = requestAnimationFrame(updateDragWarningPos)
  }
}

const onDragLeaveAll = () => {
  isDragOverAll.value = false
  showDragWarningTooltip.value = false
  if (dragRafId) {
    cancelAnimationFrame(dragRafId)
    dragRafId = null
  }
}

const onDropSidebar = async (e: DragEvent) => {
  e.preventDefault()
  isDragOverAll.value = false
  showDragWarningTooltip.value = false
  if (dragRafId) {
    cancelAnimationFrame(dragRafId)
    dragRafId = null
  }

  if (!e.dataTransfer) return
  const dataStr = e.dataTransfer.getData('application/json')
  if (!dataStr) return

  try {
    const data = JSON.parse(dataStr)
    if (data.type === 'set' || data.type === 'sets') {
      const setIds: number[] = data.ids || (data.id ? [data.id] : [])
      if (setIds.length === 0) return
      for (const id of setIds) {
        await moveSet(id, null)
      }
      showToast(setIds.length > 1 ? `Unnested ${setIds.length} sets.` : 'Unnested set to top level.', 'success')
      store.selectedSetIds.clear()
      await loadData()
    }
  } catch (err) {}
}

const onDropAll = onDropSidebar

const isDescendantOf = (targetId: number, ancestorId: number): boolean => {
  let cur = allSets.value.find(s => s.id === targetId)
  const visited = new Set<number>()
  while (cur && cur.parentSetsEntityId && !visited.has(cur.id)) {
    if (cur.parentSetsEntityId === ancestorId) return true
    visited.add(cur.id)
    cur = allSets.value.find(s => s.id === cur!.parentSetsEntityId)
  }
  return false
}

const onDropSet = async (data: any, targetSetId: number) => {
  if (!data || !data.type) return

  if (data.type === 'items') {
    const itemIds = data.ids
    if (!itemIds || itemIds.length === 0) return
    try {
      await moveItems(itemIds, targetSetId)
      showToast(`Moved ${itemIds.length} item(s).`, 'success')
      store.selectedItemIds.clear()
      store.selectionMode = false
      await loadData()
    } catch (e) {
      showToast('Failed to move items.', 'error')
    }
  } else if (data.type === 'set' || data.type === 'sets') {
    const setIds: number[] = data.ids || (data.id ? [data.id] : [])
    if (setIds.length === 0) return
    try {
      for (const id of setIds) {
        if (id === targetSetId || isDescendantOf(targetSetId, id)) {
          showToast('Cannot move a set inside one of its own subsets.', 'warning')
          continue
        }
        await moveSet(id, targetSetId)
        store.expandedSets.add(targetSetId)
      }
      showToast(setIds.length > 1 ? `Moved ${setIds.length} sets.` : 'Moved set.', 'success')
      store.selectedSetIds.clear()
      await loadData()
    } catch (e) {
      showToast('Failed to move set(s).', 'error')
    }
  }
}

const onRenameItem = async (itemId: number) => {
  const item = allItems.value.find(i => i.id === itemId)
  if (!item) return

  // extract the file extension from completeFileName
  const parts = item.completeFileName.split('.')
  const extension = parts.length > 1 ? '.' + parts.pop() : ''

  // strip extension from the current fileName for the prompt
  let currentName = item.fileName
  if (extension && currentName.toLowerCase().endsWith(extension.toLowerCase())) {
    currentName = currentName.substring(0, currentName.length - extension.length)
  }

  const newName = await showPrompt(t('cm.rename_item_title'), t('cm.rename_item_msg'), currentName)
  if (newName && newName.trim() !== currentName) {
    try {
      await renameItemApi(itemId, newName.trim())
      showToast(t('cm.items_toggled_toast'), 'success')
      await loadData()
    } catch (err: any) {
      showToast(err.message || t('cm.failed_toast'), 'error')
    }
  }
}
// ===== Drag and Drop Import =====
const isDraggingFiles = ref(false)
const dragCounter = ref(0)

const onDragEnter = (e: DragEvent) => {
  if (!e.dataTransfer?.types?.includes('Files')) return
  e.preventDefault()
  dragCounter.value++
  if (dragCounter.value === 1) isDraggingFiles.value = true
}

const onDragLeave = (e: DragEvent) => {
  if (!e.dataTransfer?.types?.includes('Files')) return
  e.preventDefault()
  dragCounter.value--
  if (dragCounter.value === 0) isDraggingFiles.value = false
}

const onDragOver = (e: DragEvent) => {
  if (!e.dataTransfer?.types?.includes('Files')) return
  e.preventDefault()
}

const onDropFiles = async (e: DragEvent) => {
  if (!e.dataTransfer?.types?.includes('Files')) return
  e.preventDefault()
  dragCounter.value = 0
  isDraggingFiles.value = false

  if (!e.dataTransfer?.files?.length) return

  const files = Array.from(e.dataTransfer.files)
  const validExts = ['.package', '.sims3pack', '.zip', '.rar', '.7z']
  const validFiles = files.filter(f => {
    const lower = f.name.toLowerCase()
    return validExts.some(ext => lower.endsWith(ext))
  })

  if (validFiles.length === 0) {
    const debugNames = files.map(f => f.name).join(', ')
    showToast(`Failed to import. Names: [${debugNames}]`, 'error')
    return
  }

  try {
    const dupCheck = await checkImportDuplicates(validFiles)
    let duplicateAction = 'rename'

    if (dupCheck.hasDuplicates) {
      const choice = await showDuplicateImportModal(dupCheck.duplicates)
      if (!choice) return // User cancelled import
      duplicateAction = choice
    }

    showToast(`Importing ${validFiles.length} file(s)...`, 'info')
    const targetSetId = store.selectedSetId !== null ? store.selectedSetId : undefined
    const res = await uploadFiles(validFiles, duplicateAction, targetSetId)
    const reader = res.body?.getReader()
    if (reader) {
      const decoder = new TextDecoder()
      while (true) {
        const { done } = await reader.read()
        if (done) break
      }
    }
    showToast('Import completed successfully!', 'success')
    loadData()
  } catch (err: any) {
    showToast(err.message || 'Failed to import files', 'error')
  }
}
</script>
