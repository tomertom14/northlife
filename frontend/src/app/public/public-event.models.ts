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

export interface MapEvent {
  id: string;
  title: string;
  startAt: string;
  venueName: string;
  locality: string;
  address: string;
  latitude: number;
  longitude: number;
  category: EventCategory;
}

export interface MapEventsResponse {
  items: MapEvent[];
  truncated: boolean;
}

export interface MapBounds {
  north: number;
  south: number;
  east: number;
  west: number;
}

export interface PublicConfiguration {
  googleMapsApiKey: string;
  googleMapsMapId: string;
}

export const CATEGORY_LABELS: Record<EventCategory, string> = {
  Music: 'מוזיקה',
  Nightlife: 'חיי לילה',
  Food: 'אוכל',
  Outdoors: 'טבע וטיולים',
  Culture: 'תרבות',
  Workshops: 'סדנאות',
  Sports: 'ספורט',
  Other: 'אחר',
};

/** Identity colours for category marks (never for text); equal OKLCH lightness and chroma. */
export const CATEGORY_COLORS: Record<EventCategory, string> = {
  Music: '#835bae',
  Nightlife: '#516cbd',
  Food: '#ae5528',
  Outdoors: '#2f8543',
  Culture: '#aa4d75',
  Workshops: '#996700',
  Sports: '#007faa',
  Other: '#6a746e',
};

/** Northern localities offered as suggestions; the API matches locality names exactly. */
export const NORTHERN_LOCALITIES = [
  'קריית שמונה',
  'צפת',
  'טבריה',
  'ראש פינה',
  'קצרין',
  'כרמיאל',
  'נהריה',
  'עכו',
  'מטולה',
  'מעלות־תרשיחא',
  'תל חי',
  'עפולה',
  'בית שאן',
  'נצרת',
];

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

const wholeShekels = new Intl.NumberFormat('he-IL', { style: 'currency', currency: 'ILS', maximumFractionDigits: 0 });
const shekelsAndAgorot = new Intl.NumberFormat('he-IL', { style: 'currency', currency: 'ILS', minimumFractionDigits: 2 });

export function formatPrice(price: number): string {
  if (price === 0) return 'חינם';
  return (Number.isInteger(price) ? wholeShekels : shekelsAndAgorot).format(price);
}
