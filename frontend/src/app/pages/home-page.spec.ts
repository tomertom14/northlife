import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { SkyState } from '../public/sky-state';
import { HomePage } from './home-page';

describe('HomePage', () => {
  function render(params: Record<string, string>) {
    TestBed.configureTestingModule({
      imports: [HomePage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: ActivatedRoute, useValue: { queryParamMap: of(convertToParamMap(params)) } },
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
    http.verify();
  });

  it("asks for tomorrow's editor's picks when tomorrow is selected", () => {
    const { http } = render({ period: 'tomorrow' });

    http.expectOne((request) => request.url === '/api/events');
    const picks = http.expectOne((request) => request.url === '/api/events/top-picks');
    expect(picks.request.params.get('period')).toBe('tomorrow');
    http.verify();
  });

  it('asks for dates instead of calling the API when a range has none', () => {
    const { fixture, http } = render({ period: 'range' });

    http.expectNone((request) => request.url.startsWith('/api/events'));
    expect(fixture.componentInstance.statusLine()).toContain('בחרו');
    http.verify();
  });

  it('paints the sky for the selected time', () => {
    render({ period: 'tonight' });
    TestBed.tick();

    expect(TestBed.inject(SkyState).name()).toBe('night');
  });
});
