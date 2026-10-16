import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { DatePipe } from '@angular/common';
import { DeviceCard } from './components/device-card';
import { FansCard } from './components/fans-card';
import { SystemTemps } from './components/system-temps';
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
  imports: [DatePipe, DeviceCard, FansCard, SystemTemps, WakeLockButton],
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
}
