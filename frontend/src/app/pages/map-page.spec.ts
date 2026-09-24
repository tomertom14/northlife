import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { LocationPickerHandle, MAP_ADAPTER, MapAdapter, MapEventGroup, MapRenderOptions } from '../maps/map-adapter';
import { PublicConfiguration } from '../public/public-event.models';
import { MapPage } from './map-page';

class FakeMapAdapter implements MapAdapter {
  groups: MapEventGroup[] = [];
  renders: MapRenderOptions[] = [];
  render(_host: HTMLElement, groups: MapEventGroup[], _config: PublicConfiguration, options: MapRenderOptions) {
    this.groups = groups;
    this.renders.push(options);
    return Promise.resolve();
  }
  pick(): Promise<LocationPickerHandle> {
    return Promise.resolve({ moveTo: () => undefined });
  }
}

const events = [
  { id: '1', title: 'One', startAt: '2027-01-01T10:00:00Z', venueName: 'V', locality: 'L', address: 'A', latitude: 33.2, longitude: 35.5, category: 'Culture' },
  { id: '2', title: 'Two', startAt: '2027-01-01T11:00:00Z', venueName: 'V', locality: 'L', address: 'A', latitude: 33.2, longitude: 35.5, category: 'Music' },
];

describe('MapPage', () => {
  function setup() {
    const adapter = new FakeMapAdapter();
    TestBed.configureTestingModule({
      imports: [MapPage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), { provide: MAP_ADAPTER, useValue: adapter }],
    });
    const fixture = TestBed.createComponent(MapPage);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    return { adapter, fixture, http };
  }

  it('groups colocated events before rendering through the adapter', async () => {
    const { adapter, fixture, http } = setup();
    http.expectOne((request) => request.url === '/api/config/public').flush({ googleMapsApiKey: 'key', googleMapsMapId: 'map' });
    http.expectOne((request) => request.url === '/api/events/map').flush({ truncated: false, items: events });
    await fixture.whenStable();

    expect(adapter.groups.length).toBe(1);
    expect(adapter.groups[0].events.length).toBe(2);
    expect(adapter.renders[0].fitToMarkers).toBe(true);
    http.verify();
  });

  it('keeps the list usable and skips the map when Google Maps is not configured', async () => {
    const { adapter, fixture, http } = setup();
    http.expectOne((request) => request.url === '/api/config/public').flush({ googleMapsApiKey: '', googleMapsMapId: '' });
    http.expectOne((request) => request.url === '/api/events/map').flush({ truncated: false, items: events });
    await fixture.whenStable();

    expect(adapter.renders.length).toBe(0);
    expect(fixture.componentInstance.mapState()).toBe('unavailable');
    expect(fixture.nativeElement.querySelectorAll('.rows li').length).toBe(2);
    http.verify();
  });
});
