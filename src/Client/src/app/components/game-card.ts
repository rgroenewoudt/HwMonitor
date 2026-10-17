import { ChangeDetectionStrategy, Component, input, linkedSignal } from '@angular/core';
import { GameInfo } from '../sensors/models';

/** Card for the game in front: its icon, name, graphics API, FPS, 1% low FPS and frame time. Only shown while there is an FPS value. */
@Component({
  selector: 'app-game-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'card' },
  template: `
    <header class="card-head">
      <span class="kind">Game</span>
      <div class="title">
        @if (iconUrl(); as url) {
          <img class="icon" [src]="url" alt="" (error)="iconUrl.set(null)" data-testid="game-icon" />
        } @else {
          <svg class="icon fallback" viewBox="0 0 24 24" aria-hidden="true">
            <path d="M7 8h10a5 5 0 0 1 4.9 6l-.6 3a2.5 2.5 0 0 1-4.3 1.2L15 16H9l-2 2.2A2.5 2.5 0 0 1 2.7 17l-.6-3A5 5 0 0 1 7 8Z" />
            <path d="M7 11v3M5.5 12.5h3M15.5 12h.01M17.5 13.5h.01" />
          </svg>
        }
        <h2 class="name" [title]="game()?.name ?? ''" data-testid="game-name">{{ game()?.name ?? 'Unknown app' }}</h2>
        @if (game()?.api; as api) {
          <span class="api" data-testid="game-api">{{ api }}</span>
        }
      </div>
      <ng-content />
    </header>

    <dl class="stats">
      <div>
        <dt>FPS</dt>
        <dd data-testid="fps">{{ fps().toFixed(0) }}</dd>
      </div>
      @if (fpsLow(); as low) {
        <div>
          <dt>1% low</dt>
          <dd data-testid="fps-low">{{ low.toFixed(0) }}</dd>
        </div>
      }
      <div>
        <dt>Frame time</dt>
        <dd data-testid="frame-time">{{ (1000 / fps()).toFixed(1) }}<small>ms</small></dd>
      </div>
    </dl>
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: 10px; }
    .title { display: flex; align-items: center; gap: 8px; min-width: 0; }
    .stats { display: grid; grid-template-columns: repeat(auto-fit, minmax(80px, 1fr)); gap: 12px; margin: 0; }
    .stats > div { padding: 10px 12px; border-radius: 14px; background: var(--surface-2); min-width: 0; }
    dt { font-size: 0.75rem; color: var(--muted); text-transform: uppercase; letter-spacing: 0.08em; }
    dd { margin: 2px 0 0; font-size: 1.35rem; font-weight: 650; font-variant-numeric: tabular-nums; }
    .icon { width: 22px; height: 22px; flex: none; object-fit: contain; }
    .api {
      flex: none;
      padding: 2px 8px;
      border-radius: 999px;
      background: var(--surface-2);
      color: var(--muted);
      font-size: 0.75rem;
      font-weight: 600;
    }
    .fallback {
      fill: none;
      stroke: var(--accent);
      stroke-width: 1.6;
      stroke-linecap: round;
      stroke-linejoin: round;
    }
    small { font-size: 0.8rem; color: var(--muted); margin-left: 2px; font-weight: 500; }
    @media (max-width: 600px), (max-height: 500px) {
      .icon { width: 18px; height: 18px; }
      .stats { grid-template-columns: repeat(auto-fit, minmax(64px, 1fr)); gap: 6px; }
      .stats > div { padding: 6px 8px; border-radius: 10px; }
      dd { font-size: 1.1rem; }
    }
  `,
})
export class GameCard {
  readonly fps = input.required<number>();
  readonly game = input.required<GameInfo | null>();
  /** Only when frames are counted with ETW. */
  readonly fpsLow = input<number | null>(null);

  /** Set to null when the icon fails to load (e.g. the server restarted and lost it); then the fallback is shown. */
  /** Each snapshot is a new object, so the id (a string) is the source: an error stays until the game changes. */
  protected readonly iconUrl = linkedSignal({
    source: () => this.game()?.iconId ?? null,
    computation: (id): string | null => (id ? `/api/game-icon/${id}` : null),
  });
}
