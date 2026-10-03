import { EventCategory, EventSummary } from '../public/public-event.models';

export type PlaceCategory = 'Food' | 'Cafe' | 'Nightlife' | 'Classes' | 'Sports' | 'Culture' | 'Outdoors' | 'Services';

export const PLACE_CATEGORY_LABELS: Record<PlaceCategory, string> = {
  Food: 'מסעדות ואוכל',
  Cafe: 'בתי קפה',
  Nightlife: 'ברים ובילוי',
  Classes: 'חוגים וסדנאות',
  Sports: 'ספורט וכושר',
  Culture: 'תרבות ואמנות',
  Outdoors: 'טבע ואטרקציות',
  Services: 'שירותים מקומיים',
};

export const PLACE_CATEGORIES = Object.keys(PLACE_CATEGORY_LABELS) as PlaceCategory[];

/** The event category whose illustration stands in for a place without a loadable image. */
export const PLACE_COVER_CATEGORY: Record<PlaceCategory, EventCategory> = {
  Food: 'Food',
  Cafe: 'Food',
  Nightlife: 'Nightlife',
  Classes: 'Workshops',
  Sports: 'Sports',
  Culture: 'Culture',
  Outdoors: 'Outdoors',
  Services: 'Other',
};

/** One opening interval: day of week (0 = Sunday) and minutes from midnight, Israel time. */
export interface PlaceHours {
  day: number;
  opens: number;
  closes: number;
}

export interface PlaceOpenStatus {
  hasHours: boolean;
  isOpen: boolean;
  alwaysOpen: boolean;
  nextKind: 'opens' | 'closes' | null;
  nextDay: number | null;
  nextMinute: number | null;
  nextDaysAhead: number | null;
}

export interface PlaceSummary {
  id: string;
  name: string;
  category: PlaceCategory;
  locality: string;
  imageUrl: string;
  studentPerk: string | null;
  open: PlaceOpenStatus;
  distanceKm: number | null;
}

export interface PlaceDetails {
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
  imageUrl: string;
  businessName: string;
  hours: PlaceHours[];
  open: PlaceOpenStatus;
  upcomingEvents: EventSummary[];
}

export interface MapPlace {
  id: string;
  name: string;
  category: PlaceCategory;
  locality: string;
  latitude: number;
  longitude: number;
  isOpen: boolean;
}

export interface MapPlacesResponse {
  items: MapPlace[];
  truncated: boolean;
}

export type PlaceSort = 'name' | 'near';

export interface PlaceQuery {
  category?: PlaceCategory;
  locality?: string;
  openNow: boolean;
  q?: string;
  sort: PlaceSort;
  latitude?: number;
  longitude?: number;
  page: number;
  pageSize: number;
}
