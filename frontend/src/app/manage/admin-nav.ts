import { Component } from '@angular/core';
import { RouterLink, RouterLinkActive } from '@angular/router';

/** Section tabs shared by the administrator screens. */
@Component({
  selector: 'app-admin-nav',
  imports: [RouterLink, RouterLinkActive],
  template: `
    <nav class="admin-nav" aria-label="ניהול המערכת">
      <a routerLink="/manage/admin" routerLinkActive="current" [routerLinkActiveOptions]="{ exact: true }" ariaCurrentWhenActive="page">תור בדיקה</a>
      <a routerLink="/manage/admin/places" routerLinkActive="current" ariaCurrentWhenActive="page">מקומות</a>
      <a routerLink="/manage/admin/users" routerLinkActive="current" ariaCurrentWhenActive="page">משתמשים</a>
      <a routerLink="/manage/admin/audit" routerLinkActive="current" ariaCurrentWhenActive="page">יומן פעולות</a>
      <a routerLink="/manage/admin/auto-moderation" routerLinkActive="current" ariaCurrentWhenActive="page">אישור אוטומטי</a>
    </nav>
  `,
  styles: [`
    .admin-nav { border-bottom: 1px solid var(--line); display: flex; gap: clamp(1rem, 4vw, 2rem); overflow-x: auto; }
    a { border-bottom: 3px solid transparent; color: var(--muted); font-weight: 600; margin-bottom: -1px; padding: .65rem 0; white-space: nowrap; }
    a:hover { color: var(--ink); }
    a.current { border-bottom-color: var(--ink); color: var(--ink); }
  `],
})
export class AdminNav {}
