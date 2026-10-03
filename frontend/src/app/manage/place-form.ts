import {
  ChangeDetectorRef,
  Component,
  ElementRef,
  Injector,
  afterNextRender,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { EventImageUpload } from '../images/event-image-upload';
import { ImageUploadResponse } from '../images/image-upload-api';
import { LocationPicker, SelectedCoordinates } from '../maps/location-picker';
import { OwnerPlace, OwnerPlaceInput } from '../owner/owner-places-api';
import { DAY_NAMES, MINUTES_PER_DAY, formatMinute, parseMinute } from '../places/opening-hours';
import { PLACE_CATEGORIES, PLACE_CATEGORY_LABELS, PLACE_COVER_CATEGORY, PlaceCategory, PlaceHours } from '../places/places.models';
import { NORTHERN_LOCALITIES } from '../public/public-event.models';

interface IntervalModel {
  opens: string;
  closes: string;
}

interface DayModel {
  day: number;
  name: string;
  intervals: IntervalModel[];
}

interface PlaceFormModel {
  name: string;
  category: PlaceCategory;
  description: string;
  locality: string;
  address: string;
  latitude: number | null;
  longitude: number | null;
  phone: string;
  website: string;
  instagram: string;
  studentPerk: string;
  imageId: string;
  days: DayModel[];
}

// Hebrew wording for fields the API reports; its own messages are English.
const FIELD_MESSAGES: Record<string, string> = {
  name: 'כתבו את שם המקום, עד 150 תווים.',
  description: 'כתבו תיאור של המקום, עד 3,000 תווים.',
  locality: 'כתבו את שם היישוב, עד 120 תווים.',
  address: 'כתבו כתובת, עד 300 תווים.',
  latitude: 'קו רוחב צריך להיות בין ‎-90 ל־90.',
  longitude: 'קו אורך צריך להיות בין ‎-180 ל־180.',
  phone: 'מספר טלפון בספרות, רווחים או מקפים, אפשר עם + בהתחלה.',
  website: 'כתובת אתר מלאה שמתחילה ב-https:// או http://.',
  instagram: 'שם משתמש באינסטגרם: אותיות לועזיות, ספרות, נקודה או קו תחתון, עד 30 תווים.',
  studentPerk: 'הטבה לסטודנטים עד 200 תווים.',
  hours: 'יש לתקן את שעות הפתיחה.',
  imageId: 'העלו תמונה למקום.',
};

let nextFormId = 0;

@Component({
  selector: 'app-place-form',
  imports: [FormsModule, EventImageUpload, LocationPicker],
  templateUrl: './place-form.html',
  styleUrls: ['./event-form.scss', './place-form.scss'],
})
export class PlaceForm {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private readonly changeDetector = inject(ChangeDetectorRef);

  readonly place = input<OwnerPlace | null>(null);
  readonly saving = input(false);
  readonly serverErrors = input<Record<string, string>>({});
  readonly save = output<OwnerPlaceInput>();
  readonly cancel = output<void>();

  readonly formId = `place-form-${++nextFormId}`;
  readonly categories = PLACE_CATEGORIES;
  readonly categoryLabels = PLACE_CATEGORY_LABELS;
  readonly cover = PLACE_COVER_CATEGORY;
  readonly localities = NORTHERN_LOCALITIES;
  readonly clientErrors = signal<Record<string, string>>({});
  model: PlaceFormModel = this.toModel(null);

  readonly errors = computed<Record<string, string>>(() => {
    const merged: Record<string, string> = {};
    for (const field of Object.keys(this.serverErrors())) {
      if (FIELD_MESSAGES[field]) merged[field] = FIELD_MESSAGES[field];
    }
    return { ...merged, ...this.clientErrors() };
  });
  readonly errorCount = computed(() => Object.keys(this.errors()).length);
  readonly editing = computed(() => this.place() !== null);
  readonly republishWarning = computed(() => this.place()?.status === 'Published');

  constructor() {
    effect(() => {
      const place = this.place();
      untracked(() => {
        this.model = this.toModel(place);
        this.clientErrors.set({});
        this.changeDetector.markForCheck();
      });
    });
  }

  fieldId(field: string): string {
    return `${this.formId}-${field}`;
  }

  errorId(field: string): string | null {
    return this.errors()[field] ? `${this.formId}-${field}-error` : null;
  }

  imageUploaded(image: ImageUploadResponse): void {
    this.model.imageId = image.id;
    this.clearError('imageId');
  }

  coordinatesSelected(coordinates: SelectedCoordinates): void {
    this.model.latitude = Number(coordinates.latitude.toFixed(6));
    this.model.longitude = Number(coordinates.longitude.toFixed(6));
    this.clearError('latitude');
    this.clearError('longitude');
    this.changeDetector.markForCheck();
  }

  addInterval(day: DayModel): void {
    if (day.intervals.length >= 3) return;
    const last = day.intervals.at(-1);
    day.intervals.push(last ? { opens: last.closes, closes: '23:00' } : { opens: '09:00', closes: '17:00' });
    this.clearError('hours');
  }

  removeInterval(day: DayModel, index: number): void {
    day.intervals.splice(index, 1);
    this.clearError('hours');
  }

  /** Sunday's hours for Monday to Thursday, the common Israeli work-week pattern. */
  copySundayToWeekdays(): void {
    const sunday = this.model.days[0].intervals;
    for (const day of this.model.days.slice(1, 5)) day.intervals = sunday.map((interval) => ({ ...interval }));
    this.clearError('hours');
  }

  clearError(field: string): void {
    if (!this.clientErrors()[field]) return;
    const { [field]: _removed, ...rest } = this.clientErrors();
    this.clientErrors.set(rest);
  }

  submit(): void {
    const hours = this.collectHours();
    const errors = this.validate(this.model, hours);
    this.clientErrors.set(errors);
    if (Object.keys(errors).length > 0 || hours === null) {
      afterNextRender(() => this.host.nativeElement.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus(), {
        injector: this.injector,
      });
      return;
    }

    const model = this.model;
    this.save.emit({
      name: model.name.trim(),
      category: model.category,
      description: model.description.trim(),
      locality: model.locality.trim(),
      address: model.address.trim(),
      latitude: model.latitude!,
      longitude: model.longitude!,
      phone: model.phone.trim() || null,
      website: model.website.trim() || null,
      instagram: model.instagram.trim().replace(/^@/, '') || null,
      studentPerk: model.studentPerk.trim() || null,
      imageId: model.imageId,
      hours,
      revision: this.place()?.revision,
    });
  }

  /** The editor's rows as API intervals, or null when a time cannot be read. */
  private collectHours(): PlaceHours[] | null {
    const hours: PlaceHours[] = [];
    for (const day of this.model.days) {
      for (const interval of day.intervals) {
        const opens = parseMinute(interval.opens);
        const closes = parseMinute(interval.closes);
        if (opens === null || closes === null) return null;
        hours.push({ day: day.day, opens, closes });
      }
    }
    return hours;
  }

  private validate(model: PlaceFormModel, hours: PlaceHours[] | null): Record<string, string> {
    const errors: Record<string, string> = {};
    const text: [keyof PlaceFormModel, number][] = [['name', 150], ['description', 3000], ['locality', 120], ['address', 300]];
    for (const [field, max] of text) {
      const value = String(model[field] ?? '').trim();
      if (!value || value.length > max) errors[field] = FIELD_MESSAGES[field];
    }

    if (model.latitude === null || !(model.latitude >= -90 && model.latitude <= 90)) errors['latitude'] = FIELD_MESSAGES['latitude'];
    if (model.longitude === null || !(model.longitude >= -180 && model.longitude <= 180)) errors['longitude'] = FIELD_MESSAGES['longitude'];
    if (model.phone.trim() && !/^\+?[0-9][0-9\s-]{6,29}$/.test(model.phone.trim())) errors['phone'] = FIELD_MESSAGES['phone'];
    if (model.website.trim() && !/^https?:\/\/\S+$/i.test(model.website.trim())) errors['website'] = FIELD_MESSAGES['website'];
    if (model.instagram.trim() && !/^@?[A-Za-z0-9._]{1,30}$/.test(model.instagram.trim())) errors['instagram'] = FIELD_MESSAGES['instagram'];
    if (model.studentPerk.trim().length > 200) errors['studentPerk'] = FIELD_MESSAGES['studentPerk'];
    if (!model.imageId) errors['imageId'] = FIELD_MESSAGES['imageId'];

    const hoursError = hours === null ? 'כתבו שעות בפורמט 09:00.' : hoursProblem(hours);
    if (hoursError) errors['hours'] = hoursError;
    return errors;
  }

  private toModel(place: OwnerPlace | null): PlaceFormModel {
    const days: DayModel[] = DAY_NAMES.map((name, day) => ({
      day,
      name,
      intervals: (place?.hours ?? [])
        .filter((interval) => interval.day === day)
        .sort((a, b) => a.opens - b.opens)
        .map((interval) => ({ opens: formatMinute(interval.opens), closes: formatMinute(interval.closes) })),
    }));
    if (!place) {
      for (const day of days.slice(0, 5)) day.intervals = [{ opens: '09:00', closes: '18:00' }];
      days[5].intervals = [{ opens: '09:00', closes: '14:00' }];
    }

    return {
      name: place?.name ?? '',
      category: place?.category ?? 'Food',
      description: place?.description ?? '',
      locality: place?.locality ?? '',
      address: place?.address ?? '',
      latitude: place?.latitude ?? 33.2,
      longitude: place?.longitude ?? 35.5,
      phone: place?.phone ?? '',
      website: place?.website ?? '',
      instagram: place?.instagram ?? '',
      studentPerk: place?.studentPerk ?? '',
      imageId: place?.imageId ?? '',
      days,
    };
  }
}

/** The same per-day checks as the server: no zero-length interval except 24 hours, no overlap, at most three. */
export function hoursProblem(hours: PlaceHours[]): string {
  for (const interval of hours) {
    if (interval.opens === interval.closes && interval.opens !== 0) {
      return `ביום ${DAY_NAMES[interval.day]} יש טווח שנפתח ונסגר באותה שעה. לפתיחה של 24 שעות כתבו 00:00–00:00.`;
    }
  }
  for (let day = 0; day < 7; day++) {
    const ranges = hours
      .filter((interval) => interval.day === day)
      .map((interval) => {
        const length = interval.closes <= interval.opens ? MINUTES_PER_DAY - interval.opens + interval.closes : interval.closes - interval.opens;
        return { start: interval.opens, end: interval.opens + length };
      })
      .sort((a, b) => a.start - b.start);
    if (ranges.length > 3) return `ביום ${DAY_NAMES[day]} אפשר עד שלושה טווחי שעות.`;
    for (let index = 1; index < ranges.length; index++) {
      if (ranges[index].start < ranges[index - 1].end) return `ביום ${DAY_NAMES[day]} יש טווחי שעות חופפים.`;
    }
  }
  return '';
}
