# Security Policy

## Reporting
Report any suspected exposure of secrets, credentials, or controlled data, or any
security vulnerability, to **t.waweru@577industries.com**. Do not open a public issue
for sensitive reports.

## Repository guarantees
- **No secrets in source.** CI runs a secret scan on every push; `.gitignore` blocks
  `.env`, `*.key`, `*.pem`, and `secrets/`.
- **No real or controlled data.** See [EXCLUSIONS.md](EXCLUSIONS.md). The mission-planning
  component is synthetic and unclassified.
- **Offline by default.** `make demo` runs with no network and no API keys (deterministic
  transcript-replay mode), so reviewers never need to supply credentials.
- **Supply chain.** A CycloneDX SBOM is generated for every build (`make sbom`); dependencies
  are pinned (`global.json`, lockfiles). The cATO overlay itself scans the *surrogate* for
  weak cryptography and hardcoded secrets as a demonstration capability.
