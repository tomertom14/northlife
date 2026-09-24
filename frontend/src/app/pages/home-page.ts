import { Component, DestroyRef, OnInit, computed, effect, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subscription } from 'rxjs';
import { EventTimetable } from '../public/event-timetable';
import { FilterBar } from '../public/filter-bar';
import { PicksRail } from '../public/picks-rail';
import {
  EVENT_CATEGORIES,
  EventCategory,
  EventPeriod,
  EventSummary,
  PublicEventFilters,
} from '../public/public-event.models';
import { PublicEventsApi } from '../public/public-events-api';
import { DateRange, SkyHero } from '../public/sky-hero';
import { skyForPeriod } from '../public/sky';
import { SkyState } from '../public/sky-state';
import { buildTimetable } from '../public/timetable';
import { Clock } from '../shared/clock';
import {
  addDaysToKey,
  formatDateKey,
  formatLongDate,
  formatLongDateKey,
  jerusalemDateKey,
} from '../shared/jerusalem-time';

const PAGE_SIZE = 12;
const PERIODS: EventPeriod[] = ['now', 'today', 'tonight', 'tomorrow', 'range'];

@Component({
  selector: 'app-home-page',
  imports: [SkyHero, FilterBar, PicksRail, EventTimetable],
  templateUrl: './home-page.html',
  styleUrl: './home-page.scss',
})
export class HomePage implements OnInit {
  private readonly api = inject(PublicEventsApi);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly clock = inject(Clock);
  readonly sky = inject(SkyState);
  private eventsRequest?: Subscription;
  private picksRequest?: Subscription;

  readonly filters = signal<PublicEventFilters>(this.defaults());
  readonly events = signal<EventSummary[]>([]);
  readonly topPicks = signal<EventSummary[]>([]);
  readonly totalCount = signal(0);
  readonly loading = signal(true);
  readonly failed = signal(false);

  readonly needsRange = computed(() => {
    const { period, from, to } = this.filters();
    return period === 'range' && (!from || !to);
  });
  readonly totalPages = computed(() => Math.max(1, Math.ceil(this.totalCount() / PAGE_SIZE)));
  readonly hasActiveFilters = computed(() => {
    const { category, locality, maxPrice } = this.filters();
    return !!category || !!locality || maxPrice !== undefined;
  });
  readonly groups = computed(() => {
    const period = this.filters().period;
    const now = this.clock.now();
    const today = jerusalemDateKey(now);
    const referenceDay =
      period === 'range' ? null : period === 'tomorrow' ? addDaysToKey(today, 1) : today;
    return buildTimetable(this.events(), now, referenceDay);
  });
  readonly feedTitle = computed(() => {
    const { period, from, to } = this.filters();
    const now = this.clock.now();
    switch (period) {
      case 'now':
        return 'קורה עכשיו';
      case 'today':
        return `היום, ${formatLongDate(now)}`;
      case 'tonight':
        return 'הלילה, מ־18:00 עד 06:00';
      case 'tomorrow':
        return `מחר, ${formatLongDateKey(addDaysToKey(jerusalemDateKey(now), 1))}`;
      case 'range':
        return from && to ? `${formatDateKey(from)} עד ${formatDateKey(to)}` : 'תכנון קדימה';
    }
  });
  readonly countLabel = computed(() => countPhrase(this.totalCount()));
  readonly emptyTitle = computed(() => {
    if (this.hasActiveFilters()) return 'אין אירועים שמתאימים לסינון';
    const titles: Record<EventPeriod, string> = {
      now: 'כרגע אין אירועים פעילים',
      today: 'אין אירועים היום',
      tonight: 'אין אירועים הלילה',
      tomorrow: 'אין אירועים מחר',
      range: 'אין אירועים בתאריכים האלה',
    };
    return titles[this.filters().period];
  });
  readonly statusLine = computed(() => {
    if (this.needsRange()) return 'בחרו מתאריך ועד תאריך כדי לתכנן קדימה.';
    if (this.loading()) return 'בודקים מה קורה בצפון…';
    if (this.failed()) return '';
    const count = this.totalCount();
    const phrase = countPhrase(count);
    const { period, from, to } = this.filters();
    switch (period) {
      case 'now':
        return count === 0 ? 'כרגע אין אירועים פעילים.' : `${phrase} קורים עכשיו בצפון.`;
      case 'today':
        return `${phrase} היום בצפון.`;
      case 'tonight':
        return `${phrase} הלילה, בין 18:00 ל־06:00.`;
      case 'tomorrow':
        return `${phrase} מחר בצפון.`;
      case 'range':
        return `${phrase} בין ${formatDateKey(from!)} ל־${formatDateKey(to!)}.`;
    }
  });

  constructor() {
    effect(() => this.sky.show(skyForPeriod(this.filters().period, this.clock.now())));
    this.sky.heroActive.set(true);
    this.destroyRef.onDestroy(() => {
      this.sky.heroActive.set(false);
      this.sky.followClock();
    });
  }

  ngOnInit(): void {
    this.route.queryParamMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((params) => {
      this.filters.set({
        period: readPeriod(params.get('period')),
        category: readCategory(params.get('category')),
        locality: params.get('locality')?.trim() || undefined,
        maxPrice: readPrice(params.get('maxPrice')),
        from: params.get('from') || undefined,
        to: params.get('to') || undefined,
        page: readPositiveInt(params.get('page'), 1),
        pageSize: PAGE_SIZE,
      });
      this.load();
    });
  }

  selectPeriod(period: EventPeriod): void {
    this.navigate({ ...this.filters(), period, page: 1 });
  }

  applyRange(range: DateRange): void {
    this.navigate({ ...this.filters(), period: 'range', from: range.from, to: range.to, page: 1 });
  }

  setCategory(category: EventCategory | undefined): void {
    this.navigate({ ...this.filters(), category, page: 1 });
  }

  setLocality(locality: string | undefined): void {
    this.navigate({ ...this.filters(), locality, page: 1 });
  }

  setMaxPrice(maxPrice: number | undefined): void {
    this.navigate({ ...this.filters(), maxPrice, page: 1 });
  }

  clearFilters(): void {
    const { period, from, to } = this.filters();
    this.navigate({ ...this.defaults(), period, from, to });
  }

  goToPage(page: number): void {
    if (page < 1 || page > this.totalPages()) return;
    this.navigate({ ...this.filters(), page });
    document.getElementById('feed-title')?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  reload(): void {
    this.load();
  }

  private load(): void {
    this.eventsRequest?.unsubscribe();
    this.picksRequest?.unsubscribe();
    const filters = this.filters();
    this.failed.set(false);

    if (this.needsRange()) {
      this.loading.set(false);
      this.events.set([]);
      this.totalCount.set(0);
      this.topPicks.set([]);
      return;
    }

    this.loading.set(true);
    this.eventsRequest = this.api.getEvents(filters).subscribe({
      next: (response) => {
        this.events.set(response.items);
        this.totalCount.set(response.totalCount);
        this.loading.set(false);
      },
      error: () => {
        this.events.set([]);
        this.totalCount.set(0);
        this.failed.set(true);
        this.loading.set(false);
      },
    });

    this.picksRequest = this.api.getTopPicks(filters).subscribe({
      next: (events) => this.topPicks.set(events),
      error: () => this.topPicks.set([]),
    });
  }

  private navigate(filters: PublicEventFilters): void {
    void this.router.navigate(['/'], {
      queryParams: {
        period: filters.period === 'today' ? undefined : filters.period,
        category: filters.category,
        locality: filters.locality || undefined,
        maxPrice: filters.maxPrice,
        from: filters.period === 'range' ? filters.from : undefined,
        to: filters.period === 'range' ? filters.to : undefined,
        page: filters.page > 1 ? filters.page : undefined,
      },
    });
  }

  private defaults(): PublicEventFilters {
    return { period: 'today', page: 1, pageSize: PAGE_SIZE };
  }
}

function countPhrase(count: number): string {
  return count === 1 ? 'אירוע אחד' : `${count} אירועים`;
}

function readPeriod(value: string | null): EventPeriod {
  return PERIODS.includes(value as EventPeriod) ? (value as EventPeriod) : 'today';
}

function readCategory(value: string | null): EventCategory | undefined {
  return EVENT_CATEGORIES.includes(value as EventCategory) ? (value as EventCategory) : undefined;
}

function readPrice(value: string | null): number | undefined {
  if (value === null || value.trim() === '') return undefined;
  const price = Number(value);
  return Number.isFinite(price) && price >= 0 ? price : undefined;
}

function readPositiveInt(value: string | null, fallback: number): number {
  const parsed = Number(value);
  return Number.isInteger(parsed) && parsed > 0 ? parsed : fallback;
}
