import { ChangeDetectionStrategy, Component, computed, signal } from '@angular/core';

/** Same shape as HwMonitor.Firewall.FirewallStatus on the server. */
export interface FirewallStatus {
  blockedPorts: number[];
  profiles: string[];
  canOpen: boolean;
}

/**
 * Warns when Windows Firewall blocks the LAN port, with a button to add an allow rule.
 * Only a browser on the PC itself gets a status: when the port is blocked, other devices cannot load this page.
 */
@Component({
  selector: 'app-firewall-warning',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (status(); as s) {
      @if (s.blockedPorts.length) {
        <div class="warning" role="alert" data-testid="firewall-warning">
          <p>
            Phones and other PCs cannot open this page: Windows Firewall blocks TCP {{ portText() }}
            on the {{ s.profiles.join(' and ') }} network.
            @if (!s.canOpen) {
              Run HwMonitor as administrator to open the port here.
            }
          </p>
          @if (s.canOpen) {
            <button type="button" (click)="open()" [disabled]="busy()">
              {{ busy() ? 'Opening…' : 'Open ' + portText() }}
            </button>
          }
          @if (error(); as error) {
            <p class="error">{{ error }}</p>
          }
        </div>
      } @else if (opened()) {
        <p class="done" role="status" data-testid="firewall-opened">
          Firewall port is open. Other devices on your network can now open this page.
        </p>
      }
    }
  `,
  styles: `
    :host { display: contents; }
    .warning, .done {
      margin: 0;
      padding: 12px 16px;
      border-radius: 14px;
      font-size: 0.9rem;
    }
    .warning {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: 8px 16px;
      border: 1px solid color-mix(in srgb, var(--warm) 45%, transparent);
      background: color-mix(in srgb, var(--warm) 12%, transparent);
    }
    .done {
      border: 1px solid color-mix(in srgb, var(--cool) 45%, transparent);
      background: color-mix(in srgb, var(--cool) 12%, transparent);
    }
    p { margin: 0; }
    .error { flex-basis: 100%; color: var(--hot); }
    button {
      min-height: 44px;
      padding: 0 16px;
      border-radius: 999px;
      border: 1px solid var(--accent);
      background: var(--surface);
      color: var(--text);
      font: inherit;
      font-weight: 600;
      cursor: pointer;
      &:hover:not(:disabled), &:focus-visible { background: var(--surface-2); }
      &:disabled { opacity: 0.6; cursor: progress; }
    }
  `,
})
export class FirewallWarning {
  protected readonly status = signal<FirewallStatus | null>(null);
  protected readonly busy = signal(false);
  protected readonly opened = signal(false);
  protected readonly error = signal<string | null>(null);

  protected readonly portText = computed(() => {
    const ports = this.status()?.blockedPorts ?? [];
    return `${ports.length > 1 ? 'ports' : 'port'} ${ports.join(', ')}`;
  });

  constructor() {
    void this.load();
  }

  private async load(): Promise<void> {
    try {
      const response = await fetch('/api/firewall');
      if (response.ok) this.status.set(await response.json());
    } catch {
      // No warning when the server cannot be reached; the status badge shows that.
    }
  }

  protected async open(): Promise<void> {
    this.busy.set(true);
    this.error.set(null);
    try {
      const response = await fetch('/api/firewall/open', { method: 'POST' });
      const body = await response.json().catch(() => null);
      if (!response.ok) {
        this.error.set(body?.detail ?? `Could not change the firewall (error ${response.status}).`);
        return;
      }
      const status = body as FirewallStatus;
      this.status.set(status);
      if (status.blockedPorts.length) {
        this.error.set('The port is still blocked. Another firewall rule may block it; check Windows Firewall settings.');
      } else {
        this.opened.set(true);
      }
    } catch {
      this.error.set('Could not reach HwMonitor. Try again.');
    } finally {
      this.busy.set(false);
    }
  }
}
