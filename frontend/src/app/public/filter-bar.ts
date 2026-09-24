import { Component, input, output } from '@angular/core';
import {
  CATEGORY_COLORS,
  CATEGORY_LABELS,
  EVENT_CATEGORIES,
  EventCategory,
  NORTHERN_LOCALITIES,
} from './public-event.models';

@Component({
  selector: 'app-filter-bar',
  templateUrl: './filter-bar.html',
  styleUrl: './filter-bar.scss',
})
export class FilterBar {
  readonly category = input<EventCategory>();
  readonly locality = input<string>();
  readonly maxPrice = input<number>();
  readonly hasActiveFilters = input(false);

  readonly categoryChange = output<EventCategory | undefined>();
  readonly localityChange = output<string | undefined>();
  readonly maxPriceChange = output<number | undefined>();
  readonly clear = output<void>();

  readonly categories = EVENT_CATEGORIES;
  readonly labels = CATEGORY_LABELS;
  readonly colors = CATEGORY_COLORS;
  readonly localities = NORTHERN_LOCALITIES;
  readonly prices = [
    { value: 0, label: 'חינם' },
    { value: 50, label: 'עד 50 ₪' },
    { value: 100, label: 'עד 100 ₪' },
    { value: 200, label: 'עד 200 ₪' },
  ];

  onLocality(event: Event): void {
    const value = (event.target as HTMLInputElement).value.trim();
    if (value !== (this.locality() ?? '')) this.localityChange.emit(value || undefined);
  }

  onPrice(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    this.maxPriceChange.emit(value === '' ? undefined : Number(value));
  }
}
