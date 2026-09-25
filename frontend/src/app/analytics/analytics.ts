import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';

export type InteractionType = 'Impression' | 'DetailView' | 'Navigate' | 'Share';
export type InteractionSource = 'Feed' | 'Picks' | 'Map' | 'Details' | 'Direct' | 'Recommendations' | 'Similar';

/** Where a click came from; travels to the event page in the router state. */
export interface TrackContext {
  source: InteractionSource;
  /** 1-based position in the list the visitor saw. */
  position?: number;
}

interface PendingInteraction {
  eventId: string;
  type: InteractionType;
  source: InteractionSource;
  position?: number;
}

const VISITOR_KEY = 'northlife.visitor';
const ENDPOINT = '/api/analytics/events';
const FLUSH_DELAY_MS = 4000;
const MAX_BATCH = 50;

/**
 * First-party, anonymous interaction tracking. The browser keeps a random visitor id; no account,
 * name or address is sent, and the session token is never attached. Interactions are queued and
 * sent in batches, with sendBeacon when the tab is hidden so the last ones are not lost.
 * Browsers that send Global Privacy Control are not tracked.
 */
@Injectable({ providedIn: 'root' })
export class AnalyticsService {
  private readonly http = inject(HttpClient);
  private queue: PendingInteraction[] = [];
  private timer?: ReturnType<typeof setTimeout>;
  private memoryVisitor: string | null = null;

  readonly enabled =
    typeof navigator !== 'undefined' &&
    (navigator as Navigator & { globalPrivacyControl?: boolean }).globalPrivacyControl !== true;

  constructor() {
    if (typeof document === 'undefined') return;
    document.addEventListener('visibilitychange', () => {
      if (document.visibilityState === 'hidden') this.flush(true);
    });
    window.addEventListener('pagehide', () => this.flush(true));
  }

  track(eventId: string, type: InteractionType, context: TrackContext): void {
    if (!this.enabled || !eventId) return;
    this.queue.push({ eventId, type, source: context.source, position: context.position });
    if (this.queue.length >= MAX_BATCH) this.flush();
    else this.timer ??= setTimeout(() => this.flush(), FLUSH_DELAY_MS);
  }

  flush(useBeacon = false): void {
    clearTimeout(this.timer);
    this.timer = undefined;
    if (this.queue.length === 0) return;
    const visitorId = this.visitorId();
    while (this.queue.length > 0) {
      const interactions = this.queue.splice(0, MAX_BATCH);
      const body = { visitorId, interactions };
      const beaconSent =
        useBeacon &&
        typeof navigator.sendBeacon === 'function' &&
        navigator.sendBeacon(ENDPOINT, new Blob([JSON.stringify(body)], { type: 'application/json' }));
      if (!beaconSent) this.http.post(ENDPOINT, body).subscribe({ error: () => undefined });
    }
  }

  /** Deletes this browser's history on the server and continues under a new anonymous id. */
  resetHistory() {
    const previous = this.visitorId();
    this.queue = [];
    this.storeVisitor(crypto.randomUUID());
    return this.http.post<void>('/api/analytics/forget', { visitorId: previous });
  }

  visitorId(): string {
    try {
      const stored = localStorage.getItem(VISITOR_KEY);
      if (stored) return stored;
    } catch {
      // Storage is blocked (private mode, strict settings): keep an id for this page view only.
    }
    const created = this.memoryVisitor ?? crypto.randomUUID();
    this.storeVisitor(created);
    return created;
  }

  private storeVisitor(id: string): void {
    this.memoryVisitor = id;
    try {
      localStorage.setItem(VISITOR_KEY, id);
    } catch {
      // See visitorId().
    }
  }
}
