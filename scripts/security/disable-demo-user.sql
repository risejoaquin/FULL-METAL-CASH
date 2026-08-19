-- Run only after a real production admin user is created and validated.
-- This removes the demo principal as a production practice.

BEGIN;

SET search_path TO pos, public;

UPDATE users
SET status = 'suspended',
    locked_until = now() + interval '100 years',
    updated_at = now()
WHERE email = 'owner@solidpos.local';

UPDATE refresh_tokens rt
SET revoked_at = now(),
    revoked_reason = 'demo_user_disabled'
FROM users u
WHERE rt.tenant_id = u.tenant_id
  AND rt.user_id = u.id
  AND u.email = 'owner@solidpos.local'
  AND rt.revoked_at IS NULL;

COMMIT;
