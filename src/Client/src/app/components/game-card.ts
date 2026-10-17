import { ChangeDetectionStrategy, Component, input, linkedSignal } from '@angular/core';
import { GameInfo } from '../sensors/models';

/** Card for the game in front: its icon, name and FPS. Only shown while there is an FPS value. */
@Component({
  selector: 'app-game-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'card' },
  template: `
    <header class="card-head">
      <span class="kind">Game</span>
      <h2 class="name" [title]="game()?.name ?? ''" data-testid="game-name">{{ game()?.name ?? 'Unknown app' }}</h2>
      <ng-content />
    </header>

    <div class="body">
      @if (iconUrl(); as url) {
        <img class="icon" [src]="url" alt="" (error)="iconUrl.set(null)" data-testid="game-icon" />
      } @else {
        <svg class="icon fallback" viewBox="0 0 24 24" aria-hidden="true">
          <path d="M7 8h10a5 5 0 0 1 4.9 6l-.6 3a2.5 2.5 0 0 1-4.3 1.2L15 16H9l-2 2.2A2.5 2.5 0 0 1 2.7 17l-.6-3A5 5 0 0 1 7 8Z" />
          <path d="M7 11v3M5.5 12.5h3M15.5 12h.01M17.5 13.5h.01" />
        </svg>
      }
      <span class="value" data-testid="fps">{{ fps().toFixed(0) }}<small>FPS</small></span>
    </div>
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: 10px; }
    .body {
      display: flex;
      align-items: center;
      gap: 12px;
      padding: 10px 12px;
      border-radius: 14px;
      background: var(--surface-2);
    }
    .icon { width: 36px; height: 36px; flex: none; object-fit: contain; }
    .fallback {
      fill: none;
      stroke: var(--accent);
      stroke-width: 1.6;
      stroke-linecap: round;
      stroke-linejoin: round;
    }
    .value { font-size: 1.35rem; font-weight: 650; font-variant-numeric: tabular-nums; }
    small { font-size: 0.8rem; color: var(--muted); margin-left: 4px; font-weight: 500; }
    @media (max-width: 600px), (max-height: 500px) {
      .icon { width: 30px; height: 30px; }
      .value { font-size: 1.1rem; }
    }
  `,
})
export class GameCard {
  readonly fps = input.required<number>();
  readonly game = input.required<GameInfo | null>();

  /** Set to null when the icon fails to load (e.g. the server restarted and lost it); then the fallback is shown. */
  /** Each snapshot is a new object, so the id (a string) is the source: an error stays until the game changes. */
  protected readonly iconUrl = linkedSignal({
    source: () => this.game()?.iconId ?? null,
    computation: (id): string | null => (id ? `/api/game-icon/${id}` : null),
  });
}
