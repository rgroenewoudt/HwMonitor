import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { CHIP_LIMITS, TempLimits, tempLevel } from '../sensors/temperature';

const RADIUS = 52;
const CIRCUMFERENCE = 2 * Math.PI * RADIUS;
/** The arc is 270°, open at the bottom. */
const ARC = CIRCUMFERENCE * 0.75;

/** Round SVG gauge for a temperature. */
@Component({
  selector: 'app-gauge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[attr.data-level]': 'level()' },
  template: `
    <svg viewBox="0 0 120 120" role="img" [attr.aria-label]="ariaLabel()">
      <circle class="track" cx="60" cy="60" [attr.r]="radius"
              [attr.stroke-dasharray]="arc + ' ' + circumference" />
      <circle class="value" cx="60" cy="60" [attr.r]="radius"
              [attr.stroke-dasharray]="arc + ' ' + circumference"
              [style.stroke-dashoffset]="offset()" />
    </svg>
    <div class="center">
      <span class="number" data-testid="gauge-value">{{ value() == null ? '–' : value()!.toFixed(0) }}</span>
      <span class="unit">°C</span>
    </div>
  `,
  styles: `
    :host {
      position: relative;
      display: grid;
      place-items: center;
      width: var(--gauge-size, 168px);
      aspect-ratio: 1;
      color: var(--level-color);
    }
    svg { width: 100%; height: 100%; transform: rotate(135deg); overflow: visible; }
    circle { fill: none; stroke-width: 10; stroke-linecap: round; }
    .track { stroke: var(--track); }
    .value {
      stroke: currentColor;
      filter: drop-shadow(0 0 6px color-mix(in srgb, currentColor 55%, transparent));
      transition: stroke-dashoffset 0.9s cubic-bezier(0.22, 1, 0.36, 1), color 0.6s ease;
    }
    .center {
      position: absolute;
      display: flex;
      align-items: flex-start;
      gap: 2px;
      color: var(--text);
    }
    .number {
      font-size: calc(var(--gauge-size, 168px) * 0.3);
      font-weight: 700;
      line-height: 1;
      letter-spacing: -0.03em;
      font-variant-numeric: tabular-nums;
    }
    .unit { font-size: calc(var(--gauge-size, 168px) * 0.1); color: var(--muted); margin-top: 0.3em; }
  `,
})
export class Gauge {
  readonly value = input<number | null>(null);
  readonly max = input(100);
  readonly label = input('');
  readonly limits = input<TempLimits>(CHIP_LIMITS);

  protected readonly radius = RADIUS;
  protected readonly circumference = CIRCUMFERENCE;
  protected readonly arc = ARC;

  protected readonly level = computed(() => tempLevel(this.value(), this.limits()));
  protected readonly offset = computed(() => {
    const fraction = Math.min(1, Math.max(0, (this.value() ?? 0) / this.max()));
    return ARC * (1 - fraction);
  });
  protected readonly ariaLabel = computed(() =>
    this.value() == null ? `${this.label()}: no data` : `${this.label()}: ${this.value()!.toFixed(0)} °C`,
  );
}
