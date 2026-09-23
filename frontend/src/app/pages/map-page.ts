import { AfterViewInit, Component, ElementRef, inject, signal, viewChild } from '@angular/core';
import { RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { MAP_ADAPTER, MapEventGroup } from '../maps/map-adapter';
import { MapBounds, MapEvent, PublicConfiguration, formatEventDate } from '../public/public-event.models';
import { PublicEventsApi } from '../public/public-events-api';

@Component({
  selector: 'app-map-page',
  imports: [RouterLink],
  templateUrl: './map-page.html',
  styleUrl: './map-page.scss',
})
export class MapPage implements AfterViewInit {
  private readonly api = inject(PublicEventsApi);
  private readonly adapter = inject(MAP_ADAPTER);
  private readonly mapHost = viewChild.required<ElementRef<HTMLElement>>('mapHost');
  readonly events = signal<MapEvent[]>([]);
  readonly loading = signal(true);
  readonly failed = signal(false);
  readonly mapUnavailable = signal(false);
  readonly truncated = signal(false);
  readonly locationMessage = signal('');
  readonly formatDate = formatEventDate;
  private config: PublicConfiguration = { googleMapsApiKey: '', googleMapsMapId: '' };
  private bounds: MapBounds = { north: 33.4, south: 32.6, east: 36, west: 34.8 };
  private pendingBounds?: MapBounds;

  ngAfterViewInit(): void { this.load(); }

  searchVisibleArea(): void {
    if (this.pendingBounds) this.bounds = this.pendingBounds;
    this.load();
  }

  useMyLocation(): void {
    if (!navigator.geolocation) { this.locationMessage.set('המיקום אינו זמין בדפדפן הזה.'); return; }
    navigator.geolocation.getCurrentPosition(
      position => {
        const { latitude, longitude } = position.coords;
        this.bounds = { north: latitude + .25, south: latitude - .25, east: longitude + .35, west: longitude - .35 };
        this.locationMessage.set('מציגים אירועים לידכם.');
        this.load();
      },
      () => this.locationMessage.set('לא התקבלה הרשאת מיקום. אפשר להמשיך עם המפה והרשימה.'),
      { timeout: 8000 },
    );
  }

  navigationUrl(event: MapEvent): string {
    return `https://www.google.com/maps/dir/?api=1&destination=${event.latitude},${event.longitude}`;
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
        void this.renderMap();
      },
      error: () => { this.loading.set(false); this.failed.set(true); },
    });
  }

  private async renderMap(): Promise<void> {
    try {
      await this.adapter.render(this.mapHost().nativeElement, this.grouped(), this.config, (bounds: MapBounds) => this.pendingBounds = bounds);
      this.mapUnavailable.set(false);
    } catch {
      this.mapUnavailable.set(true);
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
