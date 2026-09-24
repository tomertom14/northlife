import {
  AfterViewInit,
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { MAP_ADAPTER, MapEventGroup } from '../maps/map-adapter';
import { CATEGORY_COLORS, MapBounds, MapEvent, PublicConfiguration } from '../public/public-event.models';
import { PublicEventsApi } from '../public/public-events-api';
import { formatTime } from '../shared/jerusalem-time';

const NORTH_DISTRICT: MapBounds = { north: 33.4, south: 32.6, east: 36, west: 34.8 };

@Component({
  selector: 'app-map-page',
  imports: [RouterLink],
  templateUrl: './map-page.html',
  styleUrl: './map-page.scss',
})
export class MapPage implements AfterViewInit {
  private readonly api = inject(PublicEventsApi);
  private readonly adapter = inject(MAP_ADAPTER);
  private readonly router = inject(Router);
  private readonly injector = inject(Injector);
  private readonly mapHost = viewChild.required<ElementRef<HTMLElement>>('mapHost');
  private config: PublicConfiguration = { googleMapsApiKey: '', googleMapsMapId: '' };
  private bounds: MapBounds = NORTH_DISTRICT;
  private pendingBounds?: MapBounds;
  private fitNextRender = true;

  readonly events = signal<MapEvent[]>([]);
  readonly loading = signal(true);
  readonly failed = signal(false);
  /** The map container stays collapsed until Google Maps is known to be configured. */
  readonly mapState = signal<'unknown' | 'ready' | 'unavailable'>('unknown');
  readonly truncated = signal(false);
  readonly locationMessage = signal('');
  readonly colors = CATEGORY_COLORS;
  readonly time = formatTime;
  readonly summary = computed(() => {
    const count = this.events().length;
    if (count === 0) return 'אין אירועים פעילים היום באזור הזה.';
    return count === 1 ? 'אירוע אחד היום באזור המוצג.' : `${count} אירועים היום באזור המוצג.`;
  });

  ngAfterViewInit(): void {
    this.load();
  }

  searchVisibleArea(): void {
    if (this.pendingBounds) this.bounds = this.pendingBounds;
    this.fitNextRender = false;
    this.load();
  }

  useMyLocation(): void {
    if (!navigator.geolocation) {
      this.locationMessage.set('הדפדפן הזה לא משתף מיקום. אפשר להמשיך עם המפה והרשימה.');
      return;
    }
    navigator.geolocation.getCurrentPosition(
      (position) => {
        const { latitude, longitude } = position.coords;
        this.bounds = { north: latitude + 0.25, south: latitude - 0.25, east: longitude + 0.35, west: longitude - 0.35 };
        this.locationMessage.set('מציגים אירועים בסביבה שלכם.');
        this.fitNextRender = true;
        this.load();
      },
      () => this.locationMessage.set('לא התקבלה הרשאת מיקום. אפשר להמשיך עם המפה והרשימה.'),
      { timeout: 8000 },
    );
  }

  navigationUrl(event: MapEvent): string {
    return `https://waze.com/ul?ll=${event.latitude},${event.longitude}&navigate=yes`;
  }

  private load(): void {
    this.loading.set(true);
    this.failed.set(false);
    forkJoin({
      response: this.api.getMap(this.bounds, { period: 'today', page: 1, pageSize: 12 }),
      config: this.api.getPublicConfiguration(),
    }).subscribe({
      next: ({ response, config }) => {
        this.events.set(response.items);
        this.truncated.set(response.truncated);
        this.config = config;
        this.loading.set(false);
        if (!config.googleMapsApiKey || !config.googleMapsMapId) {
          this.mapState.set('unavailable');
          return;
        }
        this.mapState.set('ready');
        // Render once the container is laid out; Google Maps cannot size a hidden element.
        afterNextRender(() => void this.renderMap(), { injector: this.injector });
      },
      error: () => {
        this.events.set([]);
        this.loading.set(false);
        this.failed.set(true);
      },
    });
  }

  private async renderMap(): Promise<void> {
    try {
      await this.adapter.render(this.mapHost().nativeElement, this.grouped(), this.config, {
        boundsChanged: (bounds) => (this.pendingBounds = bounds),
        openEvent: (eventId) => void this.router.navigate(['/events', eventId]),
        fitToMarkers: this.fitNextRender,
      });
    } catch {
      this.mapState.set('unavailable');
    }
  }

  private grouped(): MapEventGroup[] {
    const groups = new Map<string, MapEventGroup>();
    for (const event of this.events()) {
      const key = `${event.latitude.toFixed(6)},${event.longitude.toFixed(6)}`;
      const group = groups.get(key) ?? { latitude: event.latitude, longitude: event.longitude, events: [] };
      group.events.push(event);
      groups.set(key, group);
    }
    return [...groups.values()];
  }
}
