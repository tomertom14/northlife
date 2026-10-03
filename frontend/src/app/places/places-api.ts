import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { MapBounds, PagedResponse } from '../public/public-event.models';
import { MapPlacesResponse, PlaceCategory, PlaceDetails, PlaceQuery, PlaceSummary } from './places.models';

@Injectable({ providedIn: 'root' })
export class PlacesApi {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/places';

  list(query: PlaceQuery) {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    if (query.sort !== 'name') params = params.set('sort', query.sort);
    if (query.category) params = params.set('category', query.category);
    if (query.locality) params = params.set('locality', query.locality);
    if (query.openNow) params = params.set('openNow', true);
    if (query.q) params = params.set('q', query.q);
    if (query.latitude !== undefined && query.longitude !== undefined) {
      params = params.set('latitude', query.latitude).set('longitude', query.longitude);
    }
    return this.http.get<PagedResponse<PlaceSummary>>(this.baseUrl, { params });
  }

  get(id: string) {
    return this.http.get<PlaceDetails>(`${this.baseUrl}/${encodeURIComponent(id)}`);
  }

  map(bounds: MapBounds, category?: PlaceCategory, openNow = false) {
    let params = new HttpParams()
      .set('north', bounds.north).set('south', bounds.south)
      .set('east', bounds.east).set('west', bounds.west);
    if (category) params = params.set('category', category);
    if (openNow) params = params.set('openNow', true);
    return this.http.get<MapPlacesResponse>(`${this.baseUrl}/map`, { params });
  }
}
