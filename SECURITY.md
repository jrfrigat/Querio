# Security Policy

## Supported versions

Querio is pre-1.0; only the latest released version on NuGet receives security fixes.

| Version | Supported |
| ------- | --------- |
| latest  | yes       |
| older   | no        |

## Reporting a vulnerability

**Please do not report security vulnerabilities through public GitHub issues.**

Use GitHub's private reporting instead:

1. Go to the repository's **Security** tab.
2. Click **Report a vulnerability**.
3. Describe the issue, the affected version(s), and how to reproduce it.

We aim to acknowledge a report within a few days and will keep you updated on the fix and the
disclosure.

## What is in scope

Querio builds queries and never executes them, which shapes what a vulnerability looks like here.
The things worth reporting:

- **A value reaching query text.** Every value is meant to travel as data and be parameterised by
  the renderer. If any input can end up spliced into rendered SQL or into a 1C query, that is an
  injection and it is the most serious class of bug this project can have.
- **Identifier escaping.** Entity, field and function names come from a caller-supplied schema. If a
  name can break out of its quoting - or, for 1C, slip past the identifier check that exists because
  1C cannot quote names at all - that is in scope.
- **A parser that can be made to consume unbounded time or memory.** `Querio.Language`,
  `Querio.Http` and `Querio.Text` all read untrusted text.

## What is not

- The contents of a schema you supply. Querio renders what the schema says; deciding which entities
  and fields a given user may query is the host application's job.
- Anything about connecting to a database. Querio opens no connections and references no driver.
