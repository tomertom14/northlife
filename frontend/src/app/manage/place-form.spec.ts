import { hoursProblem } from './place-form';

const at = (hour: number, minute = 0) => hour * 60 + minute;

describe('place form hours check', () => {
  it('accepts split days, late nights and 24-hour days', () => {
    expect(
      hoursProblem([
        { day: 0, opens: 0, closes: 0 },
        { day: 1, opens: at(10), closes: at(13) },
        { day: 1, opens: at(16), closes: at(2) },
      ]),
    ).toBe('');
  });

  it('rejects overlapping intervals on the same day, naming the day', () => {
    expect(hoursProblem([{ day: 2, opens: at(9), closes: at(14) }, { day: 2, opens: at(13), closes: at(18) }])).toBe(
      'ביום שלישי יש טווחי שעות חופפים.',
    );
  });

  it('rejects an interval that opens and closes at the same time, except midnight to midnight', () => {
    expect(hoursProblem([{ day: 3, opens: at(9), closes: at(9) }])).toContain('00:00–00:00');
  });

  it('allows at most three intervals a day', () => {
    const four = [6, 9, 12, 15].map((hour) => ({ day: 4, opens: at(hour), closes: at(hour + 1) }));
    expect(hoursProblem(four)).toBe('ביום חמישי אפשר עד שלושה טווחי שעות.');
  });
});
