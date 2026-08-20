import { userFacingApiError } from './api-error';

describe('userFacingApiError', () => {
  it('maps 409 duplicate invitation to a safe message including server error when safe', () => {
    const message = userFacingApiError({
      status: 409,
      error: { error: 'A pending invitation already exists for this tenant and email.' }
    });
    expect(message).toContain('pending invitation already exists');
  });

  it('maps 403 without leaking bodies', () => {
    const message = userFacingApiError({
      status: 403,
      error: { error: 'Caller must be Owner', stack: 'at Foo.cs:12' }
    });
    expect(message).toBe('You do not have permission to perform this action.');
  });

  it('maps 401 to session expired', () => {
    expect(userFacingApiError({ status: 401 })).toContain('session');
  });

  it('rejects stack-trace-like server messages', () => {
    const message = userFacingApiError({
      status: 400,
      error: { error: 'System.InvalidOperationException at Platform.Api.Controllers' }
    });
    expect(message).not.toContain('InvalidOperationException');
    expect(message).toContain('invalid');
  });
});
