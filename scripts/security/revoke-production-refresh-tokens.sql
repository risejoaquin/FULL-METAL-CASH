-- SolidPOS Iteration 21 — revoke active refresh tokens after JWT/credential rotation.
-- Run only after confirming a real production admin can log in.

UPDATE pos.refresh_tokens
SET revoked_at = now(), revoked_reason = 'production_security_closure_rotation'
WHERE revoked_at IS NULL;
