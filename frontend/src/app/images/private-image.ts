import { HttpClient } from '@angular/common/http';
import { Directive, computed, effect, inject, input, signal } from '@angular/core';
import { EventCategory, eventImage } from '../public/public-event.models';

/**
 * Loads an image that needs the signed-in user's token (pending and rejected events are private),
 * so a plain <img src> cannot fetch it. Falls back to the category illustration on failure.
 */
@Directive({
  selector: 'img[appPrivateImage]',
  host: {
    '[attr.src]': 'source()',
  },
})
export class PrivateImage {
  private readonly http = inject(HttpClient);
  readonly appPrivateImage = input<string | null | undefined>();
  readonly category = input<EventCategory>('Other');
  private readonly objectUrl = signal<string | null>(null);
  private readonly failed = signal(false);

  readonly source = computed(() => (this.failed() ? eventImage(this.category()) : this.objectUrl()));

  constructor() {
    effect((onCleanup) => {
      const url = this.appPrivateImage();
      this.failed.set(false);
      if (!url) {
        this.failed.set(true);
        return;
      }
      let created: string | null = null;
      const request = this.http.get(url, { responseType: 'blob' }).subscribe({
        next: (blob) => {
          created = URL.createObjectURL(blob);
          this.objectUrl.set(created);
        },
        error: () => this.failed.set(true),
      });
      onCleanup(() => {
        request.unsubscribe();
        if (created) URL.revokeObjectURL(created);
        this.objectUrl.set(null);
      });
    });
  }
}
