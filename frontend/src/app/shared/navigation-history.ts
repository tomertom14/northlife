import { Injectable, inject } from '@angular/core';
import { NavigationEnd, Router } from '@angular/router';
import { filter } from 'rxjs';

/** Knows whether the browser's previous history entry belongs to this app. */
@Injectable({ providedIn: 'root' })
export class NavigationHistory {
  private completed = 0;

  constructor() {
    inject(Router)
      .events.pipe(filter((event) => event instanceof NavigationEnd))
      .subscribe(() => this.completed++);
  }

  get canGoBack(): boolean {
    return this.completed > 1;
  }
}
