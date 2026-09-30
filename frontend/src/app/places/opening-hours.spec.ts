import { formatInterval, openStatusText, parseMinute, weekRows } from './opening-hours';
import { PlaceOpenStatus } from './places.models';

const closed: PlaceOpenStatus = {
  hasHours: true,
  isOpen: false,
  alwaysOpen: false,
  nextKind: null,
  nextDay: null,
  nextMinute: null,
  nextDaysAhead: null,
};

describe('opening hours', () => {
  it('parses and formats times, rejecting impossible ones', () => {
    expect(parseMinute('08:30')).toBe(510);
    expect(parseMinute('8:05')).toBe(485);
    expect(parseMinute('24:00')).toBeNull();
    expect(parseMinute('12:60')).toBeNull();
    expect(parseMinute('noon')).toBeNull();
    expect(formatInterval({ day: 4, opens: 20 * 60, closes: 2 * 60 })).toBe('20:00–02:00');
    expect(formatInterval({ day: 0, opens: 0, closes: 0 })).toBe('24 שעות');
  });

  it('lists every day of the week, marking days without intervals as closed', () => {
    const rows = weekRows([
      { day: 1, opens: 16 * 60, closes: 20 * 60 },
      { day: 1, opens: 10 * 60, closes: 13 * 60 },
    ]);

    expect(rows).toHaveLength(7);
    expect(rows[1]).toEqual({ day: 1, name: 'שני', text: '10:00–13:00, 16:00–20:00' });
    expect(rows[6].text).toBe('סגור');
  });

  it('describes the next change relative to today', () => {
    expect(openStatusText({ ...closed, hasHours: false })).toBe('שעות הפתיחה לא פורסמו');
    expect(openStatusText({ ...closed, isOpen: true, alwaysOpen: true })).toBe('פתוח 24 שעות');
    expect(openStatusText({ ...closed, isOpen: true, nextKind: 'closes', nextDay: 4, nextMinute: 23 * 60, nextDaysAhead: 0 })).toBe(
      'פתוח עכשיו · נסגר ב-23:00',
    );
    expect(openStatusText({ ...closed, isOpen: true, nextKind: 'closes', nextDay: 5, nextMinute: 0, nextDaysAhead: 1 })).toBe(
      'פתוח עכשיו · נסגר בחצות',
    );
    expect(openStatusText({ ...closed, nextKind: 'opens', nextDay: 2, nextMinute: 8 * 60, nextDaysAhead: 1 })).toBe(
      'סגור עכשיו · נפתח מחר ב-08:00',
    );
    expect(openStatusText({ ...closed, nextKind: 'opens', nextDay: 2, nextMinute: 10 * 60, nextDaysAhead: 3 })).toBe(
      'סגור עכשיו · נפתח ביום שלישי ב-10:00',
    );
  });
});
