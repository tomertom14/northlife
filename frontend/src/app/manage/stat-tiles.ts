import { Component, input } from '@angular/core';
import { OwnerEventStatus } from '../owner/owner-events-api';
import { StatusIcon } from './status-badge';

export interface StatTile {
  label: string;
  value: number;
  status: OwnerEventStatus;
}

const numberFormat = new Intl.NumberFormat('he-IL');

/** A KPI row: sentence-case label with its status icon, and the value in the body sans. */
@Component({
  selector: 'app-stat-tiles',
  imports: [StatusIcon],
  template: `
    <dl class="tiles" [attr.aria-label]="label()">
      @for (tile of tiles(); track tile.label) {
        <div class="tile">
          <dt><app-status-icon [status]="tile.status" />{{ tile.label }}</dt>
          <dd>{{ format(tile.value) }}</dd>
        </div>
      }
    </dl>
  `,
  styles: [`
    .tiles { display: grid; gap: .75rem; grid-template-columns: repeat(auto-fit, minmax(10.5rem, 1fr)); margin: 0; }
    .tile { background: var(--surface); border: 1px solid var(--line); border-radius: var(--radius-md); padding: 1rem 1.1rem; }
    dt { align-items: center; color: var(--muted); display: flex; font-size: .875rem; font-weight: 500; gap: .45rem; }
    dd { font-size: 2.5rem; font-weight: 600; line-height: 1.1; margin: .35rem 0 0; }
    @media (max-width: 40rem) {
      .tiles { gap: .5rem; grid-template-columns: repeat(3, minmax(0, 1fr)); }
      .tile { padding: .75rem; }
      dt { align-items: flex-start; font-size: .8125rem; }
      dd { font-size: 1.875rem; }
    }
  `],
})
export class StatTiles {
  readonly tiles = input.required<StatTile[]>();
  readonly label = input('');
  format(value: number): string {
    return numberFormat.format(value);
  }
}
