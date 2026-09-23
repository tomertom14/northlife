import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthStore } from '../auth/auth-store';
import { EventImageUpload } from '../images/event-image-upload';
import { ImageUploadResponse } from '../images/image-upload-api';
import { OwnerEvent, OwnerEventInput, OwnerEventsApi } from '../owner/owner-events-api';
import { CATEGORY_LABELS, EVENT_CATEGORIES, EventCategory } from '../public/public-event.models';

@Component({
  selector: 'app-dashboard-page',
  imports: [FormsModule, EventImageUpload],
  templateUrl: './dashboard-placeholder-page.html',
  styleUrl: './placeholder-page.scss',
})
export class DashboardPage implements OnInit {
  private readonly router = inject(Router);
  private readonly api = inject(OwnerEventsApi);
  readonly auth = inject(AuthStore);
  readonly categories = EVENT_CATEGORIES;
  readonly categoryLabels = CATEGORY_LABELS;
  readonly events = signal<OwnerEvent[]>([]);
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly errorMessage = signal('');
  readonly successMessage = signal('');
  readonly editingId = signal<string | null>(null);
  readonly confirmDeleteId = signal<string | null>(null);
  readonly loggingOut = signal(false);

  form = this.emptyForm();

  ngOnInit(): void {
    this.load();
  }

  imageUploaded(image: ImageUploadResponse): void {
    this.form.imageId = image.id;
  }

  edit(event: OwnerEvent): void {
    this.editingId.set(event.id);
    this.form = {
      title: event.title,
      description: event.description,
      category: event.category,
      venueName: event.venueName,
      locality: event.locality,
      address: event.address,
      latitude: event.latitude,
      longitude: event.longitude,
      startAt: this.localDateTime(event.startAt),
      endAt: this.localDateTime(event.endAt),
      price: event.price,
      imageId: event.imageId,
      organizerName: event.organizerName,
      tagsText: event.tags.join(', '),
      revision: event.revision,
    };
    this.successMessage.set('');
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  cancelEdit(): void {
    this.editingId.set(null);
    this.form = this.emptyForm();
    this.errorMessage.set('');
  }

  save(): void {
    if (!this.form.imageId) {
      this.errorMessage.set('יש להעלות תמונה לפני שמירת האירוע.');
      return;
    }

    const input: OwnerEventInput = {
      title: this.form.title,
      description: this.form.description,
      category: this.form.category,
      venueName: this.form.venueName,
      locality: this.form.locality,
      address: this.form.address,
      latitude: this.form.latitude,
      longitude: this.form.longitude,
      startAt: new Date(this.form.startAt).toISOString(),
      endAt: new Date(this.form.endAt).toISOString(),
      price: this.form.price,
      imageId: this.form.imageId,
      organizerName: this.form.organizerName,
      tags: this.form.tagsText.split(',').map(tag => tag.trim()).filter(Boolean),
      revision: this.form.revision,
    };
    const request = this.editingId()
      ? this.api.update(this.editingId()!, input)
      : this.api.create(input);

    this.saving.set(true);
    this.errorMessage.set('');
    request.subscribe({
      next: () => {
        this.saving.set(false);
        this.successMessage.set(this.editingId()
          ? 'האירוע עודכן ונשלח מחדש לאישור.'
          : 'האירוע נוצר ונשלח לאישור.');
        this.editingId.set(null);
        this.form = this.emptyForm();
        this.load();
      },
      error: (error: HttpErrorResponse) => {
        this.saving.set(false);
        this.errorMessage.set(error.status === 409
          ? 'האירוע השתנה. טענו מחדש לפני שמירה.'
          : error.status === 404
            ? 'התמונה או האירוע אינם שייכים לחשבון הזה.'
            : 'לא הצלחנו לשמור. בדקו שכל השדות תקינים ושעת הסיום בעתיד.');
      },
    });
  }

  delete(event: OwnerEvent): void {
    if (this.confirmDeleteId() !== event.id) {
      this.confirmDeleteId.set(event.id);
      return;
    }

    this.api.delete(event.id, event.revision).subscribe({
      next: () => {
        this.confirmDeleteId.set(null);
        this.successMessage.set('האירוע נמחק.');
        this.load();
      },
      error: (error: HttpErrorResponse) => {
        this.errorMessage.set(error.status === 409
          ? 'האירוע השתנה. טענו מחדש.'
          : 'לא הצלחנו למחוק את האירוע.');
      },
    });
  }

  logout(): void {
    this.loggingOut.set(true);
    const finish = () => void this.router.navigateByUrl('/manage/login');
    this.auth.logout().subscribe({ next: finish, error: finish });
  }

  statusLabel(status: OwnerEvent['status']): string {
    return { Pending: 'ממתין לאישור', Published: 'פורסם', Rejected: 'נדחה' }[status];
  }

  private load(): void {
    this.loading.set(true);
    this.api.list().subscribe({
      next: events => {
        this.events.set(events);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.errorMessage.set('לא הצלחנו לטעון את האירועים.');
      },
    });
  }

  private emptyForm() {
    const start = new Date(Date.now() + 24 * 60 * 60 * 1000);
    const end = new Date(start.getTime() + 2 * 60 * 60 * 1000);
    return {
      title: '',
      description: '',
      category: 'Culture' as EventCategory,
      venueName: '',
      locality: '',
      address: '',
      latitude: 33.2,
      longitude: 35.5,
      startAt: this.localDateTime(start.toISOString()),
      endAt: this.localDateTime(end.toISOString()),
      price: 0,
      imageId: '',
      organizerName: this.auth.user()?.businessName ?? '',
      tagsText: '',
      revision: undefined as number | undefined,
    };
  }

  private localDateTime(value: string): string {
    const date = new Date(value);
    const local = new Date(date.getTime() - date.getTimezoneOffset() * 60_000);
    return local.toISOString().slice(0, 16);
  }
}
