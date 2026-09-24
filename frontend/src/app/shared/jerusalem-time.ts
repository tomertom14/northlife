// All event dates are selected and displayed in Asia/Jerusalem, whatever the viewer's device clock says.
const TIME_ZONE = 'Asia/Jerusalem';

const partsFormatter = new Intl.DateTimeFormat('en-US', {
  timeZone: TIME_ZONE,
  year: 'numeric',
  month: '2-digit',
  day: '2-digit',
  hour: '2-digit',
  minute: '2-digit',
  second: '2-digit',
  hourCycle: 'h23',
});
const timeFormatter = new Intl.DateTimeFormat('he-IL', {
  timeZone: TIME_ZONE,
  hour: '2-digit',
  minute: '2-digit',
  hourCycle: 'h23',
});
const longDateFormatter = new Intl.DateTimeFormat('he-IL', {
  timeZone: TIME_ZONE,
  weekday: 'long',
  day: 'numeric',
  month: 'long',
});
const shortDateFormatter = new Intl.DateTimeFormat('he-IL', {
  timeZone: TIME_ZONE,
  day: 'numeric',
  month: 'long',
});
const weekdayFormatter = new Intl.DateTimeFormat('he-IL', { timeZone: TIME_ZONE, weekday: 'long' });

export interface LocalParts {
  year: number;
  month: number;
  day: number;
  hour: number;
  minute: number;
  second: number;
}

const toDate = (value: string | Date): Date => (value instanceof Date ? value : new Date(value));
const pad = (value: number): string => String(value).padStart(2, '0');

export function jerusalemParts(date: Date): LocalParts {
  const values: Record<string, number> = {};
  for (const part of partsFormatter.formatToParts(date)) {
    if (part.type !== 'literal') values[part.type] = Number(part.value);
  }
  return {
    year: values['year'],
    month: values['month'],
    day: values['day'],
    hour: values['hour'] % 24,
    minute: values['minute'],
    second: values['second'],
  };
}

export function jerusalemHour(date: Date): number {
  return jerusalemParts(date).hour;
}

/** Calendar day in Jerusalem as YYYY-MM-DD. */
export function jerusalemDateKey(value: string | Date): string {
  const parts = jerusalemParts(toDate(value));
  return `${parts.year}-${pad(parts.month)}-${pad(parts.day)}`;
}

export function addDaysToKey(key: string, days: number): string {
  const [year, month, day] = key.split('-').map(Number);
  const shifted = new Date(Date.UTC(year, month - 1, day + days));
  return `${shifted.getUTCFullYear()}-${pad(shifted.getUTCMonth() + 1)}-${pad(shifted.getUTCDate())}`;
}

export function formatTime(value: string | Date): string {
  return timeFormatter.format(toDate(value));
}

/** "יום רביעי, 23 בספטמבר" */
export function formatLongDate(value: string | Date): string {
  return longDateFormatter.format(toDate(value));
}

/** "23 בספטמבר" */
export function formatShortDate(value: string | Date): string {
  return shortDateFormatter.format(toDate(value));
}

// Noon UTC falls on the same calendar day in Jerusalem, so a YYYY-MM-DD key never shifts a day.
const keyToNoon = (key: string): Date => {
  const [year, month, day] = key.split('-').map(Number);
  return new Date(Date.UTC(year, month - 1, day, 12));
};

/** "24 בספטמבר" for a YYYY-MM-DD key (as used by date inputs). */
export function formatDateKey(key: string): string {
  return shortDateFormatter.format(keyToNoon(key));
}

/** "יום חמישי, 24 בספטמבר" for a YYYY-MM-DD key. */
export function formatLongDateKey(key: string): string {
  return longDateFormatter.format(keyToNoon(key));
}

/** "היום", "מחר" or the weekday name, relative to now in Jerusalem. */
export function relativeDay(value: string | Date, now: Date): string {
  const key = jerusalemDateKey(value);
  const today = jerusalemDateKey(now);
  if (key === today) return 'היום';
  if (key === addDaysToKey(today, 1)) return 'מחר';
  return weekdayFormatter.format(toDate(value));
}

function offsetMinutes(date: Date): number {
  const parts = jerusalemParts(date);
  const wallClock = Date.UTC(parts.year, parts.month - 1, parts.day, parts.hour, parts.minute, parts.second);
  return Math.round((wallClock - date.getTime()) / 60_000);
}

/** ISO timestamp -> value for <input type="datetime-local">, in Jerusalem time. */
export function toJerusalemInput(value: string | Date): string {
  const parts = jerusalemParts(toDate(value));
  return `${parts.year}-${pad(parts.month)}-${pad(parts.day)}T${pad(parts.hour)}:${pad(parts.minute)}`;
}

/**
 * <input type="datetime-local"> value read as Jerusalem wall-clock time.
 * Returns null for malformed values and for times skipped by the daylight-saving change.
 */
export function fromJerusalemInput(value: string): Date | null {
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2})$/.exec(value);
  if (!match) return null;
  const [year, month, day, hour, minute] = match.slice(1).map(Number);
  const wallClock = Date.UTC(year, month - 1, day, hour, minute);
  let utc = wallClock - offsetMinutes(new Date(wallClock)) * 60_000;
  utc = wallClock - offsetMinutes(new Date(utc)) * 60_000;

  const check = jerusalemParts(new Date(utc));
  const matches =
    check.year === year && check.month === month && check.day === day &&
    check.hour === hour && check.minute === minute;
  return matches ? new Date(utc) : null;
}
