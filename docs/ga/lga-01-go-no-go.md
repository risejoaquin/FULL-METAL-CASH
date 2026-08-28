# LGA-01 Go / No-Go

## GO

GO LGA-02 when:

- Public GA remains not activated.
- Build, tests and secret scan pass.
- WPF QSR command enablement is fixed.
- Sync pending, processing and retry counts are zero.
- Sync conflicts are within known formal archive baseline.
- Dead letters are within known formal archive baseline.
- Negative stock is within baseline or adjusted.
- RLS missing table count is zero.
- Duplicate local sale count is zero.

## NO_GO

NO_GO when:

- Public GA is active.
- A new unapproved sync conflict appears.
- Dead letters exceed the archive baseline.
- Negative stock exceeds the allowed inventory baseline.
- WPF QSR command enablement fix is missing.
- RLS or duplicate local sale drift appears.
