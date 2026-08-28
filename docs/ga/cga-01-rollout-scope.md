# CGA-01 Rollout Scope

- RolloutMode: `LIMITED`
- MaxStores: `2`
- MaxConcurrentTerminals: `2`
- ObservationWindowHours: `24`
- Tenant: one production tenant only.
- Public GA: NOT ACTIVATED.

The controlled rollout is allowed only because Post-GA-12 selected `CONTROLLED_ROLLOUT`. CGA-01 does not authorize broad public launch.

Scope breach blockers:

- Active stores greater than MaxStores.
- Open shifts greater than MaxConcurrentTerminals.
- Public GA activation without explicit separate change.
