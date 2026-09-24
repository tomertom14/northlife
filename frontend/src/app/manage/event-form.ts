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
import { OwnerEvent, OwnerEventInput } from '../owner/owner-events-api';
import {
  CATEGORY_LABELS,
  EVENT_CATEGORIES,
  EventCategory,
  NORTHERN_LOCALITIES,
} from '../public/public-event.models';
import { fromJerusalemInput, toJerusalemInput } from '../shared/jerusalem-time';

interface EventFormModel {
  title: string;
  category: EventCategory;
  description: string;
  organizerName: string;
  tagsText: string;
  startAt: string;
  endAt: string;
  venueName: string;
  locality: string;
  address: string;
  latitude: number | null;
  longitude: number | null;
  price: number | null;
  imageId: string;
}

// Hebrew wording for fields the API reports; its own messages are English.
const FIELD_MESSAGES: Record<string, string> = {
  title: 'כתבו כותרת לאירוע, עד 150 תווים.',
  description: 'כתבו תיאור של האירוע, עד 5,000 תווים.',
  organizerName: 'כתבו מי מארגן את האירוע, עד 200 תווים.',
  venueName: 'כתבו את שם המקום, עד 200 תווים.',
  locality: 'כתבו את שם היישוב, עד 120 תווים.',
  address: 'כתבו כתובת או נקודת מפגש, עד 300 תווים.',
  tags: 'אפשר עד 10 תגיות, כל אחת עד 30 תווים.',
  startAt: 'בחרו מועד התחלה.',
  endAt: 'שעת הסיום צריכה להיות אחרי שעת ההתחלה.',
  latitude: 'קו רוחב צריך להיות בין ‎-90 ל־90.',
  longitude: 'קו אורך צריך להיות בין ‎-180 ל־180.',
  price: 'המחיר לא יכול להיות שלילי. לאירוע חינמי כתבו 0.',
  imageId: 'העלו תמונה לאירוע.',
};

const LIFECYCLE_MESSAGES: Record<string, string> = {
  'Event end time must be in the future.': 'שעת הסיום צריכה להיות בעתיד.',
  'Event end time must follow its start time.': 'שעת הסיום צריכה להיות אחרי שעת ההתחלה.',
};

let nextFormId = 0;

@Component({
  selector: 'app-event-form',
  imports: [FormsModule, EventImageUpload, LocationPicker],
  templateUrl: './event-form.html',
  styleUrl: './event-form.scss',
})
export class EventForm {
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private readonly changeDetector = inject(ChangeDetectorRef);

  /** The event being edited, or null to create one. */
  readonly event = input<OwnerEvent | null>(null);
  readonly mode = input<'owner' | 'admin'>('owner');
  readonly saving = input(false);
  readonly serverErrors = input<Record<string, string>>({});
  readonly defaultOrganizer = input('');

  readonly save = output<OwnerEventInput>();
  readonly cancel = output<void>();

  readonly formId = `event-form-${++nextFormId}`;
  readonly categories = EVENT_CATEGORIES;
  readonly categoryLabels = CATEGORY_LABELS;
  readonly localities = NORTHERN_LOCALITIES;
  readonly clientErrors = signal<Record<string, string>>({});
  model: EventFormModel = this.toModel(null);

  readonly errors = computed<Record<string, string>>(() => {
    const merged: Record<string, string> = {};
    for (const field of Object.keys(this.serverErrors())) {
      if (FIELD_MESSAGES[field]) merged[field] = FIELD_MESSAGES[field];
    }
    return { ...merged, ...this.clientErrors() };
  });
  readonly errorCount = computed(() => Object.keys(this.errors()).length);
  readonly formError = computed(() => {
    const message = this.serverErrors()['event'];
    return message ? LIFECYCLE_MESSAGES[message] ?? 'לא ניתן לשמור את האירוע במצב הנוכחי שלו.' : '';
  });
  readonly editing = computed(() => this.event() !== null);
  readonly republishWarning = computed(() => this.mode() === 'owner' && this.event()?.status === 'Published');
  readonly heading = computed(() => {
    if (this.editing()) return 'עריכת אירוע';
    return this.mode() === 'admin' ? 'אירוע חדש לפרסום מיידי' : 'אירוע חדש';
  });
  readonly submitLabel = computed(() => {
    if (this.saving()) return 'שומרים…';
    if (this.mode() === 'admin') return this.editing() ? 'שמירת השינויים' : 'פרסום באתר';
    return this.editing() ? 'שמירה ושליחה לאישור' : 'שליחה לאישור';
  });

  constructor() {
    effect(() => {
      const event = this.event();
      untracked(() => {
        this.model = this.toModel(event);
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

  clearError(field: string): void {
    if (!this.clientErrors()[field]) return;
    const { [field]: _removed, ...rest } = this.clientErrors();
    this.clientErrors.set(rest);
  }

  submit(): void {
    const errors = this.validate(this.model);
    this.clientErrors.set(errors);
    if (Object.keys(errors).length > 0) {
      afterNextRender(() => this.host.nativeElement.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus(), {
        injector: this.injector,
      });
      return;
    }

    const model = this.model;
    this.save.emit({
      title: model.title.trim(),
      description: model.description.trim(),
      category: model.category,
      venueName: model.venueName.trim(),
      locality: model.locality.trim(),
      address: model.address.trim(),
      latitude: model.latitude!,
      longitude: model.longitude!,
      startAt: fromJerusalemInput(model.startAt)!.toISOString(),
      endAt: fromJerusalemInput(model.endAt)!.toISOString(),
      price: model.price!,
      imageId: model.imageId,
      organizerName: model.organizerName.trim(),
      tags: splitTags(model.tagsText),
      revision: this.event()?.revision,
    });
  }

  private validate(model: EventFormModel): Record<string, string> {
    const errors: Record<string, string> = {};
    const text: [keyof EventFormModel, number][] = [
      ['title', 150], ['description', 5000], ['organizerName', 200],
      ['venueName', 200], ['locality', 120], ['address', 300],
    ];
    for (const [field, max] of text) {
      const value = String(model[field] ?? '').trim();
      if (!value || value.length > max) errors[field] = FIELD_MESSAGES[field];
    }

    const tags = splitTags(model.tagsText);
    if (tags.length > 10 || tags.some((tag) => tag.length > 30)) errors['tags'] = FIELD_MESSAGES['tags'];

    const start = fromJerusalemInput(model.startAt);
    const end = fromJerusalemInput(model.endAt);
    if (!start) errors['startAt'] = 'בחרו מועד התחלה תקין. שעות שדולגו במעבר לשעון קיץ אינן קיימות.';
    if (!end) errors['endAt'] = 'בחרו מועד סיום תקין. שעות שדולגו במעבר לשעון קיץ אינן קיימות.';
    else if (start && end <= start) errors['endAt'] = FIELD_MESSAGES['endAt'];
    else if (end.getTime() <= Date.now()) errors['endAt'] = 'שעת הסיום צריכה להיות בעתיד.';

    if (model.latitude === null || !(model.latitude >= -90 && model.latitude <= 90)) errors['latitude'] = FIELD_MESSAGES['latitude'];
    if (model.longitude === null || !(model.longitude >= -180 && model.longitude <= 180)) errors['longitude'] = FIELD_MESSAGES['longitude'];
    if (model.price === null || !(model.price >= 0)) errors['price'] = FIELD_MESSAGES['price'];
    if (!model.imageId) errors['imageId'] = FIELD_MESSAGES['imageId'];
    return errors;
  }

  private toModel(event: OwnerEvent | null): EventFormModel {
    if (event) {
      return {
        title: event.title,
        category: event.category,
        description: event.description,
        organizerName: event.organizerName,
        tagsText: event.tags.join(', '),
        startAt: toJerusalemInput(event.startAt),
        endAt: toJerusalemInput(event.endAt),
        venueName: event.venueName,
        locality: event.locality,
        address: event.address,
        latitude: event.latitude,
        longitude: event.longitude,
        price: event.price,
        imageId: event.imageId,
      };
    }
    const start = new Date(Date.now() + 24 * 60 * 60 * 1000);
    start.setMinutes(0, 0, 0);
    return {
      title: '',
      category: 'Culture',
      description: '',
      organizerName: this.defaultOrganizer(),
      tagsText: '',
      startAt: toJerusalemInput(start),
      endAt: toJerusalemInput(new Date(start.getTime() + 2 * 60 * 60 * 1000)),
      venueName: '',
      locality: '',
      address: '',
      latitude: 33.2,
      longitude: 35.5,
      price: 0,
      imageId: '',
    };
  }
}

function splitTags(text: string): string[] {
  return text.split(',').map((tag) => tag.trim()).filter(Boolean);
}
