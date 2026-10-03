import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { vi } from 'vitest';
import { niceCeiling } from '../manage/line-chart';
import { AnalyticsService } from './analytics';
import { TrackImpression } from './track-impression';

describe('AnalyticsService', () => {
  let analytics: AnalyticsService;
  let http: HttpTestingController;

  beforeEach(() => {
    localStorage.clear();
    vi.useFakeTimers();
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    analytics = TestBed.inject(AnalyticsService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    http.verify();
    vi.useRealTimers();
  });

  it('keeps one random visitor id per browser', () => {
    const first = analytics.visitorId();
    expect(first).toMatch(/^[0-9a-f-]{36}$/);
    expect(analytics.visitorId()).toBe(first);
    expect(localStorage.getItem('northlife.visitor')).toBe(first);
  });

  it('batches interactions and sends them after a short delay', () => {
    analytics.track('event-1', 'Impression', { source: 'Feed', position: 1 });
    analytics.track('event-2', 'Impression', { source: 'Feed', position: 2 });
    http.expectNone('/api/analytics/events');

    vi.advanceTimersByTime(4000);

    const request = http.expectOne('/api/analytics/events');
    expect(request.request.body.visitorId).toBe(analytics.visitorId());
    expect(request.request.body.interactions).toEqual([
      { eventId: 'event-1', type: 'Impression', source: 'Feed', position: 1 },
      { eventId: 'event-2', type: 'Impression', source: 'Feed', position: 2 },
    ]);
    request.flush({ received: 2, recorded: 2 });
  });

  it('flushes at once when a batch is full', () => {
    for (let index = 0; index < 50; index++) analytics.track(`event-${index}`, 'Impression', { source: 'Feed' });
    const request = http.expectOne('/api/analytics/events');
    expect(request.request.body.interactions.length).toBe(50);
    request.flush({});
  });

  it('forgets the old id on the server and continues with a new one', () => {
    const previous = analytics.visitorId();
    analytics.resetHistory().subscribe();
    const request = http.expectOne('/api/analytics/forget');
    expect(request.request.body).toEqual({ visitorId: previous });
    request.flush(null);
    expect(analytics.visitorId()).not.toBe(previous);
  });
});

@Component({
  imports: [TrackImpression],
  template: `<div [appTrackImpression]="'event-7'" trackSource="Picks" [trackPosition]="3"></div>`,
})
class ImpressionHost {}

describe('TrackImpression', () => {
  let callback: IntersectionObserverCallback | undefined;
  const disconnect = vi.fn();

  beforeEach(() => {
    vi.useFakeTimers();
    callback = undefined;
    disconnect.mockClear();
    vi.stubGlobal(
      'IntersectionObserver',
      class {
        constructor(handler: IntersectionObserverCallback) {
          callback = handler;
        }
        observe() {}
        disconnect() {
          disconnect();
        }
      },
    );
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.useRealTimers();
  });

  const intersect = (isIntersecting: boolean) =>
    callback!([{ isIntersecting } as IntersectionObserverEntry], {} as IntersectionObserver);

  // whenStable() would wait on the faked timers, so render synchronously instead.
  const render = () => {
    TestBed.createComponent(ImpressionHost).detectChanges();
    TestBed.tick();
  };

  it('counts a card seen for a full second, once', () => {
    const track = vi.spyOn(TestBed.inject(AnalyticsService), 'track');
    render();

    intersect(true);
    vi.advanceTimersByTime(999);
    expect(track).not.toHaveBeenCalled();
    vi.advanceTimersByTime(1);

    expect(track).toHaveBeenCalledWith('event-7', 'Impression', { source: 'Picks', position: 3 });
    expect(disconnect).toHaveBeenCalled();
  });

  it('ignores a card scrolled past quickly', () => {
    const track = vi.spyOn(TestBed.inject(AnalyticsService), 'track');
    render();

    intersect(true);
    vi.advanceTimersByTime(400);
    intersect(false);
    vi.advanceTimersByTime(2000);

    expect(track).not.toHaveBeenCalled();
  });
});

describe('niceCeiling', () => {
  it('rounds the axis maximum up to 1, 2, 2.5 or 5 times a power of ten', () => {
    expect(niceCeiling(0)).toBe(4);
    expect(niceCeiling(7)).toBe(10);
    expect(niceCeiling(12)).toBe(20);
    expect(niceCeiling(230)).toBe(250);
    expect(niceCeiling(4100)).toBe(5000);
  });
});
