import { jerusalemParts } from '../shared/jerusalem-time';
import { PlaceHours, PlaceOpenStatus } from './places.models';

/** Day of week in Israel (0 = Sunday), whatever the device's own time zone. */
export function jerusalemWeekday(date: Date): number {
  const { year, month, day } = jerusalemParts(date);
  return new Date(Date.UTC(year, month - 1, day)).getUTCDay();
}

/** Hebrew day names, Sunday first as in the Israeli week and in JavaScript's getDay(). */
export const DAY_NAMES = ['ראשון', 'שני', 'שלישי', 'רביעי', 'חמישי', 'שישי', 'שבת'];

export const MINUTES_PER_DAY = 24 * 60;

export function formatMinute(minute: number): string {
  const hours = Math.floor(minute / 60) % 24;
  return `${String(hours).padStart(2, '0')}:${String(minute % 60).padStart(2, '0')}`;
}

/** "HH:mm" to minutes from midnight, or null for anything that is not a valid time. */
export function parseMinute(value: string): number | null {
  const match = /^(\d{1,2}):(\d{2})$/.exec(value.trim());
  if (!match) return null;
  const hours = Number(match[1]);
  const minutes = Number(match[2]);
  return hours < 24 && minutes < 60 ? hours * 60 + minutes : null;
}

/** One interval as text; a closing time at or before the opening time runs to the next day. */
export function formatInterval(hours: PlaceHours): string {
  if (hours.opens === 0 && hours.closes === 0) return '24 שעות';
  return `${formatMinute(hours.opens)}–${formatMinute(hours.closes)}`;
}

export interface DayRow {
  day: number;
  name: string;
  text: string;
}

/** A row per day of the week with its intervals, or "סגור". */
export function weekRows(hours: PlaceHours[]): DayRow[] {
  return DAY_NAMES.map((name, day) => {
    const intervals = hours.filter((row) => row.day === day).sort((a, b) => a.opens - b.opens);
    return { day, name, text: intervals.length ? intervals.map(formatInterval).join(', ') : 'סגור' };
  });
}

/** When the next change happens, relative to today: "ב-23:00", "מחר ב-08:00", "ביום שלישי ב-10:00", "בחצות". */
export function whenText(status: PlaceOpenStatus): string {
  const minute = status.nextMinute ?? 0;
  const daysAhead = status.nextDaysAhead ?? 0;
  if (minute === 0 && daysAhead === 1) return 'בחצות';
  const time = `ב-${formatMinute(minute)}`;
  if (daysAhead === 0) return time;
  if (daysAhead === 1) return `מחר ${time}`;
  return `ביום ${DAY_NAMES[status.nextDay ?? 0]} ${time}`;
}

/** The status line shown on cards and place pages. */
export function openStatusText(status: PlaceOpenStatus): string {
  if (!status.hasHours) return 'שעות הפתיחה לא פורסמו';
  if (status.alwaysOpen) return 'פתוח 24 שעות';
  if (status.isOpen) return status.nextKind === 'closes' ? `פתוח עכשיו · נסגר ${whenText(status)}` : 'פתוח עכשיו';
  return status.nextKind === 'opens' ? `סגור עכשיו · נפתח ${whenText(status)}` : 'סגור עכשיו';
}
