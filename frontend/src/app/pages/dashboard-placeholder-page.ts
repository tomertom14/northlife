import { HttpErrorResponse } from '@angular/common/http';
import { Component, Injector, OnInit, afterNextRender, computed, inject, signal } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { AuthStore } from '../auth/auth-store';
import { PrivateImage } from '../images/private-image';
import { EventForm } from '../manage/event-form';
import { StatTile, StatTiles } from '../manage/stat-tiles';
import { StatusBadge } from '../manage/status-badge';
import { OwnerEvent, OwnerEventInput, OwnerEventsApi } from '../owner/owner-events-api';
import { Clock } from '../shared/clock';
import { formatLongDate, formatTime } from '../shared/jerusalem-time';
import { problemFieldErrors } from '../shared/problem-details';
import { ToastService } from '../shared/toast';

@Component({
  selector: 'app-dashboard-page',
  imports: [RouterLink, EventForm, StatTiles, StatusBadge, PrivateImage],
  templateUrl: './dashboard-placeholder-page.html',
  styleUrl: './manage-page.scss',
})
export class DashboardPage implements OnInit {
  private readonly router = inject(Router);
  private readonly api = inject(OwnerEventsApi);
  private readonly toast = inject(ToastService);
  private readonly clock = inject(Clock);
  private readonly injector = inject(Injector);
  readonly auth = inject(AuthStore);

  readonly events = signal<OwnerEvent[]>([]);
  readonly loading = signal(true);
  readonly loadFailed = signal(false);
  readonly saving = signal(false);
  readonly formOpen = signal(false);
  readonly editing = signal<OwnerEvent | null>(null);
  readonly serverErrors = signal<Record<string, string>>({});
  readonly confirmDeleteId = signal<string | null>(null);
  readonly deletingId = signal<string | null>(null);
  readonly loggingOut = signal(false);

  readonly firstName = computed(() => (this.auth.user()?.fullName ?? '').trim().split(/\s+/)[0]);
  readonly tiles = computed<StatTile[]>(() => {
    const now = this.clock.now();
    const events = this.events();
    const count = (predicate: (event: OwnerEvent) => boolean) => events.filter(predicate).length;
    return [
      { label: 'פעילים באתר', value: count((e) => e.status === 'Published' && new Date(e.endAt) > now), status: 'Published' },
      { label: 'ממתינים לאישור', value: count((e) => e.status === 'Pending'), status: 'Pending' },
      { label: 'נדחו', value: count((e) => e.status === 'Rejected'), status: 'Rejected' },
    ];
  });

  ngOnInit(): void {
    this.load();
  }

  startCreate(): void {
    this.openForm(null);
  }

  edit(event: OwnerEvent): void {
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
        this.toast.success(
          !editing
            ? 'האירוע נשלח לאישור. הסטטוס יתעדכן כאן.'
            : editing.status === 'Published'
              ? 'השינויים נשמרו. האירוע חזר לבדיקה עד לאישור.'
              : 'השינויים נשמרו ונשלחו לאישור.',
        );
        this.closeForm();
        this.load();
      },
      error: (error: HttpErrorResponse) => {
        this.saving.set(false);
        if (error.status === 400) this.serverErrors.set(problemFieldErrors(error));
        else if (error.status === 409) {
          this.toast.error('האירוע השתנה בינתיים. הרשימה רועננה, פתחו אותו שוב לעריכה.');
          this.closeForm();
          this.load();
        } else if (error.status === 404) this.toast.error('האירוע או התמונה לא נמצאו בחשבון שלכם.');
        else if (error.status !== 401) this.toast.error('השמירה נכשלה. נסו שוב בעוד רגע.');
      },
    });
  }

  askDelete(event: OwnerEvent): void {
    this.confirmDeleteId.set(event.id);
  }

  delete(event: OwnerEvent): void {
    this.deletingId.set(event.id);
    this.api.delete(event.id, event.revision).subscribe({
      next: () => {
        this.deletingId.set(null);
        this.confirmDeleteId.set(null);
        if (this.editing()?.id === event.id) this.closeForm();
        this.toast.success('האירוע נמחק.');
        this.load();
      },
      error: (error: HttpErrorResponse) => {
        this.deletingId.set(null);
        if (error.status === 409) {
          this.toast.error('האירוע השתנה בינתיים. הרשימה רועננה, נסו שוב.');
          this.load();
        } else if (error.status !== 401) this.toast.error('המחיקה נכשלה. נסו שוב.');
      },
    });
  }

  logout(): void {
    this.loggingOut.set(true);
    const finish = () => void this.router.navigateByUrl('/manage/login');
    this.auth.logout().subscribe({ next: finish, error: finish });
  }

  when(event: OwnerEvent): string {
    return `${formatLongDate(event.startAt)}, ${formatTime(event.startAt)}`;
  }

  hasEnded(event: OwnerEvent): boolean {
    return new Date(event.endAt) <= this.clock.now();
  }

  load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.api.list().subscribe({
      next: (events) => {
        this.events.set(events);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.loadFailed.set(true);
      },
    });
  }

  private openForm(event: OwnerEvent | null): void {
    this.editing.set(event);
    this.serverErrors.set({});
    this.formOpen.set(true);
    afterNextRender(
      () => document.getElementById('event-form-anchor')?.scrollIntoView({ behavior: 'smooth', block: 'start' }),
      { injector: this.injector },
    );
  }
}
