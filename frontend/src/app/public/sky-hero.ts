import { Component, input, linkedSignal, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { EventPeriod, PERIOD_LABELS } from './public-event.models';
import { SkyTheme } from './sky';
import { addDaysToKey, jerusalemDateKey } from '../shared/jerusalem-time';

export interface DateRange {
  from: string;
  to: string;
}

const MAX_RANGE_DAYS = 60;

@Component({
  selector: 'app-sky-hero',
  imports: [FormsModule],
  templateUrl: './sky-hero.html',
  styleUrl: './sky-hero.scss',
})
export class SkyHero {
  readonly period = input.required<EventPeriod>();
  readonly theme = input.required<SkyTheme>();
  readonly status = input('');
  readonly from = input<string | undefined>();
  readonly to = input<string | undefined>();

  readonly periodChange = output<EventPeriod>();
  readonly rangeApply = output<DateRange>();

  readonly periods: EventPeriod[] = ['now', 'today', 'tonight', 'tomorrow', 'range'];
  readonly labels = PERIOD_LABELS;
  readonly fromValue = linkedSignal(() => this.from() ?? '');
  readonly toValue = linkedSignal(() => this.to() ?? '');
  readonly rangeError = signal('');
  readonly today = jerusalemDateKey(new Date());
  readonly lastSelectable = addDaysToKey(this.today, 365);

  applyRange(): void {
    const from = this.fromValue();
    const to = this.toValue();
    if (!from || !to) {
      this.rangeError.set('בחרו תאריך התחלה ותאריך סיום.');
      return;
    }
    if (from > to) {
      this.rangeError.set('תאריך הסיום צריך להיות אחרי תאריך ההתחלה.');
      return;
    }
    if (to > addDaysToKey(from, MAX_RANGE_DAYS)) {
      this.rangeError.set(`אפשר לבחור עד ${MAX_RANGE_DAYS} ימים בכל פעם.`);
      return;
    }
    this.rangeError.set('');
    this.rangeApply.emit({ from, to });
  }
}
