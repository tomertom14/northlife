import { SKIES, skyForHour, skyForPeriod } from './sky';

describe('sky themes', () => {
  it('follows the Galilee day from dawn to night', () => {
    expect([4, 5, 7, 15, 16, 19, 23].map(skyForHour)).toEqual(['night', 'dawn', 'day', 'day', 'golden', 'night', 'night']);
  });

  it('encodes the selected time filter as the sky', () => {
    const evening = new Date('2026-09-23T18:00:00Z'); // 21:00 in Jerusalem
    const noon = new Date('2026-09-23T09:00:00Z'); // 12:00 in Jerusalem

    expect(skyForPeriod('now', evening)).toBe('night');
    expect(skyForPeriod('now', noon)).toBe('day');
    expect(skyForPeriod('tonight', noon)).toBe('night');
    expect(skyForPeriod('tomorrow', noon)).toBe('dawn');
    expect(skyForPeriod('range', noon)).toBe('calendar');
  });

  it('defines every sky with its own text colours', () => {
    for (const sky of Object.values(SKIES)) {
      expect(sky.ink).toMatch(/^#[0-9a-f]{6}$/);
      expect(sky.soft).toMatch(/^#[0-9a-f]{6}$/);
    }
  });
});
