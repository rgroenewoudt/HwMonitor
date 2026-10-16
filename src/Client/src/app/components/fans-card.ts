import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { FanReading } from '../sensors/models';

/** Card with one tile per fan. The fan icon spins faster when the RPM is higher. */
@Component({
  selector: 'app-fans-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'card' },
  template: `
    <header class="card-head">
      <span class="kind">Cooling</span>
      <h2 class="name">Fans</h2>
    </header>

    <ul>
      @for (fan of fans(); track fan.id) {
        <li data-testid="fan-tile">
          <svg class="icon" [class.stopped]="fan.rpm <= 0" [style.animation-duration.s]="spinSeconds(fan.rpm)"
               viewBox="0 0 24 24" aria-hidden="true">
            <path d="M12 12c0-3 .5-7 3-8.5 2-1.2 4 .3 3.6 2.4C18 9 15 11 12 12Z" />
            <path d="M12 12c3 0 7 .5 8.5 3 1.2 2-.3 4-2.4 3.6C15 18 13 15 12 12Z" />
            <path d="M12 12c0 3-.5 7-3 8.5-2 1.2-4-.3-3.6-2.4C6 15 9 13 12 12Z" />
            <path d="M12 12c-3 0-7-.5-8.5-3-1.2-2 .3-4 2.4-3.6C9 6 11 9 12 12Z" />
            <circle cx="12" cy="12" r="1.8" />
          </svg>
          <div class="text">
            <span class="label" [title]="fan.name">{{ fan.name }}</span>
            <span class="source">{{ fan.source }}</span>
          </div>
          <span class="value" data-testid="fan-rpm">{{ fan.rpm.toFixed(0) }}<small>RPM</small></span>
        </li>
      } @empty {
        <li class="empty">No fans found. Some boards do not report fans, or admin rights are missing.</li>
      }
    </ul>
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: 14px; }
    ul { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 8px; }
    li {
      display: grid;
      grid-template-columns: auto 1fr auto;
      align-items: center;
      gap: 12px;
      min-height: 44px;
      padding: 10px 12px;
      border-radius: 14px;
      background: var(--surface-2);
    }
    .icon {
      width: 30px;
      height: 30px;
      fill: var(--accent);
      filter: drop-shadow(0 0 6px color-mix(in srgb, var(--accent) 50%, transparent));
      animation: spin 1s linear infinite;
    }
    .icon.stopped { animation-play-state: paused; fill: var(--muted); filter: none; }
    @keyframes spin { to { transform: rotate(360deg); } }
    @media (prefers-reduced-motion: reduce) { .icon { animation: none; } }
    .text { display: flex; flex-direction: column; min-width: 0; }
    .label { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .source { font-size: 0.75rem; color: var(--muted); }
    .value { font-size: 1.15rem; font-weight: 650; font-variant-numeric: tabular-nums; }
    small { font-size: 0.7rem; color: var(--muted); margin-left: 4px; font-weight: 500; }
    .empty { display: block; color: var(--muted); }
  `,
})
export class FansCard {
  readonly fans = input.required<FanReading[]>();

  /** Not the real speed (that would be a blur); just faster for more RPM. Rounded so it does not jump every update. */
  protected spinSeconds(rpm: number): number {
    if (rpm <= 0) return 0;
    return Math.round(Math.min(4, Math.max(0.35, 600 / rpm)) * 10) / 10;
  }
}
