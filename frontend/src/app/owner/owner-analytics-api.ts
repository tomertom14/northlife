import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { OwnerEventStatus } from './owner-events-api';

export type AnalyticsRange = 7 | 30 | 90;

export interface AnalyticsTotals {
  impressions: number;
  detailViews: number;
  navigations: number;
  shares: number;
  uniqueVisitors: number;
  clickThroughRate: number | null;
}

export interface AnalyticsDay {
  day: string;
  impressions: number;
  detailViews: number;
  navigations: number;
  shares: number;
  uniqueVisitors: number;
}

export interface EventAnalytics {
  id: string;
  title: string;
  status: OwnerEventStatus;
  startAt: string;
  impressions: number;
  detailViews: number;
  navigations: number;
  shares: number;
  uniqueVisitors: number;
  clickThroughRate: number | null;
  trending: boolean;
  trendScore: number | null;
}

export interface OwnerAnalytics {
  from: string;
  to: string;
  totals: AnalyticsTotals;
  daily: AnalyticsDay[];
  events: EventAnalytics[];
}

export interface TrafficAnomaly {
  eventId: string;
  title: string;
  businessName: string;
  observed: number;
  baseline: number;
  zScore: number;
  kind: 'surge' | 'suspicious';
}

@Injectable({ providedIn: 'root' })
export class OwnerAnalyticsApi {
  private readonly http = inject(HttpClient);

  get(days: AnalyticsRange) {
    return this.http.get<OwnerAnalytics>('/api/manage/analytics', { params: new HttpParams().set('days', days) });
  }

  anomalies() {
    return this.http.get<TrafficAnomaly[]>('/api/admin/analytics/anomalies');
  }
}
