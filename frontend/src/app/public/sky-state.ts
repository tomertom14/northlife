import { Injectable, computed, effect, inject, signal } from '@angular/core';
import { Meta } from '@angular/platform-browser';
import { Clock } from '../shared/clock';
import { jerusalemHour } from '../shared/jerusalem-time';
import { SKIES, SkyName, skyForHour } from './sky';

/**
 * The sky shared by the public header and the home hero. Pages that show a hero pick the sky;
 * every other page follows the real time of day.
 */
@Injectable({ providedIn: 'root' })
export class SkyState {
  private readonly clock = inject(Clock);
  private readonly selected = signal<SkyName | null>(null);

  /** True while a page renders its own full sky hero under the header. */
  readonly heroActive = signal(false);
  readonly name = computed(() => this.selected() ?? skyForHour(jerusalemHour(this.clock.now())));
  readonly theme = computed(() => SKIES[this.name()]);

  constructor() {
    const meta = inject(Meta);
    effect(() => meta.updateTag({ name: 'theme-color', content: this.theme().background }));
  }

  show(name: SkyName): void {
    this.selected.set(name);
  }

  followClock(): void {
    this.selected.set(null);
  }
}
