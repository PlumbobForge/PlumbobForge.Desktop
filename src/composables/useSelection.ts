import { ref } from 'vue';
import { useAppStore } from '@/stores/app';

export function useSelection(targetSelectionSet?: Set<number>, itemSelector: string = '.selectable-card') {
  const store = useAppStore();
  const isDraggingSelection = ref(false);
  const wasJustDragging = ref(false);

  const getSelectionSet = () => targetSelectionSet || store.selectedItemIds;

  let containerElement: HTMLElement | null = null;
  let scrollContainerElement: HTMLElement | null = null;
  let selectionBox: HTMLElement | null = null;

  let startClientX = 0;
  let startClientY = 0;
  let initialScrollTop = 0;
  let initialScrollLeft = 0;

  let lastMouseX = 0;
  let lastMouseY = 0;

  let isPointerDown = false;
  let dragBaseSelection = new Set<number>();
  let dragMode: 'replace' | 'toggle' = 'replace';

  let animationFrameId: number | null = null;
  let autoScrollRafId: number | null = null;
  let activeAutoScrollSpeedY = 0;

  // Helper to find the scrollable container parent or element itself
  function findScrollContainer(el: HTMLElement | null): HTMLElement {
    let curr = el;
    while (curr && curr !== document.body) {
      const style = window.getComputedStyle(curr);
      const overflowY = style.overflowY;
      if ((overflowY === 'auto' || overflowY === 'scroll') && curr.scrollHeight > curr.clientHeight) {
        return curr;
      }
      curr = curr.parentElement;
    }
    return el || document.documentElement;
  }

  const updateSelectionBox = (mouseX: number, mouseY: number) => {
    if (!isDraggingSelection.value || !selectionBox) return;

    const scrollEl = scrollContainerElement || findScrollContainer(containerElement);
    const currentScrollTop = scrollEl ? scrollEl.scrollTop : 0;
    const currentScrollLeft = scrollEl ? scrollEl.scrollLeft : 0;

    // Calculate effective start Y/X in CURRENT viewport client coordinates
    const effectiveStartX = startClientX - (currentScrollLeft - initialScrollLeft);
    const effectiveStartY = startClientY - (currentScrollTop - initialScrollTop);

    const left = Math.min(mouseX, effectiveStartX);
    const top = Math.min(mouseY, effectiveStartY);
    const width = Math.abs(mouseX - effectiveStartX);
    const height = Math.abs(mouseY - effectiveStartY);

    selectionBox.style.left = `${left}px`;
    selectionBox.style.top = `${top}px`;
    selectionBox.style.width = `${width}px`;
    selectionBox.style.height = `${height}px`;

    if (animationFrameId === null) {
      animationFrameId = requestAnimationFrame(() => {
        checkIntersections({ left, top, right: left + width, bottom: top + height });
        animationFrameId = null;
      });
    }
  };

  const handleAutoScroll = () => {
    if (!isDraggingSelection.value || !containerElement) {
      activeAutoScrollSpeedY = 0;
      return;
    }

    const scrollEl = scrollContainerElement || findScrollContainer(containerElement);
    if (!scrollEl) return;

    const rect = scrollEl.getBoundingClientRect();
    const threshold = 40; // distance from top/bottom boundary in px
    activeAutoScrollSpeedY = 0;

    if (lastMouseY < rect.top + threshold) {
      const ratio = (rect.top + threshold - lastMouseY) / threshold;
      activeAutoScrollSpeedY = -Math.max(4, Math.round(ratio * 20));
    } else if (lastMouseY > rect.bottom - threshold) {
      const ratio = (lastMouseY - (rect.bottom - threshold)) / threshold;
      activeAutoScrollSpeedY = Math.max(4, Math.round(ratio * 20));
    }

    if (activeAutoScrollSpeedY !== 0 && autoScrollRafId === null) {
      const autoScrollLoop = () => {
        if (!isDraggingSelection.value || activeAutoScrollSpeedY === 0) {
          autoScrollRafId = null;
          return;
        }

        const sEl = scrollContainerElement || findScrollContainer(containerElement);
        if (sEl) {
          const prevTop = sEl.scrollTop;
          sEl.scrollTop += activeAutoScrollSpeedY;
          if (sEl.scrollTop !== prevTop) {
            updateSelectionBox(lastMouseX, lastMouseY);
          }
        }

        autoScrollRafId = requestAnimationFrame(autoScrollLoop);
      };
      autoScrollRafId = requestAnimationFrame(autoScrollLoop);
    }
  };

  const onScroll = () => {
    if (isDraggingSelection.value) {
      updateSelectionBox(lastMouseX, lastMouseY);
    }
  };

  const onPointerDown = (e: MouseEvent) => {
    // Only left click
    if (e.button !== 0) return;

    const target = e.target as HTMLElement;
    // Don't start selection box on interactive elements
    if (target.closest('button, input, select, .context-menu, .comfy-info-btn, .item-actions, .caret')) return;

    // Check if clicking directly on a card or tree node
    const cardEl = target.closest(itemSelector) as HTMLElement;
    const clickedId = cardEl ? Number(cardEl.dataset.id) : null;
    const currentSet = getSelectionSet();
    const isClickedAlreadySelected = clickedId !== null && !isNaN(clickedId) && currentSet.has(clickedId);

    // If starting down on an already-selected card/item without Ctrl/Shift, don't initiate drag-box selection
    if (cardEl && isClickedAlreadySelected && !e.ctrlKey && !e.metaKey && !e.shiftKey) {
      isPointerDown = false;
      return;
    }

    // Don't start drag on the scrollbar
    if (containerElement && target === containerElement) {
      if (e.offsetX > containerElement.clientWidth || e.offsetY > containerElement.clientHeight) return;
    }

    isPointerDown = true;
    startClientX = e.clientX;
    startClientY = e.clientY;
    lastMouseX = e.clientX;
    lastMouseY = e.clientY;

    scrollContainerElement = findScrollContainer(containerElement);
    initialScrollTop = scrollContainerElement ? scrollContainerElement.scrollTop : 0;
    initialScrollLeft = scrollContainerElement ? scrollContainerElement.scrollLeft : 0;

    if (store.selectionMode || e.ctrlKey || e.metaKey || e.shiftKey || isClickedAlreadySelected) {
      dragMode = 'toggle';
      dragBaseSelection = new Set(currentSet);
    } else {
      dragMode = 'replace';
      dragBaseSelection = new Set();
    }
  };

  interface CachedCardRect {
    id: number;
    left: number;
    top: number;
    right: number;
    bottom: number;
  }

  let cachedCardRects: CachedCardRect[] = [];

  function cacheCardRects() {
    if (!containerElement) return;
    const scrollEl = scrollContainerElement || findScrollContainer(containerElement);
    const scrollTop = scrollEl ? scrollEl.scrollTop : 0;
    const scrollLeft = scrollEl ? scrollEl.scrollLeft : 0;

    const cards = containerElement.querySelectorAll(itemSelector);
    cachedCardRects = [];
    cards.forEach((card) => {
      const el = card as HTMLElement;
      if (el.dataset.builtin === 'true') return;

      const id = Number(el.dataset.id);
      if (isNaN(id)) return;

      const rect = card.getBoundingClientRect();
      cachedCardRects.push({
        id,
        left: rect.left + scrollLeft,
        top: rect.top + scrollTop,
        right: rect.right + scrollLeft,
        bottom: rect.bottom + scrollTop
      });
    });
  }

  const onPointerMove = (e: MouseEvent) => {
    if (!isPointerDown) return;

    lastMouseX = e.clientX;
    lastMouseY = e.clientY;

    const dx = e.clientX - startClientX;
    const dy = e.clientY - startClientY;

    if (!isDraggingSelection.value && Math.sqrt(dx * dx + dy * dy) > 4) {
      isDraggingSelection.value = true;
      cacheCardRects();

      if (dragMode === 'replace') {
        getSelectionSet().clear();
      }

      selectionBox = document.createElement('div');
      selectionBox.className = 'selection-area';
      selectionBox.style.position = 'fixed';
      selectionBox.style.zIndex = '9999';
      selectionBox.style.pointerEvents = 'none';
      document.body.appendChild(selectionBox);

      // Attach scroll listener to handle scrolling during drag
      if (scrollContainerElement) {
        scrollContainerElement.addEventListener('scroll', onScroll, { passive: true });
      }
      window.addEventListener('scroll', onScroll, { passive: true });
    }

    if (isDraggingSelection.value) {
      updateSelectionBox(e.clientX, e.clientY);
      handleAutoScroll();
    }
  };

  const onPointerUp = (_e: MouseEvent) => {
    isPointerDown = false;
    activeAutoScrollSpeedY = 0;
    cachedCardRects = [];

    if (scrollContainerElement) {
      scrollContainerElement.removeEventListener('scroll', onScroll);
    }
    window.removeEventListener('scroll', onScroll);

    if (autoScrollRafId !== null) {
      cancelAnimationFrame(autoScrollRafId);
      autoScrollRafId = null;
    }

    if (isDraggingSelection.value) {
      wasJustDragging.value = true;
      setTimeout(() => {
        isDraggingSelection.value = false;
        wasJustDragging.value = false;
      }, 200);

      if (selectionBox) {
        selectionBox.remove();
        selectionBox = null;
      }

      if (animationFrameId !== null) {
        cancelAnimationFrame(animationFrameId);
        animationFrameId = null;
      }
    }
  };

  const checkIntersections = (box: { left: number; top: number; right: number; bottom: number }) => {
    if (!containerElement) return;

    const scrollEl = scrollContainerElement || findScrollContainer(containerElement);
    const scrollLeft = scrollEl ? scrollEl.scrollLeft : 0;
    const scrollTop = scrollEl ? scrollEl.scrollTop : 0;

    const boxLeftDoc = box.left + scrollLeft;
    const boxTopDoc = box.top + scrollTop;
    const boxRightDoc = box.right + scrollLeft;
    const boxBottomDoc = box.bottom + scrollTop;

    const newSelection = new Set(dragBaseSelection);

    for (let i = 0; i < cachedCardRects.length; i++) {
      const c = cachedCardRects[i];
      const intersects = !(
        c.right < boxLeftDoc ||
        c.left > boxRightDoc ||
        c.bottom < boxTopDoc ||
        c.top > boxBottomDoc
      );

      if (intersects) {
        if (dragMode === 'toggle') {
          if (dragBaseSelection.has(c.id)) newSelection.delete(c.id);
          else newSelection.add(c.id);
        } else {
          newSelection.add(c.id);
        }
      }
    }

    const currentSelection = getSelectionSet();

    for (const id of currentSelection) {
      if (!newSelection.has(id)) {
        currentSelection.delete(id);
      }
    }

    for (const id of newSelection) {
      if (!currentSelection.has(id)) {
        currentSelection.add(id);
      }
    }
  };

  function initSelection(containerSelector: string) {
    containerElement = document.querySelector(containerSelector) as HTMLElement;
    if (!containerElement) return;

    containerElement.addEventListener('mousedown', onPointerDown);
    document.addEventListener('mousemove', onPointerMove);
    document.addEventListener('mouseup', onPointerUp);
  }

  function destroySelection() {
    if (containerElement) {
      containerElement.removeEventListener('mousedown', onPointerDown);
    }
    if (scrollContainerElement) {
      scrollContainerElement.removeEventListener('scroll', onScroll);
    }
    window.removeEventListener('scroll', onScroll);
    document.removeEventListener('mousemove', onPointerMove);
    document.removeEventListener('mouseup', onPointerUp);

    if (autoScrollRafId !== null) {
      cancelAnimationFrame(autoScrollRafId);
      autoScrollRafId = null;
    }

    if (selectionBox) {
      selectionBox.remove();
      selectionBox = null;
    }
  }

  return { initSelection, destroySelection, isDraggingSelection, wasJustDragging };
}
