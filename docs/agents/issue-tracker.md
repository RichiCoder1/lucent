# Issue tracker: GitHub

Issues and specifications live in [GitHub Issues](https://github.com/RichiCoder1/lucent/issues). Execution work is organized in [Lucent Native Project 4](https://github.com/users/RichiCoder1/projects/4/views/1). Use the `gh` CLI for operations and add execution tickets to Project 4.

## Conventions

- Create: `gh issue create --title "..." --body "..."`
- Read: `gh issue view <number> --comments`
- List: `gh issue list --state open --json number,title,body,labels,comments`
- Comment: `gh issue comment <number> --body "..."`
- Label: `gh issue edit <number> --add-label "..."`
- Close: `gh issue close <number> --comment "..."`

Infer the repository from `git remote -v`. GitHub shares one number space across issues and pull requests, so resolve an ambiguous `#42` with `gh pr view 42` and then `gh issue view 42`.

## Pull requests as a triage surface

**PRs as a request surface: no.**

## Skill conventions

- “Publish to the issue tracker” means create a GitHub issue and add execution work to Project 4.
- “Fetch the relevant ticket” means run `gh issue view <number> --comments`.
- Use GitHub sub-issues and native issue dependencies when available. A dependency uses the blocker's numeric database ID, not its issue number or node ID.
