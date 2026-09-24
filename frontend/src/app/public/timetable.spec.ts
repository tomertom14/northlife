import { EventSummary } from './public-event.models';
import { buildTimetable } from './timetable';

function event(id: string, startAt: string, endAt: string): EventSummary {
  return {
    id,
    title: id,
    startAt,
    endAt,
    venueName: 'מקום',
    locality: 'צפת',
    price: 0,
    category: 'Music',
    imageUrl: `/api/images/${id}`,
    isHighlighted: false,
  };
}

// 21:00 in Jerusalem on Wednesday 23 September 2026.
const now = new Date('2026-09-23T18:00:00Z');

describe('buildTimetable', () => {
  const events = [
    event('live', '2026-09-23T17:00:00Z', '2026-09-23T20:00:00Z'),
    event('soon', '2026-09-23T18:25:00Z', '2026-09-23T20:00:00Z'),
    event('ten-a', '2026-09-23T19:00:00Z', '2026-09-23T22:00:00Z'),
    event('ten-b', '2026-09-23T19:00:00Z', '2026-09-23T23:00:00Z'),
    event('sunrise', '2026-09-24T02:15:00Z', '2026-09-24T06:00:00Z'),
  ];

  it('keeps running events at the top as "now" with their end time', () => {
    const [nowGroup] = buildTimetable(events, now, '2026-09-23');

    expect(nowGroup.label).toBeNull();
    expect(nowGroup.rows.map((row) => row.event.id)).toEqual(['live']);
    expect(nowGroup.rows[0]).toMatchObject({ time: 'עכשיו', note: 'עד 23:00', state: 'live' });
  });

  it('marks events starting within the hour and shows a shared start time once', () => {
    const today = buildTimetable(events, now, '2026-09-23')[1];

    expect(today.label).toBeNull();
    expect(today.rows[0]).toMatchObject({ time: '21:25', note: 'בעוד 25 דק׳', state: 'soon' });
    expect(today.rows.slice(1).map((row) => [row.time, row.showTime])).toEqual([
      ['22:00', true],
      ['22:00', false],
    ]);
  });

  it('labels a later day so an overnight list stays legible', () => {
    const groups = buildTimetable(events, now, '2026-09-23');
    const tomorrow = groups[2];

    expect(groups.length).toBe(3);
    expect(tomorrow.key).toBe('2026-09-24');
    expect(tomorrow.label).toContain('מחר');
    expect(tomorrow.rows[0].time).toBe('05:15');
  });

  it('labels every day when the feed is a date range', () => {
    const groups = buildTimetable([event('later', '2026-09-26T09:00:00Z', '2026-09-26T12:00:00Z')], now, null);

    expect(groups[0].label).toContain('26');
  });
});
