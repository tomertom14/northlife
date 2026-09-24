import { DestroyRef, Injectable, inject, signal } from '@angular/core';

/** A coarse app-wide clock: live and "starting soon" labels refresh twice a minute. */
@Injectable({ providedIn: 'root' })
export class Clock {
  private readonly current = signal(new Date());
  readonly now = this.current.asReadonly();

  constructor() {
    const timer = setInterval(() => this.current.set(new Date()), 30_000);
    inject(DestroyRef).onDestroy(() => clearInterval(timer));
  }
}
