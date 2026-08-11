<template>
  <div class="card">
    <h3 class="settings-card-title">
      <span class="material-symbols-outlined title-icon">favorite</span>
      {{ t('settings.special_thanks') }}
    </h3>

    <div class="thanks-simple-container">
      <div class="thanks-simple-section">
        <div class="thanks-section-title">
          <span class="material-symbols-outlined">code</span>
          <span>{{ t('settings.code_libraries') }}</span>
        </div>
        <div class="thanks-section-body">
          <span>granthes for <a href="https://modthesims.info/d/461888/" target="_blank" rel="noopener noreferrer">CC Magic</a> &amp; S3ToolKit</span>,
          <span>Peter for <a href="https://s3pi.sourceforge.net/" target="_blank" rel="noopener noreferrer">s3pi library</a></span>
        </div>
      </div>

      <div class="thanks-simple-section">
        <div class="thanks-section-title">
          <span class="material-symbols-outlined">translate</span>
          <span>{{ t('settings.translations') }}</span>
        </div>
        <div class="thanks-section-body">
          <span><a href="https://friendofbellas.tumblr.com/" target="_blank" rel="noopener noreferrer">friendofbellas</a> (Greek Translation, edited)</span>
        </div>
      </div>

      <div class="thanks-simple-section">
        <div class="thanks-section-title">
          <span class="material-symbols-outlined">code_blocks</span>
          <span>{{ t('settings.source_code') }}</span>
        </div>
        <div class="thanks-section-body">
          <a href="https://github.com/PlumbobForge" target="_blank" rel="noopener noreferrer">PlumbobForge on GitHub</a>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { useI18n } from '@/composables/useI18n';

const { t } = useI18n();
</script>
