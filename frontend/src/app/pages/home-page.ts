import { Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { EventCard } from '../public/event-card';
import {
  EVENT_CATEGORIES,
  EventPeriod,
  EventSummary,
  PERIOD_LABELS,
  PublicEventFilters,
  CATEGORY_LABELS,
} from '../public/public-event.models';
import { PublicEventsApi } from '../public/public-events-api';

@Component({
  selector: 'app-home-page',
  imports: [FormsModule, EventCard],
  templateUrl: './home-page.html',
  styleUrl: './home-page.scss',
})
export class HomePage implements OnInit {
  private readonly api = inject(PublicEventsApi);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private requestVersion = 0;

  readonly periods: EventPeriod[] = ['now', 'today', 'tonight', 'tomorrow', 'range'];
  readonly periodLabels = PERIOD_LABELS;
  readonly categories = EVENT_CATEGORIES;
  readonly categoryLabels = CATEGORY_LABELS;

  filters: PublicEventFilters = this.defaults();
  readonly events = signal<EventSummary[]>([]);
  readonly topPicks = signal<EventSummary[]>([]);
  readonly totalCount = signal(0);
  readonly loading = signal(true);
  readonly topPicksLoading = signal(true);
  readonly failed = signal(false);
  readonly validationMessage = signal('');
  readonly totalPages = computed(() => Math.max(1, Math.ceil(this.totalCount() / this.filters.pageSize)));

  ngOnInit(): void {
    this.route.queryParamMap
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((params) => {
        this.filters = {
          period: this.readPeriod(params.get('period')),
          category: this.readCategory(params.get('category')),
          locality: params.get('locality')?.trim() || undefined,
          maxPrice: this.readPrice(params.get('maxPrice')),
          from: params.get('from') || undefined,
          to: params.get('to') || undefined,
          page: this.readPositiveInt(params.get('page'), 1),
          pageSize: 12,
        };
        this.load();
      });
  }

  selectPeriod(period: EventPeriod): void {
    this.navigate({ ...this.filters, period, page: 1 });
  }

  applyFilters(): void {
    if (this.filters.period === 'range' && (!this.filters.from || !this.filters.to)) {
      this.validationMessage.set('יש לבחור תאריך התחלה ותאריך סיום.');
      return;
    }
    if (this.filters.period === 'range' && this.filters.from! > this.filters.to!) {
      this.validationMessage.set('תאריך הסיום חייב להיות אחרי תאריך ההתחלה.');
      return;
    }

    this.validationMessage.set('');
    this.navigate({ ...this.filters, page: 1 });
  }

  clearFilters(): void {
    this.navigate(this.defaults());
  }

  goToPage(page: number): void {
    if (page < 1 || page > this.totalPages()) return;
    this.navigate({ ...this.filters, page });
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  reload(): void {
    this.load();
  }

  private load(): void {
    const version = ++this.requestVersion;
    this.loading.set(true);
    this.failed.set(false);
    this.topPicksLoading.set(true);

    this.api.getEvents(this.filters).subscribe({
      next: (response) => {
        if (version !== this.requestVersion) return;
        this.events.set(response.items);
        this.totalCount.set(response.totalCount);
        this.loading.set(false);
      },
      error: () => {
        if (version !== this.requestVersion) return;
        this.events.set([]);
        this.totalCount.set(0);
        this.failed.set(true);
        this.loading.set(false);
      },
    });

    this.api.getTopPicks(this.filters).subscribe({
      next: (events) => {
        if (version !== this.requestVersion) return;
        this.topPicks.set(events);
        this.topPicksLoading.set(false);
      },
      error: () => {
        if (version !== this.requestVersion) return;
        this.topPicks.set([]);
        this.topPicksLoading.set(false);
      },
    });
  }

  private navigate(filters: PublicEventFilters): void {
    void this.router.navigate(['/'], { queryParams: this.toQueryParams(filters) });
  }

  private toQueryParams(filters: PublicEventFilters): Record<string, string | number | undefined> {
    return {
      period: filters.period,
      category: filters.category,
      locality: filters.locality || undefined,
      maxPrice: filters.maxPrice,
      from: filters.period === 'range' ? filters.from : undefined,
      to: filters.period === 'range' ? filters.to : undefined,
      page: filters.page > 1 ? filters.page : undefined,
    };
  }

  private defaults(): PublicEventFilters {
    return { period: 'today', page: 1, pageSize: 12 };
  }

  private readPeriod(value: string | null): EventPeriod {
    return this.periods.includes(value as EventPeriod) ? value as EventPeriod : 'today';
  }

  private readCategory(value: string | null) {
    return this.categories.includes(value as never) ? value as PublicEventFilters['category'] : undefined;
  }

  private readPrice(value: string | null): number | undefined {
    if (value === null || value.trim() === '') return undefined;
    const price = Number(value);
    return Number.isFinite(price) && price >= 0 ? price : undefined;
  }

  private readPositiveInt(value: string | null, fallback: number): number {
    const parsed = Number(value);
    return Number.isInteger(parsed) && parsed > 0 ? parsed : fallback;
  }
}
