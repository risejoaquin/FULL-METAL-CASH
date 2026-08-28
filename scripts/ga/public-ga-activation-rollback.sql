\set tenant_uuid :tenant_id
\set confirmation_phrase :confirmation_phrase
\set rollback_reason :rollback_reason

SELECT (:'confirmation_phrase' = 'ROLLBACK_PUBLIC_GA') AS confirmation_ok \gset
\if :confirmation_ok
\else
  \echo 'PUBLIC_GA_ROLLBACK_CONFIRMATION_FAILED'
  \quit 30
\endif

BEGIN;

UPDATE pos.tenant_configs
SET feature_flags = jsonb_set(
      jsonb_set(
        jsonb_set(
          jsonb_set(
            jsonb_set(
              jsonb_set(COALESCE(feature_flags, '{}'::jsonb), '{generalAvailabilityActivated}', 'false'::jsonb, true),
              '{publicGeneralAvailabilityActivated}', 'false'::jsonb, true),
            '{publicGaActivation}', '"NOT_ACTIVATED"'::jsonb, true),
          '{rolloutStage}', '"limited_ga"'::jsonb, true),
        '{publicGaRollbackReason}', to_jsonb(:'rollback_reason'::text), true),
      '{publicGaRolledBackAt}', to_jsonb(now()::text), true),
    version = version + 1,
    updated_at = now()
WHERE tenant_id = :'tenant_uuid'::uuid;

SELECT (
  SELECT count(*)
  FROM pos.tenant_configs
  WHERE tenant_id = :'tenant_uuid'::uuid
    AND COALESCE((feature_flags->>'generalAvailabilityActivated')::boolean, false) = false
    AND COALESCE((feature_flags->>'publicGeneralAvailabilityActivated')::boolean, false) = false
    AND feature_flags->>'publicGaActivation' = 'NOT_ACTIVATED'
    AND feature_flags->>'rolloutStage' = 'limited_ga'
) = 1 AS rollback_state_ok \gset

\if :rollback_state_ok
  COMMIT;
  SELECT 'PUBLIC_GA_ACTIVATION_ROLLED_BACK';
\else
  ROLLBACK;
  \echo 'PUBLIC_GA_ROLLBACK_PERSISTENCE_GUARD_FAILED'
  \quit 31
\endif
