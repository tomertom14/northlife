import { Component, computed, input } from '@angular/core';
import { OwnerEventStatus } from '../owner/owner-events-api';

export const STATUS_LABELS: Record<OwnerEventStatus, string> = {
  Published: 'פורסם',
  Pending: 'ממתין לאישור',
  Rejected: 'נדחה',
};

/** Status icon in the reserved status colour; the label always travels with it. */
@Component({
  selector: 'app-status-icon',
  template: `
    <svg viewBox="0 0 16 16" aria-hidden="true" focusable="false">
      @switch (status()) {
        @case ('Published') {
          <circle cx="8" cy="8" r="7" fill="currentColor" />
          <path d="M4.8 8.3l2.1 2.1 4.3-4.6" fill="none" stroke="#fff" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round" />
        }
        @case ('Pending') {
          <circle cx="8" cy="8" r="7" fill="currentColor" />
          <path d="M8 4.2V8l2.6 1.6" fill="none" stroke="#1d2320" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" />
        }
        @default {
          <circle cx="8" cy="8" r="7" fill="currentColor" />
          <path d="M5.5 5.5l5 5M10.5 5.5l-5 5" fill="none" stroke="#fff" stroke-width="1.7" stroke-linecap="round" />
        }
      }
    </svg>
  `,
  host: { '[class]': 'hostClass()' },
  styles: [`
    :host { display: inline-flex; }
    svg { height: 1rem; width: 1rem; }
    :host(.published) { color: var(--status-good); }
    :host(.pending) { color: var(--status-warning); }
    :host(.rejected) { color: var(--status-critical); }
  `],
})
export class StatusIcon {
  readonly status = input.required<OwnerEventStatus>();
  readonly hostClass = computed(() => this.status().toLowerCase());
}

@Component({
  selector: 'app-status-badge',
  imports: [StatusIcon],
  template: `<span class="status"><app-status-icon [status]="status()" />{{ labels[status()] }}</span>`,
})
export class StatusBadge {
  readonly status = input.required<OwnerEventStatus>();
  readonly labels = STATUS_LABELS;
}
