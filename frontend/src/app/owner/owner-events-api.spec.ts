import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { OwnerEventInput, OwnerEventsApi } from './owner-events-api';

describe('OwnerEventsApi', () => {
  let api: OwnerEventsApi;
  let http: HttpTestingController;

  const input: OwnerEventInput = {
    title: 'אירוע',
    description: 'תיאור',
    category: 'Culture',
    venueName: 'מקום',
    locality: 'קריית שמונה',
    address: 'הגליל 2',
    latitude: 33.2,
    longitude: 35.5,
    startAt: '2027-01-01T18:00:00.000Z',
    endAt: '2027-01-01T20:00:00.000Z',
    price: 45,
    imageId: '019924c0-0000-7000-a000-000000000001',
    organizerName: 'עסק',
    tags: ['צפון'],
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    api = TestBed.inject(OwnerEventsApi);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('posts a new owner event', () => {
    api.create(input).subscribe();
    const request = http.expectOne('/api/manage/events');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual(input);
    request.flush({});
  });

  it('passes the revision when deleting', () => {
    api.delete('event-1', 4).subscribe();
    const request = http.expectOne('/api/manage/events/event-1?revision=4');
    expect(request.request.method).toBe('DELETE');
    request.flush(null);
  });
});
