import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { AutoModerationOverview } from '../admin/auto-moderation-api';
import { ToastService } from '../shared/toast';
import { AdminAutoModerationPage } from './admin-auto-moderation-page';

const overview = (mode: 'Off' | 'NotesOnly' | 'Approve' = 'NotesOnly'): AutoModerationOverview => ({
  settings: {
    mode,
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
    bannedWords: ['קזינו', 'הימורים'],
  },
  updatedAt: '2026-09-29T00:00:00Z',
  nextRunAt: mode === 'Off' ? null : '2026-10-01T04:00:00Z',
  runs: [
    {
      id: 'run-1', trigger: 'Scheduled', triggeredByName: null, mode: 'NotesOnly', startedAt: '2026-09-30T04:00:05Z',
      finishedAt: '2026-09-30T04:00:06Z', checked: 4, approved: 0, wouldApprove: 1, held: 3, failed: false,
    },
  ],
});

describe('AdminAutoModerationPage', () => {
  function setup(mode: 'Off' | 'NotesOnly' | 'Approve' = 'NotesOnly') {
    TestBed.configureTestingModule({
      imports: [AdminAutoModerationPage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    const fixture = TestBed.createComponent(AdminAutoModerationPage);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    http.expectOne('/api/admin/auto-moderation').flush(overview(mode));
    fixture.detectChanges();
    return { fixture, http, page: fixture.componentInstance, element: fixture.nativeElement as HTMLElement };
  }

  it('shows the mode, the next run, the terms and the recent runs', async () => {
    const { fixture, element } = setup();
    await fixture.whenStable();
    fixture.detectChanges();

    expect(element.querySelector('#status-title')?.textContent).toContain('הערות בלבד');
    expect(element.textContent).toContain('ההרצה הבאה:');
    expect((element.querySelector('#bannedWords') as HTMLTextAreaElement).value).toBe('קזינו\nהימורים');
    expect(element.querySelectorAll('.runs li').length).toBe(1);
    expect(element.querySelector('.runs .what')?.textContent).toContain('נבדקו 4 אירועים');
  });

  it('saves the settings with one banned word per line, ignoring blank lines', () => {
    const { page, http } = setup();
    page.form!.mode = 'Approve';
    page.bannedWordsText = ' קזינו \n\nהלוואות\n';
    page.save();

    const request = http.expectOne('/api/admin/auto-moderation/settings');
    expect(request.request.body.mode).toBe('Approve');
    expect(request.request.body.bannedWords).toEqual(['קזינו', 'הלוואות']);
    request.flush(overview('Approve'));
    expect(page.overview()?.settings.mode).toBe('Approve');
  });

  it('shows the server field errors in Hebrew', async () => {
    const { page, http, fixture, element } = setup();
    page.save();
    http.expectOne('/api/admin/auto-moderation/settings').flush(
      { errors: { maxPrice: ['Must be between 0 and 100000.'] }, code: 'invalid_settings' },
      { status: 400, statusText: 'Bad Request' },
    );
    fixture.detectChanges();
    await fixture.whenStable();

    expect(element.querySelector('#maxPrice')?.getAttribute('aria-invalid')).toBe('true');
    expect(element.textContent).toContain('הערך צריך להיות בין 0 ל־100000.');
  });

  it('runs now, reports the result and refreshes the runs without touching unsaved edits', () => {
    const { page, http } = setup();
    const toast = TestBed.inject(ToastService);
    page.form!.maxPrice = 250;
    page.run();

    http.expectOne('/api/admin/auto-moderation/run').flush({ runId: 'run-2', mode: 'NotesOnly', checked: 2, approved: 0, wouldApprove: 2, held: 0 });
    expect(toast.current()?.text).toBe('נבדקו 2 אירועים: 2 עומדים בכל התנאים, 0 נשארו לבדיקה.');
    http.expectOne('/api/admin/auto-moderation').flush(overview());
    expect(page.form!.maxPrice).toBe(250);
  });

  it('explains a busy run', () => {
    const { page, http } = setup();
    const toast = TestBed.inject(ToastService);
    page.run();
    http.expectOne('/api/admin/auto-moderation/run').flush({ code: 'run_in_progress' }, { status: 409, statusText: 'Conflict' });
    expect(toast.current()?.text).toBe('הרצה אחרת כבר פועלת. נסו שוב בעוד רגע.');
  });

  it('disables "run now" while the service is off', async () => {
    const { fixture, element } = setup('Off');
    await fixture.whenStable();
    fixture.detectChanges();

    const button = [...element.querySelectorAll('button')].find((candidate) => candidate.textContent?.includes('הרצה עכשיו'));
    expect(button?.disabled).toBe(true);
    expect(element.textContent).toContain('השירות כבוי. אין הרצות מתוכננות.');
  });
});
