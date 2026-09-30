import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Params, Router, RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import { EventImage } from '../public/event-image';
import { NORTHERN_LOCALITIES } from '../public/public-event.models';
import { openStatusText } from '../places/opening-hours';
import {
  PLACE_CATEGORIES,
  PLACE_CATEGORY_LABELS,
  PLACE_COVER_CATEGORY,
  PlaceCategory,
  PlaceQuery,
  PlaceSort,
  PlaceSummary,
} from '../places/places.models';
import { PlacesApi } from '../places/places-api';
import { GeoLocationService } from '../shared/geo-location';

const PAGE_SIZE = 12;

/** The places directory: filters live in the URL, so a filtered list can be shared. */
@Component({
  selector: 'app-places-page',
  imports: [RouterLink, EventImage],
  templateUrl: './places-page.html',
  styleUrl: './places-page.scss',
})
export class PlacesPage {
  private readonly api = inject(PlacesApi);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly geo = inject(GeoLocationService);
  private request?: Subscription;

  readonly categories = PLACE_CATEGORIES;
  readonly labels = PLACE_CATEGORY_LABELS;
  readonly cover = PLACE_COVER_CATEGORY;
  readonly localities = NORTHERN_LOCALITIES;
  readonly statusText = openStatusText;

  readonly query = signal<PlaceQuery>({ openNow: false, sort: 'name', page: 1, pageSize: PAGE_SIZE });
  readonly places = signal<PlaceSummary[]>([]);
  readonly total = signal(0);
  readonly loading = signal(true);
  readonly failed = signal(false);
  readonly geoMessage = signal('');

  readonly totalPages = computed(() => Math.max(1, Math.ceil(this.total() / PAGE_SIZE)));
  readonly hasFilters = computed(() => {
    const query = this.query();
    return !!query.category || !!query.locality || !!query.q || query.openNow;
  });
  readonly countLabel = computed(() => (this.total() === 1 ? 'מקום אחד' : `${this.total()} מקומות`));

  constructor() {
    this.route.queryParamMap.pipe(takeUntilDestroyed(inject(DestroyRef))).subscribe((params) => {
      const category = params.get('category') as PlaceCategory | null;
      const page = Number(params.get('page') ?? '1');
      this.query.set({
        category: category && PLACE_CATEGORIES.includes(category) ? category : undefined,
        locality: params.get('locality') ?? undefined,
        q: params.get('q') ?? undefined,
        openNow: params.get('open') === '1',
        sort: params.get('sort') === 'near' ? 'near' : 'name',
        page: Number.isInteger(page) && page > 0 ? page : 1,
        pageSize: PAGE_SIZE,
      });
      void this.load();
    });
  }

  setCategory(category: PlaceCategory | undefined): void {
    this.navigate({ category: category ?? null, page: null });
  }

  setOpenNow(openNow: boolean): void {
    this.navigate({ open: openNow ? '1' : null, page: null });
  }

  setSort(sort: PlaceSort): void {
    this.navigate({ sort: sort === 'near' ? 'near' : null, page: null });
  }

  onLocality(event: Event): void {
    const value = (event.target as HTMLInputElement).value.trim();
    this.navigate({ locality: value || null, page: null });
  }

  onSearch(event: Event): void {
    event.preventDefault();
    const form = event.target as HTMLFormElement;
    const value = String(new FormData(form).get('q') ?? '').trim();
    this.navigate({ q: value || null, page: null });
  }

  clearFilters(): void {
    this.navigate({ category: null, locality: null, q: null, open: null, page: null });
  }

  goToPage(page: number): void {
    this.navigate({ page: page > 1 ? page : null });
  }

  reload(): void {
    void this.load();
  }

  distance(km: number | null): string {
    if (km === null) return '';
    return km < 1 ? `${Math.round(km * 1000 / 10) * 10} מ׳ ממך` : `${km.toFixed(1)} ק״מ ממך`;
  }

  private navigate(changes: Params): void {
    void this.router.navigate([], { relativeTo: this.route, queryParams: changes, queryParamsHandling: 'merge' });
  }

  private async load(): Promise<void> {
    const query = { ...this.query() };
    this.geoMessage.set('');
    if (query.sort === 'near') {
      try {
        const location = await this.geo.locate();
        query.latitude = location.latitude;
        query.longitude = location.longitude;
      } catch {
        this.geoMessage.set('לא קיבלנו גישה למיקום, ולכן המקומות מסודרים לפי שם. אפשר לאשר מיקום בהגדרות הדפדפן.');
        query.sort = 'name';
      }
    } else if (this.geo.location()) {
      // Distances are shown whenever the visitor already shared a location in this visit.
      query.latitude = this.geo.location()!.latitude;
      query.longitude = this.geo.location()!.longitude;
    }

    this.request?.unsubscribe();
    this.loading.set(true);
    this.failed.set(false);
    this.request = this.api.list(query).subscribe({
      next: (page) => {
        this.places.set(page.items);
        this.total.set(page.totalCount);
        this.loading.set(false);
      },
      error: () => {
        this.places.set([]);
        this.total.set(0);
        this.loading.set(false);
        this.failed.set(true);
      },
    });
  }
}
