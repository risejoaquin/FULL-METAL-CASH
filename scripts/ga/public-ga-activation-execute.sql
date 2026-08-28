\set tenant_uuid :tenant_id
\set confirmation_phrase :confirmation_phrase

SELECT (:'confirmation_phrase' = 'ACTIVATE_PUBLIC_GA') AS confirmation_ok \gset
\if :confirmation_ok
\else
  \echo 'PUBLIC_GA_ACTIVATION_CONFIRMATION_FAILED'
  \quit 20
\endif

BEGIN;

UPDATE pos.tenant_configs
SET feature_flags = jsonb_set(
      jsonb_set(
        jsonb_set(
          jsonb_set(
            jsonb_set(
              jsonb_set(COALESCE(feature_flags, '{}'::jsonb), '{generalAvailabilityActivated}', 'true'::jsonb, true),
              '{publicGeneralAvailabilityActivated}', 'true'::jsonb, true),
            '{publicGaActivation}', '"ACTIVATED"'::jsonb, true),
          '{rolloutStage}', '"public_ga"'::jsonb, true),
        '{publicGaActivationSource}', '"PUBLIC-GA-ACTIVATION-EXECUTION"'::jsonb, true),
      '{publicGaActivatedAt}', to_jsonb(now()::text), true),
    version = version + 1,
    updated_at = now()
WHERE tenant_id = :'tenant_uuid'::uuid
  AND COALESCE((feature_flags->>'publicGeneralAvailabilityActivated')::boolean, false) = false;

SELECT (
  SELECT count(*)
  FROM pos.tenant_configs
  WHERE tenant_id = :'tenant_uuid'::uuid
    AND COALESCE((feature_flags->>'generalAvailabilityActivated')::boolean, false) = true
    AND COALESCE((feature_flags->>'publicGeneralAvailabilityActivated')::boolean, false) = true
    AND feature_flags->>'publicGaActivation' = 'ACTIVATED'
    AND feature_flags->>'rolloutStage' = 'public_ga'
) = 1 AS persisted_state_ok \gset

\if :persisted_state_ok
  COMMIT;
  SELECT 'PUBLIC_GA_ACTIVATION_EXECUTED';
\else
  ROLLBACK;
  \echo 'PUBLIC_GA_ACTIVATION_PERSISTENCE_GUARD_FAILED'
  \quit 21
\endif
