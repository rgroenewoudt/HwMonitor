import { DOCUMENT, Injectable, inject, signal } from '@angular/core';
import { HttpTransportType, HubConnection, HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { ConnectionStatus, SensorSnapshot } from './models';

/** No message for this long means the socket is probably dead. The server sends every second. */
const STALE_AFTER_MS = 5000;

/** Live sensor values from the server via SignalR. */
@Injectable({ providedIn: 'root' })
export class SensorService {
  private readonly document = inject(DOCUMENT);
  private readonly connection: HubConnection;
  private retryTimer?: ReturnType<typeof setTimeout>;
  /** Local time of the last hub message. Not the server timestamp: the phone clock may differ from the PC. */
  private lastMessageAt = 0;

  readonly snapshot = signal<SensorSnapshot | null>(null);
  readonly status = signal<ConnectionStatus>('connecting');

  constructor() {
    this.connection = new HubConnectionBuilder()
      .withUrl('/hub/sensors', { transport: HttpTransportType.WebSockets, skipNegotiation: true })
      // Never give up: the PC may reboot, or the phone may be away from Wi-Fi for a while.
      .withAutomaticReconnect({ nextRetryDelayInMilliseconds: (ctx) => [0, 1000, 2000][ctx.previousRetryCount] ?? 5000 })
      .configureLogging(LogLevel.Warning)
      .build();

    this.connection.on('snapshot', (snapshot: SensorSnapshot) => {
      this.lastMessageAt = Date.now();
      this.snapshot.set(snapshot);
    });
    this.connection.onreconnecting(() => this.status.set('reconnecting'));
    this.connection.onreconnected(() => this.status.set('connected'));
    this.connection.onclose(() => {
      this.status.set('offline');
      this.scheduleStart(3000);
    });

    // A phone that wakes up from sleep often has a dead socket that still looks connected; connect again at once.
    this.document.addEventListener('visibilitychange', () => {
      if (this.document.visibilityState === 'visible') void this.reconnectIfStale();
    });

    this.loadFirstSnapshot();
    void this.start();
  }

  private async loadFirstSnapshot(): Promise<void> {
    try {
      const response = await fetch('/api/snapshot');
      if (response.ok && !this.snapshot()) {
        this.snapshot.set(await response.json());
      }
    } catch {
      // The hub will deliver data when the server is back.
    }
  }

  private async reconnectIfStale(): Promise<void> {
    const state = this.connection.state;
    const stale = state === HubConnectionState.Connected && Date.now() - this.lastMessageAt > STALE_AFTER_MS;
    if (state === HubConnectionState.Reconnecting || stale) {
      this.status.set('reconnecting');
      await this.connection.stop();
      // stop() calls onclose, which sets 'offline' and waits 3s; start now instead.
      this.status.set('reconnecting');
    } else if (state !== HubConnectionState.Disconnected) {
      return;
    }
    this.scheduleStart(0);
  }

  private async start(): Promise<void> {
    if (this.connection.state !== HubConnectionState.Disconnected) return;
    try {
      await this.connection.start();
      this.status.set('connected');
    } catch {
      this.status.set('offline');
      this.scheduleStart(3000);
    }
  }

  private scheduleStart(delayMs: number): void {
    clearTimeout(this.retryTimer);
    this.retryTimer = setTimeout(() => void this.start(), delayMs);
  }
}
