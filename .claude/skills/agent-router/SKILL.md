---
name: agent-router
description: Decides which agent, Claude model and effort level should handle a task before any work starts, so the coordinator spends the fewest tokens that still hold quality. Use this every time the user asks to implement, fix, refactor, test, review, investigate, document or plan anything in this repo, even when the task looks small (the router may answer "inline" for trivial work). Also use it when unsure whether to delegate at all or how many agents to spawn.
argument-hint: [one-line task description]
---

# Agent router

The main conversation is the scarce resource: everything it reads is re-billed on
every turn, and quality drops as it fills. Delegating to an agent moves the verbose
part (file reads, test output, exploration) into a disposable context and returns a
summary. The two cost levers are the **model tier** (Haiku 1x, Sonnet 2x, Opus 5x,
Fable 10x input price) and the **effort level** (`low` < `medium` < `high` < `xhigh` <
`max`). Anthropic's measurements say: sweep effort before changing model, lower effort
on a newer model often beats higher effort on an older one, and "run cheap, re-run
failures higher" gives the same pass rate at about half the cost. Details and URLs in
[references/sources.md](references/sources.md).

Effort can only be pinned in an agent definition, never per call, so the project ships
one agent per tier in `.claude/agents/`. Pick from those.

## Step 1: classify the task

| Tier | Signals |
| --- | --- |
| **inline** | Answerable from what is already in context; one-line edit; a git or shell command whose output is short. A subagent costs more than doing it. |
| **lookup** | "Where is X", "how does Y work", "which files use Z". Read-only, no judgement about design. |
| **routine change** | Scope is clear, 1 to 3 files, an existing pattern to copy (new endpoint like an existing one, a new test for a known case, a config field). |
| **complex change** | Multi-file, new abstraction, unclear approach, or touches the auth contract, `Startup.cs`, `DirectorySchema.cs`, connection pooling or secrets. Also: a bug nobody has localised yet. |
| **review / security** | Judge an existing diff or design against AGENTS.md rules. No edits. |
| **verify** | Run the build or a test filter and report only failures. |
| **plan** | The user wants a plan or an architecture decision, not code yet. |

When two tiers fit, take the cheaper one and rely on the escalation ladder below.

## Step 2: select agent, model and effort

| Tier | Agent | Model | Effort | Parallelism |
| --- | --- | --- | --- | --- |
| inline | none (coordinator does it) | current | current | 1 |
| lookup | `scout` | haiku | n/a (Haiku 4.5 has no effort control) | 1, or 2 to 3 only for independent questions |
| routine change | `builder` | sonnet | high | 1 |
| complex change | `architect` | opus | xhigh | 1 (split into independent `builder` sub-tasks only if the architect's plan says so) |
| review / security | `reviewer` | sonnet (override `model: opus` for `adrapi/Security/`, `adrapi/Ldap/Security/`, `Startup.cs` auth or rate-limit code) | high | 1 |
| verify | `test-runner` | haiku | n/a | 1 |
| plan | built-in `Plan` | inherits | inherits | 1 |

Why these defaults: independent benchmarks found single-shot code quality plateaus
at `high`, while long agentic loops at `xhigh` often use *fewer* total tokens because
better planning means fewer turns; and Sonnet 5 at `medium` matches Sonnet 4.6 at
`high`. So if `builder` bills look high on mechanical work, `medium` is the first
step-down to try (edit `.claude/agents/builder.md`), not a cheaper model.

Use `fable` only when the user asks for it by name or an `architect` run at `xhigh`
failed on a problem that is genuinely frontier (novel algorithm, deep concurrency bug),
not just large. It costs twice Opus.

Never spawn more agents than there are independent questions. Anthropic's own
research system scales as: simple fact 1 agent, comparison 2 to 4, and 10+ only for
broad research. Dependent steps run in one agent sequentially, not in several.

## Step 3: write the delegation brief

Vague briefs make agents redo each other's work or misread the goal. Every brief has:

1. **Objective**: one sentence of what "done" means.
2. **Boundaries**: files or directories in scope, what not to touch, AGENTS.md rules that apply (auth attributes, `EndpointCatalog.cs`, domain routing, no secrets in repo).
3. **Verification target**: the exact command that must pass, from the AGENTS.md build and test section (for controller changes that is always the `Authentication` test filter).
4. **Output contract**: what to return and how long. Default: a summary under 30 lines with file paths and line numbers, then the verification output trimmed to failures. Never "paste the whole file".

Preface the delegation with the routing decision so it is visible in the transcript:

```
router: tier=<tier> agent=<agent> model=<model> effort=<effort> agents=<n> verify=<command>
```

## Step 4: verify, then escalate one rung at a time

Read the agent's evidence (test output, build exit code). If it fails or the answer is
shallow, do not retry at the same level: climb one rung.

```
scout (haiku) -> builder (sonnet high) -> architect (opus xhigh) -> architect with model: fable
```

Escalate with the failure attached to the brief, so the next agent does not rediscover
it. Two failures at the top rung means the task, not the agent, needs re-scoping: stop
and ask the user.

## Coordinator habits that keep this cheap

- Do not read large files or run `dotnet test` without a filter in the main context when an agent can return the summary instead.
- Ask for evidence, not assurances: the failing test names, the command and its exit code.
- Prefer one well-briefed agent over several thin ones; every agent re-loads CLAUDE.md, AGENTS.md and the tool list.
- Independent agents go in one message so they run concurrently; dependent ones wait.
- Agents that edit files (`builder`, `architect`) run in the foreground (`run_in_background: false`): a background agent auto-denies any tool call that would ask for permission and may report an edit it never made.
- If the router says `inline`, just do it. Explaining why you did not delegate wastes more than the delegation would have saved.
