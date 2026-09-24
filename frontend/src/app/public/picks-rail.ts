import { Component, inject, input } from '@angular/core';
import { RouterLink } from '@angular/router';
import { EventImage } from './event-image';
import { EventSummary } from './public-event.models';
import { Clock } from '../shared/clock';
import { formatTime, relativeDay } from '../shared/jerusalem-time';

@Component({
  selector: 'app-picks-rail',
  imports: [RouterLink, EventImage],
  templateUrl: './picks-rail.html',
  styleUrl: './picks-rail.scss',
})
export class PicksRail {
  private readonly clock = inject(Clock);
  readonly events = input.required<EventSummary[]>();

  when(event: EventSummary): string {
    const now = this.clock.now();
    if (new Date(event.startAt) <= now) return 'עכשיו';
    return `${relativeDay(event.startAt, now)} ${formatTime(event.startAt)}`;
  }
}
