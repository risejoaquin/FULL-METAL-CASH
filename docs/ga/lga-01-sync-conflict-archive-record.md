# LGA-01 Sync Conflict Archive Record

## Known baseline

Known sync conflicts: 3.

## Cause

The known baseline came from the first controlled PosCore sync attempt executed before the remote cash shift was opened. The events were accepted into the sync inbox but failed domain validation and remained as pending conflicts.

## Formal archive decision

Decision: FORMAL_ARCHIVE.

The conflicts are not treated as real completed financial sales. They are carried as a known baseline for Limited GA only. The baseline must not increase. If the pending sync conflicts exceed 3, LGA-01 blocks.

## Public GA impact

Public GA must not be activated while this baseline exists unless a separate remediation or explicit acceptance decision is made.
