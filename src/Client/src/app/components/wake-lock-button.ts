import { ChangeDetectionStrategy, Component, DOCUMENT, inject, signal } from '@angular/core';

/**
 * Keeps the phone screen on, for a phone used as a small monitor.
 * The Wake Lock API needs a secure context (https or localhost), so on plain http over the LAN the button is hidden.
 */
@Component({
  selector: 'app-wake-lock-button',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (supported) {
      <button type="button" [attr.aria-pressed]="active()" (click)="toggle()"
              [title]="active() ? 'Screen stays on' : 'Keep screen on'">
        <svg viewBox="0 0 24 24" aria-hidden="true">
          @if (active()) {
            <path d="M12 3v2M12 19v2M4.2 4.2l1.4 1.4M18.4 18.4l1.4 1.4M3 12h2M19 12h2M4.2 19.8l1.4-1.4M18.4 5.6l1.4-1.4" />
            <circle cx="12" cy="12" r="4" />
          } @else {
            <path d="M20 14.5A8 8 0 0 1 9.5 4 8 8 0 1 0 20 14.5Z" />
          }
        </svg>
        <span class="sr-only">Keep screen on</span>
      </button>
    }
  `,
  styles: `
    button {
      display: grid;
      place-items: center;
      width: 44px;
      height: 44px;
      border-radius: 50%;
      border: 1px solid var(--border);
      background: var(--surface);
      color: var(--muted);
      cursor: pointer;
      transition: color 0.2s, background 0.2s;
    }
    button[aria-pressed='true'] { color: var(--accent); background: color-mix(in srgb, var(--accent) 15%, transparent); }
    svg { width: 20px; height: 20px; fill: none; stroke: currentColor; stroke-width: 2; stroke-linecap: round; }
  `,
})
export class WakeLockButton {
  private readonly document = inject(DOCUMENT);
  private sentinel: WakeLockSentinel | null = null;

  protected readonly supported = 'wakeLock' in navigator && window.isSecureContext;
  protected readonly active = signal(false);

  constructor() {
    // The browser drops the lock when the tab is hidden; take it again when we come back.
    this.document.addEventListener('visibilitychange', () => {
      if (this.active() && this.document.visibilityState === 'visible') void this.request();
    });
  }

  protected async toggle(): Promise<void> {
    if (this.active()) {
      this.active.set(false);
      const sentinel = this.sentinel;
      this.sentinel = null;
      await sentinel?.release();
    } else {
      this.active.set(true);
      await this.request();
    }
  }

  private async request(): Promise<void> {
    if (this.sentinel && !this.sentinel.released) return;
    try {
      const sentinel = await navigator.wakeLock.request('screen');
      // While we waited, the user may have turned it off, or another request may have finished first.
      if (!this.active() || (this.sentinel && !this.sentinel.released)) {
        await sentinel.release();
        return;
      }
      this.sentinel = sentinel;
    } catch {
      this.active.set(false);
    }
  }
}
