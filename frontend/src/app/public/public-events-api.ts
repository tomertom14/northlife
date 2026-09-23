import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { EventDetails, EventSummary, PagedResponse, PublicEventFilters } from './public-event.models';

@Injectable({ providedIn: 'root' })
export class PublicEventsApi {
  private readonly baseUrl = '/api/events';

  constructor(private readonly http: HttpClient) {}

  getEvents(filters: PublicEventFilters) {
    return this.http.get<PagedResponse<EventSummary>>(this.baseUrl, {
      params: this.toParams(filters, true),
    });
  }

  getTopPicks(filters: PublicEventFilters) {
    return this.http.get<EventSummary[]>(`${this.baseUrl}/top-picks`, {
      params: this.toParams(filters, false),
    });
  }

  getEvent(id: string) {
    return this.http.get<EventDetails>(`${this.baseUrl}/${encodeURIComponent(id)}`);
  }

  private toParams(filters: PublicEventFilters, includePage: boolean): HttpParams {
    let params = new HttpParams().set('period', filters.period);

    if (filters.category) params = params.set('category', filters.category);
    if (filters.locality) params = params.set('locality', filters.locality);
    if (filters.maxPrice !== undefined) params = params.set('maxPrice', filters.maxPrice);
    if (filters.period === 'range' && filters.from) params = params.set('from', filters.from);
    if (filters.period === 'range' && filters.to) params = params.set('to', filters.to);

    if (includePage) {
      params = params.set('page', filters.page).set('pageSize', filters.pageSize);
    }

    return params;
  }
}
