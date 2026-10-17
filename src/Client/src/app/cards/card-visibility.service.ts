import { Injectable, signal } from '@angular/core';

const STORAGE_KEY = 'hwmonitor.hiddenCards';

/**
 * Which cards the user has hidden. Saved in localStorage, so each browser (phone, desktop) has its own choice.
 * Keys: `cpu`, `gpu:<id>`, `game`, `board`, `drives`, `fans`.
 */
@Injectable({ providedIn: 'root' })
export class CardVisibilityService {
  readonly hidden = signal<ReadonlySet<string>>(load());

  isHidden(key: string): boolean {
    return this.hidden().has(key);
  }

  hide(key: string): void {
    this.update((keys) => keys.add(key));
  }

  show(key: string): void {
    this.update((keys) => keys.delete(key));
  }

  private update(change: (keys: Set<string>) => void): void {
    const keys = new Set(this.hidden());
    change(keys);
    this.hidden.set(keys);
    try {
      localStorage.setItem(STORAGE_KEY, JSON.stringify([...keys]));
    } catch {
      // Storage blocked (private mode): the choice is just not saved.
    }
  }
}

function load(): Set<string> {
  try {
    const value: unknown = JSON.parse(localStorage.getItem(STORAGE_KEY) ?? '[]');
    return new Set(Array.isArray(value) ? value.filter((k): k is string => typeof k === 'string') : []);
  } catch {
    return new Set();
  }
}
