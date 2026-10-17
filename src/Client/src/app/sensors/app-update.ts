import { DOCUMENT, Injectable, inject } from '@angular/core';
import { SwUpdate } from '@angular/service-worker';

/** Angular output hashing in index.html: main-XIAXEOH3.js, styles-FXYNHMNM.css. */
const HASHED_FILE = /(?:src|href)="([\w-]+-[A-Z0-9]{8}\.(?:js|css))"/g;
/** The build we last reloaded for, so a cache that still serves old files cannot cause a reload loop. */
const RELOADED_FOR_KEY = 'hwmonitor.reloadedFor';
const SW_UPDATE_TIMEOUT_MS = 5000;

/** Reloads the page when the server has a newer build than the one running here (HwMonitor was updated). */
@Injectable({ providedIn: 'root' })
export class AppUpdateService {
  private readonly document = inject(DOCUMENT);
  private readonly swUpdate = inject(SwUpdate);
  private checking = false;

  async reloadIfOutdated(): Promise<void> {
    if (this.checking) return;
    this.checking = true;
    try {
      // ngsw-bypass: the service worker would answer with its cached (old) index.html.
      const response = await fetch('/index.html?ngsw-bypass=true', { cache: 'no-store' });
      // Not ok: a Debug build without UI, or the server is still starting.
      if (!response.ok) return;
      const serverFiles = hashedFiles(await response.text());
      const ownFiles = new Set(hashedFiles(this.document.documentElement.outerHTML));
      // No hashes under `ng serve`, so this never reloads there.
      if (serverFiles.length === 0 || serverFiles.every((file) => ownFiles.has(file))) return;

      const build = serverFiles.sort().join(' ');
      if (readReloadedFor() === build) return;
      writeReloadedFor(build);

      await this.activateServiceWorkerUpdate();
      this.document.location.reload();
    } catch {
      // Try again on the next reconnect.
    } finally {
      this.checking = false;
    }
  }

  /** Else the reload gets the old files from the service worker cache again. */
  private async activateServiceWorkerUpdate(): Promise<void> {
    // No controller: this page did not come from the service worker cache (plain http, first visit).
    if (!this.swUpdate.isEnabled || !navigator.serviceWorker?.controller) return;
    try {
      const update = async () => {
        // checkForUpdate() is false when the new version was already downloaded, so always activate.
        await this.swUpdate.checkForUpdate();
        await this.swUpdate.activateUpdate();
      };
      // A service worker that does not answer must not stop the reload.
      await Promise.race([update(), new Promise((resolve) => setTimeout(resolve, SW_UPDATE_TIMEOUT_MS))]);
    } catch {
      // Reload anyway; the loop guard stops a second reload for the same build.
    }
  }
}

function hashedFiles(html: string): string[] {
  return [...new Set([...html.matchAll(HASHED_FILE)].map((match) => match[1]))];
}

function readReloadedFor(): string | null {
  try {
    return sessionStorage.getItem(RELOADED_FOR_KEY);
  } catch {
    return null;
  }
}

function writeReloadedFor(build: string): void {
  try {
    sessionStorage.setItem(RELOADED_FOR_KEY, build);
  } catch {
    // Without storage there is no loop guard, but the reload still works.
  }
}
