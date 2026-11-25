# Plan: Show session cost in WebApp

Repository: `D:\dev\CodeAssistant`

## TL;DR

CodeAssistant moves to LLMLib 0.3.0 and CopilotLib 0.7.0, whose `AskStreamingAsync` streams typed items: text chunks plus usage updates with the response and session cost in dollars. In the TUI and planner, text handling moves to the new text items. The planner also forwards usage through a new `IPlanningInteraction.OnUsage` callback. In the web app, chat usage is sent as a new usage chunk on the `SendMessage` stream and planning usage as a `PlanUsage` hub event. The Chat and Plan views show "Session cost: $X.XXXX", updated live and reset when a new chat or plan starts. A small test project covers the server-side mapping, and the README is updated.

## Findings

- CodeAssistant.Lib references `Neph.GenericLibs.Net.LLMLib` 0.2.0. CodeAssistant.WebApp and CodeAssistant.TUI reference `Neph.GenericLibs.Net.CopilotLib` 0.6.0. The packages restore from D:\dev\GenericLibs.Net\local-nuget-feed (nuget.config).
- Three callers of `AskStreamingAsync` must handle the typed stream: TUI `Program.StreamReplyAsync`, WebApp `ChatConversation.ProduceAsync` and Lib `FeaturePlanner.AskAsync`.
- Chat uses the SignalR streaming method `AssistantHub.SendMessage`, which returns `IAsyncEnumerable<ChatChunk>`. `ChatChunkKind` is serialized in camelCase by `JsonStringEnumConverter`.
- `PlanningRun` (the only `IPlanningInteraction` implementer) forwards callbacks as ordered hub events through `Send(method, args)`. PlanView registers handlers in a single `useEffect` list.
- In ChatView the toolbar holds Mode, ModelPicker and "New chat". In PlanView the toolbar is only rendered while running, and the phase stepper is shown whenever status != idle.
- The repository has no test projects. CodeAssistant.Lib.csproj already declares InternalsVisibleTo for CodeAssistant.Lib.Tests.
- ClientApp is React 19 with Vite and TypeScript. It has no test runner; `npm run build` type-checks it (`tsc`).

## Phases & tasks

### Upgrade libraries and adapt callers

Consume LLMLib 0.3.0 and CopilotLib 0.7.0 and adapt every `AskStreamingAsync` caller to the typed stream.

| Id | Status | Task | Files | Depends on | Parallel with | Criteria |
|----|--------|------|-------|------------|---------------|----------|
| T1.1 | [ ] | **Bump GenericLibs package references**<br>Prerequisite: the GenericLibs.Net plan task T3.2 must have packed LLMLib 0.3.0 and CopilotLib 0.7.0 into D:\dev\GenericLibs.Net\local-nuget-feed.<br><br>Then update the package references:<br>- CodeAssistant.Lib.csproj: `Neph.GenericLibs.Net.LLMLib` to 0.3.0.<br>- CodeAssistant.WebApp.csproj and CodeAssistant.TUI.csproj: `Neph.GenericLibs.Net.CopilotLib` to 0.7.0.<br><br>Run `dotnet restore CodeAssistant.slnx`. | `CodeAssistant.Lib/CodeAssistant.Lib.csproj`, `CodeAssistant.WebApp/CodeAssistant.WebApp.csproj`, `CodeAssistant.TUI/CodeAssistant.TUI.csproj` | — | — | AC-01 |
| T1.2 | [ ] | **Adapt TUI to typed stream**<br>In `Program.StreamReplyAsync`, iterate `IAsyncEnumerable<LLMStreamItem>`:<br>- For `LLMTextChunk text`, append `text.Text` to `pending` under the existing lock.<br>- Ignore `LLMUsageUpdate`, since the user chose not to show cost in the TUI.<br><br>Add `using LLMLib.DTO;`. All other TUI behaviour stays unchanged. | `CodeAssistant.TUI/Program.cs` | T1.1 | T1.3 | AC-01 |
| T1.3 | [ ] | **Forward planner usage through IPlanningInteraction**<br>1. IPlanningInteraction.cs: add `void OnUsage(decimal responseCost, decimal sessionCost);` with the doc "The LLM session reported usage: cost of the current turn so far and of the whole planning session, in US dollars (AI credits / 100)."<br>2. FeaturePlanner.cs: add an `IPlanningInteraction interaction` parameter to `AwaitCompletionAsync` and `AskAsync`, and pass it from `PlanAsync` (Design and Refinement calls) and from `RunPhaseAsync`.<br>3. In `AskAsync`, switch on each item:<br>   - `LLMTextChunk t`: `buffer.Append(t.Text)`.<br>   - `LLMUsageUpdate u`: `interaction.OnUsage(u.ResponseCost, u.SessionCost)`.<br>   - Keep the final `buffer.Flush()`.<br><br>Add `using LLMLib.DTO;`. | `CodeAssistant.Lib/Planning/IPlanningInteraction.cs`, `CodeAssistant.Lib/Planning/FeaturePlanner.cs` | T1.1 | T1.2 | AC-01, AC-04 |

### WebApp server: send cost to clients

Send cost updates from the server to the browser for both chat and planning.

| Id | Status | Task | Files | Depends on | Parallel with | Criteria |
|----|--------|------|-------|------------|---------------|----------|
| T2.1 | [ ] | **Add Usage chunk kind**<br>In Models/HubModels.cs:<br>- Add `Usage` to `ChatChunkKind`.<br>- Extend `ChatChunk` to `ChatChunk(ChatChunkKind Kind, string Text, string? ToolName = null, decimal? ResponseCost = null, decimal? SessionCost = null)`.<br>- Update the XML summary: a usage chunk has empty `Text` and carries the costs in US dollars. | `CodeAssistant.WebApp/Models/HubModels.cs` | T1.1 | T2.3 | AC-02 |
| T2.2 | [ ] | **Map usage updates in ChatConversation**<br>In `ChatConversation.ProduceAsync`, iterate the typed stream and map each item:<br>- `LLMTextChunk t` becomes `new ChatChunk(ChatChunkKind.Text, t.Text)`.<br>- `LLMUsageUpdate u` becomes `new ChatChunk(ChatChunkKind.Usage, string.Empty, null, u.ResponseCost, u.SessionCost)`.<br><br>Write them to the channel in the order received. Add `using LLMLib.DTO;`. | `CodeAssistant.WebApp/Services/ChatConversation.cs` | T2.1 | — | AC-02 |
| T2.3 | [ ] | **Emit PlanUsage hub event**<br>Implement `public void OnUsage(decimal responseCost, decimal sessionCost) => Send("PlanUsage", responseCost, sessionCost);` in `PlanningRun`.<br><br>In AssistantHub.cs, update the class summary to mention the PlanUsage event and the usage chat chunks. | `CodeAssistant.WebApp/Services/PlanningRun.cs`, `CodeAssistant.WebApp/Hubs/AssistantHub.cs` | T1.3 | T2.1 | AC-04 |

### WebApp client: show session cost

Display the live session cost in the Chat and Plan views.

| Id | Status | Task | Files | Depends on | Parallel with | Criteria |
|----|--------|------|-------|------------|---------------|----------|
| T3.1 | [ ] | **Client types and SessionCost component**<br>1. types.ts: change `ChatChunk.kind` to `'text' \| 'tool' \| 'usage'` and add the optional fields `responseCost?: number \| null` and `sessionCost?: number \| null`.<br>2. Create components/SessionCost.tsx exporting `SessionCost({ cost }: { cost: number })`. It renders `<span className="session-cost" title="AI credits / 100">Session cost: ${'$'}{cost.toFixed(4)}</span>`.<br>3. index.css: add a `.session-cost` rule using the existing muted text colour, `font-variant-numeric: tabular-nums`, `white-space: nowrap` and `margin-left: auto`, so it sits at the right of toolbars. | `CodeAssistant.WebApp/ClientApp/src/types.ts`, `CodeAssistant.WebApp/ClientApp/src/components/SessionCost.tsx`, `CodeAssistant.WebApp/ClientApp/src/index.css` | T2.1 | — | AC-03, AC-04 |
| T3.2 | [ ] | **Show session cost in ChatView**<br>In ChatView.tsx:<br>- Add `const [sessionCost, setSessionCost] = useState<number \| null>(null)`.<br>- After `StartChat` succeeds, `setSessionCost(0)`.<br>- In the stream's `next` handler, handle `chunk.kind === 'usage'` by calling `setSessionCost(chunk.sessionCost ?? 0)`. Don't append an entry. Tool and text chunks stay as before.<br>- In `newChat()`, `setSessionCost(null)`.<br>- Render `{sessionCost !== null && <SessionCost cost={sessionCost} />}` at the end of the toolbar. | `CodeAssistant.WebApp/ClientApp/src/views/ChatView.tsx` | T3.1, T2.2 | T3.3 | AC-03 |
| T3.3 | [ ] | **Show session cost in PlanView**<br>In PlanView.tsx:<br>- Add `const [sessionCost, setSessionCost] = useState(0)`.<br>- Add the handler `['PlanUsage', (_responseCost: number, cost: number) => setSessionCost(cost)]` to the handlers list.<br>- Call `setSessionCost(0)` in `start()`.<br>- Render `<SessionCost cost={sessionCost} />` whenever `status !== 'idle'`, in a row with the phase stepper (wrap `<ol className="phases">` and the cost in a flex container, with CSS in index.css if needed). This keeps it visible after completion, failure or cancellation. | `CodeAssistant.WebApp/ClientApp/src/views/PlanView.tsx`, `CodeAssistant.WebApp/ClientApp/src/index.css` | T3.1, T2.3 | T3.2 | AC-04 |

### Tests and documentation

Automated coverage of the server-side mapping, plus updated documentation.

| Id | Status | Task | Files | Depends on | Parallel with | Criteria |
|----|--------|------|-------|------------|---------------|----------|
| T4.1 | [ ] | **Add CodeAssistant.WebApp.Tests**<br>Create CodeAssistant.WebApp.Tests/CodeAssistant.WebApp.Tests.csproj:<br>- net10.0, `IsPackable=false`, `NoWarn GHCP001`.<br>- Packages `Microsoft.NET.Test.Sdk`, `xunit` and `xunit.runner.visualstudio`, with explicit versions (this repository has no central package management).<br>- A `ProjectReference` to ../CodeAssistant.WebApp/CodeAssistant.WebApp.csproj.<br>- Add it to CodeAssistant.slnx.<br><br>ChatConversationTests.cs:<br>- Write a `FakeLLMService`/`FakeLLMSession` that implement `ILLMService`/`ILLMSession` and yield `LLMTextChunk("Hi")`, `LLMUsageUpdate(0.02m, 0.05m)`, `LLMTextChunk("!")`.<br>- Call `ChatConversation.CreateAsync(fake, new ChatSettings(true, null, null), ct)`, then `SendAsync("hello", ct)`.<br>- Assert the three chunks come back in order with the right kinds and values.<br><br>PlanningRunTests.cs:<br>- A fake `IFeaturePlanner` calls `interaction.OnUsage(0.01m, 0.03m)` and returns `new FeaturePlanResult([], "s")`.<br>- A recording fake `IClientProxy` captures the `SendCoreAsync` calls.<br>- Run `PlanningRun.Start(...)`, then await `DisposeAsync()` after the run completes (poll until "PlanCompleted" is recorded).<br>- Assert that ("PlanUsage", 0.01, 0.03) was recorded before "PlanCompleted". | `CodeAssistant.WebApp.Tests/CodeAssistant.WebApp.Tests.csproj`, `CodeAssistant.WebApp.Tests/ChatConversationTests.cs`, `CodeAssistant.WebApp.Tests/PlanningRunTests.cs`, `CodeAssistant.slnx` | T2.2, T2.3 | T4.2 | AC-02, AC-04 |
| T4.2 | [ ] | **Document session cost in README**<br>In README.md, Web app section, add one sentence: the Chat and Plan views show the current session cost, updated live, in US dollars (GitHub AI credits / 100). | `README.md` | — | T4.1 | AC-05 |

## Acceptance criteria

### AC-01

Given LLMLib 0.3.0 and CopilotLib 0.7.0 are in the local feed, when the CodeAssistant solution is built, then it compiles. The TUI still renders only the assistant's reply text and shows no cost.

| Test | Kind | Description | How to run | Expected result |
|------|------|-------------|------------|-----------------|
| AT-01.1 | Integration | Restore and build the whole solution. | dotnet build CodeAssistant.slnx | The build succeeds with no errors. |
| AT-01.2 | Manual | Run the TUI, ask a question, and check the output. | dotnet run --project CodeAssistant.TUI; enter a token, then ask "hello" | The reply is rendered as before, as Markdown after "Assistant>", with no cost text and no stray item type names. |

### AC-02

Given a started chat, when the LLM session streams an `LLMUsageUpdate`, then the `SendMessage` stream yields a `ChatChunk` with kind usage and the update's `responseCost` and `sessionCost`. Text and tool chunks are unchanged.

| Test | Kind | Description | How to run | Expected result |
|------|------|-------------|------------|-----------------|
| AT-02.1 | Unit | `ChatConversationTests`: a fake `ILLMService` returns a fake session that yields `LLMTextChunk("Hi")`, `LLMUsageUpdate(0.02, 0.05)`, `LLMTextChunk("!")`. | dotnet test CodeAssistant.WebApp.Tests --filter FullyQualifiedName~ChatConversationTests | `SendAsync` yields Text "Hi", then Usage (0.02, 0.05), then Text "!", in that order. |

### AC-03

Given the Chat view, when a chat is running, then the toolbar shows "Session cost: $X.XXXX". It updates every time a usage chunk arrives during the stream. When "New chat" is clicked, it resets until the next chat starts at $0.0000.

| Test | Kind | Description | How to run | Expected result |
|------|------|-------------|------------|-----------------|
| AT-03.1 | Manual | Run the web app, open /chat, send two messages, then click "New chat". | dotnet run --project CodeAssistant.WebApp, then use the browser at http://localhost:5173/chat | The cost appears at $0.0000 after the first send and grows during and after each reply. The second reply's total is >= the first. After "New chat" the indicator disappears, then shows $0.0000 again on the next send. |

### AC-04

Given a feature plan is running, when the planner's LLM session reports usage, then the client receives a `PlanUsage(responseCost, sessionCost)` hub event. The Plan view shows "Session cost: $X.XXXX" next to the phase stepper, updated live. It stays visible after the plan completes, fails or is cancelled, and resets to $0.0000 when a new plan starts.

| Test | Kind | Description | How to run | Expected result |
|------|------|-------------|------------|-----------------|
| AT-04.1 | Unit | `PlanningRunTests`: a fake `IFeaturePlanner` calls `interaction.OnUsage(0.01m, 0.03m)` and then returns a result. A fake `IClientProxy` records each `SendCoreAsync` call. | dotnet test CodeAssistant.WebApp.Tests --filter FullyQualifiedName~PlanningRunTests | The recorded calls include ("PlanUsage", [0.01, 0.03]) before "PlanCompleted". |
| AT-04.2 | Manual | Start a plan in the web app, follow it through Discovery, then cancel it. Start a new plan. | dotnet run --project CodeAssistant.WebApp, then use http://localhost:5173/plan | The cost increases during the agent's work and is still shown after "Planning cancelled." The new plan starts at $0.0000. |

### AC-05

Given the README, when a user reads the Web app section, then it states that the Chat and Plan views show the current session cost, in dollars, as AI credits / 100.

| Test | Kind | Description | How to run | Expected result |
|------|------|-------------|------------|-----------------|
| AT-05.1 | Manual | Review README.md. | Open README.md | The Web app section contains the session cost sentence. |

## Relevant files

- `CodeAssistant.Lib/CodeAssistant.Lib.csproj` (modify) — LLMLib 0.3.0.
- `CodeAssistant.WebApp/CodeAssistant.WebApp.csproj` (modify) — CopilotLib 0.7.0.
- `CodeAssistant.TUI/CodeAssistant.TUI.csproj` (modify) — CopilotLib 0.7.0.
- `CodeAssistant.TUI/Program.cs` (modify) — Consumes text items only.
- `CodeAssistant.Lib/Planning/IPlanningInteraction.cs` (modify) — New `OnUsage` callback.
- `CodeAssistant.Lib/Planning/FeaturePlanner.cs` (modify) — Handles typed items and forwards usage.
- `CodeAssistant.WebApp/Models/HubModels.cs` (modify) — Usage chunk kind and cost fields.
- `CodeAssistant.WebApp/Services/ChatConversation.cs` (modify) — Maps typed items to chat chunks.
- `CodeAssistant.WebApp/Services/PlanningRun.cs` (modify) — Emits the PlanUsage hub event.
- `CodeAssistant.WebApp/Hubs/AssistantHub.cs` (modify) — Documents the new events.
- `CodeAssistant.WebApp/ClientApp/src/types.ts` (modify) — Usage chunk typing.
- `CodeAssistant.WebApp/ClientApp/src/components/SessionCost.tsx` (create) — Shared cost display.
- `CodeAssistant.WebApp/ClientApp/src/index.css` (modify) — Cost styling.
- `CodeAssistant.WebApp/ClientApp/src/views/ChatView.tsx` (modify) — Shows the chat session cost.
- `CodeAssistant.WebApp/ClientApp/src/views/PlanView.tsx` (modify) — Shows the planning session cost.
- `CodeAssistant.WebApp.Tests/CodeAssistant.WebApp.Tests.csproj` (create) — New test project.
- `CodeAssistant.WebApp.Tests/ChatConversationTests.cs` (create) — Tests the usage chunk mapping.
- `CodeAssistant.WebApp.Tests/PlanningRunTests.cs` (create) — Tests the PlanUsage event.
- `CodeAssistant.slnx` (modify) — Registers the test project.
- `README.md` (modify) — Documents the cost display.
- `nuget.config` (reference) — Local feed from which the new library versions are restored.

## Decisions

- Session cost is shown in both the Chat and Plan views and updated on every usage event (the user chose this). Format: "Session cost: $" followed by the amount with 4 decimals.
- Chat cost travels in the existing `SendMessage` stream, as a new kind `ChatChunkKind.Usage` with nullable `ResponseCost` and `SessionCost` fields. Its order relative to the text is kept and no new hub method is needed.
- Planning cost travels through a new `IPlanningInteraction.OnUsage(responseCost, sessionCost)` callback, which `PlanningRun` forwards as a `PlanUsage` hub event, consistent with PlanPhase, PlanMessage and PlanTool.
- The TUI is only adapted to the typed stream and shows no cost (the user chose this).
- The web app creates new sessions only, so each displayed cost starts at $0 when a chat or plan starts.
- A new CodeAssistant.WebApp.Tests xUnit project covers the server-side mapping. The UI is verified manually, because the ClientApp has no test tooling.

## Out of scope

- Cost display in the TUI.
- Per-message cost badges, cost history, and persisting costs across app restarts.
- Budget limits or warnings.
- The GenericLibs.Net library changes themselves, which are covered by the GenericLibs.Net plan.

## Further considerations

1. Showing the cost of the last response (`responseCost`) next to each assistant message would be a small extension, since the data is already sent.
2. A planning session runs many turns. A per-phase cost breakdown could be added later if useful.
3. If the SDK only delivers usage after the turn ends, the cost updates once per turn rather than during the stream. This depends on the GenericLibs.Net task T2.1/T2.2.
