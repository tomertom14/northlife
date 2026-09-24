import { HttpErrorResponse } from '@angular/common/http';
import { Component, Injector, OnInit, afterNextRender, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { Observable, forkJoin } from 'rxjs';
import { AdminEvent, AdminEventsApi } from '../admin/admin-events-api';
import { AuthStore } from '../auth/auth-store';
import { PrivateImage } from '../images/private-image';
import { EventForm } from '../manage/event-form';
import { StatTile, StatTiles } from '../manage/stat-tiles';
import { StatusBadge } from '../manage/status-badge';
import { OwnerEventInput, OwnerEventStatus } from '../owner/owner-events-api';
import { Clock } from '../shared/clock';
import { formatLongDate, formatTime } from '../shared/jerusalem-time';
import { problemFieldErrors } from '../shared/problem-details';
import { ToastService } from '../shared/toast';

@Component({
  selector: 'app-admin-page',
  imports: [FormsModule, RouterLink, EventForm, StatTiles, StatusBadge, PrivateImage],
  templateUrl: './admin-page.html',
  styleUrl: './manage-page.scss',
})
export class AdminPage implements OnInit {
  private readonly api = inject(AdminEventsApi);
  private readonly router = inject(Router);
  private readonly toast = inject(ToastService);
  private readonly clock = inject(Clock);
  private readonly injector = inject(Injector);
  readonly auth = inject(AuthStore);

  readonly events = signal<AdminEvent[]>([]);
  readonly overview = signal<AdminEvent[]>([]);
  readonly loading = signal(true);
  readonly loadFailed = signal(false);
  readonly saving = signal(false);
  readonly formOpen = signal(false);
  readonly editing = signal<AdminEvent | null>(null);
  readonly serverErrors = signal<Record<string, string>>({});
  readonly busyId = signal<string | null>(null);
  readonly rejectingId = signal<string | null>(null);
  readonly confirmDeleteId = signal<string | null>(null);
  readonly rejectError = signal('');
  status: '' | OwnerEventStatus = 'Pending';
  search = '';
  rejectionReason = '';

  readonly tiles = computed<StatTile[]>(() => {
    const now = this.clock.now();
    const events = this.overview();
    const count = (predicate: (event: AdminEvent) => boolean) => events.filter(predicate).length;
    return [
      { label: 'ממתינים לבדיקה', value: count((e) => e.status === 'Pending'), status: 'Pending' },
      { label: 'מפורסמים ופעילים', value: count((e) => e.status === 'Published' && new Date(e.endAt) > now), status: 'Published' },
      { label: 'נדחו', value: count((e) => e.status === 'Rejected'), status: 'Rejected' },
    ];
  });
  readonly resultLabel = computed(() => {
    const count = this.events().length;
    return count === 1 ? 'אירוע אחד' : `${count} אירועים`;
  });

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    forkJoin({
      queue: this.api.list(this.status || undefined, this.search),
      overview: this.api.list(),
    }).subscribe({
      next: ({ queue, overview }) => {
        this.events.set(queue);
        this.overview.set(overview);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.loadFailed.set(true);
      },
    });
  }

  startCreate(): void {
    this.openForm(null);
  }

  edit(event: AdminEvent): void {
    this.openForm(event);
  }

  closeForm(): void {
    this.formOpen.set(false);
    this.editing.set(null);
    this.serverErrors.set({});
  }

  save(input: OwnerEventInput): void {
    const editing = this.editing();
    this.saving.set(true);
    this.serverErrors.set({});
    const request = editing ? this.api.update(editing.id, input) : this.api.create(input);
    request.subscribe({
      next: () => {
        this.saving.set(false);
        this.toast.success(editing ? 'השינויים נשמרו.' : 'האירוע פורסם באתר.');
        this.closeForm();
        this.load();
      },
      error: (error: HttpErrorResponse) => {
        this.saving.set(false);
        if (error.status === 400) this.serverErrors.set(problemFieldErrors(error));
        else this.fail(error);
      },
    });
  }

  approve(event: AdminEvent): void {
    this.run(event, this.api.approve(event), 'האירוע אושר ופורסם באתר.');
  }

  startReject(event: AdminEvent): void {
    this.rejectingId.set(event.id);
    this.rejectionReason = '';
    this.rejectError.set('');
  }

  reject(event: AdminEvent): void {
    const reason = this.rejectionReason.trim();
    if (!reason) {
      this.rejectError.set('כתבו לבעל העסק מה צריך לתקן.');
      return;
    }
    this.run(event, this.api.reject(event, reason), 'האירוע נדחה. בעל העסק יראה את הסיבה בלוח שלו.');
  }

  toggleHighlight(event: AdminEvent): void {
    this.run(
      event,
      this.api.highlight(event, !event.isHighlighted),
      event.isHighlighted ? 'האירוע הוסר מבחירות העורכים.' : 'האירוע נוסף לבחירות העורכים.',
    );
  }

  delete(event: AdminEvent): void {
    this.run(event, this.api.delete(event), 'האירוע נמחק.');
  }

  logout(): void {
    const finish = () => void this.router.navigateByUrl('/manage/login');
    this.auth.logout().subscribe({ next: finish, error: finish });
  }

  when(event: AdminEvent): string {
    return `${formatLongDate(event.startAt)}, ${formatTime(event.startAt)}`;
  }

  hasEnded(event: AdminEvent): boolean {
    return new Date(event.endAt) <= this.clock.now();
  }

  private run(event: AdminEvent, request: Observable<unknown>, message: string): void {
    this.busyId.set(event.id);
    request.subscribe({
      next: () => {
        this.busyId.set(null);
        this.rejectingId.set(null);
        this.confirmDeleteId.set(null);
        if (this.editing()?.id === event.id) this.closeForm();
        this.toast.success(message);
        this.load();
      },
      error: (error: HttpErrorResponse) => {
        this.busyId.set(null);
        this.fail(error);
      },
    });
  }

  private fail(error: HttpErrorResponse): void {
    if (error.status === 401) return;
    if (error.status === 409) {
      this.toast.error('האירוע השתנה בינתיים. הרשימה רועננה, בדקו אותו שוב.');
      this.load();
      return;
    }
    const message = problemFieldErrors(error)['event'];
    this.toast.error(
      message === 'Event end time must be in the future.'
        ? 'אי אפשר לאשר אירוע שכבר הסתיים.'
        : 'הפעולה נכשלה. נסו שוב בעוד רגע.',
    );
  }

  private openForm(event: AdminEvent | null): void {
    this.editing.set(event);
    this.serverErrors.set({});
    this.formOpen.set(true);
    afterNextRender(
      () => document.getElementById('event-form-anchor')?.scrollIntoView({ behavior: 'smooth', block: 'start' }),
      { injector: this.injector },
    );
  }
}
