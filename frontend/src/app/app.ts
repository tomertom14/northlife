import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { NavigationHistory } from './shared/navigation-history';

@Component({
  imports: [RouterOutlet],
  selector: 'app-root',
  styleUrl: './app.scss',
  templateUrl: './app.html',
})
export class App {
  constructor() {
    // Created with the root component so the very first navigation is counted.
    inject(NavigationHistory);
  }
}
