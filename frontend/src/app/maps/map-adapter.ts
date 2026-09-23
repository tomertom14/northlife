import { InjectionToken } from '@angular/core';
import { importLibrary, setOptions } from '@googlemaps/js-api-loader';
import { MapBounds, MapEvent, PublicConfiguration } from '../public/public-event.models';

export interface MapEventGroup {
  latitude: number;
  longitude: number;
  events: MapEvent[];
}

export interface MapAdapter {
  render(host: HTMLElement, groups: MapEventGroup[], config: PublicConfiguration, boundsChanged: (bounds: MapBounds) => void): Promise<void>;
  pick(host: HTMLElement, latitude: number, longitude: number, config: PublicConfiguration, selected: (latitude: number, longitude: number) => void): Promise<void>;
}

export class GoogleMapsAdapter implements MapAdapter {
  private configured = false;

  async render(host: HTMLElement, groups: MapEventGroup[], config: PublicConfiguration, boundsChanged: (bounds: MapBounds) => void): Promise<void> {
    if (!config.googleMapsApiKey || !config.googleMapsMapId) throw new Error('Google Maps is not configured.');
    if (!this.configured) {
      setOptions({ key: config.googleMapsApiKey, v: 'weekly', language: 'he', region: 'IL' });
      this.configured = true;
    }
    const [{ Map, InfoWindow, LatLngBounds }, { AdvancedMarkerElement }] = await Promise.all([
      importLibrary('maps'),
      importLibrary('marker'),
    ]);
    const map = new Map(host, { center: { lat: 33.05, lng: 35.35 }, zoom: 9, mapId: config.googleMapsMapId });
    const info = new InfoWindow();
    const fit = new LatLngBounds();
    for (const group of groups) {
      const position = { lat: group.latitude, lng: group.longitude };
      fit.extend(position);
      const marker = new AdvancedMarkerElement({ map, position, title: group.events.length === 1 ? group.events[0].title : `${group.events.length} אירועים` });
      marker.addListener('click', () => {
        const content = document.createElement('div');
        content.className = 'map-popup';
        for (const event of group.events) {
          const link = document.createElement('a');
          link.href = `/events/${encodeURIComponent(event.id)}`;
          link.textContent = event.title;
          content.append(link);
        }
        info.setContent(content);
        info.open({ map, anchor: marker });
      });
    }
    if (groups.length > 0) map.fitBounds(fit, 48);
    map.addListener('idle', () => {
      const bounds = map.getBounds();
      if (!bounds) return;
      const northEast = bounds.getNorthEast();
      const southWest = bounds.getSouthWest();
      boundsChanged({ north: northEast.lat(), east: northEast.lng(), south: southWest.lat(), west: southWest.lng() });
    });
  }

  async pick(host: HTMLElement, latitude: number, longitude: number, config: PublicConfiguration, selected: (latitude: number, longitude: number) => void): Promise<void> {
    if (!config.googleMapsApiKey || !config.googleMapsMapId) throw new Error('Google Maps is not configured.');
    if (!this.configured) {
      setOptions({ key: config.googleMapsApiKey, v: 'weekly', language: 'he', region: 'IL' });
      this.configured = true;
    }
    const [{ Map }, { AdvancedMarkerElement }] = await Promise.all([importLibrary('maps'), importLibrary('marker')]);
    const position = { lat: latitude, lng: longitude };
    const map = new Map(host, { center: position, zoom: 13, mapId: config.googleMapsMapId });
    const marker = new AdvancedMarkerElement({ map, position, title: 'מיקום האירוע' });
    map.addListener('click', (event: { latLng: { lat(): number; lng(): number } | null }) => {
      const point = event.latLng;
      if (!point) return;
      marker.position = point;
      selected(point.lat(), point.lng());
    });
  }
}

export const MAP_ADAPTER = new InjectionToken<MapAdapter>('MapAdapter', {
  providedIn: 'root',
  factory: () => new GoogleMapsAdapter(),
});
