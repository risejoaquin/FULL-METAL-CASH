# SolidPOS GA-06 HOTFIX 06.5

## Terminal collection response normalization

GA-06.4 exposed a PowerShell collection-shape bug. The terminals endpoint returns an object whose collection is in `items`. Treating that wrapper as the terminal array caused member enumeration of `.id` to concatenate every terminal UUID, invalidating the reduced-cohort invariant and the following SQL UUID cast.

GA-06.5 adopts the existing repository `Get-Items` normalization pattern and therefore requires exactly one controlled target terminal and one distinct outside-cohort terminal before any stable promotion occurs.

This hotfix changes no production data contract, schema version, sync contract, migration, backend API, or GA activation state.
