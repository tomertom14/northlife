import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { OwnerEvent, OwnerEventInput, OwnerEventStatus } from '../owner/owner-events-api';

export interface AdminEvent extends OwnerEvent {
  ownerId: string;
  ownerName: string;
  ownerBusinessName: string;
  isHighlighted: boolean;
}

@Injectable({ providedIn: 'root' })
export class AdminEventsApi {
  private readonly baseUrl = '/api/admin/events';
  constructor(private readonly http: HttpClient) {}

  list(status?: OwnerEventStatus, search?: string) {
    let params = new HttpParams();
    if (status) params = params.set('status', status);
    if (search?.trim()) params = params.set('search', search.trim());
    return this.http.get<AdminEvent[]>(this.baseUrl, { params });
  }

  create(input: OwnerEventInput) { return this.http.post<AdminEvent>(this.baseUrl, input); }
  update(id: string, input: OwnerEventInput) { return this.http.put<AdminEvent>(`${this.baseUrl}/${id}`, input); }
  approve(event: AdminEvent) { return this.http.post<AdminEvent>(`${this.baseUrl}/${event.id}/approve`, { revision: event.revision }); }
  reject(event: AdminEvent, reason: string) { return this.http.post<AdminEvent>(`${this.baseUrl}/${event.id}/reject`, { revision: event.revision, reason }); }
  highlight(event: AdminEvent, isHighlighted: boolean) { return this.http.post<AdminEvent>(`${this.baseUrl}/${event.id}/highlight`, { revision: event.revision, isHighlighted }); }
  delete(event: AdminEvent) {
    return this.http.delete<void>(`${this.baseUrl}/${event.id}`, { params: new HttpParams().set('revision', event.revision) });
  }
}
