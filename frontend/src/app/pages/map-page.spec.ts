import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { MAP_ADAPTER, MapAdapter, MapEventGroup } from '../maps/map-adapter';
import { MapBounds, PublicConfiguration } from '../public/public-event.models';
import { MapPage } from './map-page';

class FakeMapAdapter implements MapAdapter {
  groups: MapEventGroup[] = [];
  render(_host: HTMLElement, groups: MapEventGroup[], _config: PublicConfiguration, _changed: (bounds: MapBounds) => void) {
    this.groups = groups;
    return Promise.resolve();
  }
  pick() { return Promise.resolve(); }
}

describe('MapPage', () => {
  it('groups colocated events before rendering through the adapter', async () => {
    const adapter = new FakeMapAdapter();
    TestBed.configureTestingModule({
      imports: [MapPage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([]), { provide: MAP_ADAPTER, useValue: adapter }],
    });
    const fixture = TestBed.createComponent(MapPage);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    http.expectOne(request => request.url === '/api/config/public').flush({ googleMapsApiKey: '', googleMapsMapId: '' });
    http.expectOne(request => request.url === '/api/events/map').flush({
      truncated: false,
      items: [
        { id: '1', title: 'One', startAt: '2027-01-01T10:00:00Z', venueName: 'V', locality: 'L', address: 'A', latitude: 33.2, longitude: 35.5, category: 'Culture' },
        { id: '2', title: 'Two', startAt: '2027-01-01T11:00:00Z', venueName: 'V', locality: 'L', address: 'A', latitude: 33.2, longitude: 35.5, category: 'Music' },
      ],
    });
    await fixture.whenStable();
    expect(adapter.groups.length).toBe(1);
    expect(adapter.groups[0].events.length).toBe(2);
    http.verify();
  });
});
