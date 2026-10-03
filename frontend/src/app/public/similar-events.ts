import { Component, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TrackImpression } from '../analytics/track-impression';
import { Clock } from '../shared/clock';
import { formatTime, relativeDay } from '../shared/jerusalem-time';
import { EventImage } from './event-image';
import { CATEGORY_COLORS, CATEGORY_LABELS, EventSummary, formatPrice } from './public-event.models';

/** "More like this" under an event: its nearest neighbours by text and by what visitors open together. */
@Component({
  selector: 'app-similar-events',
  imports: [RouterLink, EventImage, TrackImpression],
  template: `
    <section class="similar" aria-labelledby="similar-title">
      <h2 id="similar-title">עוד אירועים כאלה</h2>
      <ul>
        @for (event of events(); track event.id) {
          <li>
            <a
              [routerLink]="['/events', event.id]"
              [state]="{ source: 'Similar', position: $index + 1, context: 'similar' }"
              [appTrackImpression]="event.id"
              trackSource="Similar"
              [trackPosition]="$index + 1"
              trackContext="similar">
              <img [appEventImage]="event.imageUrl" [category]="event.category" alt="" width="136" height="96" loading="lazy" />
              <span class="text">
                <span class="title">{{ event.title }}</span>
                <span class="meta">{{ when(event.startAt) }}, {{ event.locality }}</span>
                <span class="meta"><span class="cat-dot" [style.--dot]="colors[event.category]"></span>{{ labels[event.category] }}, {{ price(event.price) }}</span>
              </span>
            </a>
          </li>
        }
      </ul>
    </section>
  `,
  styles: [`
    :host { display: block; }
    .similar { border-top: 1px solid var(--line); margin-top: 2.5rem; padding-top: 1.75rem; }
    h2 { font-size: clamp(1.25rem, 3vw, 1.5rem); font-weight: 600; margin: 0 0 1rem; }
    ul { display: grid; gap: .75rem; grid-template-columns: repeat(auto-fill, minmax(17rem, 1fr)); list-style: none; margin: 0; padding: 0; }
    a { align-items: center; background: var(--surface); border: 1px solid var(--line); border-radius: var(--radius-md); color: var(--ink); display: flex; gap: .85rem; padding: .6rem; }
    a:hover { border-color: var(--line-strong); }
    a:hover .title { text-decoration: underline; }
    img { aspect-ratio: 17 / 12; border-radius: var(--radius-sm); flex: none; height: auto; object-fit: cover; width: 5.5rem; }
    .text { display: grid; gap: .2rem; min-width: 0; }
    .title { font-weight: 600; line-height: 1.3; }
    .meta { align-items: center; color: var(--muted); display: inline-flex; font-size: .8125rem; gap: .35rem; }
  `],
})
export class SimilarEvents {
  private readonly clock = inject(Clock);
  readonly events = input.required<EventSummary[]>();
  readonly labels = CATEGORY_LABELS;
  readonly colors = CATEGORY_COLORS;
  readonly price = formatPrice;

  when(startAt: string): string {
    const now = this.clock.now();
    if (new Date(startAt) <= now) return 'עכשיו';
    return `${relativeDay(startAt, now)} ${formatTime(startAt)}`;
  }
}
