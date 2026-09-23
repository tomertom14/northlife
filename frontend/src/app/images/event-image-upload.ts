import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnDestroy, inject, output, signal } from '@angular/core';
import { ImageUploadApi, ImageUploadResponse } from './image-upload-api';

@Component({
  selector: 'app-event-image-upload',
  templateUrl: './event-image-upload.html',
  styleUrl: './event-image-upload.scss',
})
export class EventImageUpload implements OnDestroy {
  private readonly api = inject(ImageUploadApi);
  private selectedFile: File | null = null;

  readonly previewUrl = signal<string | null>(null);
  readonly result = signal<ImageUploadResponse | null>(null);
  readonly errorMessage = signal('');
  readonly uploading = signal(false);
  readonly uploaded = output<ImageUploadResponse>();

  selectFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0] ?? null;
    this.clearPreview();
    this.selectedFile = null;
    this.result.set(null);
    this.errorMessage.set('');

    if (!file) return;
    if (!['image/jpeg', 'image/png', 'image/webp'].includes(file.type)) {
      this.errorMessage.set('אפשר להעלות JPEG, PNG או WebP בלבד.');
      input.value = '';
      return;
    }
    if (file.size > 5 * 1024 * 1024) {
      this.errorMessage.set('גודל התמונה חייב להיות עד 5 MB.');
      input.value = '';
      return;
    }

    this.selectedFile = file;
    this.previewUrl.set(URL.createObjectURL(file));
  }

  upload(): void {
    if (!this.selectedFile) {
      this.errorMessage.set('יש לבחור תמונה.');
      return;
    }

    this.uploading.set(true);
    this.errorMessage.set('');
    this.api.upload(this.selectedFile).subscribe({
      next: (result) => {
        this.result.set(result);
        this.uploading.set(false);
        this.uploaded.emit(result);
      },
      error: (error: HttpErrorResponse) => {
        this.uploading.set(false);
        this.errorMessage.set(
          error.status === 413
            ? 'התמונה גדולה מדי.'
            : error.error?.errors?.file?.[0] ??
              'לא הצלחנו לעבד את התמונה. בדקו את הקובץ ונסו שוב.',
        );
      },
    });
  }

  ngOnDestroy(): void {
    this.clearPreview();
  }

  private clearPreview(): void {
    const current = this.previewUrl();
    if (current) URL.revokeObjectURL(current);
    this.previewUrl.set(null);
  }
}
