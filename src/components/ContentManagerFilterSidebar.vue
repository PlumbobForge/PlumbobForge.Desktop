<template>
  <div class="cm-filter-sidebar">
    <div class="cm-sidebar-header" style="display: flex; align-items: center; justify-content: space-between;">
      <span>{{ t('cm.filters') }}</span>
    </div>

    <div class="filter-group">
      <div class="search-container" style="position: relative;">
        <span class="material-symbols-outlined search-icon">search</span>
        <input
          type="text"
          :value="searchQuery"
          @input="$emit('update:searchQuery', ($event.target as HTMLInputElement).value)"
          class="search-input"
          :placeholder="t('cm.search_placeholder')"
          @focus="isSearchFocused = true"
          @blur="onSearchBlur"
          @keydown.enter="addSearchHistory(searchQuery)"
        />
        <span v-if="searchQuery" class="material-symbols-outlined search-clear-icon" @click="$emit('update:searchQuery', '')">close</span>

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

    <!-- Type Filters -->
    <div class="filter-group">
      <div class="filter-header-row" @click="isTypeFilterCollapsed = !isTypeFilterCollapsed">
        <label class="filter-label" style="cursor: pointer; margin: 0;">{{ t('cm.type') }}</label>
        <span class="material-symbols-outlined expand-icon" style="font-size: 18px; color: var(--text-muted);">
          {{ isTypeFilterCollapsed ? 'expand_more' : 'expand_less' }}
        </span>
      </div>

      <div v-show="!isTypeFilterCollapsed" class="flex-col gap-2" style="margin-top: 0.25rem;">
        <!-- CAS Main Category Button -->
        <button class="btn filter-btn" :class="{ active: filterTypeCAS }" @click="$emit('update:filterTypeCAS', !filterTypeCAS)">
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
            <div class="filter-sublabel-row" style="display: flex; align-items: center; justify-content: space-between;">
              <span class="filter-sublabel" style="font-size: 0.72rem; color: var(--text-muted); font-weight: 600; text-transform: uppercase; letter-spacing: 0.05em; cursor: pointer; flex: 1;" @click="isCasCategoryCollapsed = !isCasCategoryCollapsed">{{ t('cm.category') }}</span>
              <button class="btn-text-action" style="font-size: 0.7rem; color: var(--primary); background: none; border: none; cursor: pointer; margin-right: 6px; padding: 0;" @click.stop="$emit('toggle-all-cas-categories')">
                {{ activeCasCategories.size === casCategoriesList.length ? t('cm.clear_all') : t('cm.select_all') }}
              </button>
              <span class="material-symbols-outlined expand-icon-sm" style="font-size: 16px; color: var(--text-muted); cursor: pointer;" @click="isCasCategoryCollapsed = !isCasCategoryCollapsed">
                {{ isCasCategoryCollapsed ? 'expand_more' : 'expand_less' }}
              </span>
            </div>
            <div v-show="!isCasCategoryCollapsed" style="display: flex; flex-wrap: wrap; gap: 0.25rem;">
              <button
                v-for="cat in casCategoriesList"
                :key="cat"
                class="cas-category-pill"
                :class="{ active: activeCasCategories.has(cat) }"
                @click="$emit('toggle-cas-category', cat)"
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
            <div class="filter-sublabel-row" style="display: flex; align-items: center; justify-content: space-between;">
              <span class="filter-sublabel" style="font-size: 0.72rem; color: var(--text-muted); font-weight: 600; text-transform: uppercase; letter-spacing: 0.05em; cursor: pointer; flex: 1;" @click="isCasAgeCollapsed = !isCasAgeCollapsed">{{ t('cm.age') }}</span>
              <button class="btn-text-action" style="font-size: 0.7rem; color: var(--primary); background: none; border: none; cursor: pointer; margin-right: 6px; padding: 0;" @click.stop="$emit('toggle-all-cas-ages')">
                {{ activeCasAges.size === casAgesList.length ? t('cm.clear_all') : t('cm.select_all') }}
              </button>
              <span class="material-symbols-outlined expand-icon-sm" style="font-size: 16px; color: var(--text-muted); cursor: pointer;" @click="isCasAgeCollapsed = !isCasAgeCollapsed">
                {{ isCasAgeCollapsed ? 'expand_more' : 'expand_less' }}
              </span>
            </div>
            <div v-show="!isCasAgeCollapsed" style="display: flex; flex-wrap: wrap; gap: 0.25rem;">
              <button
                v-for="age in casAgesList"
                :key="age"
                class="cas-category-pill"
                :class="{ active: activeCasAges.has(age) }"
                @click="$emit('toggle-cas-age', age)"
              >
                {{ t('cas_ages.' + age) }}
              </button>
            </div>
          </div>

          <!-- Gender -->
          <div>
            <div class="filter-sublabel-row" style="display: flex; align-items: center; justify-content: space-between;">
              <span class="filter-sublabel" style="font-size: 0.72rem; color: var(--text-muted); font-weight: 600; text-transform: uppercase; letter-spacing: 0.05em; cursor: pointer; flex: 1;" @click="isCasGenderCollapsed = !isCasGenderCollapsed">{{ t('cm.gender') }}</span>
              <button class="btn-text-action" style="font-size: 0.7rem; color: var(--primary); background: none; border: none; cursor: pointer; margin-right: 6px; padding: 0;" @click.stop="$emit('toggle-all-cas-genders')">
                {{ activeCasGenders.size === casGendersList.length ? t('cm.clear_all') : t('cm.select_all') }}
              </button>
              <span class="material-symbols-outlined expand-icon-sm" style="font-size: 16px; color: var(--text-muted); cursor: pointer;" @click="isCasGenderCollapsed = !isCasGenderCollapsed">
                {{ isCasGenderCollapsed ? 'expand_more' : 'expand_less' }}
              </span>
            </div>
            <div v-show="!isCasGenderCollapsed" style="display: flex; flex-wrap: wrap; gap: 0.25rem;">
              <button
                v-for="gen in casGendersList"
                :key="gen"
                class="cas-category-pill"
                :class="{ active: activeCasGenders.has(gen) }"
                @click="$emit('toggle-cas-gender', gen)"
              >
                {{ t('cas_genders.' + gen) }}
              </button>
            </div>
          </div>

          <!-- Outfit Category -->
          <div>
            <div class="filter-sublabel-row" style="display: flex; align-items: center; justify-content: space-between;">
              <span class="filter-sublabel" style="font-size: 0.72rem; color: var(--text-muted); font-weight: 600; text-transform: uppercase; letter-spacing: 0.05em; cursor: pointer; flex: 1;" @click="isCasOutfitCollapsed = !isCasOutfitCollapsed">{{ t('cm.outfit_category') }}</span>
              <button class="btn-text-action" style="font-size: 0.7rem; color: var(--primary); background: none; border: none; cursor: pointer; margin-right: 6px; padding: 0;" @click.stop="$emit('toggle-all-cas-outfits')">
                {{ activeCasOutfits.size === casOutfitsList.length ? t('cm.clear_all') : t('cm.select_all') }}
              </button>
              <span class="material-symbols-outlined expand-icon-sm" style="font-size: 16px; color: var(--text-muted); cursor: pointer;" @click="isCasOutfitCollapsed = !isCasOutfitCollapsed">
                {{ isCasOutfitCollapsed ? 'expand_more' : 'expand_less' }}
              </span>
            </div>
            <div v-show="!isCasOutfitCollapsed" style="display: flex; flex-wrap: wrap; gap: 0.25rem;">
              <button
                v-for="outfit in casOutfitsList"
                :key="outfit"
                class="cas-category-pill"
                :class="{ active: activeCasOutfits.has(outfit) }"
                @click="$emit('toggle-cas-outfit', outfit)"
              >
                {{ t('cas_outfits.' + outfit) }}
              </button>
            </div>
          </div>
        </div>

        <!-- Build/Buy Main Category Button -->
        <button class="btn filter-btn" :class="{ active: filterTypeBuildBuy }" @click="$emit('update:filterTypeBuildBuy', !filterTypeBuildBuy)">
          <div style="display: flex; align-items: center;">
            <span class="material-symbols-outlined mr-2">chair</span>
            <span>{{ t('cm.buildbuy_items') }}</span>
          </div>
          <span class="material-symbols-outlined filter-btn-chevron" style="visibility: hidden;">
            expand_less
          </span>
        </button>

        <!-- Other Main Category Button -->
        <button class="btn filter-btn" :class="{ active: filterTypeOther }" @click="$emit('update:filterTypeOther', !filterTypeOther)">
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
            @click="$emit('toggle-other-subcategory', sub)"
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
        <button class="btn filter-btn" :class="{ active: filterModeEnabled }" @click="$emit('update:filterModeEnabled', !filterModeEnabled)">
          <div style="display: flex; align-items: center;">
            <span class="material-symbols-outlined mr-2">check_box</span>
            <span>{{ t('cm.enabled') }}</span>
          </div>
        </button>
        <button class="btn filter-btn" :class="{ active: filterModeDisabled }" @click="$emit('update:filterModeDisabled', !filterModeDisabled)">
          <div style="display: flex; align-items: center;">
            <span class="material-symbols-outlined mr-2">disabled_by_default</span>
            <span>{{ t('cm.disabled') }}</span>
          </div>
        </button>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, computed } from 'vue';
import { useI18n } from '@/composables/useI18n';

const { t } = useI18n();

const props = withDefaults(defineProps<{
  searchQuery: string;
  userTagsList?: string[];
  filterTypeCAS: boolean;
  filterTypeBuildBuy: boolean;
  filterTypeOther: boolean;
  activeCasCategories: Set<string>;
  activeCasAges: Set<string>;
  activeCasGenders: Set<string>;
  activeCasOutfits: Set<string>;
  activeOtherSubCategories: Set<string>;
  filterModeEnabled: boolean;
  filterModeDisabled: boolean;
}>(), {
  userTagsList: () => []
});

const emit = defineEmits<{
  (e: 'update:searchQuery', val: string): void;
  (e: 'update:filterTypeCAS', val: boolean): void;
  (e: 'update:filterTypeBuildBuy', val: boolean): void;
  (e: 'update:filterTypeOther', val: boolean): void;
  (e: 'toggle-cas-category', cat: string): void;
  (e: 'toggle-cas-age', age: string): void;
  (e: 'toggle-cas-gender', gen: string): void;
  (e: 'toggle-cas-outfit', outfit: string): void;
  (e: 'toggle-all-cas-categories'): void;
  (e: 'toggle-all-cas-ages'): void;
  (e: 'toggle-all-cas-genders'): void;
  (e: 'toggle-all-cas-outfits'): void;
  (e: 'toggle-other-subcategory', sub: string): void;
  (e: 'toggle-collapse'): void;
  (e: 'update:filterModeEnabled', val: boolean): void;
  (e: 'update:filterModeDisabled', val: boolean): void;
}>();

const isSearchFocused = ref(false);
const searchHistory = ref<string[]>(JSON.parse(localStorage.getItem('plumbobforge_search_history') || '[]'));

const isTypeFilterCollapsed = ref(false);
const isCasSectionCollapsed = ref(false);
const isCasCategoryCollapsed = ref(false);
const isCasAgeCollapsed = ref(false);
const isCasGenderCollapsed = ref(false);
const isCasOutfitCollapsed = ref(false);
const isOtherSectionCollapsed = ref(false);
const isModeFilterCollapsed = ref(false);

const casCategoriesList = ['Hair', 'Full body', 'Tops', 'Bottoms', 'Shoes', 'Details', 'Skins', 'Accessories', 'Sliders', 'Presets', 'Other'];
const casAgesList = ['Baby', 'Toddler', 'Child', 'Teen', 'YoungAdult', 'Adult', 'Elder'];
const casGendersList = ['Male', 'Female'];
const casOutfitsList = ['Everyday', 'Formal', 'Sleepwear', 'Swimwear', 'Athletic', 'Career', 'Outerwear'];
const otherSubCategoriesList = ['Worlds', 'Sims', 'Lots', 'Misc'];

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
};

const otherSubCategoryIcons: Record<string, string> = {
  'Worlds': 'public',
  'Sims': 'person',
  'Lots': 'home',
  'Misc': 'category'
};

const filteredTagSuggestions = computed(() => {
  const list = props.userTagsList || [];
  const query = props.searchQuery.trim().toLowerCase();
  if (!query) return list.slice(0, 5);
  return list.filter(tag => tag.toLowerCase().includes(query)).slice(0, 5);
});

function onSearchBlur() {
  setTimeout(() => { isSearchFocused.value = false; }, 200);
}

function selectSearchSuggestion(val: string) {
  emit('update:searchQuery', val);
  addSearchHistory(val);
  isSearchFocused.value = false;
}

function addSearchHistory(val: string) {
  const trimmed = val.trim();
  if (!trimmed) return;
  const history = searchHistory.value.filter(h => h.toLowerCase() !== trimmed.toLowerCase());
  history.unshift(trimmed);
  if (history.length > 5) history.pop();
  searchHistory.value = history;
  localStorage.setItem('plumbobforge_search_history', JSON.stringify(history));
}

function removeSearchHistory(val: string) {
  searchHistory.value = searchHistory.value.filter(h => h !== val);
  localStorage.setItem('plumbobforge_search_history', JSON.stringify(searchHistory.value));
}
</script>
