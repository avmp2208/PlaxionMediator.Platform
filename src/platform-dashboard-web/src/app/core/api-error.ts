/**
 * Maps HttpErrorResponse-like errors from Platform.Api into short user-facing
 * messages. Never surfaces raw stack traces or full response bodies.
 */
export function userFacingApiError(
  err: unknown,
  fallback = 'Something went wrong. Please try again.'
): string {
  const http = err as {
    status?: number;
    error?: unknown;
  } | null;

  const status = http?.status;
  const serverMessage = extractServerErrorMessage(http?.error);

  if (status === 0) {
    return 'Unable to reach the server. Check your connection and try again.';
  }
  if (status === 401) {
    return 'Your session has expired. Sign in again to continue.';
  }
  if (status === 403) {
    return 'You do not have permission to perform this action.';
  }
  if (status === 404) {
    return 'The requested item was not found. It may have already been removed.';
  }
  if (status === 409) {
    return serverMessage && isSafeUserMessage(serverMessage)
      ? serverMessage
      : 'This invitation conflicts with an existing one (for example a duplicate pending invite).';
  }
  if (status === 400) {
    return serverMessage && isSafeUserMessage(serverMessage)
      ? serverMessage
      : 'The request was invalid. Check the form and try again.';
  }

  return fallback;
}

function extractServerErrorMessage(body: unknown): string {
  if (!body) {
    return '';
  }
  if (typeof body === 'string') {
    // Avoid dumping HTML/problem-details dumps.
    const trimmed = body.trim();
    if (trimmed.length > 0 && trimmed.length <= 200 && !trimmed.startsWith('<')) {
      return trimmed;
    }
    return '';
  }
  if (typeof body === 'object') {
    const record = body as Record<string, unknown>;
    const error = record['error'];
    if (typeof error === 'string') {
      return error.trim();
    }
    const title = record['title'];
    if (typeof title === 'string') {
      return title.trim();
    }
  }
  return '';
}

function isSafeUserMessage(message: string): boolean {
  if (!message || message.length > 200) {
    return false;
  }
  // Reject anything that looks like a stack / exception dump.
  if (/at\s+\w+\.|Exception|StackTrace|Microsoft\.|System\./i.test(message)) {
    return false;
  }
  return true;
}
