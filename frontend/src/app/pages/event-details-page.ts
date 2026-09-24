import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { Location } from '@angular/common';
import { Title } from '@angular/platform-browser';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subscription } from 'rxjs';
import { EventImage } from '../public/event-image';
import {
  CATEGORY_COLORS,
  CATEGORY_LABELS,
  EventDetails,
  formatPrice,
} from '../public/public-event.models';
import { PublicEventsApi } from '../public/public-events-api';
import { Clock } from '../shared/clock';
import {
  addDaysToKey,
  formatLongDate,
  formatTime,
  jerusalemDateKey,
} from '../shared/jerusalem-time';
import { NavigationHistory } from '../shared/navigation-history';

@Component({
  selector: 'app-event-details-page',
  imports: [RouterLink, EventImage],
  templateUrl: './event-details-page.html',
  styleUrl: './event-details-page.scss',
})
export class EventDetailsPage {
  private readonly api = inject(PublicEventsApi);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly location = inject(Location);
  private readonly title = inject(Title);
  private readonly history = inject(NavigationHistory);
  private readonly clock = inject(Clock);
  private request?: Subscription;
  private eventId = '';

  readonly event = signal<EventDetails | null>(null);
  readonly loading = signal(true);
  readonly unavailable = signal(false);
  readonly failed = signal(false);
  readonly shareMessage = signal('');
  readonly categoryLabels = CATEGORY_LABELS;
  readonly colors = CATEGORY_COLORS;
  readonly price = formatPrice;

  readonly live = computed(() => {
    const item = this.event();
    const now = this.clock.now();
    return !!item && new Date(item.startAt) <= now && new Date(item.endAt) > now;
  });
  readonly when = computed(() => {
    const item = this.event();
    return item ? describeTimes(item.startAt, item.endAt) : { date: '', hours: '' };
  });
  readonly endTime = computed(() => {
    const item = this.event();
    return item ? formatTime(item.endAt) : '';
  });

  constructor() {
    this.route.paramMap.pipe(takeUntilDestroyed(inject(DestroyRef))).subscribe((params) => {
      this.eventId = params.get('id') ?? '';
      this.load();
    });
  }

  reload(): void {
    this.load();
  }

  goBack(): void {
    if (this.history.canGoBack) this.location.back();
    else void this.router.navigate(['/']);
  }

  wazeUrl(item: EventDetails): string {
    return `https://waze.com/ul?ll=${item.latitude},${item.longitude}&navigate=yes`;
  }

  googleMapsUrl(item: EventDetails): string {
    return `https://www.google.com/maps/dir/?api=1&destination=${item.latitude},${item.longitude}`;
  }

  async share(item: EventDetails): Promise<void> {
    const url = window.location.href;
    if (typeof navigator.share === 'function') {
      try {
        await navigator.share({ title: item.title, text: `${item.title}, ${item.venueName}`, url });
      } catch {
        // The visitor closed the share sheet.
      }
      return;
    }
    try {
      await navigator.clipboard.writeText(url);
      this.shareMessage.set('הקישור לאירוע הועתק.');
    } catch {
      this.shareMessage.set('ההעתקה לא הצליחה. אפשר להעתיק את הכתובת מסרגל הדפדפן.');
    }
  }

  private load(): void {
    this.request?.unsubscribe();
    this.shareMessage.set('');
    if (!this.eventId) {
      this.unavailable.set(true);
      this.loading.set(false);
      return;
    }

    this.loading.set(true);
    this.failed.set(false);
    this.unavailable.set(false);
    this.request = this.api.getEvent(this.eventId).subscribe({
      next: (item) => {
        this.event.set(item);
        this.loading.set(false);
        this.title.setTitle(`${item.title} | NorthLife`);
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

function describeTimes(startAt: string, endAt: string): { date: string; hours: string } {
  const date = formatLongDate(startAt);
  const startKey = jerusalemDateKey(startAt);
  const endKey = jerusalemDateKey(endAt);
  const start = formatTime(startAt);
  const end = formatTime(endAt);
  if (startKey === endKey) return { date, hours: `${start} עד ${end}` };
  if (endKey === addDaysToKey(startKey, 1)) return { date, hours: `${start} עד ${end} למחרת` };
  return { date, hours: `${start} עד ${formatLongDate(endAt)}, ${end}` };
}
