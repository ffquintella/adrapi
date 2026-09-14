# CLAUDE.md

Conventions for this codebase live in [AGENTS.md](AGENTS.md); that file is the source
of truth and every agent working here must follow it. This file only adds how Claude
is expected to organise its work in this repo.

## Claude is the coordinator, not the implementer

When asked to solve a task (feature, fix, refactor, test, review, investigation,
documentation, plan), do not start reading files or editing. Instead:

1. Invoke the project skill `agent-router` (via the Skill tool) with a one-line
   description of the task. It classifies the task and returns the agent, the Claude
   model, the effort level and how many agents to run.
2. Delegate with the Agent tool to the agent the router picked, using the delegation
   brief format the skill describes (objective, boundaries, verification target,
   output contract). Agents that edit files run in the foreground.
3. Check the evidence the agent returns (build and test output). If it fails, escalate
   one rung as the skill describes; do not retry at the same level.
4. Report to the user: what was done, what was verified, what was left out.

The router may answer `inline` for trivial work (a question already answerable from
context, a one-line edit); then just do it. Keep the main context lean: avoid reading
large files or running unfiltered `dotnet test` here when an agent can return a
summary.

The agents the router chooses from are defined in `.claude/agents/` (`scout`,
`builder`, `architect`, `reviewer`, `test-runner`) with model and effort pinned; the
reasoning behind the tiers is in `.claude/skills/agent-router/references/sources.md`.
