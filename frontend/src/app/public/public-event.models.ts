export type EventPeriod = 'now' | 'today' | 'tonight' | 'tomorrow' | 'range';

export type EventCategory =
  | 'Music'
  | 'Culture'
  | 'Food'
  | 'Outdoors'
  | 'Workshops'
  | 'Sports'
  | 'Nightlife'
  | 'Other';

export interface PublicEventFilters {
  period: EventPeriod;
  category?: EventCategory;
  locality?: string;
  maxPrice?: number;
  from?: string;
  to?: string;
  page: number;
  pageSize: number;
}

export interface EventSummary {
  id: string;
  title: string;
  startAt: string;
  endAt: string;
  venueName: string;
  locality: string;
  price: number;
  category: EventCategory;
  imageUrl: string;
  isHighlighted: boolean;
}

export interface EventDetails extends EventSummary {
  description: string;
  address: string;
  latitude: number;
  longitude: number;
  organizerName: string;
  tags: string[];
}

export interface PagedResponse<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export const CATEGORY_LABELS: Record<EventCategory, string> = {
  Music: 'מוזיקה',
  Culture: 'תרבות',
  Food: 'אוכל',
  Outdoors: 'טבע וטיולים',
  Workshops: 'סדנאות',
  Sports: 'ספורט',
  Nightlife: 'חיי לילה',
  Other: 'אחר',
};

export const PERIOD_LABELS: Record<EventPeriod, string> = {
  now: 'עכשיו',
  today: 'היום',
  tonight: 'הלילה',
  tomorrow: 'מחר',
  range: 'תאריכים',
};

export const EVENT_CATEGORIES = Object.keys(CATEGORY_LABELS) as EventCategory[];

export function eventImage(category: EventCategory): string {
  if (category === 'Music' || category === 'Nightlife') return '/images/events/music.svg';
  if (category === 'Outdoors' || category === 'Sports') return '/images/events/outdoors.svg';
  return '/images/events/workshop.svg';
}

const dateTime = new Intl.DateTimeFormat('he-IL', {
  weekday: 'long',
  day: 'numeric',
  month: 'long',
  hour: '2-digit',
  minute: '2-digit',
  timeZone: 'Asia/Jerusalem',
});

export function formatEventDate(value: string): string {
  return dateTime.format(new Date(value));
}

export function formatPrice(price: number): string {
  return price === 0 ? 'חינם' : new Intl.NumberFormat('he-IL', {
    style: 'currency',
    currency: 'ILS',
    maximumFractionDigits: 0,
  }).format(price);
}
