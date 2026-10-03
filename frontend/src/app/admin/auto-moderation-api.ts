import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { OwnerEventStatus } from '../owner/owner-events-api';

export type AutoModerationMode = 'Off' | 'NotesOnly' | 'Approve';
export type AutoReviewOutcome = 'Approved' | 'WouldApprove' | 'Held';

export interface AutoModerationSettings {
  mode: AutoModerationMode;
  /** Israel local time of the daily run, "HH:mm". */
  runAt: string;
  minAccountAgeDays: number;
  minApprovedEvents: number;
  rejectionLookbackDays: number;
  maxAutoApprovalsPerOwnerPerDay: number;
  maxDaysAhead: number;
  maxDurationDays: number;
  maxPrice: number;
  duplicateSimilarity: number;
  duplicateDistanceMeters: number;
  bannedWords: string[];
}

export interface AutoModerationRun {
  id: string;
  trigger: 'Scheduled' | 'Manual';
  triggeredByName: string | null;
  mode: AutoModerationMode;
  startedAt: string;
  finishedAt: string | null;
  checked: number;
  approved: number;
  wouldApprove: number;
  held: number;
  failed: boolean;
}

export interface AutoModerationOverview {
  settings: AutoModerationSettings;
  updatedAt: string;
  nextRunAt: string | null;
  runs: AutoModerationRun[];
}

export interface AutoModerationRunSummary {
  runId: string;
  mode: AutoModerationMode;
  checked: number;
  approved: number;
  wouldApprove: number;
  held: number;
}

export interface AutoReviewReason {
  code: string;
  values?: Record<string, string | number> | null;
}

/** The service's latest verdict on an event's current revision. */
export interface AutoReview {
  outcome: AutoReviewOutcome;
  reasons: AutoReviewReason[];
  decidedAt: string;
}

@Injectable({ providedIn: 'root' })
export class AutoModerationApi {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api/admin/auto-moderation';

  overview() {
    return this.http.get<AutoModerationOverview>(this.baseUrl);
  }

  save(settings: AutoModerationSettings) {
    return this.http.put<AutoModerationOverview>(`${this.baseUrl}/settings`, settings);
  }

  run() {
    return this.http.post<AutoModerationRunSummary>(`${this.baseUrl}/run`, {});
  }
}

export const MODE_LABELS: Record<AutoModerationMode, string> = {
  Off: 'כבוי',
  NotesOnly: 'הערות בלבד',
  Approve: 'אישור אוטומטי',
};

const CONTACT_KINDS: Record<string, string> = { link: 'קישור', email: 'כתובת אימייל', phone: 'מספר טלפון' };

const days = (count: number): string => (count === 1 ? 'יום אחד' : `${count} ימים`);

/** One Hebrew sentence for a hold reason, with its numbers. */
export function describeReason(reason: AutoReviewReason): string {
  const values = reason.values ?? {};
  const number = (key: string) => Number(values[key] ?? 0);
  const text = (key: string) => String(values[key] ?? '');
  switch (reason.code) {
    case 'owner_suspended':
      return 'החשבון של בעל העסק מושעה';
    case 'owner_email_unconfirmed':
      return 'בעל העסק עוד לא אימת את כתובת האימייל';
    case 'owner_new_account': {
      const age = number('days') === 0 ? 'החשבון נפתח היום' : `החשבון נפתח לפני ${days(number('days'))}`;
      return `${age} (נדרשים ${days(number('need'))})`;
    }
    case 'owner_few_approvals': {
      const have = number('have');
      const published = have === 0 ? 'לבעל העסק עוד אין אירועים שפורסמו' : have === 1 ? 'לבעל העסק פורסם אירוע אחד' : `לבעל העסק פורסמו ${have} אירועים`;
      return `${published} (נדרשים ${number('need')})`;
    }
    case 'owner_recent_rejection':
      return number('daysAgo') === 0 ? 'אירוע של בעל העסק נדחה היום' : `אירוע של בעל העסק נדחה לפני ${days(number('daysAgo'))}`;
    case 'owner_daily_cap':
      return `בעל העסק הגיע למכסה של ${number('cap')} אישורים אוטומטיים ביום`;
    case 'ended':
      return 'האירוע כבר הסתיים';
    case 'outside_region':
      return 'המיקום מחוץ לאזור הצפון';
    case 'too_far_ahead':
      return `האירוע מתחיל בעוד ${days(number('days'))} (עד ${number('max')} ימים)`;
    case 'too_long':
      return `האירוע נמשך ${days(number('days'))} (עד ${number('max')} ימים)`;
    case 'price_too_high':
      return `המחיר ${number('price')} ₪ גבוה מהתקרה של ${number('max')} ₪`;
    case 'banned_word':
      return `בטקסט מופיע ״${text('word')}״ מרשימת המילים האסורות`;
    case 'contact_details':
      return `בטקסט יש ${CONTACT_KINDS[text('kind')] ?? 'פרטי קשר'}`;
    case 'possible_duplicate':
      return `דומה מאוד ל״${text('title')}״, באותו יום ובאותו מקום`;
    case 'changed_during_check':
      return 'האירוע השתנה בזמן הבדיקה';
    default:
      return reason.code;
  }
}

export interface AutoReviewNote {
  tone: 'pass' | 'held';
  title: string;
  lines: string[];
}

/** The note a pending queue card shows, or null when there is nothing current to say. */
export function autoReviewNote(event: { status: OwnerEventStatus; autoReview?: AutoReview | null }): AutoReviewNote | null {
  const review = event.autoReview;
  if (!review || event.status !== 'Pending') return null;
  if (review.outcome === 'WouldApprove') {
    return { tone: 'pass', title: 'עומד בכל התנאים. במצב "הערות בלבד" השירות לא מפרסם בעצמו.', lines: [] };
  }
  if (review.outcome === 'Held') {
    return { tone: 'held', title: 'האישור האוטומטי השאיר את האירוע לבדיקה:', lines: review.reasons.map(describeReason) };
  }
  return null;
}

/** "נבדקו 8 אירועים: 3 אושרו, 5 נשארו לבדיקה." */
export function describeRunSummary(run: Pick<AutoModerationRunSummary, 'mode' | 'checked' | 'approved' | 'wouldApprove' | 'held'>): string {
  if (run.checked === 0) return 'אין אירועים שממתינים לאישור.';
  const count = (value: number, one: string, many: string) => (value === 1 ? one : `${value} ${many}`);
  const checked = run.checked === 1 ? 'נבדק אירוע אחד' : `נבדקו ${run.checked} אירועים`;
  const passed = run.mode === 'Approve'
    ? count(run.approved, 'אחד אושר', 'אושרו')
    : count(run.wouldApprove, 'אחד עומד בכל התנאים', 'עומדים בכל התנאים');
  return `${checked}: ${passed}, ${count(run.held, 'אחד נשאר לבדיקה', 'נשארו לבדיקה')}.`;
}
