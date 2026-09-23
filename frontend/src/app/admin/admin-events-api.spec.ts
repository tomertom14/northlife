import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AdminEvent, AdminEventsApi } from './admin-events-api';

describe('AdminEventsApi', () => {
  let api: AdminEventsApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(AdminEventsApi);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('filters the moderation queue', () => {
    api.list('Pending', 'galil').subscribe();
    const request = http.expectOne('/api/admin/events?status=Pending&search=galil');
    expect(request.request.method).toBe('GET');
    request.flush([]);
  });

  it('sends the current revision when approving', () => {
    api.approve({ id: 'event-1', revision: 7 } as AdminEvent).subscribe();
    const request = http.expectOne('/api/admin/events/event-1/approve');
    expect(request.request.body).toEqual({ revision: 7 });
    request.flush({});
  });
});
