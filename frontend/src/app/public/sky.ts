import { EventPeriod } from './public-event.models';
import { jerusalemHour } from '../shared/jerusalem-time';

export type SkyName = 'dawn' | 'day' | 'golden' | 'night' | 'calendar';

export interface SkyTheme {
  name: SkyName;
  background: string;
  ridgeFar: string;
  ridgeNear: string;
  snow: string;
  /** Text on the sky; 7:1 or better against background. */
  ink: string;
  /** Secondary text on the sky; 4.5:1 or better against background. */
  soft: string;
  disc: string;
}

export const SKIES: Record<SkyName, SkyTheme> = {
  night: {
    name: 'night',
    background: '#1b2440',
    ridgeFar: '#27335a',
    ridgeNear: '#121a30',
    snow: '#8e97b5',
    ink: '#f2f4f3',
    soft: '#9aa3bf',
    disc: '#f3e9c6',
  },
  day: {
    name: 'day',
    background: '#a9d6ef',
    ridgeFar: '#86b79b',
    ridgeNear: '#4e7d64',
    snow: '#f2f4f3',
    ink: '#1d2320',
    soft: '#3f5563',
    disc: '#ffd45c',
  },
  golden: {
    name: 'golden',
    background: '#f4a259',
    ridgeFar: '#c97a52',
    ridgeNear: '#7d4b3b',
    snow: '#f8d9b0',
    ink: '#1d2320',
    soft: '#5a3a2a',
    disc: '#ffe08a',
  },
  dawn: {
    name: 'dawn',
    background: '#f6c9b1',
    ridgeFar: '#d99d8f',
    ridgeNear: '#8d6a75',
    snow: '#fbe3d6',
    ink: '#1d2320',
    soft: '#6a4e55',
    disc: '#fff1d6',
  },
  calendar: {
    name: 'calendar',
    background: '#dde5e0',
    ridgeFar: '#c4cfc8',
    ridgeNear: '#a6b4ab',
    snow: '#e9eeeb',
    ink: '#1d2320',
    soft: '#56605b',
    disc: 'transparent',
  },
};

/** The real sky over the Galilee at a given Jerusalem hour. */
export function skyForHour(hour: number): SkyName {
  if (hour >= 5 && hour < 7) return 'dawn';
  if (hour >= 7 && hour < 16) return 'day';
  if (hour >= 16 && hour < 19) return 'golden';
  return 'night';
}

/** The selected time filter is encoded as the colour of the sky. */
export function skyForPeriod(period: EventPeriod, now: Date): SkyName {
  switch (period) {
    case 'now':
      return skyForHour(jerusalemHour(now));
    case 'today':
      return 'day';
    case 'tonight':
      return 'night';
    case 'tomorrow':
      return 'dawn';
    case 'range':
      return 'calendar';
  }
}
