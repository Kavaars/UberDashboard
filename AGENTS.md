
# PROJECT DEVELOPMENT RULES

## 1. EXECUTION WORKFLOW

- Follow: EXPLORE -> PLAN -> TEST -> IMPLEMENT -> VERIFY -> DOCUMENT.
- Before changing code, inspect the project structure, existing architecture, Git status, recent commits and relevant diffs.
- Explain the proposed technical approach, relevant edge cases and likely side effects before substantial changes.
- Keep updates concise. Never claim that an operation, test or build succeeded unless its result confirms success.
- Never truncate requested code with `...`. Provide complete, functional implementations and files.
- Follow existing project conventions and tooling unless there is a concrete reason to change them.
- Do not apply rules that are incompatible with the project's language, framework or architecture.

## 2. BUG REPORTS AND REGRESSION ANALYSIS

- When the user reports a problem in an existing project, create or update `problemy.md` in the project root.
- Record an ISO 8601 timestamp with timezone, application version or Git commit, affected module, concise user report, expected behavior, actual behavior and reproduction steps when known.
- Record relevant environment details and error messages. Never log API keys, passwords, access tokens or unrelated personal data.
- Before investigating unrelated causes, inspect recent commits, diffs and changes that could have introduced the problem.
- Check whether the issue correlates with a recent change. Test that hypothesis before proceeding to broader diagnosis.
- Search previous entries in `problemy.md` for duplicates, related incidents and proven solutions.
- Do not invent missing reproduction steps, versions or root causes. Mark unknown details explicitly.
- After fixing a problem, record the confirmed cause, fix and regression test in the issue log.

## 3. PERFORMANCE AND USER EXPERIENCE

- Never perform long-running or blocking operations on the UI main thread.
- Keep ordinary UI interactions responsive; target a reaction time below 50 ms where practical.
- Use async/await, workers, background tasks, batching, debouncing, incremental persistence, lazy loading and chunked rendering where appropriate to the technology stack.
- Background operations must expose real loading, progress, success, error, retry and cancellation states as applicable.
- Derive progress and status from real operations. Never use fake delays or guessed progress values.
- Interactive controls must have accessible labels and tooltips where helpful. Tooltips must explain the effect of the action or its consequences, not merely repeat the control label.
- Use a consistent global toast notification system for non-blocking feedback.
- Avoid unnecessary rendering, duplicate requests, repeated expensive computations and synchronous disk/network operations on the UI thread.
- Do not sacrifice correctness, accessibility or maintainability to meet a performance target.

## 4. LOGGING AND DIAGNOSTICS

- Use structured logs with ISO 8601 timestamps, origin module, severity and a structured payload.
- Supported severity levels: DEBUG, INFO, WARN and ERROR.
- Include relevant operation identifiers and error details while redacting secrets and sensitive data.
- Choose persistence according to the application architecture:
  - Browser UI state: IndexedDB or LocalStorage where appropriate.
  - Desktop/server diagnostics: a suitable persistent file or logging service.
- Do not assume that browser storage and filesystem logs are interchangeable.
- For applications that require a Live System Log window, implement it as a non-modal, draggable and resizable overlay with:
  - Regex search, level/module filters, auto-scroll toggle, clear and JSON export.
- Keep the interface usable while the log window is open.
- If file logging is used, write application logs under `/LOGS/`, using `.txt` files unless the existing project specifies another format.
- Implement log rotation with a configurable retention policy. Never delete user data or unrelated files during log cleanup.
- Use timestamps and real progress indicators for long-running console operations. Do not add progress bars to trivial commands.

## 5. TESTING AND VERIFICATION

- Prefer tests before implementation when practical. For bugs, first add or update a regression test that reproduces the failure.
- Test normal behavior, failure paths, edge cases and relevant regressions.
- Aim for at least 75% meaningful automated coverage of new or modified business logic where practical. Do not optimize for coverage percentage alone.
- Tests must be runnable unattended and must not require manual input.
- UI dialogs and notifications may be tested; tests must not hang waiting for a human interaction.
- Discover the project's actual scripts and toolchain before running commands.
- In Node.js projects, run `npm run typecheck` and `npm run test` when those scripts exist. Otherwise use the project's configured equivalents.
- Run relevant linting, type checks, unit tests, integration tests and build checks according to the project.
- Capture command output and check exit codes. Summarize failures without hiding diagnostics.
- If a check cannot run, explain why. Never report unexecuted checks as passing.
- Run tests again after the final code changes.

## 6. PROJECT STATE AND CHANGE HISTORY

After meaningful implementation, bug fixes or behavioral changes, update the relevant project records:

1. `listaZmian.md` — timestamp and a very short description of the change.
2. `wersje.md` — timestamp, version/build identifier and concise task summary.
3. `changelog.txt` — user-visible changes and fixes.

- Do not copy the complete user prompt into project history.
- Do not record secrets or unnecessary personal information.
- Avoid duplicate or contradictory entries between the files.
- For trivial changes that do not affect behavior, use judgment and follow the project's existing release policy.

## 7. VERSIONING AND RELEASES

- Follow semantic versioning where the project uses it.
- A bug fix intended for release normally increments the patch version; compatible features increment the minor version; breaking changes increment the major version.
- Do not increment the application version merely because a development build was started.
- Give every build a unique build identifier or output directory. Use the application version plus a timestamp or build number where appropriate.
- Update `package.json` only when it is the applicable source of truth for versioning.
- Before executing release/build scripts, determine which version changes are required by the release policy.
- Never claim a release is ready without running the applicable verification steps.

## 8. WINDOWS 11 BUILD AND AUTOMATION

Apply this section to Windows-targeted projects and only where compatible with the project's toolchain.

- Build scripts must check command exit codes and stop immediately on failure.
- Use the correct error-handling syntax for each shell: `.bat`, PowerShell and Bash are not interchangeable.
- Do not continue packaging after a failed build or test.
- Scripts that remove files must be restricted to generated output directories and must avoid user data and configuration.
- Make destructive cleanup operations explicit and provide a dry-run or confirmation option where practical.
- For projects using the following packaging convention, maintain:
  - `budujdev.bat` — build and run the unpacked development version.
  - `budujprod.bat` — build and run the production version.
  - `budujfast.bat` — build and run the unpacked version without tests; explicitly mark it as unverified.
  - `budujinst.bat` — build the installer.
  - `testuj.bat` — run all applicable tests.
  - `usun.bat` — remove obsolete generated builds according to a retention policy.
  - `folder.bat` — open the release directory.
- When this convention is adopted, use `Release/Dev/`, `Release/Prod/` and `Release/Instal/`.
- Keep application settings and user data separate from installed program files so they survive upgrades and reinstalls.
- Use `C:\Program Files\Hometree\[App]` only for products that actually belong to Hometree; otherwise follow the product's configured installation path.

## 9. ARCHITECTURE AND CODE QUALITY

- Prefer feature-oriented organization such as `/features/{feature-name}` when appropriate to the framework and application.
- Keep each feature cohesive and minimize unnecessary coupling between features.
- Avoid importing another feature's internal implementation. Prefer a stable public API, shared contract or domain event when cross-feature interaction is necessary.
- Prefer simple solutions over speculative abstractions. Reuse shared code when its behavior is stable and genuinely common; do not duplicate large or complex logic merely to avoid every shared dependency.
- Avoid circular dependencies and hidden cross-feature side effects.
- Respect `// CONTRACT (AI-READ-ONLY)` comments. Do not change protected logic without explicit user authorization.
- Keep code readable and consistent with existing conventions.
- Remove unused variables, unreachable code and obsolete implementations introduced by the change.
- New or modified code must pass applicable linting and type checks without warnings where the toolchain supports this.
- Preserve backward compatibility unless the task explicitly requires a breaking change.

## 10. UI, CSS AND ACCESSIBILITY

- Scope CSS overrides to the relevant component or feature unless a global change is explicitly intended.
- Maintain readable foreground/background contrast in light and dark themes.
- Do not introduce accidental global style inheritance or cross-component visual regressions.
- Preserve keyboard navigation, focus visibility, accessible names and appropriate semantics.
- Verify responsive layouts where the application supports multiple window sizes.
- Reuse existing design tokens and UI components instead of introducing inconsistent alternatives.

## 11. SECURITY AND DATA INTEGRITY

- Never expose API keys, secrets, passwords, tokens or private user data in logs, screenshots, test fixtures or project history.
- Keep credentials out of source control and generated release artifacts.
- Do not execute unknown scripts or downloaded code without inspecting their origin and expected effects.
- Confirm destructive operations and explain their scope before execution.
- Use least-privilege access and validate external input at appropriate boundaries.
- Do not weaken security checks merely to make a test or build pass.
- Preserve existing user data and settings unless the task explicitly requires a change.
- For consequential changes, inspect the diff and verify that unrelated files were not modified.

## 12. COMPLETION REPORT

At the end of a task, report concisely:
- What changed.
- Which tests, type checks, linting and builds actually ran.
- Which checks passed or failed.
- Any unresolved issues, assumptions or risks.
- Relevant version or build identifier, when applicable.
