import { HttpErrorResponse } from '@angular/common/http';
import { Component, Injector, OnInit, afterNextRender, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthStore } from '../auth/auth-store';
import { PrivateImage } from '../images/private-image';
import { PlaceForm } from '../manage/place-form';
import { StatusBadge } from '../manage/status-badge';
import { OwnerPlace, OwnerPlaceInput, OwnerPlacesApi } from '../owner/owner-places-api';
import { PLACE_CATEGORY_LABELS, PLACE_COVER_CATEGORY } from '../places/places.models';
import { problemFieldErrors } from '../shared/problem-details';
import { ToastService } from '../shared/toast';

/** "המקומות שלי": a business owner's places, with the same review flow as events. */
@Component({
  selector: 'app-owner-places-page',
  imports: [RouterLink, PlaceForm, StatusBadge, PrivateImage],
  templateUrl: './owner-places-page.html',
  styleUrl: './manage-page.scss',
})
export class OwnerPlacesPage implements OnInit {
  private readonly api = inject(OwnerPlacesApi);
  private readonly toast = inject(ToastService);
  private readonly injector = inject(Injector);
  readonly auth = inject(AuthStore);

  readonly places = signal<OwnerPlace[]>([]);
  readonly loading = signal(true);
  readonly loadFailed = signal(false);
  readonly saving = signal(false);
  readonly formOpen = signal(false);
  readonly editing = signal<OwnerPlace | null>(null);
  readonly serverErrors = signal<Record<string, string>>({});
  readonly confirmDeleteId = signal<string | null>(null);
  readonly deletingId = signal<string | null>(null);
  readonly labels = PLACE_CATEGORY_LABELS;
  readonly cover = PLACE_COVER_CATEGORY;

  ngOnInit(): void {
    this.load();
  }

  startCreate(): void {
    this.openForm(null);
  }

  edit(place: OwnerPlace): void {
    this.openForm(place);
  }

  closeForm(): void {
    this.formOpen.set(false);
    this.editing.set(null);
    this.serverErrors.set({});
  }

  save(input: OwnerPlaceInput): void {
    const editing = this.editing();
    this.saving.set(true);
    this.serverErrors.set({});
    const request = editing ? this.api.update(editing.id, input) : this.api.create(input);
    request.subscribe({
      next: () => {
        this.saving.set(false);
        this.toast.success(
          !editing
            ? 'המקום נשלח לאישור. הסטטוס יתעדכן כאן.'
            : editing.status === 'Published'
              ? 'השינויים נשמרו. המקום חזר לבדיקה עד לאישור.'
              : 'השינויים נשמרו ונשלחו לאישור.',
        );
        this.closeForm();
        this.load();
      },
      error: (error: HttpErrorResponse) => {
        this.saving.set(false);
        if (error.status === 400) this.serverErrors.set(problemFieldErrors(error));
        else if (error.status === 409) {
          this.toast.error('המקום השתנה בינתיים. הרשימה רועננה, פתחו אותו שוב לעריכה.');
          this.closeForm();
          this.load();
        } else if (error.status === 403 && error.error?.code === 'email_not_verified') {
          this.toast.error('אמתו קודם את כתובת האימייל. אפשר לשלוח קישור חדש מלוח הבקרה.');
        } else if (error.status === 404) this.toast.error('המקום או התמונה לא נמצאו בחשבון שלכם.');
        else if (error.status !== 401) this.toast.error('השמירה נכשלה. נסו שוב בעוד רגע.');
      },
    });
  }

  askDelete(place: OwnerPlace): void {
    this.confirmDeleteId.set(place.id);
  }

  delete(place: OwnerPlace): void {
    this.deletingId.set(place.id);
    this.api.delete(place.id, place.revision).subscribe({
      next: () => {
        this.deletingId.set(null);
        this.confirmDeleteId.set(null);
        if (this.editing()?.id === place.id) this.closeForm();
        this.toast.success('המקום נמחק.');
        this.load();
      },
      error: (error: HttpErrorResponse) => {
        this.deletingId.set(null);
        if (error.status === 409) {
          this.toast.error('המקום השתנה בינתיים. הרשימה רועננה, נסו שוב.');
          this.load();
        } else if (error.status !== 401) this.toast.error('המחיקה נכשלה. נסו שוב.');
      },
    });
  }

  load(): void {
    this.loading.set(true);
    this.loadFailed.set(false);
    this.api.list().subscribe({
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

  private openForm(place: OwnerPlace | null): void {
    this.editing.set(place);
    this.serverErrors.set({});
    this.formOpen.set(true);
    afterNextRender(
      () => document.getElementById('place-form-anchor')?.scrollIntoView({ behavior: 'smooth', block: 'start' }),
      { injector: this.injector },
    );
  }
}
