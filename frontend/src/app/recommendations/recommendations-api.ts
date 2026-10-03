import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { EventSummary, PublicEventFilters } from '../public/public-event.models';

export interface RecommendationItem {
  event: EventSummary;
  /** "similar" (because of an event the visitor viewed) or "popular" (filler, or no history yet). */
  reason: 'similar' | 'popular';
  becauseOfEventId: string | null;
  becauseOfTitle: string | null;
}

export interface Recommendations {
  personalised: boolean;
  items: RecommendationItem[];
}

@Injectable({ providedIn: 'root' })
export class RecommendationsApi {
  private readonly http = inject(HttpClient);

  /** Picks for this browser's anonymous visitor id, within the feed's current period and filters. */
  forYou(visitorId: string | null, filters: PublicEventFilters, limit = 10) {
    let params = new HttpParams().set('limit', limit).set('period', filters.period);
    if (visitorId) params = params.set('visitorId', visitorId);
    if (filters.category) params = params.set('category', filters.category);
    if (filters.locality) params = params.set('locality', filters.locality);
    if (filters.maxPrice !== undefined) params = params.set('maxPrice', filters.maxPrice);
    if (filters.period === 'range' && filters.from) params = params.set('from', filters.from);
    if (filters.period === 'range' && filters.to) params = params.set('to', filters.to);
    return this.http.get<Recommendations>('/api/recommendations', { params });
  }

  similar(eventId: string, limit = 6) {
    return this.http.get<EventSummary[]>(`/api/events/${encodeURIComponent(eventId)}/similar`, {
      params: new HttpParams().set('limit', limit),
    });
  }
}
