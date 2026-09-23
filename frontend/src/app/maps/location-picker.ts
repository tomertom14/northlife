import { AfterViewInit, Component, ElementRef, inject, input, output, signal, viewChild } from '@angular/core';
import { MAP_ADAPTER } from './map-adapter';
import { PublicEventsApi } from '../public/public-events-api';

export interface SelectedCoordinates { latitude: number; longitude: number; }

@Component({
  selector: 'app-location-picker',
  template: `
    <section class="picker">
      <div class="picker-head"><strong>בחירת נקודה במפה</strong><button type="button" (click)="useMyLocation()">המיקום שלי</button></div>
      @if (message()) { <p role="status">{{ message() }}</p> }
      <div #host class="picker-map" aria-label="בחירת מיקום האירוע"></div>
    </section>
  `,
  styles: [`
    .picker { margin-top: 1rem; }
    .picker-head { align-items: center; display: flex; justify-content: space-between; margin-bottom: .5rem; }
    .picker-map { background: #edf1ed; border: 1px solid #c7d4ca; border-radius: .7rem; height: 280px; }
    button { background: white; border: 1px solid var(--accent); border-radius: .55rem; color: var(--accent-dark); font-weight: 800; padding: .5rem .75rem; }
    p { color: var(--muted); }
  `],
})
export class LocationPicker implements AfterViewInit {
  private readonly adapter = inject(MAP_ADAPTER);
  private readonly api = inject(PublicEventsApi);
  private readonly host = viewChild.required<ElementRef<HTMLElement>>('host');
  readonly latitude = input.required<number>();
  readonly longitude = input.required<number>();
  readonly coordinatesSelected = output<SelectedCoordinates>();
  readonly message = signal('');

  ngAfterViewInit(): void {
    this.api.getPublicConfiguration().subscribe({
      next: config => void this.adapter.pick(
        this.host().nativeElement, this.latitude(), this.longitude(), config,
        (latitude, longitude) => this.coordinatesSelected.emit({ latitude, longitude }),
      ).catch(() => this.message.set('המפה אינה מוגדרת. אפשר להזין קואורדינטות ידנית.')),
      error: () => this.message.set('המפה אינה זמינה. אפשר להזין קואורדינטות ידנית.'),
    });
  }

  useMyLocation(): void {
    if (!navigator.geolocation) { this.message.set('המיקום אינו זמין בדפדפן הזה.'); return; }
    navigator.geolocation.getCurrentPosition(
      position => this.coordinatesSelected.emit({ latitude: position.coords.latitude, longitude: position.coords.longitude }),
      () => this.message.set('לא התקבלה הרשאת מיקום. הזינו קואורדינטות או בחרו במפה.'),
      { timeout: 8000 },
    );
  }
}
