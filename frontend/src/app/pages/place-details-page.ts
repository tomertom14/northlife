import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { Location } from '@angular/common';
import { Title } from '@angular/platform-browser';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subscription } from 'rxjs';
import { EventImage } from '../public/event-image';
import { CATEGORY_COLORS, CATEGORY_LABELS, formatPrice } from '../public/public-event.models';
import { jerusalemWeekday, openStatusText, weekRows } from '../places/opening-hours';
import { PLACE_CATEGORY_LABELS, PLACE_COVER_CATEGORY, PlaceDetails } from '../places/places.models';
import { PlacesApi } from '../places/places-api';
import { Clock } from '../shared/clock';
import { formatTime, relativeDay } from '../shared/jerusalem-time';
import { NavigationHistory } from '../shared/navigation-history';

@Component({
  selector: 'app-place-details-page',
  imports: [RouterLink, EventImage],
  templateUrl: './place-details-page.html',
  styleUrls: ['./event-details-page.scss', './place-details-page.scss'],
})
export class PlaceDetailsPage {
  private readonly api = inject(PlacesApi);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly location = inject(Location);
  private readonly title = inject(Title);
  private readonly history = inject(NavigationHistory);
  private readonly clock = inject(Clock);
  private request?: Subscription;
  private placeId = '';

  readonly place = signal<PlaceDetails | null>(null);
  readonly loading = signal(true);
  readonly unavailable = signal(false);
  readonly failed = signal(false);
  readonly labels = PLACE_CATEGORY_LABELS;
  readonly cover = PLACE_COVER_CATEGORY;
  readonly eventLabels = CATEGORY_LABELS;
  readonly eventColors = CATEGORY_COLORS;
  readonly price = formatPrice;

  readonly rows = computed(() => weekRows(this.place()?.hours ?? []));
  readonly today = computed(() => jerusalemWeekday(this.clock.now()));
  readonly status = computed(() => {
    const place = this.place();
    return place ? openStatusText(place.open) : '';
  });

  constructor() {
    this.route.paramMap.pipe(takeUntilDestroyed(inject(DestroyRef))).subscribe((params) => {
      this.placeId = params.get('id') ?? '';
      this.load();
    });
  }

  reload(): void {
    this.load();
  }

  goBack(): void {
    if (this.history.canGoBack) this.location.back();
    else void this.router.navigate(['/places']);
  }

  wazeUrl(place: PlaceDetails): string {
    return `https://waze.com/ul?ll=${place.latitude},${place.longitude}&navigate=yes`;
  }

  googleMapsUrl(place: PlaceDetails): string {
    return `https://www.google.com/maps/dir/?api=1&destination=${place.latitude},${place.longitude}`;
  }

  instagramUrl(handle: string): string {
    return `https://www.instagram.com/${encodeURIComponent(handle)}/`;
  }

  telUrl(phone: string): string {
    return `tel:${phone.replace(/[^\d+]/g, '')}`;
  }

  websiteLabel(url: string): string {
    try {
      return new URL(url).host.replace(/^www\./, '');
    } catch {
      return url;
    }
  }

  when(startAt: string): string {
    const now = this.clock.now();
    if (new Date(startAt) <= now) return 'עכשיו';
    return `${relativeDay(startAt, now)} ${formatTime(startAt)}`;
  }

  private load(): void {
    this.request?.unsubscribe();
    if (!this.placeId) {
      this.unavailable.set(true);
      this.loading.set(false);
      return;
    }

    this.loading.set(true);
    this.failed.set(false);
    this.unavailable.set(false);
    this.request = this.api.get(this.placeId).subscribe({
      next: (place) => {
        this.place.set(place);
        this.loading.set(false);
        this.title.setTitle(`${place.name} | NorthLife`);
      },
      error: (error: { status?: number }) => {
        this.place.set(null);
        this.loading.set(false);
        if (error.status === 404) this.unavailable.set(true);
        else this.failed.set(true);
      },
    });
  }
}
