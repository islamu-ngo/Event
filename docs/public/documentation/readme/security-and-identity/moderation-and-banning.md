# Moderation and banning

Moderation restrictions apply to a qualified subject, jurisdiction and action.
A publishing restriction is not an instance-wide login or admission ban.
Independent contributions retain their original scopes and expiry; reversing
one contribution does not remove another applicable restriction.

The shared moderation engine records decisions and minimized change intents in
the local primary transaction. Retries preserve the accepted result instead of
creating another restriction. Permission must remain current through that
transaction, and an unavailable authority is not an unrestricted result.
Private evidence belongs to separate custody, not change intents or telemetry.

A proposal must refer to the current open case. A correction replaces only the
named contribution, requires authority over its old and new scopes, and cannot
widen the restriction's reach. Scheduled state is reconciled from the current
time, but an expired restriction stops applying at its exact end even before a
maintenance sweep. A review deadline alone does not expire a restriction.

## Implementation and activation

The current programme is building the authoritative ledger and native
integration. Installing its schema does not enable a policy, replace existing
actor protection or certify a deployment as protected.

Installation identity is recorded separately from tenant qualification. Changing
request coordinates or repeating setup cannot silently replace the recorded
trust ancestry. The selected maintenance principal must already exist; creating
the binding or a qualification revision does not grant it permission.

The native installation operation requires a current human instance
administrator and checks the selected service and tenant in the transaction.
User-owned API keys cannot borrow their owner's administrator privilege.
An identical repeat preserves the established identities and current revision;
conflicting coordinates do not overwrite historical ancestry.

A final finding is tied to the actual case and rule revision, with a response
or qualified urgent-protection reference. Reversal preserves the original
record and adds its accountable correction. These checked foundations do not
by themselves enable the new moderation runtime.

Review requires current case-specific controller and conflict qualification.
Two accounts controlled by the same human do not become two independent
approvals. Qualification expires at its exact boundary, and withdrawn assignment
cannot be restored by renewing old evidence.

Adopting rule definitions is separate from activating restrictions. An active
publication needs current prerequisite and complete consumer evidence for its
effects; an identifier or administrator role alone is insufficient. Published
meaning is retained across replacement revisions, and incomplete profiles do
not become active by omitting unsupported effects.

Qualification schemas preserve case ownership, reviewer tenant boundaries and
one current policy head per jurisdiction. Database constraints and concurrent
revision checks reject malformed or stale qualification updates. Applying these
migrations does not adopt live evidence, register enforcement consumers or
activate a moderation profile.

Native source changes retain accountable case-specific evidence and an exact
operation receipt. They check the current human's actual administrative
assignment rather than a claimed role, and user-owned API keys cannot borrow
that privilege. A subject records their own response without an administrator
fabricating it; administrative privilege and active membership are not response
prerequisites. Controller and conflict qualification must be current and
independent, with validity bounded by the adopted policy.

The source update, qualification revision and receipt commit together. An
identical retry by a currently authorized human returns the original result;
reusing the operation identity for different content does not create another
update. Policy replacement retains adopted rule meaning and remains inactive
until complete activation qualification succeeds. These source operations do
not register an enforcement consumer or enable a moderation profile.

Private resource downloads and redirects recheck current native authority
immediately before releasing their prepared content or destination. If authority
or the attachment changes, the preparation is discarded rather than disclosed
using an earlier permission check. Audience detail and collection responses also
retain their metadata privately until a fresh check at response execution,
including revocation after links were prepared. A stale private representation
is refused rather than serialized. These checks do not enable a moderation
profile: current machine-source and canonical moderation enforcement still
require complete qualification.

Management pages, audit and metadata exports also keep their representations
private until final authority checks. Audit pages are refused if retained entries
expire or retention policy changes during preparation. Exports recheck the
download decisions behind their links; retry a refused projection to obtain
current metadata rather than reusing its prepared result.

Machine requests retain their exact API-key ID, owning authority tenant and
owner coordinates and credential authority, without inheriting the owner's human identity. Audience
continuations and prepared responses cannot transfer between those identities.
Existing audience cursors must be refreshed from the first page after this
change; they are paging positions, never transferable permission grants.

Prepared machine responses also recheck the current key source before delivery.
Revoked, suspended, expired or deleted persisted keys, changed ownership or
changed authority tenancy refuse the response. Configured clients must remain
uniquely configured, active, unexpired and bound to the same owner. A response
cannot switch between persisted and configured credentials with matching key
names. These source checks do not complete canonical owner qualification or
enable a moderation profile.

User-owned credentials must also retain their actual user and active membership
in the credential's tenant. Deleting the user or removing that membership
prevents a prepared resource response from being delivered even if its key is
still Active. The machine does not acquire the user's roles or human identity.
Instance-administrator-owned credentials must remain globally owned by a
current user with a current platform-administrator assignment. Removing that
assignment or deleting its user also prevents delivery of an already prepared
response, even when the key remains Active. This ownership check does not grant
administrator privileges to the machine.

Every tenant-bound credential must retain an active owning tenant, including
when it reads a public resource in another tenant. Suspending, archiving or
purging the credential's tenant prevents a prepared response from being
delivered while leaving the other tenant's resource available to qualified
callers. Tenant-owned keys must name that exact owning tenant.

Organization- and group-owned credentials also require the actual owner and
its current, nonsuspended placement in the credential's tenant. Deleting the
owner, removing its placement or suspending that placement prevents a prepared
response from being delivered even while the key remains Active. This checks
ownership, without borrowing an issuer's human management permission.

Installing moderation alone does not change resource access; existing
resource authorization remains in force until the applicable ReadResource
policy is adopted for enforcement. After adoption, missing canonical identity
or unavailable moderation authority prevents disclosure. Inactive definitions
do not activate restrictions, and unrestricted moderation does not grant access.
Adoption still requires current canonical identity. Withdrawing support for new
activations does not remove existing sanctions or restore restricted resource access.

Canonical machine moderation uses an explicit binding to an existing service
identity, not the key owner's human identity. A replacement key can select the
same service so changing credentials does not create an unrelated moderation
subject. The native installation command requires current instance-administrator
authority and verifies the selected credential and service; keys cannot install
their own bindings. Credential ownership and source are pinned, and a revoked
binding cannot be reinstated by repeating installation.

Configured keys also need explicit service selection; matching names do not
establish identity. Missing, revoked or unavailable bindings cannot establish
canonical machine provenance. These installation foundations do not activate
a protected machine consumer or a moderation profile: canonical service
complete consumer conformance and activation qualification are still required.

Resource upload-session responses also require current read authority. This
includes reservations, finalization, cancellation and their idempotent replays,
even when finalization or cancellation uses the generic storage routes. Permission
to change a resource does not automatically permit reading its stored upload
metadata. A committed upload operation can succeed while its response is withheld
after read authority changes; keep the same operation identity when recovering
and requalify access instead of treating a lost response as a fresh upload.

Native upload commands return `StorageUploadSessionResult`. Ordinary uploads
retain `Response`; resource-owned success retains only `Projection`. Dispose the
result, prepare the representation privately, and inspect the terminal outcome
before transferring fields. The HTTP success envelope remains unchanged.
This response boundary does not certify complete moderation activation.

Embedded native integrations must consume audience and management query `Projection` through
`DiscloseAsync` instead of reading `Value`. Build the representation privately
and transfer it only in the delivery callback; check the terminal outcome.
Existing HTTP routes and response shapes remain the same.

Activation requires the adopted profile's complete registered consumers,
current reviewer qualification, usable protected remedies, consequence owners
and required operator prerequisites. Imported or edited drafts remain inert
until that complete activation check succeeds. A reference profile does not
supply deployment-specific legal, staffing or safeguarding adoption.
