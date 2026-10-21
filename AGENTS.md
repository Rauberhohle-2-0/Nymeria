# Agent instructions

Read [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) and
[docs/GIT_GUIDELINES.md](docs/GIT_GUIDELINES.md) before changing this repository.
They are the authoritative project instructions.

- The human maintainer owns architecture and scope.
- Request human approval before changing architecture, dependencies, repository
  settings, or workflow policy.
- Do not add speculative scaffolding, placeholder C# sources or projects, or
  unrequested implementation.
- Keep Nymeria in one Git monorepo.
- The only approved automatic merge is the Dependabot auto-merge workflow, which
  is gated on `Repository checks`. Do not extend it without approval.
- Verify every change, commit small logical changes, push them, and confirm the
  actual GitHub Actions result. Never claim an unverified push or check result.
- Follow [docs/SECURITY.md](docs/SECURITY.md) and use
  [docs/SECURITY_CHECKLIST.md](docs/SECURITY_CHECKLIST.md) for changes that touch
  untrusted input, native or GPU resources, dependencies, or automation.
- Never force-push or rewrite published history without explicit approval.

## Git Commit Attribution

- Do not add AI agents as commit authors or committers.
- Do not add YOURSELF as commit authors or committers.
- Do not add `Co-authored-by` trailers identifying AI tools.
- Preserve the repository's configured Git author identity.
- Never modify Git author or committer configuration.
- Never rewrite Git history without explicit approval.
