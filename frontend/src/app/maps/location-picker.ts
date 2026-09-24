import { AfterViewInit, Component, ElementRef, effect, inject, input, output, signal, viewChild } from '@angular/core';
import { LocationPickerHandle, MAP_ADAPTER } from './map-adapter';
import { PublicEventsApi } from '../public/public-events-api';

export interface SelectedCoordinates { latitude: number; longitude: number; }

@Component({
  selector: 'app-location-picker',
  template: `
    <div class="picker">
      <div class="picker-head">
        <span>בחרו את נקודת האירוע במפה</span>
        <button type="button" class="btn" (click)="useMyLocation()">המיקום שלי</button>
      </div>
      @if (message()) { <p class="picker-message" role="status">{{ message() }}</p> }
      <div #host class="picker-map" [class.hidden]="unavailable()" role="application" aria-label="מפה לבחירת מיקום האירוע"></div>
    </div>
  `,
  styles: [`
    .picker { display: grid; gap: .6rem; }
    .picker-head { align-items: center; display: flex; flex-wrap: wrap; gap: .5rem 1rem; justify-content: space-between; font-size: .875rem; font-weight: 600; }
    .picker-head .btn { min-height: 2.5rem; }
    .picker-message { color: var(--muted); font-size: .875rem; margin: 0; }
    .picker-map { background: #e3e8e5; border: 1px solid var(--line-strong); border-radius: var(--radius-sm); height: clamp(14rem, 40vw, 18rem); }
    .picker-map.hidden { display: none; }
  `],
})
export class LocationPicker implements AfterViewInit {
  private readonly adapter = inject(MAP_ADAPTER);
  private readonly api = inject(PublicEventsApi);
  private readonly host = viewChild.required<ElementRef<HTMLElement>>('host');
  private handle?: LocationPickerHandle;
  readonly latitude = input.required<number>();
  readonly longitude = input.required<number>();
  readonly coordinatesSelected = output<SelectedCoordinates>();
  readonly message = signal('');
  readonly unavailable = signal(false);

  constructor() {
    // Keep the marker on the coordinates typed into the form or loaded for editing.
    effect(() => {
      const latitude = this.latitude();
      const longitude = this.longitude();
      if (Number.isFinite(latitude) && Number.isFinite(longitude)) this.handle?.moveTo(latitude, longitude);
    });
  }

  ngAfterViewInit(): void {
    this.api.getPublicConfiguration().subscribe({
      next: (config) =>
        void this.adapter
          .pick(this.host().nativeElement, this.latitude(), this.longitude(), config, (latitude, longitude) =>
            this.coordinatesSelected.emit({ latitude, longitude }),
          )
          .then((handle) => {
            this.handle = handle;
            handle.moveTo(this.latitude(), this.longitude());
          })
          .catch(() => this.showUnavailable('המפה אינה זמינה כרגע. אפשר להזין קו רוחב וקו אורך ידנית.')),
      error: () => this.showUnavailable('המפה אינה זמינה כרגע. אפשר להזין קו רוחב וקו אורך ידנית.'),
    });
  }

  useMyLocation(): void {
    if (!navigator.geolocation) {
      this.message.set('הדפדפן הזה לא משתף מיקום. הזינו קואורדינטות ידנית.');
      return;
    }
    navigator.geolocation.getCurrentPosition(
      (position) => {
        this.message.set('');
        this.coordinatesSelected.emit({ latitude: position.coords.latitude, longitude: position.coords.longitude });
      },
      () => this.message.set('לא התקבלה הרשאת מיקום. בחרו נקודה במפה או הזינו קואורדינטות.'),
      { timeout: 8000 },
    );
  }

  private showUnavailable(text: string): void {
    this.unavailable.set(true);
    this.message.set(text);
  }
}
