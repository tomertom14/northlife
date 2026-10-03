import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import {
  AutoModerationApi,
  AutoModerationSettings,
  autoReviewNote,
  describeReason,
  describeRunSummary,
} from './auto-moderation-api';

const settings: AutoModerationSettings = {
  mode: 'Approve',
  runAt: '07:00',
  minAccountAgeDays: 7,
  minApprovedEvents: 3,
  rejectionLookbackDays: 90,
  maxAutoApprovalsPerOwnerPerDay: 5,
  maxDaysAhead: 180,
  maxDurationDays: 14,
  maxPrice: 1000,
  duplicateSimilarity: 0.85,
  duplicateDistanceMeters: 1000,
  bannedWords: ['קזינו'],
};

describe('AutoModerationApi', () => {
  let api: AutoModerationApi;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(AutoModerationApi);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('reads the overview, saves settings and starts a run', () => {
    api.overview().subscribe();
    expect(http.expectOne('/api/admin/auto-moderation').request.method).toBe('GET');

    api.save(settings).subscribe();
    const save = http.expectOne('/api/admin/auto-moderation/settings');
    expect(save.request.method).toBe('PUT');
    expect(save.request.body).toEqual(settings);

    api.run().subscribe();
    expect(http.expectOne('/api/admin/auto-moderation/run').request.method).toBe('POST');
  });
});

describe('describeReason', () => {
  it('writes each hold reason as a Hebrew sentence with its numbers', () => {
    expect(describeReason({ code: 'owner_few_approvals', values: { have: 1, need: 3 } })).toBe('לבעל העסק פורסם אירוע אחד (נדרשים 3)');
    expect(describeReason({ code: 'owner_few_approvals', values: { have: 0, need: 3 } })).toBe('לבעל העסק עוד אין אירועים שפורסמו (נדרשים 3)');
    expect(describeReason({ code: 'owner_new_account', values: { days: 0, need: 7 } })).toBe('החשבון נפתח היום (נדרשים 7 ימים)');
    expect(describeReason({ code: 'contact_details', values: { kind: 'phone' } })).toBe('בטקסט יש מספר טלפון');
    expect(describeReason({ code: 'possible_duplicate', values: { title: 'ערב ג׳אז', similarity: 0.97 } })).toBe(
      'דומה מאוד ל״ערב ג׳אז״, באותו יום ובאותו מקום',
    );
    expect(describeReason({ code: 'outside_region' })).toBe('המיקום מחוץ לאזור הצפון');
  });

  it('falls back to the code for a reason it does not know', () => {
    expect(describeReason({ code: 'something_new' })).toBe('something_new');
  });
});

describe('autoReviewNote', () => {
  const review = (outcome: 'Approved' | 'WouldApprove' | 'Held') => ({
    outcome,
    reasons: outcome === 'Held' ? [{ code: 'outside_region' }, { code: 'contact_details', values: { kind: 'link' } }] : [],
    decidedAt: '2026-10-01T04:00:00Z',
  });

  it('lists the reasons on a held pending event', () => {
    const note = autoReviewNote({ status: 'Pending', autoReview: review('Held') });
    expect(note?.tone).toBe('held');
    expect(note?.lines).toEqual(['המיקום מחוץ לאזור הצפון', 'בטקסט יש קישור']);
  });

  it('marks a pending event that passed every term in notes-only mode', () => {
    expect(autoReviewNote({ status: 'Pending', autoReview: review('WouldApprove') })?.tone).toBe('pass');
  });

  it('says nothing without a current review or once the event is no longer pending', () => {
    expect(autoReviewNote({ status: 'Pending', autoReview: null })).toBeNull();
    expect(autoReviewNote({ status: 'Published', autoReview: review('Approved') })).toBeNull();
  });
});

describe('describeRunSummary', () => {
  it('reports approvals in approve mode and passes in notes-only mode', () => {
    expect(describeRunSummary({ mode: 'Approve', checked: 8, approved: 3, wouldApprove: 0, held: 5 })).toBe(
      'נבדקו 8 אירועים: 3 אושרו, 5 נשארו לבדיקה.',
    );
    expect(describeRunSummary({ mode: 'NotesOnly', checked: 1, approved: 0, wouldApprove: 1, held: 0 })).toBe(
      'נבדק אירוע אחד: אחד עומד בכל התנאים, 0 נשארו לבדיקה.',
    );
    expect(describeRunSummary({ mode: 'Approve', checked: 10, approved: 1, wouldApprove: 0, held: 9 })).toBe(
      'נבדקו 10 אירועים: אחד אושר, 9 נשארו לבדיקה.',
    );
    expect(describeRunSummary({ mode: 'Approve', checked: 0, approved: 0, wouldApprove: 0, held: 0 })).toBe(
      'אין אירועים שממתינים לאישור.',
    );
  });
});
