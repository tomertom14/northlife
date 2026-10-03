import { EventSummary } from './public-event.models';
import { addDaysToKey, formatLongDate, formatTime, jerusalemDateKey, relativeDay } from '../shared/jerusalem-time';

export type TimetableState = 'live' | 'soon' | 'upcoming';

export interface TimetableRow {
  event: EventSummary;
  /** Position of the event in the list the timetable was built from, starting at 0. */
  index: number;
  /** "20:00", or "עכשיו" for an event that is running. */
  time: string;
  /** False when the row above shows the same time, so the column reads as a schedule. */
  showTime: boolean;
  note: string;
  state: TimetableState;
}

export interface TimetableGroup {
  key: string;
  /** Day heading; null for the day the feed is about. */
  label: string | null;
  rows: TimetableRow[];
}

const SOON_MINUTES = 60;

/**
 * Groups start-ordered events into days. Running events stay at the top as "now";
 * every later day gets a heading so overnight and multi-day lists stay legible.
 *
 * @param referenceDayKey the Jerusalem day (YYYY-MM-DD) the feed is about; its events need no
 *   heading. Pass null when every day should be labelled (date ranges).
 */
export function buildTimetable(
  events: readonly EventSummary[],
  now: Date,
  referenceDayKey: string | null,
): TimetableGroup[] {
  const groups: TimetableGroup[] = [];
  const todayKey = jerusalemDateKey(now);
  let current: TimetableGroup | null = null;
  let previousTime = '';

  const open = (key: string, label: string | null): TimetableGroup => {
    const group: TimetableGroup = { key, label, rows: [] };
    groups.push(group);
    previousTime = '';
    return group;
  };

  for (const [index, event] of events.entries()) {
    const start = new Date(event.startAt);
    const end = new Date(event.endAt);

    if (start <= now && end > now) {
      current ??= open('now', null);
      current.rows.push({
        event,
        index,
        time: 'עכשיו',
        showTime: previousTime !== 'עכשיו',
        note: `עד ${formatTime(end)}`,
        state: 'live',
      });
      previousTime = 'עכשיו';
      continue;
    }

    const dayKey = jerusalemDateKey(start);
    if (!current || current.key !== dayKey) {
      const label = dayKey === referenceDayKey ? null : dayHeading(start, dayKey, todayKey);
      current = open(dayKey, label);
    }

    const time = formatTime(start);
    const minutes = Math.round((start.getTime() - now.getTime()) / 60_000);
    const soon = minutes > 0 && minutes <= SOON_MINUTES;
    current.rows.push({
      event,
      index,
      time,
      showTime: time !== previousTime,
      note: soon ? `בעוד ${minutes} דק׳` : '',
      state: soon ? 'soon' : 'upcoming',
    });
    previousTime = time;
  }

  return groups;
}

/**
 * One flat list for "hot" and "near me": the order is the ranking, so rows are not grouped by day and
 * each row carries its own day and time, with the distance as a note when there is one.
 */
export function buildRankedList(
  events: readonly EventSummary[],
  now: Date,
  distanceNote: (km: number) => string,
): TimetableGroup[] {
  if (events.length === 0) return [];
  const rows: TimetableRow[] = events.map((event, index) => {
    const start = new Date(event.startAt);
    const end = new Date(event.endAt);
    const live = start <= now && end > now;
    const minutes = Math.round((start.getTime() - now.getTime()) / 60_000);
    const soon = !live && minutes > 0 && minutes <= SOON_MINUTES;
    return {
      event,
      index,
      time: live ? 'עכשיו' : `${relativeDay(start, now)} ${formatTime(start)}`,
      showTime: true,
      note: event.distanceKm != null ? distanceNote(event.distanceKm) : live ? `עד ${formatTime(end)}` : '',
      state: live ? 'live' : soon ? 'soon' : 'upcoming',
    };
  });
  return [{ key: 'ranked', label: null, rows }];
}

function dayHeading(start: Date, dayKey: string, todayKey: string): string {
  const date = formatLongDate(start);
  if (dayKey === todayKey) return `היום, ${date}`;
  if (dayKey === addDaysToKey(todayKey, 1)) return `מחר, ${date}`;
  return date;
}
