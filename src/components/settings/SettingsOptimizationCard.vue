<template>
  <div class="card">
    <h3 class="settings-card-title">
      <span class="material-symbols-outlined title-icon">speed</span>
      {{ t('settings.game_optimization') }}
    </h3>

    <div class="settings-form-flex" style="flex-direction: column; gap: 1.25rem;">
      <!-- Cache Method -->
      <div class="form-group">
        <label>{{ t('settings.cache_method') }}</label>
        <div class="sort-trigger-wrapper" @click.stop="cacheMethodDropdownOpen = !cacheMethodDropdownOpen">
          <button class="btn sort-trigger" style="width: 100%; justify-content: space-between;">
            <span>{{ getCacheMethodLabel(cacheMethod) }}</span>
            <span class="material-symbols-outlined" style="font-size:20px;">expand_more</span>
          </button>
          <div v-if="cacheMethodDropdownOpen" class="context-menu" style="position: absolute; width: 100%; top: 100%; margin-top: 4px; z-index: 100;">
            <div class="context-menu-item" :style="{ color: cacheMethod === 'Dynamic' ? 'var(--primary)' : 'var(--text-main)' }" @click.stop="selectCacheMethod('Dynamic')">
              {{ t('settings.cache_method_dynamic') }}
            </div>
            <div class="context-menu-item" :style="{ color: cacheMethod === 'Static' ? 'var(--primary)' : 'var(--text-main)' }" @click.stop="selectCacheMethod('Static')">
              {{ t('settings.cache_method_static') }}
            </div>
          </div>
        </div>
        <div class="form-text" style="margin-top: 0.4rem;">
          {{ cacheMethod === 'Static' ? t('settings.cache_method_static_desc') : t('settings.cache_method_dynamic_desc') }}
        </div>
      </div>

      <!-- Compression Level -->
      <div class="form-group">
        <label>{{ t('settings.cache_compression') }}</label>
        <div class="sort-trigger-wrapper" @click.stop="compressionDropdownOpen = !compressionDropdownOpen">
          <button class="btn sort-trigger" style="width: 100%; justify-content: space-between;">
            <span>{{ getCompressionLabel(compressionLevel) }}</span>
            <span class="material-symbols-outlined" style="font-size:20px;">expand_more</span>
          </button>
          <div v-if="compressionDropdownOpen" class="context-menu" style="position: absolute; width: 100%; top: 100%; margin-top: 4px; z-index: 100;">
            <div class="context-menu-item" :style="{ color: compressionLevel === 0 ? 'var(--primary)' : 'var(--text-main)' }" @click.stop="selectCompression(0)">{{ t('settings.no_compression') }}</div>
            <div class="context-menu-item" :style="{ color: compressionLevel === 1 ? 'var(--primary)' : 'var(--text-main)' }" @click.stop="selectCompression(1)">{{ t('settings.low_compression') }}</div>
            <div class="context-menu-item" :style="{ color: compressionLevel === 2 ? 'var(--primary)' : 'var(--text-main)' }" @click.stop="selectCompression(2)">{{ t('settings.medium_compression') }}</div>
            <div class="context-menu-item" :style="{ color: compressionLevel === 3 ? 'var(--primary)' : 'var(--text-main)' }" @click.stop="selectCompression(3)">{{ t('settings.high_compression') }}</div>
            <div class="context-menu-item" :style="{ color: compressionLevel === 4 ? 'var(--primary)' : 'var(--text-main)' }" @click.stop="selectCompression(4)">{{ t('settings.very_high_compression') }}</div>
          </div>
        </div>
        <div class="form-text" style="margin-top: 0.4rem;">
          {{ t('settings.compression_desc') }}
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
  cacheMethod: string;
  compressionLevel: number;
}>();

const emit = defineEmits<{
  (e: 'select-cache-method', method: string): void;
  (e: 'select-compression-level', level: number): void;
}>();

const cacheMethodDropdownOpen = ref(false);
const compressionDropdownOpen = ref(false);

function getCacheMethodLabel(method: string) {
  return method === 'Static' ? t('settings.cache_method_static') : t('settings.cache_method_dynamic');
}

function selectCacheMethod(method: string) {
  cacheMethodDropdownOpen.value = false;
  emit('select-cache-method', method);
}

function getCompressionLabel(level: number) {
  switch (level) {
    case 0: return t('settings.no_compression');
    case 1: return t('settings.low_compression');
    case 2: return t('settings.medium_compression');
    case 3: return t('settings.high_compression');
    case 4: return t('settings.very_high_compression');
    default: return t('settings.low_compression');
  }
}

function selectCompression(level: number) {
  compressionDropdownOpen.value = false;
  emit('select-compression-level', level);
}
</script>
