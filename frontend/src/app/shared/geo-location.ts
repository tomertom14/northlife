import { Injectable, signal } from '@angular/core';

export interface VisitorLocation {
  latitude: number;
  longitude: number;
}

/**
 * The visitor's location for "near me", asked for only when they choose it. Coordinates are rounded
 * to three decimals (about 100 m) before they leave the browser, kept for this visit only and never
 * stored on the server.
 */
@Injectable({ providedIn: 'root' })
export class GeoLocationService {
  readonly location = signal<VisitorLocation | null>(null);

  locate(): Promise<VisitorLocation> {
    const known = this.location();
    if (known) return Promise.resolve(known);
    if (typeof navigator === 'undefined' || !navigator.geolocation) {
      return Promise.reject(new Error('unsupported'));
    }
    return new Promise((resolve, reject) => {
      navigator.geolocation.getCurrentPosition(
        (position) => {
          const location = {
            latitude: Math.round(position.coords.latitude * 1000) / 1000,
            longitude: Math.round(position.coords.longitude * 1000) / 1000,
          };
          this.location.set(location);
          resolve(location);
        },
        (error) => reject(error),
        { timeout: 10_000, maximumAge: 10 * 60_000 },
      );
    });
  }
}
