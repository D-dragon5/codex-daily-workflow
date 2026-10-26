# Public source and release rules

This directory is a public distribution. Keep all user data and machine-specific
configuration outside it. Use synthetic examples in source, documentation,
screenshots and tests. Do not copy a working installation into this tree.

Before every GitHub push or release:

1. Review the complete change, filenames and newly tracked files for personal
   tasks, names, course or calendar details, chat/automation identifiers,
   credentials, server addresses, local paths and logs. Automated scanning does
   not establish that all semantic private information has been removed.
2. Run the public tests and `python tools/audit_release.py . --git-index
   --git-history`. If a local private-values list is configured, pass that file
   with `--private-values-file`; keep the list outside this repository and never
   print its contents or matched values.
3. Build packages with `python scripts/package.py` and audit the resulting ZIPs
   with `python tools/audit_release.py . --archives artifacts`. Use the same
   external private-values list when available. Stop on any finding.
4. Inspect the final diff and package inventory before pushing or uploading.
   Preserve public author credits and upstream license notices.

Enable the local pre-push check with `git config core.hooksPath .githooks`.
Optional local-only settings are `workflow.auditPython` and
`workflow.privateValuesFile`. Never commit their machine-specific values.

Maintain the public configuration and stable task-ID interface. Changes must not
read or modify another installation's ledger, display preferences or schedules.
