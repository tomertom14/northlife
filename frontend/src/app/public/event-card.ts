import { Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import {
  CATEGORY_LABELS,
  EventSummary,
  eventImage,
  formatEventDate,
  formatPrice,
} from './public-event.models';

@Component({
  selector: 'app-event-card',
  imports: [RouterLink],
  templateUrl: './event-card.html',
  styleUrl: './event-card.scss',
})
export class EventCard {
  readonly event = input.required<EventSummary>();
  readonly compact = input(false);
  readonly categoryLabels = CATEGORY_LABELS;

  formatDate = formatEventDate;
  formatPrice = formatPrice;
  imageFor(event: EventSummary) {
    return eventImage(event.category);
  }
}
