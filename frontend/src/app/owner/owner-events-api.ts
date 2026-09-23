import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { EventCategory } from '../public/public-event.models';

export type OwnerEventStatus = 'Pending' | 'Published' | 'Rejected';

export interface OwnerEvent {
  id: string;
  title: string;
  description: string;
  category: EventCategory;
  venueName: string;
  locality: string;
  address: string;
  latitude: number;
  longitude: number;
  startAt: string;
  endAt: string;
  price: number;
  imageId: string;
  imageUrl: string;
  organizerName: string;
  tags: string[];
  status: OwnerEventStatus;
  rejectionReason?: string;
  updatedAt: string;
  revision: number;
}

export interface OwnerEventInput {
  title: string;
  description: string;
  category: EventCategory;
  venueName: string;
  locality: string;
  address: string;
  latitude: number;
  longitude: number;
  startAt: string;
  endAt: string;
  price: number;
  imageId: string;
  organizerName: string;
  tags: string[];
  revision?: number;
}

@Injectable({ providedIn: 'root' })
export class OwnerEventsApi {
  private readonly baseUrl = '/api/manage/events';
  constructor(private readonly http: HttpClient) {}

  list() {
    return this.http.get<OwnerEvent[]>(this.baseUrl);
  }

  create(input: OwnerEventInput) {
    return this.http.post<OwnerEvent>(this.baseUrl, input);
  }

  update(id: string, input: OwnerEventInput) {
    return this.http.put<OwnerEvent>(`${this.baseUrl}/${id}`, input);
  }

  delete(id: string, revision: number) {
    return this.http.delete<void>(`${this.baseUrl}/${id}`, {
      params: new HttpParams().set('revision', revision),
    });
  }
}
