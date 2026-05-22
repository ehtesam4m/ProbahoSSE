# Contributing to ProbahoSSE

Thank you for taking the time to contribute! 🎉

---

## Table of Contents

- [Getting Started](#getting-started)
- [How to Contribute](#how-to-contribute)
- [Development Setup](#development-setup)
- [Coding Standards](#coding-standards)
- [Commit Messages](#commit-messages)
- [Pull Request Process](#pull-request-process)
- [Reporting Bugs](#reporting-bugs)
- [Suggesting Features](#suggesting-features)

---

## Getting Started

1. **Fork** the repository
2. **Clone** your fork locally
3. Create a **feature branch** — `git checkout -b feat/my-feature`
4. Make your changes
5. Open a **Pull Request** against `main`

---

## How to Contribute

| Type | Branch prefix | Example |
|---|---|---|
| New feature | `feat/` | `feat/azure-service-bus-backplane` |
| Bug fix | `fix/` | `fix/redis-reconnect-loop` |
| Documentation | `docs/` | `docs/update-readme` |
| Refactor / housekeeping | `chore/` | `chore/extract-connection-factory` |
| Tests | `test/` | `test/stream-backplane-replay` |

---

## Development Setup

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://www.docker.com/products/docker-desktop) (for integration tests)

### Build

```bash
dotnet build ProbahoSSE.sln
```

### Run unit tests

```bash
dotnet test tests/ProbahoSSE.Tests.Unit
```

### Run integration tests

Integration tests require Docker. They spin up Redis / RabbitMQ via Testcontainers automatically.

```bash
dotnet test tests/ProbahoSSE.Tests.Integration
```

### Run a sample

```bash
cd samples/Sample.RedisPubSub
docker compose up --build
# Open http://localhost:8080
```

---

## Coding Standards

- Target **C# 13 / .NET 10**
- Follow existing code style
- All public types and members must have **XML doc comments** (`///`)
- Prefer `ILogger<T>` structured logging with named placeholders (`{Property}` not `{0}`)

---

## Commit Messages

Follow [Conventional Commits](https://www.conventionalcommits.org/):

```
feat: add Azure Service Bus backplane
fix: handle Redis reconnect race condition
docs: update RabbitMQ configuration reference
chore: extract connection factory helper
test: add replay integration test for Redis Stream
```

The CI pipeline uses commit messages to determine the next version:
- `feat:` → minor bump (e.g. `0.3.0` → `0.4.0`)
- `fix:` / `chore:` / `docs:` / `test:` → patch bump
- `BREAKING CHANGE:` in footer → major bump

> Add `[skip-tag]` anywhere in the commit message to push without triggering a release.

---

## Pull Request Process

1. Ensure all tests pass locally before opening the PR
2. Update `CHANGELOG.md` under a new `## [Unreleased]` section describing your changes
3. Update `README.md` if your change affects public APIs or configuration
4. Keep PRs focused — one feature or fix per PR
5. PRs require at least **one approving review** before merge
6. Squash merge is preferred to keep history clean

---

## Reporting Bugs

Open a [GitHub Issue](https://github.com/ehtesam4m/ProbahoSSE/issues/new) and include:

- .NET version (`dotnet --version`)
- ProbahoSSE package version
- Backplane being used (Redis PubSub / Redis Stream / RabbitMQ)
- Minimal reproduction steps
- Expected vs actual behaviour
- Any relevant logs or stack traces

---

## Suggesting Features

Open a [GitHub Issue](https://github.com/ehtesam4m/ProbahoSSE/issues/new) with the label `enhancement` and describe:

- The problem you're trying to solve
- Your proposed solution
- Any alternatives you considered

---

## Code of Conduct

This project follows the [Contributor Covenant Code of Conduct](CODE_OF_CONDUCT.md). By participating you agree to abide by its terms.

