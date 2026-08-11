<template>
  <div class="card">
    <h3 class="settings-card-title">
      <span class="material-symbols-outlined title-icon">tune</span>
      {{ t('settings.preferences_appearance') }}
    </h3>

    <div class="settings-form-row">
      <!-- Language -->
      <div class="form-group flex-1">
        <label>{{ t('settings.language') }}</label>
        <div class="sort-trigger-wrapper" @click.stop="languageDropdownOpen = !languageDropdownOpen">
          <button class="btn sort-trigger" style="width: 100%; justify-content: space-between;">
            <span>{{ getLanguageLabel(language) }}</span>
            <span class="material-symbols-outlined" style="font-size:20px;">expand_more</span>
          </button>
          <div v-if="languageDropdownOpen" class="context-menu" style="position: absolute; width: 100%; top: 100%; margin-top: 4px; z-index: 100;">
            <div class="context-menu-item" :style="{ color: language === 'auto' ? 'var(--primary)' : 'var(--text-main)' }" @click.stop="selectLang('auto')">{{ t('settings.language_auto') }}</div>
            <div class="context-menu-item" :style="{ color: language === 'en' ? 'var(--primary)' : 'var(--text-main)' }" @click.stop="selectLang('en')">English</div>
            <div class="context-menu-item" :style="{ color: language === 'pl' ? 'var(--primary)' : 'var(--text-main)' }" @click.stop="selectLang('pl')">Polski</div>
            <div class="context-menu-item" :style="{ color: language === 'uk' ? 'var(--primary)' : 'var(--text-main)' }" @click.stop="selectLang('uk')">Українська</div>
            <div class="context-menu-item" :style="{ color: language === 'el' ? 'var(--primary)' : 'var(--text-main)' }" @click.stop="selectLang('el')">Ελληνικά</div>
          </div>
        </div>
      </div>

      <!-- Theme -->
      <div class="form-group flex-1">
        <label>{{ t('settings.theme') }}</label>
        <div class="sort-trigger-wrapper" @click.stop="themeDropdownOpen = !themeDropdownOpen">
          <button class="btn sort-trigger" style="width: 100%; justify-content: space-between;">
            <span>{{ getThemeLabel(theme) }}</span>
            <span class="material-symbols-outlined" style="font-size:20px;">expand_more</span>
          </button>
          <div v-if="themeDropdownOpen" class="context-menu" style="position: absolute; width: 100%; top: 100%; margin-top: 4px; z-index: 100;">
            <div class="context-menu-item" :style="{ color: theme === 'auto' ? 'var(--primary)' : 'var(--text-main)' }" @click.stop="selectTh('auto')">{{ t('settings.theme_auto') }}</div>
            <div class="context-menu-item" :style="{ color: theme === 'dark' ? 'var(--primary)' : 'var(--text-main)' }" @click.stop="selectTh('dark')">{{ t('settings.theme_dark') }}</div>
            <div class="context-menu-item" :style="{ color: theme === 'light' ? 'var(--primary)' : 'var(--text-main)' }" @click.stop="selectTh('light')">{{ t('settings.theme_light') }}</div>
          </div>
        </div>
      </div>
    </div>

    <!-- App Statistics Badges -->
    <div class="settings-stats-row">
      <div class="stat-badge">
        <span class="material-symbols-outlined stat-icon">folder_zip</span>
        <div class="stat-info">
          <span class="stat-num">{{ setsCount }}</span>
          <span class="stat-lbl">{{ t('settings.sets_stat') }}</span>
        </div>
      </div>
      <div class="stat-badge">
        <span class="material-symbols-outlined stat-icon">inventory_2</span>
        <div class="stat-info">
          <span class="stat-num">{{ itemsCount }}</span>
          <span class="stat-lbl">{{ t('settings.items_stat') }}</span>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref } from 'vue';
import { useI18n } from '@/composables/useI18n';

const { t } = useI18n();

const props = defineProps<{
  language: string;
  theme: string;
  setsCount: number;
  itemsCount: number;
}>();

const emit = defineEmits<{
  (e: 'select-language', lang: string): void;
  (e: 'select-theme', theme: string): void;
}>();

const languageDropdownOpen = ref(false);
const themeDropdownOpen = ref(false);

function getLanguageLabel(lang: string) {
  switch (lang) {
    case 'auto': return t('settings.language_auto');
    case 'en': return 'English';
    case 'pl': return 'Polski';
    case 'uk': return 'Українська';
    case 'el': return 'Ελληνικά';
    default: return t('settings.language_auto');
  }
}

function selectLang(lang: string) {
  languageDropdownOpen.value = false;
  emit('select-language', lang);
}

function getThemeLabel(th: string) {
  switch (th) {
    case 'auto': return t('settings.theme_auto');
    case 'dark': return t('settings.theme_dark');
    case 'light': return t('settings.theme_light');
    default: return t('settings.theme_auto');
  }
}

function selectTh(th: string) {
  themeDropdownOpen.value = false;
  emit('select-theme', th);
}
</script>
