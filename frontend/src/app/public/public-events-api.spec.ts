import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { PublicEventsApi } from './public-events-api';

describe('PublicEventsApi', () => {
  let api: PublicEventsApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(PublicEventsApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('serializes shareable feed filters', () => {
    api.getEvents({
      period: 'range',
      category: 'Music',
      locality: 'קריית שמונה',
      maxPrice: 100,
      from: '2026-09-23',
      to: '2026-09-25',
      page: 2,
      pageSize: 12,
    }).subscribe();

    const request = http.expectOne((candidate) => candidate.url === '/api/events');
    expect(request.request.params.get('period')).toBe('range');
    expect(request.request.params.get('category')).toBe('Music');
    expect(request.request.params.get('locality')).toBe('קריית שמונה');
    expect(request.request.params.get('maxPrice')).toBe('100');
    expect(request.request.params.get('from')).toBe('2026-09-23');
    expect(request.request.params.get('to')).toBe('2026-09-25');
    expect(request.request.params.get('page')).toBe('2');
    expect(request.request.params.get('pageSize')).toBe('12');
    request.flush({ items: [], page: 2, pageSize: 12, totalCount: 0 });
  });

  it('keeps pagination out of top-pick requests', () => {
    api.getTopPicks({ period: 'today', page: 4, pageSize: 12 }).subscribe();

    const request = http.expectOne((candidate) => candidate.url === '/api/events/top-picks');
    expect(request.request.params.get('period')).toBe('today');
    expect(request.request.params.has('page')).toBe(false);
    request.flush([]);
  });

  it('loads an encoded event details route', () => {
    api.getEvent('event id').subscribe();

    const request = http.expectOne('/api/events/event%20id');
    expect(request.request.method).toBe('GET');
    request.flush({});
  });
});
