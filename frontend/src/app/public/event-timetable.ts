import { Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { EventImage } from './event-image';
import { CATEGORY_COLORS, CATEGORY_LABELS, formatPrice } from './public-event.models';
import { TimetableGroup } from './timetable';

@Component({
  selector: 'app-event-timetable',
  imports: [RouterLink, EventImage],
  templateUrl: './event-timetable.html',
  styleUrl: './event-timetable.scss',
})
export class EventTimetable {
  readonly groups = input<TimetableGroup[]>([]);
  readonly loading = input(false);

  readonly labels = CATEGORY_LABELS;
  readonly colors = CATEGORY_COLORS;
  readonly price = formatPrice;
  readonly placeholders = [1, 2, 3, 4, 5];
}
