# Plan: CodeAssistant.Lib — feature-planning agent harness

## TL;DR

New net10.0 class library `CodeAssistant.Lib` in this repo, depending only on the provider-agnostic `LLMLib` (`ILLMService`, `AIFunction` tools). It exposes a `FeaturePlanner` that drives a planning agent through a harness-controlled state machine — **Discovery → Alignment → Design → Refinement (loop until approved)** — inside a single `ILLMSession`. The agent explores the target codebase only through custom sandboxed read-only tools, asks clarifying questions through an `ask_user` tool, and submits a structured `FeaturePlan` (phases, tasks, acceptance criteria, acceptance tests per criterion) through a `submit_plan` tool that the harness validates. The approved plan is rendered to markdown and saved to `plans/<yyyyMMdd>-<slug>.md`. A small change in GenericLibs.Net (per-session tool restriction) guarantees the planner is read-only regardless of host configuration. The TUI gets a "Plan feature" mode.

The machine-readable version of this plan (same shape as the future `FeaturePlan` model) is [CodeAssistant.Lib.plan.json](CodeAssistant.Lib.plan.json).

## Phases & tasks

### Phase 0 — GenericLibs.Net: per-session tool restriction

| Id | Task | Depends on |
|----|------|------------|
| T0.1 | Add `bool RestrictToCustomTools { get; init; }` to `LLMSessionOptions` (`D:\dev\GenericLibs.Net\LLMs\LLMLib\ILLMSession.cs`); bump LLMLib to 0.3.0 (additive, non-breaking). | — |
| T0.2 | In `CopilotService.GetSessionAsync`, when the flag is set: `AvailableTools = ["web_fetch", ..customToolNames]` and use the restrictive permission handler (public URLs + caller custom tools only) even if `EnableBuiltInTools`/`ApproveAllPermissions` are on. Bump CopilotLib to 0.6.0, reference LLMLib 0.3.0. | T0.1 |
| T0.3 | `dotnet pack` LLMLib and CopilotLib into `D:\dev\GenericLibs.Net\local-nuget-feed` (already a source in [nuget.config](nuget.config)). Publish order when releasing: LLMLib first, then CopilotLib. | T0.2 |

### Phase 1 — Project skeleton & models

| Id | Task | Depends on |
|----|------|------------|
| T1.1 | Create `CodeAssistant.Lib/CodeAssistant.Lib.csproj` (net10.0, Nullable, ImplicitUsings; refs: `Neph.GenericLibs.Net.LLMLib` 0.3.0, `Microsoft.Extensions.FileSystemGlobbing`, `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Logging.Abstractions`). Add to [CodeAssistant.slnx](CodeAssistant.slnx). | T0.3 |
| T1.2 | `Models/`: `FeatureRequest`, `FeaturePlan`, `PlanPhase`, `PlanTask`, `AcceptanceCriterion`, `AcceptanceTest` (+ `AcceptanceTestKind`), `RelevantFile` (+ `FileChangeKind`), `PlannerQuestion`, `PlanReview`, `FeaturePlanResult`, `PlanningPhase` enum. Block-scoped namespaces, System.Text.Json friendly (camelCase). | T1.1 |

### Phase 2 — Sandboxed read-only tools

| Id | Task | Depends on |
|----|------|------------|
| T2.1 | `Tools/RepositorySandbox`: resolve paths with `Path.GetFullPath` against `RepositoryRoot`, reject anything outside root (root + separator, case-insensitive on Windows), reject reparse points escaping root, skip excluded directories (`bin`, `obj`, `.git`, `node_modules`, configurable). | T1.2 |
| T2.2 | `Tools/ReadOnlyRepositoryTools.Create(sandbox, options)` → `AIFunction`s via `AIFunctionFactory.Create`: `list_dir`, `read_file` (line range, max lines/bytes, binary detection), `grep_search` (text/regex with `matchTimeout`, include glob, capped results), `file_search` (glob). Report each invocation through a callback. | T2.1 |

### Phase 3 — Interaction tools, validator, renderer (parallel with Phase 2)

| Id | Task | Depends on |
|----|------|------------|
| T3.1 | `Validation/FeaturePlanValidator`: required title/summary/≥1 task; unique task/criterion/test ids; `DependsOn`/`ParallelWith`/`AcceptanceCriterionIds` reference existing ids; every criterion has ≥1 acceptance test; every test belongs to its criterion; every task linked to ≥1 criterion; `Modify`/`Reference` files exist inside the sandbox. Returns a list of actionable errors. | T1.2 |
| T3.2 | `Rendering/FeaturePlanMarkdownRenderer`: TL;DR, Findings, Phases/Tasks (with dependencies), Acceptance criteria with per-criterion test table, Relevant files, Decisions, Out of scope, Further considerations. | T1.2 |
| T3.3 | `Tools/PlannerInteractionTools` (per run, with a run-state holder): `ask_user` (flushes buffered assistant text, then `IPlanningInteraction.AskUserAsync`), `submit_plan(FeaturePlan plan)` (validate → return errors so the model resubmits, or store and return "accepted, wait for review"). | T3.1, T4.1 |

### Phase 4 — Orchestrator & DI

| Id | Task | Depends on |
|----|------|------------|
| T4.1 | `Planning/PlannerOptions` (`RepositoryRoot`, `Model`, `ReasoningEffort`, `PlansOutputDirectory` default `<root>/plans`, `ExcludedDirectories`, `MaxReadLines`, `MaxFileBytes`, `MaxSearchResults`, `MaxClarifyingQuestions`, `MaxSubmitRetries`), `Planning/IPlanningInteraction` (`OnPhaseChanged`, `OnAssistantMessage`, `OnToolInvoked`, `AskUserAsync`, `ReviewPlanAsync`), `Planning/PlannerPrompts` (system message + per-phase prompts). | T1.2 |
| T4.2 | `Planning/FeaturePlanner : IFeaturePlanner.PlanAsync(FeatureRequest, IPlanningInteraction, CancellationToken)`: create session with `RestrictToCustomTools = true` and all tools; buffer streamed chunks into whole messages; run Discovery → Alignment → Design (nudge up to `MaxSubmitRetries` if no plan submitted) → Refinement loop (`ReviewPlanAsync`, feedback → revise + resubmit) until approved or cancelled. | T2.2, T3.2, T3.3, T4.1 |
| T4.3 | Persist approved plan to `PlansOutputDirectory/yyyyMMdd-<slug>.md`; slug sanitized from title, final path verified inside the output directory. | T4.2 |
| T4.4 | `CodeAssistantServiceCollectionExtensions.AddCodeAssistantLib(Action<PlannerOptions>)` registering options + `IFeaturePlanner` (host registers `ILLMService` separately). | T4.2 |

### Phase 5 — TUI integration

| Id | Task | Depends on |
|----|------|------------|
| T5.1 | Add ProjectReference to CodeAssistant.Lib and bump `Neph.GenericLibs.Net.CopilotLib` to 0.6.0 in [CodeAssistant.TUI.csproj](CodeAssistant.TUI/CodeAssistant.TUI.csproj). | T4.4 |
| T5.2 | [Program.cs](CodeAssistant.TUI/Program.cs): add "Plan feature" to `PromptForMode`; call `AddCodeAssistantLib` (`RepositoryRoot = Environment.CurrentDirectory`, selected model/effort); make `RenderToString` internal for reuse. | T5.1 |
| T5.3 | `CodeAssistant.TUI/PlanningConsole.cs : IPlanningInteraction`: prompt for feature description, phase `Rule` headers, markdown-rendered assistant messages, grey tool-call lines, questions via [UserInputPrompt.cs](CodeAssistant.TUI/UserInputPrompt.cs), review = rendered plan + "Approve / Request changes" + feedback prompt, print saved path. | T5.2 |

### Phase 6 — Tests (parallel with Phase 5)

| Id | Task | Depends on |
|----|------|------------|
| T6.1 | Create `CodeAssistant.Lib.Tests` (xUnit), add to slnx. | T1.1 |
| T6.2 | Sandbox & tools tests (path escapes, junctions, excluded dirs, caps, regex timeout). | T2.2, T6.1 |
| T6.3 | Validator, renderer, slug/save-path tests; deserialize [CodeAssistant.Lib.plan.json](CodeAssistant.Lib.plan.json) into `FeaturePlan` and validate it as a fixture. | T3.1, T3.2, T4.3, T6.1 |
| T6.4 | `FeaturePlanner` tests with a fake `ILLMService`/`ILLMSession` that invokes tools directly: happy path, resubmit after validation errors, missing-submission nudges, feedback loop, `RestrictToCustomTools` set. | T4.2, T6.1 |

## Acceptance criteria & tests

| Id | Criterion | Tests |
|----|-----------|-------|
| AC-01 | Given a host that registered CopilotLib with built-in tools and `ApproveAllPermissions = true`, when a planning session runs, then only `web_fetch` and the planner's custom tools are available/approved. | AT-01.1 (unit) `FeaturePlanner` passes `RestrictToCustomTools = true`; AT-01.2 (manual) TUI plan mode, ask the agent to create a file → impossible, `git status` clean except `plans/`. |
| AC-02 | The solution builds with the new projects and no new warnings. | AT-02.1 (integration) `dotnet build CodeAssistant.slnx`. |
| AC-03 | `FeaturePlan` round-trips through JSON and this repo's plan JSON is a valid instance. | AT-03.1 (unit) serialize/deserialize equality; AT-03.2 (unit) deserialize + validate `CodeAssistant.Lib.plan.json`. |
| AC-04 | Read-only tools cannot access anything outside `RepositoryRoot` or inside excluded directories. | AT-04.1 (unit) `..\`, absolute and junction paths rejected; AT-04.2 (unit) excluded dirs hidden; AT-04.3 (manual) ask agent to read `..\README.md` → refused. |
| AC-05 | Tool output is bounded and regex search cannot hang. | AT-05.1 (unit) `read_file` caps lines/bytes and detects binary; AT-05.2 (unit) `grep_search` caps results and times out on catastrophic regex. |
| AC-06 | Invalid plans are rejected with actionable errors; valid plans are accepted. | AT-06.1 (unit) one test per validator rule; AT-06.2 (unit) `submit_plan` returns errors and stores nothing for invalid plans. |
| AC-07 | The approved plan is rendered with all sections and saved as `yyyyMMdd-<slug>.md` in the output directory. | AT-07.1 (unit) renderer sections; AT-07.2 (unit) file written at expected path. |
| AC-08 | The planner runs Discovery → Alignment → Design in order and routes clarifying questions to the host. | AT-08.1 (unit) phase-change sequence and `AskUserAsync` call with fake session. |
| AC-09 | Feedback triggers revisions until approval; a missing submission triggers bounded reminders. | AT-09.1 (unit) feedback → resubmit → approve; AT-09.2 (unit) no submission → `MaxSubmitRetries` nudges then failure. |
| AC-10 | Plan file names cannot escape the output directory. | AT-10.1 (unit) titles with `..`, `/`, `\`, reserved names produce safe slugs inside the output dir. |
| AC-11 | The TUI "Plan feature" mode works end to end. | AT-11.1 (manual) in this repo, request "add a /clear command to the chat": tool calls shown, ≥1 question, plan submitted and validated, one feedback round, approval, file in `plans/`. |

## Relevant files

- [CodeAssistant.slnx](CodeAssistant.slnx) — add lib + tests projects.
- [nuget.config](nuget.config) — already points at `..\GenericLibs.Net\local-nuget-feed`.
- [CodeAssistant.TUI/Program.cs](CodeAssistant.TUI/Program.cs) — mode choice, DI wiring, `RenderToString` reuse.
- [CodeAssistant.TUI/UserInputPrompt.cs](CodeAssistant.TUI/UserInputPrompt.cs) — reused for planner questions.
- [CodeAssistant.TUI/CodeAssistant.TUI.csproj](CodeAssistant.TUI/CodeAssistant.TUI.csproj) — ProjectReference, CopilotLib 0.6.0.
- `D:\dev\GenericLibs.Net\LLMs\LLMLib\ILLMSession.cs` — `LLMSessionOptions.RestrictToCustomTools`.
- `D:\dev\GenericLibs.Net\LLMs\CopilotLib\CopilotService.cs` — per-session restriction in `GetSessionAsync`.

## Decisions

- Library lives in this repo for now (ProjectReference from the TUI); may move to GenericLibs.Net later.
- Depends on LLMLib only; no direct CopilotLib / Copilot SDK dependency.
- Codebase access only via custom read-only tools sandboxed to `RepositoryRoot`; only the harness writes (the plan file).
- Harness-controlled phases in one session; approval (or cancellation) is the only exit.
- Plans must include tasks, acceptance criteria, and at least one acceptance test per criterion.
- GenericLibs.Net change limited to an additive per-session flag; no tool/reasoning events, no structured-output API, no ask-user abstraction in LLMLib.

## Out of scope

- An implementation agent that executes the plan.
- MCP servers, multi-agent/subagent orchestration.
- Resuming interrupted planning sessions.
- Publishing CodeAssistant.Lib to NuGet.

## Further considerations

1. Also save the `FeaturePlan` as JSON next to the markdown so a future implementation agent can consume it without parsing markdown (recommended, cheap).
2. If the nested `submit_plan` schema causes issues in CopilotLib's AIFunction → SDK conversion, fix it in CopilotLib rather than falling back to a `planJson` string parameter.
3. Default output under `<root>/plans` writes into the target repo; alternative is `%LOCALAPPDATA%/CodeAssistant/plans/<repo-name>`.
