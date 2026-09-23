import { Component, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { AuthStore } from '../auth/auth-store';

@Component({
  selector: 'app-dashboard-page',
  templateUrl: './dashboard-placeholder-page.html',
  styleUrl: './placeholder-page.scss',
})
export class DashboardPage {
  private readonly router = inject(Router);
  readonly auth = inject(AuthStore);
  readonly loggingOut = signal(false);

  logout(): void {
    this.loggingOut.set(true);
    const finish = () => void this.router.navigateByUrl('/manage/login');
    this.auth.logout().subscribe({ next: finish, error: finish });
  }
}
