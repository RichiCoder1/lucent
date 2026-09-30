# Lucent.Templates

Four standard .NET templates for Lucent: `lucent-app`, `lucent-library`,
`lucent-tests`, and `lucent-component`. The template package participates in the
coordinated Lucent release inventory and consumer checks. Use a version from a
successfully published release; a local package is development evidence.

Generation runs no post-actions. The selected package stamps exact Lucent and
.NET SDK pins. Restore explicitly after configuring the supported package feed,
then commit the generated lock file. No credentials, machine-specific paths, or
editor configuration are included.

See https://github.com/RichiCoder1/lucent/blob/main/docs/TEMPLATES.md for scope,
commands, and maintainer checks.
