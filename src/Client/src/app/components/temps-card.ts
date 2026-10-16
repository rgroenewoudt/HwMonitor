import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { TempReading } from '../sensors/models';
import { BOARD_LIMITS, DRIVE_LIMITS, tempLevel } from '../sensors/temperature';

interface Group {
  name: string;
  items: (TempReading & { level: string; percent: number })[];
}

/** Card with temperatures that are not the CPU or GPU, e.g. the motherboard (VRM, chipset, coolers) or the drives. */
@Component({
  selector: 'app-temps-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'card' },
  template: `
    <header class="card-head">
      <span class="kind">{{ kind() }}</span>
      <h2 class="name">Temperatures</h2>
      <ng-content />
    </header>

    @for (group of groups(); track group.name) {
      <section>
        @if (groups().length > 1) {
          <h3>{{ group.name }}</h3>
        }
        <ul>
          @for (item of group.items; track item.id) {
            <li [attr.data-level]="item.level" data-testid="system-temp">
              <span class="label" [title]="item.name">{{ item.name }}</span>
              <span class="value">{{ item.tempC.toFixed(0) }}<small>°C</small></span>
              <span class="bar"><span [style.width.%]="item.percent"></span></span>
            </li>
          }
        </ul>
      </section>
    } @empty {
      <p class="empty">{{ emptyText() }}</p>
    }
  `,
  styles: `
    :host { display: flex; flex-direction: column; gap: 14px; }
    section { display: flex; flex-direction: column; gap: 6px; }
    h3 {
      margin: 0;
      font-size: 0.75rem;
      font-weight: 600;
      color: var(--muted);
      text-transform: uppercase;
      letter-spacing: 0.08em;
    }
    ul { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 6px; }
    li {
      display: grid;
      grid-template-columns: 1fr auto;
      align-items: center;
      gap: 6px 12px;
      padding: 10px 12px;
      border-radius: 14px;
      background: var(--surface-2);
      color: var(--level-color);
    }
    .label { color: var(--text); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; min-width: 0; }
    .value { color: var(--text); font-size: 1.15rem; font-weight: 650; font-variant-numeric: tabular-nums; }
    small { font-size: 0.75rem; color: var(--muted); margin-left: 2px; font-weight: 500; }
    .bar { grid-column: 1 / -1; height: 4px; border-radius: 2px; background: var(--track); overflow: hidden; }
    .bar span {
      display: block;
      height: 100%;
      border-radius: inherit;
      background: currentColor;
      box-shadow: 0 0 8px currentColor;
      transition: width 0.9s cubic-bezier(0.22, 1, 0.36, 1);
    }
    .empty { margin: 0; color: var(--muted); }
  `,
})
export class TempsCard {
  readonly kind = input.required<string>();
  readonly temps = input.required<TempReading[]>();
  readonly emptyText = input.required<string>();

  protected readonly groups = computed<Group[]>(() => {
    const groups = new Map<string, Group>();
    for (const temp of this.temps()) {
      const limits = temp.group === 'Drives' ? DRIVE_LIMITS : BOARD_LIMITS;
      const group = groups.get(temp.group) ?? { name: temp.group, items: [] };
      group.items.push({ ...temp, level: tempLevel(temp.tempC, limits), percent: Math.min(100, temp.tempC) });
      groups.set(temp.group, group);
    }
    return [...groups.values()];
  });
}
