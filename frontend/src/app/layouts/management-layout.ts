import { Component } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';

@Component({
  selector: 'app-management-layout',
  imports: [RouterLink, RouterOutlet],
  templateUrl: './management-layout.html',
  styleUrl: './management-layout.scss',
})
export class ManagementLayout {}
