import { ChangeDetectionStrategy, Component, inject, input } from '@angular/core';
import { CardVisibilityService } from '../cards/card-visibility.service';

/** Small button in the corner of a card that hides the card. */
@Component({
  selector: 'app-hide-button',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button type="button" (click)="visibility.hide(key())" [attr.aria-label]="'Hide ' + label()"
            [title]="'Hide ' + label()" data-testid="hide-card">
      <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M6 6l12 12M18 6L6 18" /></svg>
    </button>
  `,
  styles: `
    :host { position: absolute; top: 4px; right: 4px; }
    button {
      display: grid;
      place-items: center;
      width: 44px;
      height: 44px;
      padding: 0;
      border: none;
      border-radius: 50%;
      background: transparent;
      color: var(--muted);
      cursor: pointer;
      transition: color 0.2s, background 0.2s;
    }
    button:hover, button:focus-visible { color: var(--text); background: var(--surface-2); }
    svg { width: 16px; height: 16px; fill: none; stroke: currentColor; stroke-width: 2; stroke-linecap: round; }
  `,
})
export class HideButton {
  protected readonly visibility = inject(CardVisibilityService);

  readonly key = input.required<string>();
  readonly label = input.required<string>();
}
