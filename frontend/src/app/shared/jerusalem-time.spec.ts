import {
  addDaysToKey,
  formatTime,
  fromJerusalemInput,
  jerusalemDateKey,
  relativeDay,
  toJerusalemInput,
} from './jerusalem-time';

describe('Jerusalem time helpers', () => {
  it('reads datetime-local values as Israel time in summer and winter', () => {
    expect(fromJerusalemInput('2026-09-23T20:30')?.toISOString()).toBe('2026-09-23T17:30:00.000Z');
    expect(fromJerusalemInput('2026-01-15T20:30')?.toISOString()).toBe('2026-01-15T18:30:00.000Z');
  });

  it('rejects malformed values and times skipped by the daylight-saving change', () => {
    expect(fromJerusalemInput('')).toBeNull();
    expect(fromJerusalemInput('2026-09-23')).toBeNull();
    // Clocks jump from 02:00 to 03:00 on Friday 27 March 2026.
    expect(fromJerusalemInput('2026-03-27T02:30')).toBeNull();
  });

  it('round-trips an ISO timestamp through the datetime-local format', () => {
    expect(toJerusalemInput('2026-09-23T17:30:00Z')).toBe('2026-09-23T20:30');
    expect(toJerusalemInput(fromJerusalemInput('2026-12-01T09:05')!)).toBe('2026-12-01T09:05');
  });

  it('formats times and calendar days in Jerusalem regardless of the device zone', () => {
    expect(formatTime('2026-09-23T21:30:00Z')).toBe('00:30');
    expect(jerusalemDateKey('2026-09-23T21:30:00Z')).toBe('2026-09-24');
    expect(addDaysToKey('2026-12-31', 1)).toBe('2027-01-01');
  });

  it('names today and tomorrow relative to now', () => {
    const now = new Date('2026-09-23T18:00:00Z');
    expect(relativeDay('2026-09-23T19:00:00Z', now)).toBe('היום');
    expect(relativeDay('2026-09-24T06:00:00Z', now)).toBe('מחר');
  });
});
