import { HttpErrorResponse } from '@angular/common/http';

/**
 * Field messages from an ASP.NET Core validation problem, keyed by camelCase field name.
 * Handles both service errors ({ title: [...] }) and model-binding keys ("Title", "$.startAt").
 */
export function problemFieldErrors(error: unknown): Record<string, string> {
  if (!(error instanceof HttpErrorResponse)) return {};
  const errors: unknown = error.error?.errors;
  if (!errors || typeof errors !== 'object') return {};

  const result: Record<string, string> = {};
  for (const [key, messages] of Object.entries(errors as Record<string, unknown>)) {
    const field = key.replace(/^\$\./, '');
    const name = field.charAt(0).toLowerCase() + field.slice(1);
    if (Array.isArray(messages) && messages.length > 0) result[name] = String(messages[0]);
  }
  return result;
}

export function problemCode(error: unknown): string | undefined {
  return error instanceof HttpErrorResponse ? error.error?.code : undefined;
}
