<template>
  <div class="item-card selectable-card" :class="{ selected: selected, disabled: !item.enabled, compact: viewMode === 'compact' }"
       :data-id="item.id"
       :title="item.fileName"
       draggable="true"
       @click="onClick"
       @dragstart.stop="onDragStart"
       @dragend.stop="onDragEnd">

    <template v-if="viewMode === 'compact'">
      <!-- Compact View Mode -->
      <div class="compact-layout">
        <div class="compact-thumb-container">
          <img v-if="!imageError" :src="`${API_BASE}/items/${item.id}/thumbnail`" @error="imageError = true" class="compact-thumb" loading="lazy" decoding="async" />
          <div v-else class="compact-fallback">
            <span class="material-symbols-outlined compact-fallback-icon">image</span>
          </div>
        </div>

        <div class="compact-title" :title="item.fileName">{{ item.fileName }}</div>

        <div class="compact-meta">
          <span class="compact-size">{{ (item.fileSize / 1024).toFixed(2) }} MB</span>
          <span v-for="(badge, bIdx) in itemBadges" :key="bIdx" class="badge" :class="`badge-type-${badge.type}`">
            {{ badge.label }}
          </span>
          <span v-if="!item.enabled" class="badge badge-disabled">{{ t('cm.disabled') }}</span>
        </div>

        <div class="compact-actions compact-actions-flex">
          <button class="btn icon-btn custom-tooltip-container compact-action-btn" :data-tooltip="t('context.search_google')" @click.stop="searchGoogle">
            <span class="material-symbols-outlined icon-18">search</span>
          </button>
          <button class="btn icon-btn custom-tooltip-container compact-action-btn" :data-tooltip="t('context.rename')" @click.stop="renameItem">
            <span class="material-symbols-outlined icon-18">edit</span>
          </button>
        </div>

        <input v-if="selectionMode" type="checkbox" class="compact-checkbox" :checked="selected" />
      </div>
    </template>

    <template v-else>
      <!-- Comfy (Grid) View Mode -->
      <img v-if="!imageError" :src="`${API_BASE}/items/${item.id}/thumbnail`" @error="imageError = true" class="item-thumbnail" loading="lazy" decoding="async" />
      <div v-else class="thumbnail-fallback">
        <span class="material-symbols-outlined comfy-fallback-icon">image</span>
      </div>

      <!-- Top Left User Tags Container -->
      <div class="comfy-top-left-tags">
        <span v-for="tag in parsedUserTags" :key="tag" class="comfy-tag-badge">
          {{ tag }}
        </span>
        <button class="comfy-tag-add-btn" :title="t('context.user_tags')" @click.stop="$emit('edit-tags', item)">
          <span class="material-symbols-outlined" style="font-size: 14px;">add</span>
        </button>
      </div>

      <div class="item-overlay"></div>

      <div class="item-content-wrapper">
        <div class="comfy-checkbox-container">
          <input v-if="selectionMode" type="checkbox" class="comfy-checkbox" :checked="selected" />
        </div>
        <div class="item-info">
          <div class="item-title" :title="item.fileName">{{ item.fileName }}</div>
          <div class="item-meta comfy-meta-flex">
            <div class="comfy-meta-left">
              <span>{{ (item.fileSize / 1024).toFixed(2) }} MB</span>
              <span v-for="(badge, bIdx) in itemBadges" :key="bIdx" class="badge" :class="`badge-type-${badge.type}-comfy`">
                {{ badge.label }}
              </span>
              <span v-if="!item.enabled" class="badge badge-disabled-comfy">{{ t('cm.disabled') }}</span>
            </div>
            <button class="btn icon-btn comfy-info-btn" @click.stop="showInfo = !showInfo">
              <span class="material-symbols-outlined icon-20">info</span>
            </button>
          </div>

          <!-- Extra Info Panel -->
          <div class="item-extra-info" :class="{ 'info-open': showInfo }">
            <div class="comfy-install-date">
              {{ t('cm.added', { date: item.installDate || 'Unknown' }) }}
            </div>
            <div class="comfy-actions-flex">
              <button class="btn icon-btn custom-tooltip-container comfy-action-btn" :data-tooltip="t('context.search_google')" @click.stop="searchGoogle">
                <span class="material-symbols-outlined icon-18">search</span>
              </button>
              <button class="btn icon-btn custom-tooltip-container comfy-action-btn" :data-tooltip="t('context.rename')" @click.stop="renameItem">
                <span class="material-symbols-outlined icon-18">edit</span>
              </button>
              <button class="btn icon-btn custom-tooltip-container comfy-action-btn" :data-tooltip="t('context.retag')" @click.stop="retagItem">
                <span class="material-symbols-outlined icon-18">sell</span>
              </button>
            </div>
          </div>
        </div>
      </div>
    </template>
  </div>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue';
import type { ItemEntity } from '@/types';
import { API_BASE } from '@/api/client';
import { useI18n } from '@/composables/useI18n';

const { t } = useI18n();
const imageError = ref(false);
const showInfo = ref(false);

const props = withDefaults(defineProps<{
  item: ItemEntity;
  selected: boolean;
  selectionMode: boolean;
  viewMode?: string;
}>(), {
  viewMode: 'comfy'
});

const emit = defineEmits<{
  (e: 'select', event: MouseEvent, itemId: number): void;
  (e: 'dragstart', event: DragEvent): void;
  (e: 'dragend', event: DragEvent): void;
  (e: 'rename', itemId: number): void;
  (e: 'retag', item: ItemEntity): void;
  (e: 'edit-tags', item: ItemEntity): void;
}>();

const isPkg = computed(() => props.item.completeFileName.toLowerCase().endsWith('.package'));

const parsedUserTags = computed(() => {
  if (!props.item.userTags) return [];
  return props.item.userTags.split(',').map(s => s.trim()).filter(Boolean);
});

interface ItemBadge {
  label: string;
  type: string;
}

const itemBadges = computed<ItemBadge[]>(() => {
  const pt = props.item.packageType || '';
  const fn = (props.item.fileName || '').toLowerCase();

  // 1. CAS Items
  if (pt === 'CAS') {
    if (props.item.casCategories && props.item.casCategories !== 'Other') {
      const cats = props.item.casCategories.split(',').map(c => c.trim()).filter(c => c && c !== 'Other');
      if (cats.length > 0) {
        return cats.map(c => ({
          label: t('cas_categories.' + c) || c,
          type: 'cas'
        }));
      }
    }
    return [{ label: 'CAS', type: 'cas' }];
  }

  // 2. BuildBuy Items
  if (pt === 'BuildBuy' || pt === 'Build/Buy' || pt === 'Object') {
    return [{ label: t('cm.buildbuy') || 'Build/Buy', type: 'buildbuy' }];
  }

  // 3. Worlds
  if (pt === 'World' || fn.endsWith('.world')) {
    return [{ label: t('other_subcategories.Worlds') || 'Worlds', type: 'world' }];
  }

  // 4. Sims
  if (pt === 'Sim' || fn.endsWith('.sim')) {
    return [{ label: t('other_subcategories.Sims') || 'Sims', type: 'sim' }];
  }

  // 5. Lots
  if (pt === 'Lot') {
    return [{ label: t('other_subcategories.Lots') || 'Lots', type: 'lot' }];
  }

  // 6. Other / Misc -> No badge
  return [];
});

function onClick(e: MouseEvent) {
  emit('select', e, props.item.id);
}

function onDragStart(e: DragEvent) {
  emit('dragstart', e);
}

function onDragEnd(e: DragEvent) {
  emit('dragend', e);
}

function searchGoogle() {
  const query = encodeURIComponent(props.item.fileName);
  window.open(`https://www.google.com/search?q=${query}`, '_blank');
}

function renameItem() {
  emit('rename', props.item.id);
}

function retagItem() {
  emit('retag', props.item);
}
</script>

<style scoped>
.item-card {
  padding: 0;
  overflow: hidden;
  position: relative;
  display: flex;
  flex-direction: column;
  width: 100%;
  height: 100%;
  border: 1px solid var(--border-subtle);
  border-radius: var(--radius-md);
  background: var(--bg-card);
  content-visibility: auto;
  contain-intrinsic-size: auto 256px;
}

.item-thumbnail {
  position: absolute;
  top: 0;
  left: 0;
  width: 100%;
  height: 100%;
  object-fit: cover;
  transition: transform 0.3s ease;
  z-index: 1;
}

.thumbnail-fallback {
  position: absolute;
  top: 0;
  left: 0;
  width: 100%;
  height: 100%;
  display: flex;
  align-items: center;
  justify-content: center;
  background: rgba(0, 0, 0, 0.3);
  color: var(--text-muted);
  z-index: 1;
}

.item-overlay {
  position: absolute;
  bottom: 0;
  left: 0;
  width: 100%;
  height: 60%;
  background: linear-gradient(to top, rgba(12, 15, 20, 0.95) 0%, rgba(12, 15, 20, 0.5) 60%, transparent 100%);
  z-index: 2;
  pointer-events: none;
}

:root[data-theme="light"] .item-overlay,
[data-theme="light"] .item-overlay {
  background: linear-gradient(to top, rgba(255, 255, 255, 0.96) 0%, rgba(255, 255, 255, 0.75) 65%, transparent 100%);
}

.item-content-wrapper {
  position: absolute;
  top: 0;
  left: 0;
  width: 100%;
  height: 100%;
  display: flex;
  flex-direction: column;
  justify-content: space-between;
  z-index: 3;
}

.item-info {
  padding: 1rem;
}

.item-title {
  display: -webkit-box;
  -webkit-line-clamp: 2;
  -webkit-box-orient: vertical;
  overflow: hidden;
  text-overflow: ellipsis;
  font-size: 0.9rem;
  font-weight: 500;
  color: #fff;
  margin-bottom: 0.5rem;
  text-shadow: 0 1px 3px rgba(0,0,0,0.8);
}

:root[data-theme="light"] .item-title,
[data-theme="light"] .item-title {
  color: #0f172a;
  text-shadow: 0 1px 2px rgba(255, 255, 255, 0.9);
}

.item-meta {
  font-size: 0.8rem;
  color: #cbd5e1;
  text-shadow: 0 1px 2px rgba(0,0,0,0.8);
}

:root[data-theme="light"] .item-meta,
[data-theme="light"] .item-meta {
  color: #475569;
  text-shadow: 0 1px 2px rgba(255, 255, 255, 0.9);
}

.item-extra-info {
  max-height: 0;
  opacity: 0;
  overflow: hidden;
  transition: all 0.3s ease;
  margin-top: 0;
}

.item-extra-info.info-open {
  max-height: 80px;
  opacity: 1;
  margin-top: 0.5rem;
}

.item-card:hover .item-thumbnail {
  transform: scale(1.05);
}

.item-card.disabled .item-thumbnail {
  opacity: 0.5;
  filter: grayscale(100%);
}

.item-card.disabled .item-title {
  text-decoration: line-through;
  color: #94a3b8;
}

/* Compact Mode Styles */
.item-card.compact {
  height: auto !important;
  flex-shrink: 0;
  border-radius: var(--radius-sm);
  background: var(--bg-surface);
  contain-intrinsic-size: auto 48px;
}

.compact-layout {
  display: flex;
  align-items: center;
  padding: 0.5rem 1rem;
  width: 100%;
}

.compact-thumb-container {
  width: 32px;
  height: 32px;
  flex-shrink: 0;
  margin-right: 1rem;
  border-radius: 4px;
  overflow: hidden;
  background: rgba(0,0,0,0.2);
  display: flex;
  align-items: center;
  justify-content: center;
}

.compact-thumb {
  width: 100%;
  height: 100%;
  object-fit: cover;
}

.compact-fallback {
  color: var(--text-muted);
}

.compact-title {
  flex: 1;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  font-size: 0.9rem;
  font-weight: 500;
  color: var(--text-main);
}

.compact-meta {
  display: flex;
  align-items: center;
  font-size: 0.85rem;
  color: var(--text-muted);
  margin-left: 1rem;
  flex-wrap: wrap;
  gap: 0.25rem;
}

.item-card.compact:hover {
  background: var(--bg-card);
}

.item-card.compact.disabled .compact-title {
  text-decoration: line-through;
  color: var(--text-muted);
}

.comfy-top-left-tags {
  position: absolute;
  top: 8px;
  left: 8px;
  z-index: 5;
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 4px;
  max-width: calc(100% - 36px);
}

.comfy-tag-badge {
  background: rgba(0, 0, 0, 0.65);
  backdrop-filter: blur(8px);
  color: #ffffff;
  font-size: 0.72rem;
  font-weight: 600;
  padding: 2px 7px;
  border-radius: 4px;
  border: 1px solid rgba(255, 255, 255, 0.2);
  white-space: nowrap;
}

.comfy-tag-add-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 22px;
  height: 22px;
  border-radius: 4px;
  background: rgba(0, 0, 0, 0.65);
  backdrop-filter: blur(8px);
  border: 1px solid rgba(255, 255, 255, 0.25);
  color: #ffffff;
  cursor: pointer;
  opacity: 0;
  transition: opacity 0.2s ease, background 0.2s ease, transform 0.15s ease;
}

.item-card:hover .comfy-tag-add-btn {
  opacity: 1;
}

.comfy-tag-add-btn:hover {
  background: var(--primary);
  border-color: var(--primary);
  transform: scale(1.1);
}
</style>
