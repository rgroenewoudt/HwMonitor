import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { DeviceReading } from '../sensors/models';
import { tempLevel } from '../sensors/temperature';
import { Gauge } from './gauge';

/** Card for a CPU or GPU: big temperature gauge plus load, power and hot spot. */
@Component({
  selector: 'app-device-card',
  imports: [Gauge],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'card', '[attr.data-level]': 'level()' },
  template: `
    <header class="card-head">
      <span class="kind">{{ kind() }}</span>
      <h2 class="name" [title]="device().name">{{ device().name }}</h2>
    </header>

    <app-gauge [value]="device().tempC" [label]="kind() + ' temperature'" />

    <dl class="stats">
      <div>
        <dt>Load</dt>
        <dd>{{ format(device().loadPercent, 0) }}<small>%</small></dd>
        <div class="bar"><span [style.width.%]="device().loadPercent ?? 0"></span></div>
      </div>
      <div>
        <dt>Power</dt>
        <dd>{{ format(device().powerW, 0) }}<small>W</small></dd>
      </div>
      @if (device().hotSpotC != null) {
        <div>
          <dt>Hot spot</dt>
          <dd>{{ format(device().hotSpotC, 0) }}<small>°C</small></dd>
        </div>
      }
    </dl>
  `,
  styles: `
    :host { display: flex; flex-direction: column; align-items: center; gap: 12px; }
    app-gauge { --gauge-size: clamp(150px, 46vw, 188px); margin-block: 4px; }
    .stats {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(80px, 1fr));
      gap: 12px;
      width: 100%;
      margin: 0;
    }
    .stats > div {
      padding: 10px 12px;
      border-radius: 14px;
      background: var(--surface-2);
      min-width: 0;
    }
    dt { font-size: 0.75rem; color: var(--muted); text-transform: uppercase; letter-spacing: 0.08em; }
    dd { margin: 2px 0 0; font-size: 1.35rem; font-weight: 650; font-variant-numeric: tabular-nums; }
    small { font-size: 0.8rem; color: var(--muted); margin-left: 2px; font-weight: 500; }
    .bar { height: 4px; margin-top: 8px; border-radius: 2px; background: var(--track); overflow: hidden; }
    .bar span {
      display: block;
      height: 100%;
      border-radius: inherit;
      background: var(--accent);
      transition: width 0.9s cubic-bezier(0.22, 1, 0.36, 1);
    }
  `,
})
export class DeviceCard {
  readonly device = input.required<DeviceReading>();
  readonly kind = input.required<'CPU' | 'GPU'>();

  protected readonly level = computed(() => tempLevel(this.device().tempC));

  protected format(value: number | null, digits: number): string {
    return value == null ? '–' : value.toFixed(digits);
  }
}
