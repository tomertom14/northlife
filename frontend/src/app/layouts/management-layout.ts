import { Component } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';
import { Toast } from '../shared/toast';

@Component({
  selector: 'app-management-layout',
  imports: [RouterLink, RouterOutlet, Toast],
  templateUrl: './management-layout.html',
  styleUrl: './management-layout.scss',
})
export class ManagementLayout {}
