# Post-GA-12 Decision Record

Decision options:

```text
GO_CONTROLLED_GA_ROLLOUT
CAPACITY_SCALE_UP
NO_GO_REMEDIATION
```

Default recommended path after GA-12 is `GO_CONTROLLED_GA_ROLLOUT`, with capacity and DB observations kept visible.

This decision record does not flip any public launch flag and does not activate public General Availability.

Required invariant:

```text
public_ga_activation_requires_explicit_post_ga12_decision
publicGeneralAvailabilityActivated=False
generalAvailabilityActivated=False
```

The record must show that it does not flip production GA flags automatically.
