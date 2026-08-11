import type { SetEntity, ItemEntity, Configuration } from '@/types';

export let API_BASE = 'http://localhost:5184/api';

export async function initializeApi() {
  if (typeof window !== 'undefined' && (window as any).electronAPI && (window as any).electronAPI.getBackendPort) {
    try {
      const port = await (window as any).electronAPI.getBackendPort();
      if (port) {
        API_BASE = `http://localhost:${port}/api`;
        
        // Wait for backend to be ready
        for (let i = 0; i < 20; i++) {
          try {
            await fetch(`${API_BASE}/settings`, { cache: 'no-store' });
            break; // Success
          } catch (e) {
            await new Promise(r => setTimeout(r, 500));
          }
        }
      }
    } catch (e) {
      console.warn('Failed to get backend port from electronAPI', e);
    }
  }
}

export async function fetchSets(): Promise<SetEntity[]> {
  const res = await fetch(`${API_BASE}/sets`, { cache: 'no-store' });
  if (!res.ok) throw new Error('Failed to fetch sets');
  return res.json();
}

export interface ItemFilterQuery {
  setId?: number | null;
  search?: string;
  packageType?: string;
  enabled?: boolean;
  sortBy?: string;
  page?: number;
  pageSize?: number;
}

export async function fetchItems(query?: ItemFilterQuery): Promise<ItemEntity[]> {
  let url = `${API_BASE}/items`;
  if (query) {
    const params = new URLSearchParams();
    if (query.setId !== undefined && query.setId !== null) params.append('setId', query.setId.toString());
    if (query.search) params.append('search', query.search);
    if (query.packageType) params.append('packageType', query.packageType);
    if (query.enabled !== undefined) params.append('enabled', query.enabled.toString());
    if (query.sortBy) params.append('sortBy', query.sortBy);
    if (query.page) params.append('page', query.page.toString());
    if (query.pageSize) params.append('pageSize', query.pageSize.toString());

    const queryString = params.toString();
    if (queryString) {
      url += `?${queryString}`;
    }
  }

  const res = await fetch(url, { cache: 'no-store' });
  if (!res.ok) throw new Error('Failed to fetch items');
  return res.json();
}

export async function fetchConfigurations(): Promise<Configuration[]> {
  const res = await fetch(`${API_BASE}/configurations`, { cache: 'no-store' });
  if (!res.ok) throw new Error('Failed to fetch configurations');
  return res.json();
}

export async function fetchSettings(): Promise<any> {
  const res = await fetch(`${API_BASE}/settings`, { cache: 'no-store' });
  if (!res.ok) throw new Error('Failed to fetch settings');
  return res.json();
}

export async function createSet(name: string, parentSetsEntityId: number | null): Promise<any> {
  const res = await fetch(`${API_BASE}/sets`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ name, parentSetsEntityId })
  });
  if (!res.ok) throw new Error('Failed to create set');
  return res.json();
}

export async function renameSet(id: number, name: string): Promise<any> {
  const res = await fetch(`${API_BASE}/sets/${id}`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ name })
  });
  if (!res.ok) throw new Error('Failed to rename set');
  return res.json();
}

export async function deleteSet(id: number, deleteItems: boolean): Promise<any> {
  const res = await fetch(`${API_BASE}/sets/${id}?deleteItems=${deleteItems}`, {
    method: 'DELETE'
  });
  if (!res.ok) {
    const err = await res.json().catch(() => ({}));
    throw new Error(err.message || 'Failed to delete set');
  }
  return res.json().catch(() => ({}));
}

export async function moveSet(id: number, parentSetsEntityId: number | null): Promise<any> {
  const res = await fetch(`${API_BASE}/sets/${id}/move`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ parentSetsEntityId })
  });
  if (!res.ok) throw new Error('Failed to move set');
  return res.json();
}

export async function moveItems(itemIds: number[], targetSetId: number | null): Promise<any> {
  const res = await fetch(`${API_BASE}/items/move`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ itemIds, targetSetId })
  });
  if (!res.ok) throw new Error('Failed to move items');
  return res.json();
}

export async function setItemEnabled(itemIds: number[], enabled: boolean): Promise<void> {
  const res = await fetch(`${API_BASE}/items/enabled`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ itemIds, enabled })
  });
  if (!res.ok) throw new Error('Failed to toggle item state');
}

export async function renameItemApi(id: number, newName: string): Promise<any> {
  const res = await fetch(`${API_BASE}/items/${id}/rename`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ newName })
  });
  if (!res.ok) {
    const err = await res.json().catch(() => ({}));
    throw new Error(err.message || 'Failed to rename item');
  }
  return res.json();
}

export async function deleteItems(itemIds: number[], permanent: boolean): Promise<any> {
  const res = await fetch(`${API_BASE}/items`, {
    method: 'DELETE',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ itemIds, permanent })
  });
  if (!res.ok) throw new Error('Failed to delete items');
  return res.json();
}

export async function createConfiguration(): Promise<Configuration> {
  const res = await fetch(`${API_BASE}/configurations`, {
    method: 'POST'
  });
  if (!res.ok) throw new Error('Failed to create configuration');
  return res.json();
}

export async function duplicateConfiguration(id: number): Promise<Configuration> {
  const res = await fetch(`${API_BASE}/configurations/${id}/duplicate`, {
    method: 'POST'
  });
  if (!res.ok) throw new Error('Failed to duplicate configuration');
  return res.json();
}

export async function renameConfiguration(id: number, name: string): Promise<any> {
  const res = await fetch(`${API_BASE}/configurations/${id}?name=${encodeURIComponent(name)}`, {
    method: 'PUT'
  });
  if (!res.ok) throw new Error('Failed to rename configuration');
  return res.json();
}

export async function updateConfigurationDescription(id: number, description: string): Promise<any> {
  const res = await fetch(`${API_BASE}/configurations/${id}?description=${encodeURIComponent(description)}`, {
    method: 'PUT'
  });
  if (!res.ok) throw new Error('Failed to update configuration description');
  return res.json();
}

export async function deleteConfiguration(id: number): Promise<any> {
  const res = await fetch(`${API_BASE}/configurations/${id}`, {
    method: 'DELETE'
  });
  if (!res.ok) throw new Error('Failed to delete configuration');
  return res.text().then(t => t ? JSON.parse(t) : {});
}

export async function activateConfiguration(id: number): Promise<any> {
  const res = await fetch(`${API_BASE}/configurations/${id}/active`, {
    method: 'PUT'
  });
  if (!res.ok) throw new Error('Failed to activate configuration');
  return res.text().then(t => t ? JSON.parse(t) : {});
}

export async function updateConfigurationSets(id: number, setIds: number[]): Promise<any> {
  const res = await fetch(`${API_BASE}/configurations/${id}/sets`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ setIds })
  });
  if (!res.ok) throw new Error('Failed to update configuration sets');
  return res.text().then(t => t ? JSON.parse(t) : {});
}

export async function saveSettings(settings: any, moveFolder: boolean = false): Promise<any> {
  const res = await fetch(`${API_BASE}/settings`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ Options: settings, MoveFolder: moveFolder })
  });
  if (!res.ok) throw new Error('Failed to save settings');
  return res.json();
}

export async function autodetectSettings(): Promise<any> {
  const res = await fetch(`${API_BASE}/settings/autodetect`, {
    method: 'POST'
  });
  if (!res.ok) throw new Error('Failed to autodetect settings');
  return res.json();
}

export async function migrate(): Promise<any> {
  const res = await fetch(`${API_BASE}/migrate`, {
    method: 'POST'
  });
  if (!res.ok) {
    const err = await res.json().catch(() => ({}));
    throw new Error(err.message || 'Failed to migrate');
  }
  return res.json();
}

export async function autoFixDatabase(): Promise<Response> {
  const res = await fetch(`${API_BASE}/fix`, {
    method: 'POST'
  });
  if (!res.ok) throw new Error('Failed to start auto-fix');
  return res;
}

export async function startScan(): Promise<Response> {
  const res = await fetch(`${API_BASE}/scan`, {
    method: 'POST'
  });
  return res;
}

export async function startRecheckTypes(skipUserTagged: boolean = true): Promise<Response> {
  const res = await fetch(`${API_BASE}/settings/recheck-types?skipUserTagged=${skipUserTagged}`, {
    method: 'POST'
  });
  if (!res.ok) throw new Error('Failed to start rechecking types');
  return res;
}

export async function retagItems(itemIds: number[], packageType: string, casCategories: string): Promise<void> {
  const res = await fetch(`${API_BASE}/items/retag`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ itemIds, packageType, casCategories })
  });
  if (!res.ok) throw new Error('Failed to retag items');
}

export async function updateUserTags(
  itemIds: number[],
  options: { setTags?: string[]; addTags?: string[]; removeAll?: boolean }
): Promise<void> {
  const res = await fetch(`${API_BASE}/items/user-tags`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      itemIds,
      setTags: options.setTags || null,
      addTags: options.addTags || null,
      removeAll: options.removeAll || false
    })
  });
  if (!res.ok) throw new Error('Failed to update user tags');
}

export async function checkImportDuplicates(filesOrNames: (File | string)[]): Promise<{ hasDuplicates: boolean, duplicates: string[] }> {
  if (filesOrNames.length > 0 && typeof filesOrNames[0] !== 'string') {
    const formData = new FormData();
    (filesOrNames as File[]).forEach(f => formData.append('files', f));
    const res = await fetch(`${API_BASE}/import/check-duplicates`, {
      method: 'POST',
      body: formData
    });
    if (!res.ok) throw new Error('Failed to check for duplicate files');
    return res.json();
  } else {
    const fileNames = filesOrNames as string[];
    const res = await fetch(`${API_BASE}/import/check-duplicates`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ fileNames })
    });
    if (!res.ok) throw new Error('Failed to check for duplicate files');
    return res.json();
  }
}

export async function uploadFiles(files: File[], duplicateAction: string = 'rename', targetSetId?: number): Promise<Response> {
  const formData = new FormData();
  files.forEach(f => formData.append('files', f));
  formData.append('duplicateAction', duplicateAction);
  if (targetSetId) {
    formData.append('targetSetId', targetSetId.toString());
  }
  const res = await fetch(`${API_BASE}/upload-files`, {
    method: 'POST',
    body: formData
  });
  if (!res.ok) throw new Error('Failed to start uploading files');
  return res;
}

export async function importFiles(filePaths: string[], duplicateAction: string = 'rename', targetSetId?: number): Promise<Response> {
  const res = await fetch(`${API_BASE}/import-files`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ filePaths, duplicateAction, targetSetId })
  });
  if (!res.ok) throw new Error('Failed to start importing files');
  return res;
}

export async function checkDownloadsDuplicates(): Promise<{ hasDuplicates: boolean, duplicates: string[] }> {
  const res = await fetch(`${API_BASE}/import-downloads/check-duplicates`);
  if (!res.ok) throw new Error('Failed to check downloads duplicates');
  return res.json();
}

export async function importDownloads(duplicateAction: string = 'rename'): Promise<Response> {
  const res = await fetch(`${API_BASE}/import-downloads?duplicateAction=${encodeURIComponent(duplicateAction)}`, {
    method: 'POST'
  });
  if (!res.ok) throw new Error('Failed to start importing downloads');
  return res;
}

export async function autodetectGameFiles(): Promise<string> {
  const res = await fetch(`${API_BASE}/settings/autodetect-gamefiles`);
  if (!res.ok) throw new Error('Failed to autodetect game files directory');
  const data = await res.json();
  return data.path;
}

export async function validateGameFiles(path: string): Promise<{ valid: boolean; normalizedPath: string }> {
  const res = await fetch(`${API_BASE}/settings/validate-gamefiles`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ path })
  });
  if (!res.ok) throw new Error('Failed to validate game files directory');
  const data = await res.json();
  return { valid: !!data.valid, normalizedPath: data.normalizedPath || path };
}
