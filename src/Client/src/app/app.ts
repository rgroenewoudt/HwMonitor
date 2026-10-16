import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { CardVisibilityService } from './cards/card-visibility.service';
import { DeviceCard } from './components/device-card';
import { FansCard } from './components/fans-card';
import { HideButton } from './components/hide-button';
import { TempsCard } from './components/temps-card';
import { WakeLockButton } from './components/wake-lock-button';
import { SensorService } from './sensors/sensor.service';

const STATUS_TEXT = {
  connecting: 'Connecting…',
  connected: 'Live',
  reconnecting: 'Reconnecting…',
  offline: 'Offline',
} as const;

@Component({
  selector: 'app-root',
  imports: [DatePipe, DecimalPipe, DeviceCard, FansCard, HideButton, TempsCard, WakeLockButton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  private readonly sensors = inject(SensorService);

  protected readonly snapshot = this.sensors.snapshot;
  protected readonly status = this.sensors.status;
  protected readonly statusText = computed(() => STATUS_TEXT[this.status()]);
  protected readonly warning = computed(() => this.snapshot()?.status.message ?? null);

  /** Drives get their own card; motherboard, VRM, chipset and coolers share the other one. */
  protected readonly driveTemps = computed(() => this.snapshot()?.systemTemps.filter((t) => t.group === 'Drives') ?? []);
  protected readonly boardTemps = computed(() => this.snapshot()?.systemTemps.filter((t) => t.group !== 'Drives') ?? []);

  protected readonly visibility = inject(CardVisibilityService);

  /** Hidden cards that exist in the current snapshot, for the "show again" buttons. */
  protected readonly hiddenCards = computed(() => {
    const s = this.snapshot();
    if (!s) return [];
    const cards = [
      ...(s.cpu ? [{ key: 'cpu', label: s.cpu.name }] : []),
      ...s.gpus.map((gpu) => ({ key: `gpu:${gpu.id}`, label: gpu.name })),
      { key: 'board', label: 'Motherboard temperatures' },
      { key: 'drives', label: 'Drive temperatures' },
      { key: 'fans', label: 'Fans' },
    ];
    const hidden = this.visibility.hidden();
    return cards.filter((card) => hidden.has(card.key));
  });
}
