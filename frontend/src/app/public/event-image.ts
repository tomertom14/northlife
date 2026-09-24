import { Directive, computed, input, signal } from '@angular/core';
import { EventCategory, eventImage } from './public-event.models';

/**
 * Shows an event's uploaded image and falls back to the category illustration when the
 * event has no image or the file cannot be loaded.
 */
@Directive({
  selector: 'img[appEventImage]',
  host: {
    '[src]': 'source()',
    '(error)': 'useFallback()',
    decoding: 'async',
  },
})
export class EventImage {
  readonly appEventImage = input<string | null | undefined>();
  readonly category = input.required<EventCategory>();
  private readonly failedUrl = signal<string | null>(null);

  readonly source = computed(() => {
    const url = this.appEventImage();
    return url && url !== this.failedUrl() ? url : eventImage(this.category());
  });

  useFallback(): void {
    const url = this.appEventImage();
    if (url) this.failedUrl.set(url);
  }
}
