import { Component, DestroyRef, inject, signal } from '@angular/core';
import { Location } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import {
  CATEGORY_LABELS,
  EventDetails,
  eventImage,
  formatEventDate,
  formatPrice,
} from '../public/public-event.models';
import { PublicEventsApi } from '../public/public-events-api';

@Component({
  selector: 'app-event-details-page',
  imports: [RouterLink],
  templateUrl: './event-details-page.html',
  styleUrl: './event-details-page.scss',
})
export class EventDetailsPage {
  private readonly api = inject(PublicEventsApi);
  private readonly route = inject(ActivatedRoute);
  private readonly location = inject(Location);
  private readonly destroyRef = inject(DestroyRef);
  private eventId = '';

  readonly event = signal<EventDetails | null>(null);
  readonly loading = signal(true);
  readonly unavailable = signal(false);
  readonly failed = signal(false);
  readonly categoryLabels = CATEGORY_LABELS;
  formatDate = formatEventDate;
  formatPrice = formatPrice;

  constructor() {
    this.route.paramMap
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((params) => {
        this.eventId = params.get('id') ?? '';
        this.load();
      });
  }

  reload(): void {
    this.load();
  }

  goBack(): void {
    this.location.back();
  }

  imageFor(event: EventDetails): string {
    return eventImage(event.category);
  }

  mapUrl(event: EventDetails): string {
    const query = encodeURIComponent(`${event.latitude},${event.longitude}`);
    return `https://www.google.com/maps/search/?api=1&query=${query}`;
  }

  private load(): void {
    if (!this.eventId) {
      this.unavailable.set(true);
      this.loading.set(false);
      return;
    }

    this.loading.set(true);
    this.failed.set(false);
    this.unavailable.set(false);

    this.api.getEvent(this.eventId).subscribe({
      next: (event) => {
        this.event.set(event);
        this.loading.set(false);
      },
      error: (error: { status?: number }) => {
        this.event.set(null);
        this.loading.set(false);
        if (error.status === 404) this.unavailable.set(true);
        else this.failed.set(true);
      },
    });
  }
}
