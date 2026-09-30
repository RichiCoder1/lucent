# Lucent.Templates

Four standard .NET templates for Lucent: `lucent-app`, `lucent-library`,
`lucent-tests`, and `lucent-component`. This local first slice is awaiting
integration into the compatible release inventory; it is not a published
onboarding release.

Generation runs no post-actions. The selected package stamps exact Lucent and
.NET SDK pins. Restore explicitly after configuring the supported package feed,
then commit the generated lock file. No credentials, machine-specific paths, or
editor configuration are included.

See https://github.com/RichiCoder1/lucent/blob/main/docs/TEMPLATES.md for scope,
commands, and maintainer checks.
