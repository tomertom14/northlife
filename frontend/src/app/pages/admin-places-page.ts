import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AdminPlace, AdminPlacesApi } from '../admin/admin-places-api';
import { PrivateImage } from '../images/private-image';
import { AdminNav } from '../manage/admin-nav';
import { StatusBadge } from '../manage/status-badge';
import { OwnerEventStatus } from '../owner/owner-events-api';
import { weekRows } from '../places/opening-hours';
import { PLACE_CATEGORY_LABELS, PLACE_COVER_CATEGORY } from '../places/places.models';
import { ToastService } from '../shared/toast';

/** The review queue for places: pending first, approve or reject with a reason the owner sees. */
@Component({
  selector: 'app-admin-places-page',
  imports: [FormsModule, RouterLink, AdminNav, StatusBadge, PrivateImage],
  templateUrl: './admin-places-page.html',
  styleUrl: './manage-page.scss',
})
export class AdminPlacesPage implements OnInit {
  private readonly api = inject(AdminPlacesApi);
  private readonly toast = inject(ToastService);

  readonly places = signal<AdminPlace[]>([]);
  readonly loading = signal(true);
  readonly loadFailed = signal(false);
  readonly busyId = signal<string | null>(null);
  readonly rejectingId = signal<string | null>(null);
  readonly rejectError = signal('');
  readonly confirmDeleteId = signal<string | null>(null);
  readonly expandedId = signal<string | null>(null);
  readonly labels = PLACE_CATEGORY_LABELS;
  readonly cover = PLACE_COVER_CATEGORY;
  readonly hoursRows = weekRows;

  status: OwnerEventStatus | '' = 'Pending';
  search = '';
  rejectionReason = '';

  readonly resultLabel = computed(() => {
    const count = this.places().length;
    return count === 1 ? 'מקום אחד' : `${count} מקומות`;
  });

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.api.list(this.status || undefined, this.search).subscribe({
      next: (places) => {
        this.places.set(places);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.loadFailed.set(true);
      },
    });
  }

  toggleDetails(place: AdminPlace): void {
    this.expandedId.set(this.expandedId() === place.id ? null : place.id);
  }

  approve(place: AdminPlace): void {
    this.busyId.set(place.id);
    this.api.approve(place).subscribe({
      next: () => {
        this.busyId.set(null);
        this.toast.success(`"${place.name}" פורסם.`);
        this.load();
      },
      error: (error: HttpErrorResponse) => this.failed(error),
    });
  }

  startReject(place: AdminPlace): void {
    this.rejectingId.set(place.id);
    this.rejectionReason = '';
    this.rejectError.set('');
  }

  reject(place: AdminPlace): void {
    const reason = this.rejectionReason.trim();
    if (!reason) {
      this.rejectError.set('כתבו לבעל העסק מה צריך לתקן.');
      return;
    }
    this.busyId.set(place.id);
    this.api.reject(place, reason).subscribe({
      next: () => {
        this.busyId.set(null);
        this.rejectingId.set(null);
        this.toast.success('הדחייה נשלחה לבעל העסק.');
        this.load();
      },
      error: (error: HttpErrorResponse) => this.failed(error),
    });
  }

  delete(place: AdminPlace): void {
    this.busyId.set(place.id);
    this.api.delete(place).subscribe({
      next: () => {
        this.busyId.set(null);
        this.confirmDeleteId.set(null);
        this.toast.success('המקום נמחק.');
        this.load();
      },
      error: (error: HttpErrorResponse) => this.failed(error),
    });
  }

  private failed(error: HttpErrorResponse): void {
    this.busyId.set(null);
    if (error.status === 409) {
      this.toast.error('המקום השתנה בינתיים. הרשימה רועננה, נסו שוב.');
      this.load();
    } else if (error.status === 400) {
      this.toast.error('לא ניתן לבצע את הפעולה במצב הנוכחי של המקום.');
      this.load();
    } else if (error.status !== 401) {
      this.toast.error('הפעולה נכשלה. נסו שוב.');
    }
  }
}
