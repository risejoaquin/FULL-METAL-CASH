# GA-06 Stable Channel Promotion and Cohort Update Dry Run

GA-06 validates the stable promotion contract from the GA-05 production-proven RC. It creates or reuses the exact same release identity across internal, beta and stable and targets exactly one controlled active terminal.

The gate validates stable channel state, cohort targeting, update check behavior, release notes, signature/hash identity, rollback target, promotion audit and schema version 4. It does not download or install the package and does not authorize public rollout.

A targeted release is invisible outside its cohort. Legacy releases with no `update_release_targets` rows retain previous tenant-wide behavior for backward compatibility.
