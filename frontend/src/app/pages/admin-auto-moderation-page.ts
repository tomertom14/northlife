import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  AutoModerationApi,
  AutoModerationMode,
  AutoModerationOverview,
  AutoModerationRun,
  AutoModerationSettings,
  MODE_LABELS,
  describeRunSummary,
} from '../admin/auto-moderation-api';
import { AdminNav } from '../manage/admin-nav';
import { formatLongDate, formatTime } from '../shared/jerusalem-time';
import { problemCode, problemFieldErrors } from '../shared/problem-details';
import { ToastService } from '../shared/toast';

type NumericSetting = {
  [Key in keyof AutoModerationSettings]: AutoModerationSettings[Key] extends number ? Key : never;
}[keyof AutoModerationSettings];

interface NumberField {
  key: NumericSetting;
  label: string;
  unit: string;
  min: number;
  max: number;
  step?: number;
}

@Component({
  selector: 'app-admin-auto-moderation-page',
  imports: [FormsModule, AdminNav],
  templateUrl: './admin-auto-moderation-page.html',
  styleUrls: ['./admin-people.scss', './admin-auto-moderation-page.scss'],
})
export class AdminAutoModerationPage implements OnInit {
  private readonly api = inject(AutoModerationApi);
  private readonly toast = inject(ToastService);

  readonly overview = signal<AutoModerationOverview | null>(null);
  readonly loading = signal(true);
  readonly failed = signal(false);
  readonly saving = signal(false);
  readonly running = signal(false);
  readonly lastRun = signal('');
  readonly errors = signal<Record<string, string>>({});
  readonly modeLabels = MODE_LABELS;
  readonly summary = describeRunSummary;
  readonly nextRun = computed(() => {
    const next = this.overview()?.nextRunAt;
    return next ? this.when(next) : null;
  });

  readonly modes: { value: AutoModerationMode; hint: string }[] = [
    { value: 'Off', hint: 'השירות לא רץ. כל אירוע מחכה לאישור ידני.' },
    { value: 'NotesOnly', hint: 'ההרצה היומית רושמת על כל אירוע אם הוא עומד בתנאים, אבל לא מפרסמת. מתאים לתקופת ניסיון.' },
    { value: 'Approve', hint: 'אירוע שעומד בכל התנאים מתפרסם. כל השאר נשארים בתור עם הסבר.' },
  ];
  readonly ownerFields: NumberField[] = [
    { key: 'minAccountAgeDays', label: 'ותק מינימלי של החשבון', unit: 'ימים', min: 0, max: 365 },
    { key: 'minApprovedEvents', label: 'אירועים מפורסמים לפחות', unit: 'אירועים', min: 0, max: 100 },
    { key: 'rejectionLookbackDays', label: 'בלי אירוע שנדחה ב־', unit: 'ימים אחרונים', min: 0, max: 730 },
    { key: 'maxAutoApprovalsPerOwnerPerDay', label: 'מכסת אישורים אוטומטיים', unit: 'ביום', min: 1, max: 100 },
  ];
  readonly placeFields: NumberField[] = [
    { key: 'maxDaysAhead', label: 'מתחיל בתוך', unit: 'ימים', min: 1, max: 730 },
    { key: 'maxDurationDays', label: 'משך מקסימלי', unit: 'ימים', min: 1, max: 60 },
    { key: 'maxPrice', label: 'מחיר מקסימלי', unit: '₪', min: 0, max: 100000 },
  ];
  readonly duplicateFields: NumberField[] = [
    { key: 'duplicateSimilarity', label: 'דמיון טקסט מינימלי', unit: 'בין 0.5 ל־1', min: 0.5, max: 1, step: 0.01 },
    { key: 'duplicateDistanceMeters', label: 'מרחק מקסימלי', unit: 'מטרים', min: 0, max: 10000 },
  ];

  form: AutoModerationSettings | null = null;
  bannedWordsText = '';

  ngOnInit(): void {
    this.api.overview().subscribe({
      next: (overview) => {
        this.show(overview, true);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.failed.set(true);
      },
    });
  }

  save(): void {
    if (!this.form) return;
    const settings: AutoModerationSettings = {
      ...this.form,
      bannedWords: this.bannedWordsText.split('\n').map((word) => word.trim()).filter((word) => word.length > 0),
    };
    this.saving.set(true);
    this.errors.set({});
    this.api.save(settings).subscribe({
      next: (overview) => {
        this.saving.set(false);
        this.show(overview, true);
        this.toast.success('ההגדרות נשמרו.');
      },
      error: (error: HttpErrorResponse) => {
        this.saving.set(false);
        if (error.status === 401) return;
        if (error.status === 400) {
          this.errors.set(problemFieldErrors(error));
          this.toast.error('יש שדות שצריך לתקן.');
          return;
        }
        this.toast.error('השמירה נכשלה. נסו שוב בעוד רגע.');
      },
    });
  }

  run(): void {
    this.running.set(true);
    this.api.run().subscribe({
      next: (summary) => {
        this.running.set(false);
        const text = describeRunSummary(summary);
        this.lastRun.set(text);
        this.toast.success(text);
        // Refresh the runs list and the next run, without discarding unsaved edits in the form.
        this.api.overview().subscribe({ next: (overview) => this.show(overview, false), error: () => undefined });
      },
      error: (error: HttpErrorResponse) => {
        this.running.set(false);
        if (error.status === 401) return;
        const code = problemCode(error);
        this.toast.error(
          code === 'run_in_progress'
            ? 'הרצה אחרת כבר פועלת. נסו שוב בעוד רגע.'
            : code === 'mode_off'
              ? 'השירות כבוי. בחרו מצב, שמרו ונסו שוב.'
              : 'ההרצה נכשלה. נסו שוב בעוד רגע.',
        );
      },
    });
  }

  when(value: string): string {
    return `${formatLongDate(value)}, ${formatTime(value)}`;
  }

  trigger(run: AutoModerationRun): string {
    return run.trigger === 'Scheduled' ? 'הרצה יומית' : `הרצה ידנית${run.triggeredByName ? ` של ${run.triggeredByName}` : ''}`;
  }

  rangeError(field: NumberField): string {
    return `הערך צריך להיות בין ${field.min} ל־${field.max}.`;
  }

  private show(overview: AutoModerationOverview, resetForm: boolean): void {
    this.overview.set(overview);
    if (resetForm) {
      this.form = { ...overview.settings };
      this.bannedWordsText = overview.settings.bannedWords.join('\n');
    }
  }
}
