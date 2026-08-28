# PILOT-03 GO / NO-GO

## GO

PILOT-03 is GO when:

- dashboard validation passes;
- health and readiness pass;
- admin login passes;
- protected metrics pass;
- terminal registration passes;
- cash shift opens;
- current open shift resolves correctly;
- cash in/cash out/no-sale drawer movement pass;
- two cash sales are reflected in summary;
- counted cash equals expected cash;
- difference is zero;
- audit events exist;
- SQL assertion returns PASS and GO.

## NO-GO

PILOT-03 is NO-GO when:

- any production endpoint fails;
- expected cash arithmetic is inconsistent;
- shift cannot close;
- final difference is not zero;
- audit trail is missing;
- SQL assertion returns NO-GO or fails.
