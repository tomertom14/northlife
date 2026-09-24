import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnDestroy, inject, input, output, signal } from '@angular/core';
import { ImageUploadApi, ImageUploadResponse } from './image-upload-api';
import { PrivateImage } from './private-image';
import { EventCategory } from '../public/public-event.models';

const ACCEPTED_TYPES = ['image/jpeg', 'image/png', 'image/webp'];
const MAX_BYTES = 5 * 1024 * 1024;

@Component({
  selector: 'app-event-image-upload',
  imports: [PrivateImage],
  templateUrl: './event-image-upload.html',
  styleUrl: './event-image-upload.scss',
})
export class EventImageUpload implements OnDestroy {
  private readonly api = inject(ImageUploadApi);

  /** The event's saved image, shown until a new one is chosen. */
  readonly currentUrl = input<string | null>(null);
  readonly category = input<EventCategory>('Other');
  readonly invalid = input(false);
  readonly describedBy = input<string | null>(null);
  readonly uploaded = output<ImageUploadResponse>();

  readonly previewUrl = signal<string | null>(null);
  readonly status = signal<'idle' | 'uploading' | 'done'>('idle');
  readonly errorMessage = signal('');

  selectFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';
    this.errorMessage.set('');
    if (!file) return;

    if (!ACCEPTED_TYPES.includes(file.type)) {
      this.errorMessage.set('אפשר להעלות רק תמונות JPEG, PNG או WebP.');
      return;
    }
    if (file.size > MAX_BYTES) {
      this.errorMessage.set('התמונה גדולה מ־5MB. בחרו קובץ קטן יותר.');
      return;
    }

    this.setPreview(URL.createObjectURL(file));
    this.status.set('uploading');
    this.api.upload(file).subscribe({
      next: (result) => {
        this.status.set('done');
        this.uploaded.emit(result);
      },
      error: (error: HttpErrorResponse) => {
        this.status.set('idle');
        this.setPreview(null);
        this.errorMessage.set(
          error.status === 413
            ? 'התמונה גדולה מדי.'
            : error.error?.errors?.file?.[0] ?? 'לא הצלחנו לעבד את התמונה. בדקו את הקובץ ונסו שוב.',
        );
      },
    });
  }

  ngOnDestroy(): void {
    this.setPreview(null);
  }

  private setPreview(url: string | null): void {
    const previous = this.previewUrl();
    if (previous) URL.revokeObjectURL(previous);
    this.previewUrl.set(url);
  }
}
