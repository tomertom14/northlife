import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { SkyState } from '../public/sky-state';
import { GeoLocationService } from '../shared/geo-location';
import { HomePage } from './home-page';

describe('HomePage', () => {
  function render(params: Record<string, string>, location?: { latitude: number; longitude: number }) {
    const geo = { location: signal(location ?? null), locate: () => (location ? Promise.resolve(location) : Promise.reject(new Error('denied'))) };
    TestBed.configureTestingModule({
      imports: [HomePage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { queryParamMap: of(convertToParamMap(params)) } },
        { provide: GeoLocationService, useValue: geo },
      ],
    });
    const fixture = TestBed.createComponent(HomePage);
    fixture.detectChanges();
    return { fixture, http: TestBed.inject(HttpTestingController) };
  }

  it("loads today's feed and editor's picks by default", () => {
    const { http } = render({});

    const feed = http.expectOne((request) => request.url === '/api/events');
    expect(feed.request.params.get('period')).toBe('today');
    http.expectOne((request) => request.url === '/api/events/top-picks');
    http.expectOne((request) => request.url === '/api/recommendations');
    http.verify();
  });

  it("asks for tomorrow's editor's picks when tomorrow is selected", () => {
    const { http } = render({ period: 'tomorrow' });

    http.expectOne((request) => request.url === '/api/events');
    const picks = http.expectOne((request) => request.url === '/api/events/top-picks');
    expect(picks.request.params.get('period')).toBe('tomorrow');
    const recommendations = http.expectOne((request) => request.url === '/api/recommendations');
    expect(recommendations.request.params.get('period')).toBe('tomorrow');
    expect(recommendations.request.params.get('visitorId')).toMatch(/^[0-9a-f-]{36}$/);
    http.verify();
  });

  it('asks for dates instead of calling the API when a range has none', () => {
    const { fixture, http } = render({ period: 'range' });

    http.expectNone((request) => request.url.startsWith('/api/events'));
    expect(fixture.componentInstance.statusLine()).toContain('בחרו');
    http.verify();
  });

  it('asks for the hot ranking without a location until the visitor shares one', () => {
    const { http } = render({ sort: 'hot' });

    const feed = http.expectOne((request) => request.url === '/api/events');
    expect(feed.request.params.get('sort')).toBe('hot');
    expect(feed.request.params.has('latitude')).toBe(false);
    http.expectOne((request) => request.url === '/api/events/top-picks');
    http.expectOne((request) => request.url === '/api/recommendations');
    http.verify();
  });

  it('sends the rounded location with a near-me link once it is known', () => {
    const { http } = render({ sort: 'near' }, { latitude: 33.207, longitude: 35.57 });

    const feed = http.expectOne((request) => request.url === '/api/events');
    expect(feed.request.params.get('sort')).toBe('near');
    expect(feed.request.params.get('latitude')).toBe('33.207');
    expect(feed.request.params.get('longitude')).toBe('35.57');
    http.expectOne((request) => request.url === '/api/events/top-picks');
    http.expectOne((request) => request.url === '/api/recommendations');
    http.verify();
  });

  it('shows the "for you" rail only for a visitor with history', () => {
    const { fixture, http } = render({});
    http.expectOne((request) => request.url === '/api/events').flush({ items: [], page: 1, pageSize: 12, totalCount: 0 });
    http.expectOne((request) => request.url === '/api/events/top-picks').flush([]);
    const item = {
      event: { id: 'e1', title: 'ערב ג׳אז', startAt: '2027-01-01T18:00:00Z', endAt: '2027-01-01T20:00:00Z', venueName: 'V', locality: 'צפת', price: 0, category: 'Music', imageUrl: '/x', isHighlighted: false },
      reason: 'similar',
      becauseOfEventId: 'e0',
      becauseOfTitle: 'הופעה אקוסטית',
    };
    http.expectOne((request) => request.url === '/api/recommendations').flush({ personalised: true, items: [item] });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('בשבילך');
    expect(fixture.nativeElement.textContent).toContain('כי פתחתם את ״הופעה אקוסטית״');
    http.verify();
  });

  it('hides the rail when the list is only popular filler', () => {
    const { fixture, http } = render({});
    http.expectOne((request) => request.url === '/api/events').flush({ items: [], page: 1, pageSize: 12, totalCount: 0 });
    http.expectOne((request) => request.url === '/api/events/top-picks').flush([]);
    http.expectOne((request) => request.url === '/api/recommendations').flush({ personalised: false, items: [] });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).not.toContain('בשבילך');
  });

  it('paints the sky for the selected time', () => {
    render({ period: 'tonight' });
    TestBed.tick();

    expect(TestBed.inject(SkyState).name()).toBe('night');
  });
});
