import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { Observable } from 'rxjs';
import { AdminEvent, AdminEventsApi } from '../admin/admin-events-api';
import { AuthStore } from '../auth/auth-store';
import { EventImageUpload } from '../images/event-image-upload';
import { ImageUploadResponse } from '../images/image-upload-api';
import { OwnerEventInput, OwnerEventStatus } from '../owner/owner-events-api';
import { CATEGORY_LABELS, EVENT_CATEGORIES, EventCategory } from '../public/public-event.models';
import { LocationPicker, SelectedCoordinates } from '../maps/location-picker';

@Component({
  selector: 'app-admin-page',
  imports: [FormsModule, EventImageUpload, LocationPicker],
  templateUrl: './admin-page.html',
  styleUrl: './placeholder-page.scss',
})
export class AdminPage implements OnInit {
  private readonly api = inject(AdminEventsApi);
  private readonly router = inject(Router);
  readonly auth = inject(AuthStore);
  readonly events = signal<AdminEvent[]>([]);
  readonly loading = signal(true);
  readonly errorMessage = signal('');
  readonly successMessage = signal('');
  readonly editingId = signal<string | null>(null);
  readonly categories = EVENT_CATEGORIES;
  readonly categoryLabels = CATEGORY_LABELS;
  status: '' | OwnerEventStatus = 'Pending';
  search = '';
  rejectionReasons: Record<string, string> = {};
  form = this.emptyForm();

  ngOnInit(): void { this.load(); }

  load(): void {
    this.loading.set(true);
    this.api.list(this.status || undefined, this.search).subscribe({
      next: events => { this.events.set(events); this.loading.set(false); },
      error: () => { this.loading.set(false); this.errorMessage.set('טעינת תור הבדיקה נכשלה.'); },
    });
  }

  imageUploaded(image: ImageUploadResponse): void { this.form.imageId = image.id; }
  coordinatesSelected(coordinates: SelectedCoordinates): void {
    this.form.latitude = Number(coordinates.latitude.toFixed(6));
    this.form.longitude = Number(coordinates.longitude.toFixed(6));
  }

  save(): void {
    if (!this.form.imageId) { this.errorMessage.set('יש להעלות תמונה.'); return; }
    const request = this.editingId()
      ? this.api.update(this.editingId()!, this.toInput())
      : this.api.create(this.toInput());
    this.run(request, this.editingId() ? 'האירוע עודכן.' : 'האירוע פורסם.', true);
  }

  edit(event: AdminEvent): void {
    this.editingId.set(event.id);
    this.form = {
      title: event.title, description: event.description, category: event.category,
      venueName: event.venueName, locality: event.locality, address: event.address,
      latitude: event.latitude, longitude: event.longitude,
      startAt: this.localDateTime(event.startAt), endAt: this.localDateTime(event.endAt),
      price: event.price, imageId: event.imageId, organizerName: event.organizerName,
      tagsText: event.tags.join(', '), revision: event.revision,
    };
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  cancelEdit(): void { this.editingId.set(null); this.form = this.emptyForm(); }
  approve(event: AdminEvent): void { this.run(this.api.approve(event), 'האירוע אושר ופורסם.'); }
  reject(event: AdminEvent): void {
    const reason = this.rejectionReasons[event.id]?.trim();
    if (!reason) { this.errorMessage.set('נדרשת סיבת דחייה.'); return; }
    this.run(this.api.reject(event, reason), 'האירוע נדחה והסיבה זמינה לבעל העסק.');
  }
  highlight(event: AdminEvent): void {
    this.run(this.api.highlight(event, !event.isHighlighted), event.isHighlighted ? 'ההדגשה הוסרה.' : 'האירוע הודגש.');
  }
  delete(event: AdminEvent): void { this.run(this.api.delete(event), 'האירוע נמחק.'); }

  logout(): void {
    const done = () => void this.router.navigateByUrl('/manage/login');
    this.auth.logout().subscribe({ next: done, error: done });
  }

  statusLabel(status: OwnerEventStatus): string {
    return { Pending: 'ממתין', Published: 'פורסם', Rejected: 'נדחה' }[status];
  }

  private run(request: Observable<unknown>, message: string, resetForm = false): void {
    this.errorMessage.set('');
    request.subscribe({
      next: () => {
        this.successMessage.set(message);
        if (resetForm) { this.editingId.set(null); this.form = this.emptyForm(); }
        this.load();
      },
      error: (error: HttpErrorResponse) => this.fail(error),
    });
  }

  private fail(error: HttpErrorResponse): void {
    this.errorMessage.set(error.status === 409 ? 'האירוע השתנה. הרשימה נטענה מחדש.' : 'הפעולה נכשלה. בדקו את הנתונים.');
    if (error.status === 409) this.load();
  }

  private toInput(): OwnerEventInput {
    return {
      title: this.form.title, description: this.form.description, category: this.form.category,
      venueName: this.form.venueName, locality: this.form.locality, address: this.form.address,
      latitude: this.form.latitude, longitude: this.form.longitude,
      startAt: new Date(this.form.startAt).toISOString(), endAt: new Date(this.form.endAt).toISOString(),
      price: this.form.price, imageId: this.form.imageId, organizerName: this.form.organizerName,
      tags: this.form.tagsText.split(',').map(tag => tag.trim()).filter(Boolean), revision: this.form.revision,
    };
  }

  private emptyForm() {
    const start = new Date(Date.now() + 86_400_000);
    const end = new Date(start.getTime() + 7_200_000);
    return {
      title: '', description: '', category: 'Culture' as EventCategory, venueName: '', locality: '', address: '',
      latitude: 33.2, longitude: 35.5, startAt: this.localDateTime(start.toISOString()),
      endAt: this.localDateTime(end.toISOString()), price: 0, imageId: '',
      organizerName: this.auth.user()?.businessName ?? 'NorthLife', tagsText: '', revision: undefined as number | undefined,
    };
  }

  private localDateTime(value: string): string {
    const date = new Date(value);
    return new Date(date.getTime() - date.getTimezoneOffset() * 60_000).toISOString().slice(0, 16);
  }
}
