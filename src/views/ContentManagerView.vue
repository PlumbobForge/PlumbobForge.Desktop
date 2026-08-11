<template>
  <div id="view-content" class="view">
    <div class="content-manager-layout">

      <!-- Sidebar: Sets Tree -->
      <div class="cm-sidebar" id="cm-sidebar" @contextmenu.prevent="onSidebarContextMenu" @dragover.prevent @drop.prevent="onDropSidebar">
        <div class="cm-sidebar-header" style="position: relative; display: flex; align-items: center; justify-content: space-between;">
          <span>{{ t('cm.sets') }}</span>
          <div style="display: flex; align-items: center; gap: 0.25rem;">
            <button class="btn sort-trigger" style="padding: 4px 8px; font-size: 0.8rem; display: flex; align-items: center; gap: 4px;" :data-tooltip="t('cm.sort_sets')" @click.stop="toggleSetSortDropdown">
              <span class="material-symbols-outlined" style="font-size: 16px;">sort</span>
            </button>
            <button id="btn-create-set" class="btn btn-sm" @click.stop="createSet">+</button>
          </div>
          <div v-if="setSortDropdownOpen" class="context-menu dropdown-menu-left" style="top: 15%; left: 0; min-width: 150px; z-index: 100;">
            <div class="context-menu-item" :class="{ active: setSortBy === 'date' }" @click="setSortBy = 'date'; setSortDropdownOpen = false">
              {{ t('cm.sort_date') }}
            </div>
            <div class="context-menu-divider"></div>
            <div class="context-menu-item" :class="{ active: setSortBy === 'name_asc' }" @click="setSortBy = 'name_asc'; setSortDropdownOpen = false">
              {{ t('cm.sort_name_asc') }}
            </div>
            <div class="context-menu-item" :class="{ active: setSortBy === 'name_desc' }" @click="setSortBy = 'name_desc'; setSortDropdownOpen = false">
              {{ t('cm.sort_name_desc') }}
            </div>
          </div>
        </div>
        <div class="cm-tree-view" v-if="loadingSets">
          <div class="text-muted-padded">Loading Sets...</div>
        </div>
        <div class="cm-tree-view" id="cm-tree-view" v-else @wheel="onTreeWheel" @dragover.prevent @drop.prevent="onDropSidebar">
          <!-- All Items Node -->
          <div class="tree-item" :class="{ active: store.selectedSetId === null, 'drag-over-invalid': isDragOverAll }" @click="selectSet(null)" @contextmenu.prevent.stop @dragover.prevent="onDragOverAll($event)" @dragleave="onDragLeaveAll" @drop.prevent.stop="onDropAll">
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
            @select="selectSet"
            @context-menu="onSetContextMenu"
            @drop="onDropSet"
          />
        </div>
      </div>

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
        <div class="cm-main-header">
          <div class="cm-header-flex gap-4">
            <h3 id="cm-items-title">{{ currentSetName === 'All Items' ? t('cm.all_items') : (currentSetName === 'Legacy' ? t('cm.legacy') : currentSetName) }}</h3>
            <div class="cm-items-stats" id="cm-items-stats" style="color: var(--text-muted); font-size: 0.9rem;">
              {{ t('cm.items_count', { count: filteredItems.length, size: formatSize(totalSize) }) }}
            </div>
          </div>
          <div class="cm-header-flex gap-2">
            <!-- Sort Dropdown -->
            <div class="sort-trigger-wrapper" @click.stop="sortDropdownOpen = !sortDropdownOpen">
              <button class="btn sort-trigger">
                <span class="material-symbols-outlined" style="font-size:20px;">sort</span>
                {{ t('cm.sort') }}
              </button>
              <div v-if="sortDropdownOpen" class="context-menu dropdown-menu-right">
                <div class="context-menu-item" :style="{ color: sortMode === 'date_desc' ? 'var(--primary)' : 'var(--text-main)' }" @click.stop="sortMode = 'date_desc'; sortDropdownOpen = false">
                  {{ t('cm.sort_latest') }}
                </div>
                <div class="context-menu-item" :style="{ color: sortMode === 'date_asc' ? 'var(--primary)' : 'var(--text-main)' }" @click.stop="sortMode = 'date_asc'; sortDropdownOpen = false">
                  {{ t('cm.sort_oldest') }}
                </div>
                <div class="context-menu-divider"></div>
                <div class="context-menu-item" :style="{ color: sortMode === 'alpha_asc' ? 'var(--primary)' : 'var(--text-main)' }" @click.stop="sortMode = 'alpha_asc'; sortDropdownOpen = false">
                  {{ t('cm.sort_alpha_asc') }}
                </div>
                <div class="context-menu-item" :style="{ color: sortMode === 'alpha_desc' ? 'var(--primary)' : 'var(--text-main)' }" @click.stop="sortMode = 'alpha_desc'; sortDropdownOpen = false">
                  {{ t('cm.sort_alpha_desc') }}
                </div>
              </div>
            </div>

            <!-- View Mode Toggles -->
            <div class="view-toggle-container">
              <div class="custom-tooltip-container" :data-tooltip="t('cm.comfy_view')">
                <button class="btn btn-view-toggle" :class="{ active: viewMode === 'comfy' }" @click="viewMode = 'comfy'">
                  <span class="material-symbols-outlined" style="font-size:20px;">grid_view</span>
                </button>
              </div>
              <div class="custom-tooltip-container" :data-tooltip="t('cm.compact_view')">
                <button class="btn btn-view-toggle" :class="{ active: viewMode === 'compact' }" @click="viewMode = 'compact'">
                  <span class="material-symbols-outlined" style="font-size:20px;">view_list</span>
                </button>
              </div>
            </div>

            <div id="cm-action-bar" v-if="store.selectionMode && store.selectedItemIds.size > 0" class="cm-action-bar">
              <span class="cm-action-bar-text">{{ t('cm.selected', { count: store.selectedItemIds.size }) }}</span>
              <button id="btn-enable-selected" class="btn btn-action custom-tooltip-container" :data-tooltip="t('context.enable')" @click="enableSelected(true)">
                <span class="material-symbols-outlined" style="font-size:18px;">check_circle</span>
              </button>
              <button id="btn-disable-selected" class="btn btn-action custom-tooltip-container" :data-tooltip="t('context.disable')" @click="enableSelected(false)">
                <span class="material-symbols-outlined" style="font-size:18px;">block</span>
              </button>
              <button id="btn-retag-selected" class="btn btn-action custom-tooltip-container" :data-tooltip="t('context.retag')" @click="onRetagItem()">
                <span class="material-symbols-outlined" style="font-size:18px;">sell</span>
              </button>
              <button id="btn-tags-selected" class="btn btn-action custom-tooltip-container" :data-tooltip="t('context.user_tags')" @click="onEditTags()">
                <span class="material-symbols-outlined" style="font-size:18px;">label</span>
              </button>
              <button id="btn-move-selected" class="btn btn-action custom-tooltip-container" :data-tooltip="t('modal.move')" @click="moveSelected">
                <span class="material-symbols-outlined" style="font-size:18px;">drive_file_move</span>
              </button>
              <button id="btn-delete-selected" class="btn btn-action btn-danger-outline custom-tooltip-container" :data-tooltip="t('modal.delete')" @click="deleteSelected">
                <span class="material-symbols-outlined" style="font-size:18px;">delete</span>
              </button>
            </div>
            <button
              v-if="store.selectionMode"
              id="btn-select-all"
              class="btn btn-action custom-tooltip-container mr-2"
              :data-tooltip="store.selectedItemIds.size === filteredItems.length && filteredItems.length > 0 ? t('cm.deselect_all') : t('cm.select_all')"
              @click="toggleSelectAll"
            >
              <span class="material-symbols-outlined" style="font-size:18px;">
                {{ store.selectedItemIds.size === filteredItems.length && filteredItems.length > 0 ? 'deselect' : 'select_all' }}
              </span>
            </button>
            <button id="btn-toggle-select" class="btn btn-select-toggle" :class="{ active: store.selectionMode }" @click="toggleSelectionMode">
              {{ store.selectionMode ? t('cm.done') : t('cm.manual_select') }}
            </button>
          </div>
        </div>

        <div :class="viewMode === 'comfy' ? 'cm-items-grid' : 'cm-items-list'" id="cm-items-grid" ref="gridRef" @click.self="clearSelection">
          <div v-if="loadingItems" class="text-muted-padded col-span-full">{{ t('cm.loading_items') }}</div>
          <div v-else-if="filteredItems.length === 0" class="text-muted-padded col-span-full">{{ t('cm.no_items') }}</div>
          <ItemCard
            v-else
            v-for="item in filteredItems"
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
        </div>
      </div>

      <!-- Filter Sidebar -->
      <div class="cm-filter-sidebar">
        <div class="cm-sidebar-header">{{ t('cm.filters') }}</div>

        <div class="filter-group">
          <div class="search-container" style="position: relative;">
            <span class="material-symbols-outlined search-icon">search</span>
            <input
              type="text"
              v-model="searchQuery"
              class="search-input"
              :placeholder="t('cm.search_placeholder')"
              @focus="isSearchFocused = true"
              @blur="onSearchBlur"
              @keydown.enter="addSearchHistory(searchQuery)"
            />
            <span v-if="searchQuery" class="material-symbols-outlined search-clear-icon" @click="searchQuery = ''">close</span>

            <!-- Autocomplete & Search History Dropdown -->
            <div v-if="isSearchFocused && (filteredTagSuggestions.length > 0 || searchHistory.length > 0)" class="search-autocomplete-dropdown">
              <template v-if="filteredTagSuggestions.length > 0">
                <div class="search-autocomplete-section">{{ t('cm.user_tags') }}</div>
                <div v-for="tag in filteredTagSuggestions" :key="'tag-' + tag" class="search-autocomplete-item" @mousedown.prevent="selectSearchSuggestion(tag)">
                  <span class="material-symbols-outlined" style="font-size: 16px; color: var(--primary);">label</span>
                  <span>{{ tag }}</span>
                </div>
              </template>

              <template v-if="searchHistory.length > 0 && !searchQuery">
                <div class="search-autocomplete-section" style="margin-top: 0.25rem;">{{ t('cm.recent_searches') }}</div>
                <div v-for="item in searchHistory" :key="'hist-' + item" class="search-autocomplete-item" @mousedown.prevent="selectSearchSuggestion(item)">
                  <span class="material-symbols-outlined" style="font-size: 16px; color: var(--text-muted);">history</span>
                  <span style="flex: 1;">{{ item }}</span>
                  <span class="material-symbols-outlined" style="font-size: 14px; color: var(--text-muted);" @click.stop="removeSearchHistory(item)">close</span>
                </div>
              </template>
            </div>
          </div>
        </div>

        <div class="filter-group">
          <!-- Type Filter Header -->
          <div class="filter-header-row" @click="isTypeFilterCollapsed = !isTypeFilterCollapsed">
            <label class="filter-label" style="cursor: pointer; margin: 0;">{{ t('cm.type') }}</label>
            <span class="material-symbols-outlined expand-icon" style="font-size: 18px; color: var(--text-muted);">
              {{ isTypeFilterCollapsed ? 'expand_more' : 'expand_less' }}
            </span>
          </div>

          <div v-show="!isTypeFilterCollapsed" class="flex-col gap-2" style="margin-top: 0.25rem;">
            <!-- CAS Main Category Button -->
            <button class="btn filter-btn" :class="{ active: filterTypeCAS }" @click="filterTypeCAS = !filterTypeCAS">
              <div style="display: flex; align-items: center;">
                <span class="material-symbols-outlined mr-2">checkroom</span>
                <span>{{ t('cm.cas_items') }}</span>
              </div>
              <span
                class="material-symbols-outlined filter-btn-chevron"
                :style="{ visibility: filterTypeCAS ? 'visible' : 'hidden' }"
                :title="isCasSectionCollapsed ? 'Expand CAS Subcategories' : 'Collapse CAS Subcategories'"
                @click.stop="isCasSectionCollapsed = !isCasSectionCollapsed"
              >
                {{ isCasSectionCollapsed ? 'expand_more' : 'expand_less' }}
              </span>
            </button>

            <!-- CAS Sub-groups -->
            <div v-if="filterTypeCAS && !isCasSectionCollapsed" class="cas-categories-container" style="display: flex; flex-direction: column; gap: 0.75rem; margin-top: 0.25rem;">
              <!-- Category -->
              <div>
                <div class="filter-sublabel-row" @click="isCasCategoryCollapsed = !isCasCategoryCollapsed">
                  <span class="filter-sublabel" style="font-size: 0.72rem; color: var(--text-muted); font-weight: 600; text-transform: uppercase; letter-spacing: 0.05em;">{{ t('cm.category') }}</span>
                  <span class="material-symbols-outlined expand-icon-sm" style="font-size: 16px; color: var(--text-muted);">
                    {{ isCasCategoryCollapsed ? 'expand_more' : 'expand_less' }}
                  </span>
                </div>
                <div v-show="!isCasCategoryCollapsed" style="display: flex; flex-wrap: wrap; gap: 0.25rem;">
                  <button
                    v-for="cat in casCategoriesList"
                    :key="cat"
                    class="cas-category-pill"
                    :class="{ active: activeCasCategories.has(cat) }"
                    @click="toggleCasCategory(cat)"
                  >
                    <span v-if="casCategoryIcons[cat]" class="material-symbols-outlined mr-1" style="font-size: 14px;">
                      {{ casCategoryIcons[cat] }}
                    </span>
                    {{ t('cas_categories.' + cat) }}
                  </button>
                </div>
              </div>

              <!-- Age -->
              <div>
                <div class="filter-sublabel-row" @click="isCasAgeCollapsed = !isCasAgeCollapsed">
                  <span class="filter-sublabel" style="font-size: 0.72rem; color: var(--text-muted); font-weight: 600; text-transform: uppercase; letter-spacing: 0.05em;">{{ t('cm.age') }}</span>
                  <span class="material-symbols-outlined expand-icon-sm" style="font-size: 16px; color: var(--text-muted);">
                    {{ isCasAgeCollapsed ? 'expand_more' : 'expand_less' }}
                  </span>
                </div>
                <div v-show="!isCasAgeCollapsed" style="display: flex; flex-wrap: wrap; gap: 0.25rem;">
                  <button
                    v-for="age in casAgesList"
                    :key="age"
                    class="cas-category-pill"
                    :class="{ active: activeCasAges.has(age) }"
                    @click="toggleCasAge(age)"
                  >
                    {{ t('cas_ages.' + age) }}
                  </button>
                </div>
              </div>

              <!-- Gender -->
              <div>
                <div class="filter-sublabel-row" @click="isCasGenderCollapsed = !isCasGenderCollapsed">
                  <span class="filter-sublabel" style="font-size: 0.72rem; color: var(--text-muted); font-weight: 600; text-transform: uppercase; letter-spacing: 0.05em;">{{ t('cm.gender') }}</span>
                  <span class="material-symbols-outlined expand-icon-sm" style="font-size: 16px; color: var(--text-muted);">
                    {{ isCasGenderCollapsed ? 'expand_more' : 'expand_less' }}
                  </span>
                </div>
                <div v-show="!isCasGenderCollapsed" style="display: flex; flex-wrap: wrap; gap: 0.25rem;">
                  <button
                    v-for="gen in casGendersList"
                    :key="gen"
                    class="cas-category-pill"
                    :class="{ active: activeCasGenders.has(gen) }"
                    @click="toggleCasGender(gen)"
                  >
                    {{ t('cas_genders.' + gen) }}
                  </button>
                </div>
              </div>

              <!-- Outfit Category -->
              <div>
                <div class="filter-sublabel-row" @click="isCasOutfitCollapsed = !isCasOutfitCollapsed">
                  <span class="filter-sublabel" style="font-size: 0.72rem; color: var(--text-muted); font-weight: 600; text-transform: uppercase; letter-spacing: 0.05em;">{{ t('cm.outfit_category') }}</span>
                  <span class="material-symbols-outlined expand-icon-sm" style="font-size: 16px; color: var(--text-muted);">
                    {{ isCasOutfitCollapsed ? 'expand_more' : 'expand_less' }}
                  </span>
                </div>
                <div v-show="!isCasOutfitCollapsed" style="display: flex; flex-wrap: wrap; gap: 0.25rem;">
                  <button
                    v-for="outfit in casOutfitsList"
                    :key="outfit"
                    class="cas-category-pill"
                    :class="{ active: activeCasOutfits.has(outfit) }"
                    @click="toggleCasOutfit(outfit)"
                  >
                    {{ t('cas_outfits.' + outfit) }}
                  </button>
                </div>
              </div>
            </div>

            <!-- Build/Buy Main Category Button -->
            <button class="btn filter-btn" :class="{ active: filterTypeBuildBuy }" @click="filterTypeBuildBuy = !filterTypeBuildBuy">
              <div style="display: flex; align-items: center;">
                <span class="material-symbols-outlined mr-2">chair</span>
                <span>{{ t('cm.buildbuy_items') }}</span>
              </div>
              <span class="material-symbols-outlined filter-btn-chevron" style="visibility: hidden;">
                expand_less
              </span>
            </button>

            <!-- Other Main Category Button -->
            <button class="btn filter-btn" :class="{ active: filterTypeOther }" @click="filterTypeOther = !filterTypeOther">
              <div style="display: flex; align-items: center;">
                <span class="material-symbols-outlined mr-2">inventory_2</span>
                <span>{{ t('cm.other_items') }}</span>
              </div>
              <span
                class="material-symbols-outlined filter-btn-chevron"
                :style="{ visibility: filterTypeOther ? 'visible' : 'hidden' }"
                :title="isOtherSectionCollapsed ? 'Expand Other Subcategories' : 'Collapse Other Subcategories'"
                @click.stop="isOtherSectionCollapsed = !isOtherSectionCollapsed"
              >
                {{ isOtherSectionCollapsed ? 'expand_more' : 'expand_less' }}
              </span>
            </button>

            <!-- Other Sub-categories -->
            <div v-if="filterTypeOther && !isOtherSectionCollapsed" class="cas-categories-container" style="display: flex; flex-wrap: wrap; gap: 0.25rem; margin-top: 0.25rem;">
              <button
                v-for="sub in otherSubCategoriesList"
                :key="sub"
                class="cas-category-pill"
                :class="{ active: activeOtherSubCategories.has(sub) }"
                @click="toggleOtherSubCategory(sub)"
              >
                <span v-if="otherSubCategoryIcons[sub]" class="material-symbols-outlined mr-1" style="font-size: 14px;">
                  {{ otherSubCategoryIcons[sub] }}
                </span>
                {{ t('other_subcategories.' + sub) }}
              </button>
            </div>
          </div>
        </div>

        <!-- Mode Filter Header (Enabled / Disabled) -->
        <div class="filter-group">
          <div class="filter-header-row" @click="isModeFilterCollapsed = !isModeFilterCollapsed">
            <label class="filter-label" style="cursor: pointer; margin: 0;">{{ t('cm.mode') }}</label>
            <span class="material-symbols-outlined expand-icon" style="font-size: 18px; color: var(--text-muted);">
              {{ isModeFilterCollapsed ? 'expand_more' : 'expand_less' }}
            </span>
          </div>

          <div v-show="!isModeFilterCollapsed" class="flex-col gap-2" style="margin-top: 0.25rem;">
            <button class="btn filter-btn" :class="{ active: filterModeEnabled }" @click="filterModeEnabled = !filterModeEnabled">
              <div style="display: flex; align-items: center;">
                <span class="material-symbols-outlined mr-2">check_box</span>
                <span>{{ t('cm.enabled') }}</span>
              </div>
            </button>
            <button class="btn filter-btn" :class="{ active: filterModeDisabled }" @click="filterModeDisabled = !filterModeDisabled">
              <div style="display: flex; align-items: center;">
                <span class="material-symbols-outlined mr-2">disabled_by_default</span>
                <span>{{ t('cm.disabled') }}</span>
              </div>
            </button>
          </div>
        </div>
      </div>

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
import { ref, computed, onMounted, onUnmounted, watch } from 'vue'
import { fetchSets, fetchItems, createSet as createSetApi, renameSet, deleteSet, moveItems, moveSet, deleteItems, setItemEnabled, renameItemApi, retagItems, updateUserTags, importFiles, uploadFiles, checkImportDuplicates } from '@/api/client'
import { useModal } from '@/composables/useModal'
import { useToast } from '@/composables/useToast'
import { useSelection } from '@/composables/useSelection'
import { useContextMenu } from '@/composables/useContextMenu'
import { useAppStore } from '@/stores/app'
import type { SetEntity, ItemEntity } from '@/types'
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
const allItems = ref<ItemEntity[]>([])
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
      while (cur && cur.parentSetsEntityId) {
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
const filterTypeCAS = ref(true)
const filterTypeBuildBuy = ref(true)
const filterTypeOther = ref(true)

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

const activeCasCategories = ref(new Set(casCategoriesList))
const activeCasAges = ref(new Set(casAgesList))
const activeCasGenders = ref(new Set(casGendersList))
const activeCasOutfits = ref(new Set(casOutfitsList))

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

const otherSubCategoriesList = ['Worlds', 'Sims', 'Lots', 'Misc']

const otherSubCategoryIcons: Record<string, string> = {
  'Worlds': 'public',
  'Sims': 'person',
  'Lots': 'home',
  'Misc': 'category'
}

const activeOtherSubCategories = ref(new Set(otherSubCategoriesList))

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

const filterModeEnabled = ref(true)
const filterModeDisabled = ref(true)

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
      store.isDirty = allSets.value.some(s => s.dirty)
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
  store.selectedSetId = id
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
          const newName = await showPrompt(t('cm.rename_set_prompt', { name: set.name }))
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
          showToast(`Deleted ${targetIds.length} item(s).`, 'success')
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
        if (id !== targetSetId) {
          await moveSet(id, targetSetId)
          store.expandedSets.add(targetSetId)
        }
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
