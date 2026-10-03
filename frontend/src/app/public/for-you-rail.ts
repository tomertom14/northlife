import { Component, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TrackImpression } from '../analytics/track-impression';
import { RecommendationItem } from '../recommendations/recommendations-api';
import { Clock } from '../shared/clock';
import { formatTime, relativeDay } from '../shared/jerusalem-time';
import { EventImage } from './event-image';
import { CATEGORY_COLORS, CATEGORY_LABELS } from './public-event.models';

/** "For you": personal picks, each with the event that explains it. */
@Component({
  selector: 'app-for-you-rail',
  imports: [RouterLink, EventImage, TrackImpression],
  template: `
    <section class="for-you" aria-labelledby="for-you-title">
      <h2 id="for-you-title">בשבילך</h2>
      <p class="lead">לפי האירועים שפתחתם באתר, בלי חשבון ובלי פרטים אישיים.</p>
      <ul class="rail">
        @for (item of items(); track item.event.id) {
          <li>
            <a
              class="card"
              [routerLink]="['/events', item.event.id]"
              [state]="{ source: 'Recommendations', position: $index + 1, context: 'recs' }"
              [appTrackImpression]="item.event.id"
              trackSource="Recommendations"
              [trackPosition]="$index + 1"
              trackContext="recs">
              <img [appEventImage]="item.event.imageUrl" [category]="item.event.category" alt="" width="320" height="200" loading="lazy" />
              <span class="text">
                <span class="title">{{ item.event.title }}</span>
                <span class="meta">{{ when(item.event.startAt) }}, {{ item.event.locality }}</span>
                <span class="cat"><span class="cat-dot" [style.--dot]="colors[item.event.category]"></span>{{ labels[item.event.category] }}</span>
                <span class="why">{{ reason(item) }}</span>
              </span>
            </a>
          </li>
        }
      </ul>
    </section>
  `,
  styles: [`
    :host { display: block; }
    .for-you { margin: 0 auto; max-width: var(--page-max); padding-top: clamp(1.75rem, 4vw, 3rem); }
    h2 { font-size: clamp(1.375rem, 3vw, 1.75rem); font-weight: 600; margin: 0; padding-inline: var(--gutter); }
    .lead { color: var(--muted); font-size: .875rem; margin: .25rem 0 1rem; padding-inline: var(--gutter); }
    .rail {
      display: flex; gap: clamp(.75rem, 2vw, 1.25rem); list-style: none; margin: 0; overflow-x: auto;
      padding: 0 var(--gutter) .75rem; scroll-padding-inline: var(--gutter); scroll-snap-type: x mandatory; scrollbar-width: thin;
    }
    .rail li { flex: none; scroll-snap-align: start; width: clamp(14rem, 62vw, 17.5rem); }
    .card {
      background: var(--surface); border: 1px solid var(--line); border-radius: var(--radius-md); color: var(--ink);
      display: grid; height: 100%; overflow: hidden;
    }
    .card:hover { border-color: var(--line-strong); }
    .card:hover .title { text-decoration: underline; }
    img { aspect-ratio: 16 / 10; display: block; height: auto; object-fit: cover; width: 100%; }
    .text { display: grid; gap: .3rem; padding: .8rem .95rem 1rem; }
    .title { font-size: 1.0625rem; font-weight: 600; line-height: 1.3; }
    .meta, .cat { color: var(--muted); font-size: .8125rem; }
    .cat { align-items: center; display: inline-flex; gap: .4rem; }
    .why { border-top: 1px solid var(--line); color: var(--ink); font-size: .8125rem; margin-top: .3rem; padding-top: .45rem; }
  `],
})
export class ForYouRail {
  private readonly clock = inject(Clock);
  readonly items = input.required<RecommendationItem[]>();
  readonly labels = CATEGORY_LABELS;
  readonly colors = CATEGORY_COLORS;

  when(startAt: string): string {
    const now = this.clock.now();
    if (new Date(startAt) <= now) return 'עכשיו';
    return `${relativeDay(startAt, now)} ${formatTime(startAt)}`;
  }

  reason(item: RecommendationItem): string {
    return item.reason === 'similar' && item.becauseOfTitle ? `כי פתחתם את ״${item.becauseOfTitle}״` : 'פופולרי עכשיו באתר';
  }
}
