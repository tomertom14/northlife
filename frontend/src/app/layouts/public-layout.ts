import { Component } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-public-layout',
  imports: [RouterLink, RouterOutlet],
  templateUrl: './public-layout.html',
  styleUrl: './public-layout.scss',
})
export class PublicLayout {}
