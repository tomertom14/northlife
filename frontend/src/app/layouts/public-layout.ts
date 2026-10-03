import { Component, computed, inject, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AnalyticsService } from '../analytics/analytics';
import { SkyState } from '../public/sky-state';
import { Clock } from '../shared/clock';

const clockFormatter = new Intl.DateTimeFormat('he-IL', {
  timeZone: 'Asia/Jerusalem',
  weekday: 'long',
  day: 'numeric',
  month: 'long',
  hour: '2-digit',
  minute: '2-digit',
  hourCycle: 'h23',
});

@Component({
  selector: 'app-public-layout',
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  templateUrl: './public-layout.html',
  styleUrl: './public-layout.scss',
})
export class PublicLayout {
  private readonly clock = inject(Clock);
  readonly sky = inject(SkyState);
  readonly analytics = inject(AnalyticsService);
  readonly clockLabel = computed(() => clockFormatter.format(this.clock.now()));
  readonly resetting = signal(false);
  readonly resetDone = signal(false);

  resetHistory(): void {
    this.resetting.set(true);
    this.analytics.resetHistory().subscribe({
      next: () => {
        this.resetting.set(false);
        this.resetDone.set(true);
      },
      error: () => this.resetting.set(false),
    });
  }

  skipToContent(main: HTMLElement): void {
    main.focus();
    main.scrollIntoView();
  }
}
