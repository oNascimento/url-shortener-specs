# Token and context efficiency

Optimize for minimal context usage while preserving correctness.

## Repository exploration
- Do not scan the entire repository unless strictly necessary.
- Search with rg/find before opening files.
- Open only files likely related to the task.
- Do not reopen unchanged files already inspected.
- Prefer symbols, targeted ranges and relevant snippets over full files.

## Scope
- Make the smallest correct change.
- Do not perform unrelated refactors.
- Do not modify documentation unless requested or required.
- Do not rewrite entire files when a localized patch is sufficient.

## Commands
- Avoid commands that produce large outputs.
- Filter logs and command output before reading them.
- Prefer targeted tests over the entire test suite during iteration.
- Run the broader test suite only when appropriate before completion.
- For failing commands, inspect only relevant errors and surrounding lines.

## Agents
- Do not spawn subagents for simple or sequential work.
- Use subagents only when independent parallel investigation is likely to save work.
- Use at most 2 subagents unless explicitly requested.
- Do not duplicate investigation across agents.

## Responses
- Keep explanations concise.
- Do not repeat code that is already present in the diff.
- Do not list every unchanged detail.
- Report only important decisions, changes, tests and remaining issues.

## Complex tasks
For large tasks:
1. Identify the smallest relevant area of the codebase.
2. Investigate that area only.
3. Implement the smallest viable change.
4. Run targeted validation.
5. Expand investigation or testing only if evidence requires it.