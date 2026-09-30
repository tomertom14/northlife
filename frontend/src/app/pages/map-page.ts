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
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { forkJoin, map } from 'rxjs';
import { clusterEvents } from '../maps/clustering';
import { MAP_ADAPTER, MapEventGroup } from '../maps/map-adapter';
import { MapPlace, PLACE_CATEGORY_LABELS, PLACE_COVER_CATEGORY } from '../places/places.models';
import { PlacesApi } from '../places/places-api';
import { CATEGORY_COLORS, MapBounds, MapEvent, PublicConfiguration } from '../public/public-event.models';
import { PublicEventsApi } from '../public/public-events-api';
import { formatTime } from '../shared/jerusalem-time';

const NORTH_DISTRICT: MapBounds = { north: 33.4, south: 32.6, east: 36, west: 34.8 };

export type MapLayer = 'events' | 'places';

@Component({
  selector: 'app-map-page',
  imports: [RouterLink],
  templateUrl: './map-page.html',
  styleUrl: './map-page.scss',
})
export class MapPage implements AfterViewInit {
  private readonly api = inject(PublicEventsApi);
  private readonly placesApi = inject(PlacesApi);
  private readonly adapter = inject(MAP_ADAPTER);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly injector = inject(Injector);
  private readonly mapHost = viewChild.required<ElementRef<HTMLElement>>('mapHost');
  private config: PublicConfiguration = { googleMapsApiKey: '', googleMapsMapId: '' };
  private bounds: MapBounds = NORTH_DISTRICT;
  private pendingBounds?: MapBounds;
  private fitNextRender = true;
  private zoom = 9;

  /** Events of today, or places; the choice is in the URL (?layer=places) so it can be shared. */
  readonly layer = signal<MapLayer>(this.route.snapshot.queryParamMap.get('layer') === 'places' ? 'places' : 'events');
  readonly events = signal<MapEvent[]>([]);
  readonly places = signal<MapPlace[]>([]);
  readonly loading = signal(true);
  readonly failed = signal(false);
  /** The map container stays collapsed until Google Maps is known to be configured. */
  readonly mapState = signal<'unknown' | 'ready' | 'unavailable'>('unknown');
  readonly truncated = signal(false);
  readonly locationMessage = signal('');
  readonly colors = CATEGORY_COLORS;
  readonly placeLabels = PLACE_CATEGORY_LABELS;
  readonly placeCover = PLACE_COVER_CATEGORY;
  readonly time = formatTime;
  readonly summary = computed(() => {
    if (this.layer() === 'places') {
      const count = this.places().length;
      if (count === 0) return 'אין מקומות באזור הזה.';
      return count === 1 ? 'מקום אחד באזור המוצג.' : `${count} מקומות באזור המוצג.`;
    }
    const count = this.events().length;
    if (count === 0) return 'אין אירועים פעילים היום באזור הזה.';
    return count === 1 ? 'אירוע אחד היום באזור המוצג.' : `${count} אירועים היום באזור המוצג.`;
  });

  ngAfterViewInit(): void {
    this.load();
  }

  setLayer(layer: MapLayer): void {
    if (layer === this.layer()) return;
    this.layer.set(layer);
    void this.router.navigate([], { relativeTo: this.route, queryParams: { layer: layer === 'places' ? 'places' : null }, replaceUrl: true });
    this.fitNextRender = false;
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
        this.locationMessage.set(this.layer() === 'places' ? 'מציגים מקומות בסביבה שלכם.' : 'מציגים אירועים בסביבה שלכם.');
        this.fitNextRender = true;
        this.load();
      },
      () => this.locationMessage.set('לא התקבלה הרשאת מיקום. אפשר להמשיך עם המפה והרשימה.'),
      { timeout: 8000 },
    );
  }

  navigationUrl(item: { latitude: number; longitude: number }): string {
    return `https://waze.com/ul?ll=${item.latitude},${item.longitude}&navigate=yes`;
  }

  private load(): void {
    this.loading.set(true);
    this.failed.set(false);
    const layer = this.layer();
    const items = layer === 'places'
      ? this.placesApi.map(this.bounds).pipe(map((response) => ({ places: response.items, events: [] as MapEvent[], truncated: response.truncated })))
      : this.api.getMap(this.bounds, { period: 'today', page: 1, pageSize: 12 })
        .pipe(map((response) => ({ places: [] as MapPlace[], events: response.items, truncated: response.truncated })));
    forkJoin({ response: items, config: this.api.getPublicConfiguration() }).subscribe({
      next: ({ response, config }) => {
        if (layer !== this.layer()) return;
        this.events.set(response.events);
        this.places.set(response.places);
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
        this.places.set([]);
        this.loading.set(false);
        this.failed.set(true);
      },
    });
  }

  private async renderMap(): Promise<void> {
    const places = this.layer() === 'places';
    try {
      await this.adapter.render(this.mapHost().nativeElement, this.grouped(), this.config, {
        boundsChanged: (bounds, zoom) => {
          this.pendingBounds = bounds;
          // Clusters depend on the zoom level: rebuild them, keeping the visitor's viewport.
          if (zoom !== this.zoom) {
            this.zoom = zoom;
            this.fitNextRender = false;
            void this.renderMap();
          }
        },
        openEvent: (id) => void this.router.navigate([places ? '/places' : '/events', id]),
        fitToMarkers: this.fitNextRender,
        hrefFor: places ? (id) => `/places/${encodeURIComponent(id)}` : undefined,
        countLabel: places ? (count) => `${count} מקומות` : undefined,
      });
    } catch {
      this.mapState.set('unavailable');
    }
  }

  /** Places go through the same MarkerClusterer-style grid as events, as marker items. */
  private grouped(): MapEventGroup[] {
    if (this.layer() === 'events') return clusterEvents(this.events(), this.zoom);
    const markers: MapEvent[] = this.places().map((place) => ({
      id: place.id,
      title: place.name,
      startAt: '',
      venueName: place.name,
      locality: place.locality,
      address: place.locality,
      latitude: place.latitude,
      longitude: place.longitude,
      category: PLACE_COVER_CATEGORY[place.category],
    }));
    return clusterEvents(markers, this.zoom);
  }
}
