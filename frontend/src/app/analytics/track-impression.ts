import { DestroyRef, Directive, ElementRef, afterNextRender, inject, input } from '@angular/core';
import { AnalyticsService, InteractionSource } from './analytics';

/** Half the card in view for a full second counts as seen, following the IAB viewability rule. */
const VISIBLE_RATIO = 0.5;
const DWELL_MS = 1000;

/** Records one impression per element once it has really been seen, not merely scrolled past. */
@Directive({ selector: '[appTrackImpression]' })
export class TrackImpression {
  readonly eventId = input.required<string>({ alias: 'appTrackImpression' });
  readonly source = input<InteractionSource>('Feed', { alias: 'trackSource' });
  readonly position = input<number | undefined>(undefined, { alias: 'trackPosition' });

  constructor() {
    const element = inject(ElementRef<HTMLElement>).nativeElement;
    const analytics = inject(AnalyticsService);
    const destroyRef = inject(DestroyRef);

    afterNextRender(() => {
      if (typeof IntersectionObserver === 'undefined' || !analytics.enabled) return;
      let dwell: ReturnType<typeof setTimeout> | undefined;
      const observer = new IntersectionObserver(
        ([entry]) => {
          if (entry?.isIntersecting) {
            dwell ??= setTimeout(() => {
              analytics.track(this.eventId(), 'Impression', { source: this.source(), position: this.position() });
              observer.disconnect();
            }, DWELL_MS);
          } else {
            clearTimeout(dwell);
            dwell = undefined;
          }
        },
        { threshold: VISIBLE_RATIO },
      );
      observer.observe(element);
      destroyRef.onDestroy(() => {
        clearTimeout(dwell);
        observer.disconnect();
      });
    });
  }
}
