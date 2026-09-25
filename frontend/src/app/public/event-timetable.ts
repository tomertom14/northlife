import { Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { TrackImpression } from '../analytics/track-impression';
import { EventImage } from './event-image';
import { CATEGORY_COLORS, CATEGORY_LABELS, formatPrice } from './public-event.models';
import { TimetableGroup } from './timetable';

@Component({
  selector: 'app-event-timetable',
  imports: [RouterLink, EventImage, TrackImpression],
  templateUrl: './event-timetable.html',
  styleUrl: './event-timetable.scss',
})
export class EventTimetable {
  readonly groups = input<TimetableGroup[]>([]);
  readonly loading = input(false);
  /** Rows on earlier pages, so positions count from the top of the whole feed. */
  readonly rankOffset = input(0);

  readonly labels = CATEGORY_LABELS;
  readonly colors = CATEGORY_COLORS;
  readonly price = formatPrice;
  readonly placeholders = [1, 2, 3, 4, 5];
}
