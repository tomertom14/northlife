import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { OwnerEventStatus } from '../owner/owner-events-api';
import { PlaceCategory, PlaceHours } from '../places/places.models';

export interface AdminPlace {
  id: string;
  ownerId: string;
  ownerName: string;
  businessName: string;
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
  imageUrl: string;
  hours: PlaceHours[];
  status: OwnerEventStatus;
  rejectionReason: string | null;
  updatedAt: string;
  revision: number;
}

@Injectable({ providedIn: 'root' })
export class AdminPlacesApi {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/admin/places';

  list(status?: OwnerEventStatus, search?: string) {
    let params = new HttpParams();
    if (status) params = params.set('status', status);
    if (search?.trim()) params = params.set('search', search.trim());
    return this.http.get<AdminPlace[]>(this.baseUrl, { params });
  }

  approve(place: AdminPlace) {
    return this.http.post<AdminPlace>(`${this.baseUrl}/${place.id}/approve`, { revision: place.revision });
  }

  reject(place: AdminPlace, reason: string) {
    return this.http.post<AdminPlace>(`${this.baseUrl}/${place.id}/reject`, { revision: place.revision, reason });
  }

  delete(place: AdminPlace) {
    return this.http.delete<void>(`${this.baseUrl}/${place.id}`, { params: new HttpParams().set('revision', place.revision) });
  }
}
