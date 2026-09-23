import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';

export interface ImageUploadResponse {
  id: string;
  url: string;
  contentType: string;
  sizeBytes: number;
  width: number;
  height: number;
}

@Injectable({ providedIn: 'root' })
export class ImageUploadApi {
  constructor(private readonly http: HttpClient) {}

  upload(file: File) {
    const form = new FormData();
    form.append('file', file);
    return this.http.post<ImageUploadResponse>('/api/manage/images', form);
  }
}
