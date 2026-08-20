import {
  anyMembershipHasCapability,
  canManageApiKeys,
  canManageInvitations,
  capabilitiesForMembershipRole,
  hasCapability
} from './tenant-capabilities';

describe('tenant-capabilities', () => {
  it('grants Owner ManageInvitations and ManageApiKeys from membership role only', () => {
    const caps = capabilitiesForMembershipRole('Owner');
    expect(caps.has('ViewTenant')).toBeTrue();
    expect(caps.has('ManageInvitations')).toBeTrue();
    expect(caps.has('ManageApiKeys')).toBeTrue();
    expect(canManageInvitations('Owner')).toBeTrue();
    expect(canManageApiKeys('Owner')).toBeTrue();
  });

  it('does not grant Member invitation or API key management', () => {
    expect(canManageInvitations('Member')).toBeFalse();
    expect(canManageApiKeys('Member')).toBeFalse();
    expect(hasCapability('Member', 'ViewTenant')).toBeTrue();
  });

  it('treats unknown roles as view-only (safe default)', () => {
    expect(canManageInvitations('SuperAdmin')).toBeFalse();
    expect(hasCapability('', 'ViewTenant')).toBeTrue();
  });

  it('aggregates capability across memberships without using IdP roles', () => {
    const memberships = [
      { role: 'Member' },
      { role: 'Owner' }
    ];
    expect(anyMembershipHasCapability(memberships, 'ManageInvitations')).toBeTrue();
    expect(anyMembershipHasCapability([{ role: 'Member' }], 'ManageInvitations')).toBeFalse();
  });
});
