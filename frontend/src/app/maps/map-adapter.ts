/// <reference types="google.maps" />
import { InjectionToken } from '@angular/core';
import { importLibrary, setOptions } from '@googlemaps/js-api-loader';
import { MapBounds, MapEvent, PublicConfiguration } from '../public/public-event.models';

export interface MapEventGroup {
  latitude: number;
  longitude: number;
  events: MapEvent[];
  /** Set for clusters of separate places: clicking zooms to this box instead of listing them. */
  bounds?: MapBounds;
}

export interface MapRenderOptions {
  /** After every pan or zoom, with the new zoom level so clusters can be rebuilt. */
  boundsChanged: (bounds: MapBounds, zoom: number) => void;
  openEvent: (eventId: string) => void;
  /** Fit the viewport to the markers; otherwise the visitor's current viewport is kept. */
  fitToMarkers: boolean;
}

export interface LocationPickerHandle {
  moveTo(latitude: number, longitude: number): void;
}

export interface MapAdapter {
  render(host: HTMLElement, groups: MapEventGroup[], config: PublicConfiguration, options: MapRenderOptions): Promise<void>;
  pick(
    host: HTMLElement,
    latitude: number,
    longitude: number,
    config: PublicConfiguration,
    selected: (latitude: number, longitude: number) => void,
  ): Promise<LocationPickerHandle>;
}

interface EventsMapState {
  host: HTMLElement;
  map: google.maps.Map;
  info: google.maps.InfoWindow;
  markers: google.maps.marker.AdvancedMarkerElement[];
  idle?: google.maps.MapsEventListener;
}

export class GoogleMapsAdapter implements MapAdapter {
  private configured = false;
  private eventsMap?: EventsMapState;

  async render(host: HTMLElement, groups: MapEventGroup[], config: PublicConfiguration, options: MapRenderOptions): Promise<void> {
    const [{ Map, InfoWindow }, { AdvancedMarkerElement }, { LatLngBounds }] = await this.load(config);

    let state = this.eventsMap;
    if (!state || state.host !== host) {
      const map = new Map(host, {
        center: { lat: 33.05, lng: 35.35 },
        zoom: 9,
        mapId: config.googleMapsMapId,
        gestureHandling: 'cooperative',
      });
      state = { host, map, info: new InfoWindow(), markers: [] };
      this.eventsMap = state;
    }

    const { map, info } = state;
    state.idle?.remove();
    info.close();
    for (const marker of state.markers) marker.map = null;
    state.markers = [];

    const fit = new LatLngBounds();
    for (const group of groups) {
      const position = { lat: group.latitude, lng: group.longitude };
      fit.extend(position);
      const spread = group.bounds && (group.bounds.north > group.bounds.south || group.bounds.east > group.bounds.west);
      const marker = new AdvancedMarkerElement({
        map,
        position,
        title: group.events.length === 1 ? group.events[0].title : `${group.events.length} אירועים`,
        content: group.events.length > 1 ? clusterBadge(group.events.length) : undefined,
      });
      marker.addListener('click', () => {
        if (spread && group.bounds) {
          // A cluster of separate places: zoom in until it splits.
          map.fitBounds(
            new LatLngBounds({ lat: group.bounds.south, lng: group.bounds.west }, { lat: group.bounds.north, lng: group.bounds.east }),
            64,
          );
          return;
        }
        info.setContent(popupContent(group, options.openEvent));
        info.open({ map, anchor: marker });
      });
      state.markers.push(marker);
    }

    if (options.fitToMarkers && groups.length > 0) map.fitBounds(fit, 48);
    state.idle = map.addListener('idle', () => {
      const bounds = map.getBounds();
      if (!bounds) return;
      const northEast = bounds.getNorthEast();
      const southWest = bounds.getSouthWest();
      options.boundsChanged(
        { north: northEast.lat(), east: northEast.lng(), south: southWest.lat(), west: southWest.lng() },
        map.getZoom() ?? 9,
      );
    });
  }

  async pick(
    host: HTMLElement,
    latitude: number,
    longitude: number,
    config: PublicConfiguration,
    selected: (latitude: number, longitude: number) => void,
  ): Promise<LocationPickerHandle> {
    const [{ Map }, { AdvancedMarkerElement }] = await this.load(config);
    const position = { lat: latitude, lng: longitude };
    const map = new Map(host, { center: position, zoom: 13, mapId: config.googleMapsMapId, gestureHandling: 'cooperative' });
    const marker = new AdvancedMarkerElement({ map, position, title: 'מיקום האירוע' });
    map.addListener('click', (event: google.maps.MapMouseEvent) => {
      const point = event.latLng;
      if (!point) return;
      marker.position = point;
      selected(point.lat(), point.lng());
    });
    return {
      moveTo(nextLatitude: number, nextLongitude: number) {
        const next = { lat: nextLatitude, lng: nextLongitude };
        marker.position = next;
        map.panTo(next);
      },
    };
  }

  private load(config: PublicConfiguration) {
    if (!config.googleMapsApiKey || !config.googleMapsMapId) {
      return Promise.reject(new Error('Google Maps is not configured.'));
    }
    if (!this.configured) {
      setOptions({ key: config.googleMapsApiKey, v: 'weekly', language: 'he', region: 'IL' });
      this.configured = true;
    }
    return Promise.all([importLibrary('maps'), importLibrary('marker'), importLibrary('core')]);
  }
}

/** Round count badge for a cluster; Google Maps renders it outside Angular, so it is styled inline. */
function clusterBadge(count: number): HTMLElement {
  const size = Math.round(34 + Math.min(22, Math.log2(count) * 6));
  const badge = document.createElement('div');
  badge.textContent = String(count);
  badge.setAttribute('aria-hidden', 'true');
  Object.assign(badge.style, {
    alignItems: 'center',
    background: '#1d2320',
    border: '2px solid #ffffff',
    borderRadius: '50%',
    boxShadow: '0 2px 8px rgba(29, 35, 32, 0.35)',
    color: '#ffffff',
    display: 'flex',
    font: '700 14px "IBM Plex Sans Hebrew", sans-serif',
    height: `${size}px`,
    justifyContent: 'center',
    width: `${size}px`,
  });
  return badge;
}

// Plain DOM for the info window: links keep a real href (new tab works) but open inside the app.
function popupContent(group: MapEventGroup, openEvent: (eventId: string) => void): HTMLElement {
  const content = document.createElement('div');
  content.className = 'map-popup';
  content.dir = 'rtl';
  for (const event of group.events) {
    const link = document.createElement('a');
    link.href = `/events/${encodeURIComponent(event.id)}`;
    link.textContent = event.title;
    link.addEventListener('click', (click) => {
      if (click.ctrlKey || click.metaKey || click.shiftKey || click.button !== 0) return;
      click.preventDefault();
      openEvent(event.id);
    });
    content.append(link);
  }
  return content;
}

export const MAP_ADAPTER = new InjectionToken<MapAdapter>('MapAdapter', {
  providedIn: 'root',
  factory: () => new GoogleMapsAdapter(),
});
