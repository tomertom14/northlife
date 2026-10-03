import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { OwnerEventStatus } from './owner-events-api';
import { PlaceCategory, PlaceHours } from '../places/places.models';

export interface OwnerPlace {
  id: string;
  name: string;
  category: PlaceCategory;
  description: string;
  locality: string;
  address: string;
  latitude: number;
  longitude: number;
  phone: string | null;
  website: string | null;
  instagram: string | null;
  studentPerk: string | null;
  imageId: string;
  imageUrl: string;
  hours: PlaceHours[];
  status: OwnerEventStatus;
  rejectionReason?: string | null;
  updatedAt: string;
  revision: number;
}

export interface OwnerPlaceInput {
  name: string;
  category: PlaceCategory;
  description: string;
  locality: string;
  address: string;
  latitude: number;
  longitude: number;
  phone: string | null;
  website: string | null;
  instagram: string | null;
  studentPerk: string | null;
  imageId: string;
  hours: PlaceHours[];
  revision?: number;
}

@Injectable({ providedIn: 'root' })
export class OwnerPlacesApi {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/manage/places';

  list() {
    return this.http.get<OwnerPlace[]>(this.baseUrl);
  }

  create(input: OwnerPlaceInput) {
    return this.http.post<OwnerPlace>(this.baseUrl, input);
  }

  update(id: string, input: OwnerPlaceInput) {
    return this.http.put<OwnerPlace>(`${this.baseUrl}/${id}`, input);
  }

  delete(id: string, revision: number) {
    return this.http.delete<void>(`${this.baseUrl}/${id}`, { params: new HttpParams().set('revision', revision) });
  }
}
