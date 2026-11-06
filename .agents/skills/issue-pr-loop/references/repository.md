# Reservoir

Run before/after Release benchmarks through [Benchmark comparison](../../../../.github/workflows/benchmark-compare.yml) on GitHub Actions `ubuntu-latest`, with baseline and candidate sequentially in the same job on the same runner. Record both commit SHAs and link the run and artifacts. Local measurements are diagnostic only. This repository no longer requires the local Redis `performance` lock for benchmarks or local validation; retain PR/issue ownership locks.

Read [AGENTS.md](../../../../AGENTS.md) for validation and performance acceptance. Preserve zero-allocation warm Rent/Return paths and ownership/concurrency contracts; performance changes require repeatable before/after evidence. Use the project files and workflows for current framework/test coverage and the website build.

No Aspire AppHost. The queue's Redis lock container requires Docker; its namespace is `reservoir`.

The mechanics the loop defers to:

| Need | Owner |
|---|---|
| One PR's review cycle: fetch every surface, verify findings, fix, one push, reply, resolve and unresolve threads | [Review cycle](#review-cycle) below, with `AGENTS.md` § Commits and PRs |
| PR review lifecycle | [draft-review.md](draft-review.md), with [Draft review lifecycle](#draft-review-lifecycle) below |
| Merge protocol | [Merge gate](#merge-gate) below |
| Out-of-scope filing bar and procedure | `AGENTS.md` § Autonomous self-improvement and § Writing issues |
| Native sub-issue and `blocked_by` writes | [Issue relationships](#issue-relationships) below |
| Worktree location, evidence directory, cleanup | [Worktrees](#worktrees) below and [WorktreeLifecycle.md](../../../../scripts/WorktreeLifecycle.md) |

Lock owner variable: `RESERVOIR_AGENT_LOCK_OWNER_ID` (read by `scripts/AgentLocks.ps1` after `-OwnerId`, before `CODEX_THREAD_ID`).

Use commentary while active.

## Scheduling

Never wait on an item. Pending CI, reviews, benchmark runs, reviewer replies, bot comments, and label-triggered workflows all mean the same thing: leave the item and take another issue or PR. Never run `gh pr checks --watch` or `gh run watch` while work is queueable. The pending result is usually ready when a later survey reaches that item again. In single-unit mode, a later run picks the item up when the result is ready.

A benchmark you started (a `bench/**` push or a dispatched run) is reported in the PR description when a later iteration finds the run finished.

`scripts/Start-IssuePrLoop.ps1` accepts one `RESULT:` value beyond the skill's grammar: `waiting #N: review`. Report it when a draft waits only for a required reviewer's verdict on its current head, and require it as a confirmed result when coordinating. The driver returns the item when a comment, review, or reaction changes, or when the deferral expires, so a dropped request can be repeated.

## Issue priority

Priority labels here are `P0`, `P1`, and `P2`, not `priority:*`, and there is no `ci` label. Apply the tiers in [pick-an-issue.md](pick-an-issue.md) as `P0`, then `P1`, then `P2` and unlabelled issues. `scripts/Start-IssuePrLoop.ps1` reads no priority label: it ranks finished parents first, then issues that block the most others.

## Owner-reserved decisions

No issue class is reserved for the owner beyond the `[human]` list in `SKILL.md`, so `needs-human` follows the normal triage rules. A breaking change is allowed on a milestone that permits breaking changes.

## Draft review lifecycle

Lifecycle: [draft-review.md](draft-review.md), with these repository rules on top.

- Build and test CI does not run on drafts, so a review-fix push costs a review, not a CI run, and CI runs once, on the approved head.
- When CI fails on a ready PR, run `gh pr ready <N> --undo` before pushing the fix (the push also disarms auto-merge), then repeat from step 1. Codex is only tagged on drafts, so this is how a fix head gets its review.

Draft policy: [AGENTS.md](../../../../AGENTS.md) applies the skill's draft review lifecycle to every agent-created PR, including work outside the loop, which replaces the loop-only scope in draft-review.md. Before creating or promoting a PR from an existing worktree, re-read `AGENTS.md` and this skill from the shared checkout; branch copies can predate the current policy.

Required reviewers are Claude and Codex. CodeRabbit is installed but does not review this repository automatically, so it is not required.

- **Claude.** [Claude Code Review](../../../../.github/workflows/claude-code-review.yml) reviews every head automatically, drafts included, on `opened`, `synchronize`, and `reopened`. It posts its review as an issue comment from `github-actions[bot]` and reports the `claude-review` check. Its verdict for the current head is the newest such comment created after that head was pushed, from a run whose `claude-review` check on that head succeeded. A comment with no blocking finding is a positive verdict. Arming auto-merge requires that check to have succeeded on the current head. When the run was dropped, cancelled, or failed, close and immediately reopen the draft once (`gh pr close <N>` then `gh pr reopen <N>`): the `reopened` event reviews the same head and attaches the check to it. Do not use `workflow_dispatch` for this: a dispatched run posts the comment but attaches its check to `main`, not the PR head. Marking a PR ready does not start another Claude review.
- **Codex** (`chatgpt-codex-connector[bot]`). Codex reviews automatically when a PR opens for review or is marked ready, never while it is a draft. While the PR is a draft, request a review with `gh pr comment <N> --body-file <path>`, where the body starts with `<!-- issue-pr-loop -->`, then `<!-- codex-review:<full-head-SHA> -->`, then `@codex review` and `Please review current head <full-head-SHA> and identify the reviewed commit in your verdict, or add a thumbs-up to this request if clean.` Never tag Codex on a ready PR. A positive verdict is either its `<!-- codex-pull-request-review-summary -->` issue comment, which it edits in place, carrying one `codex-review-state:v1` payload whose `code_review` has `commit_sha` equal to the full current head, `status: completed`, `has_any_findings: false`, and `has_unresolved_findings: false`; or a `+1` reaction by `chatgpt-codex-connector[bot]` on this head's request comment, verified through `repos/{owner}/{repo}/issues/comments/<id>/reactions`. A null or older `commit_sha`, findings, pending status, or an unrecognised payload fails closed: request again after the 15-minute timeout instead of inferring the reviewed commit. Codex findings arrive as review threads; disposition them like any other. The review Codex runs when the PR is marked ready is a further review surface that the merge gate reads.

Draft CI: [CI/CD](../../../../.github/workflows/ci-cd.yml) and [Documentation](../../../../.github/workflows/docs.yml) skip every pull-request job on draft events before allocating a runner. `ready_for_review` starts them, and `converted_to_draft` cancels an in-progress pull-request run through workflow concurrency. Push, manual dispatch, package publishing, and Pages deployment are unchanged.

Merge: the `main` ruleset requires the `CI Passed` and `claude-review` checks; the repository admin role bypasses it, so direct pushes still work. `CI Passed` is the CI/CD aggregator job that needs every CI job. A draft run skips it under its unevaluated name expression, so a draft run cannot satisfy the requirement. `scripts/Enable-AutoMerge.ps1` arms squash auto-merge pinned to the reviewed head after `scripts/Assert-PrGreen.ps1 -ForAutoMerge` passes: no failed check, `claude-review` succeeded on the head, and no actionable review body or unresolved thread. A push to an armed PR disarms it through the `disarm-auto-merge` job in [Claude Code Review](../../../../.github/workflows/claude-code-review.yml). `scripts/Merge-Pr.ps1` merges immediately, and its gate additionally requires a successful `CI Passed`. Neither wrapper cleans up after auto-merge; the loop's `Remove-MergedWorktrees.ps1` sweep does.

Automated review rounds (rework budget): Claude posts as `github-actions[bot]` conversation comments, with no marker, and no other workflow comments on PRs: `gh api --paginate "repos/{owner}/{repo}/issues/<N>/comments" --jq '.[] | select(.user.login == "github-actions[bot]") | .id' | wc -l`.

## Merge gate

The `Merge` rules above, plus:

- Arming auto-merge is the normal way a PR merges: a ready PR that completed the lifecycle gets armed. A draft is never a merge candidate. Merge directly only a ready PR whose checks have all finished and that has no auto-merge armed.
- A direct merge requires an open, ready-for-review PR that completed the lifecycle; `MERGEABLE` and `CLEAN`; every applicable check present, terminal, and passing (`SUCCESS`, `SKIPPED`, or `NEUTRAL`, with successful legacy commit statuses); no unresolved threads, unaddressed comments, or outstanding review-body concerns; approval when required; and a bot review or CI cycle after the last fix push. Missing expected checks and nonterminal checks block merging. Reassess if the head changes.
- After a fix push, allow that cycle before considering merge in a later iteration.
- `Merge-Pr.ps1` calls `Assert-PrGreen.ps1`, merges on a passing mechanical gate, and cleans up.
- Read every review body, inline thread, and conversation comment on the current head first. The scripts are necessary but not sufficient.
- Never bypass the wrappers with direct `gh pr merge` or `gh pr merge --auto`. Never mark a PR ready, arm it, or merge it while it waits for a review of its current head.

## Review cycle

Run this under the PR lock whenever a PR has feedback. `AGENTS.md` § Commits and PRs owns the surfaces and thread-resolution policy; this section is the full procedure.

**Fetch.** Inspect all review surfaces, including pagination and replies: REST `pulls/<N>/reviews`, `pulls/<N>/comments`, and `issues/<N>/comments` under `repos/{owner}/{repo}`, and GraphQL `reviewThreads` with IDs, resolution state, bodies, authors, and timestamps.

Never infer a reviewer's surface from its identity: check both every cycle. Treat a conversation comment that carries findings or a verdict as a review; it has no thread to resolve, so disposition it with one PR comment that quotes or links it.

A `COMMENTED` review body can block with zero unresolved threads. Address outstanding concerns, `CHANGES_REQUESTED`, current inline findings, and unresolved threads. Empty acknowledgments are non-blocking. Assess older findings against the current head and later discussion; uncertain concerns block merging.

**Classify.** Verify every finding against the code.

- **Blocking:** a correctness, concurrency, security, contract, or performance defect, or missing tests for changed behavior. Fix each valid blocking finding in this PR.
- **Non-blocking:** naming, style, wording, optional refactors, extra tests for unchanged behavior, and "consider" suggestions. Implement one only when it is small and you already push a blocking fix. Otherwise decline it with a one-line reason and push nothing for it.
- Decline a wrong finding with evidence from the code. Move genuinely out-of-scope architectural work to a filed follow-up issue, linked in the reply.
- "Not done" or "left as is" without a classification, reason, or linked issue is not a disposition.

**Reply.** Reply once per review covering each finding, and reply on each thread. Discuss disagreement once; implement reaffirmed blocking feedback. A disposition-only reply on the current head needs no new push, so it starts no review cycle. Reply to addressed review-body findings and request re-review when an outstanding changes-requested review needs updating; never dismiss reviews to bypass approval requirements.

**Resolve.** Resolve every thread, human or bot, with GraphQL `resolveReviewThread` as soon as it is dispositioned: the fix commit is in the remote PR head and the reply names it, or the reply pushes back with evidence. Do not wait for another review or CI cycle; that cycle still gates merging. Leave a thread open only while it has no disposition: the fix is not pushed, the item waits on the user, or the reviewer replied after your disposition. On each later cycle re-fetch every thread, resolved ones included. A reviewer reply newer than your disposition reopens it: run GraphQL `unresolveReviewThread` and treat it as reaffirmed feedback. Then re-fetch reviews, threads, checks, and head SHA.

## Issue relationships

Writes take the numeric REST issue `.id`, not the issue number or `gh issue view --json id` (a GraphQL ID). Pass it with `-F` so it is sent as an integer:

```powershell
$childId = gh api "repos/$repoSlug/issues/<child>" --jq .id
gh api "repos/$repoSlug/issues/<parent>/sub_issues" -X POST -F "sub_issue_id=$childId"

$blockerId = gh api "repos/$repoSlug/issues/<blocker>" --jq .id
gh api "repos/$repoSlug/issues/<blocked>/dependencies/blocked_by" -X POST -F "issue_id=$blockerId"
```

Verify each write with a GET on the same path before proceeding.

## Worktrees

Capture these with the skill's shared-checkout variables:

```powershell
$repoName = ($repoSlug -split '/')[-1]
$worktreeRoot = Join-Path (Split-Path $repo -Parent) ($repoName + '-worktrees')
```

- Create worktrees under `$worktreeRoot`; use a writable temporary `<repoName>-worktrees` root if needed. Verify the repository and intended base; examples use `origin/main`.
- This repository does not use sparse-checkout.
- First push of a branch: `git push -u origin HEAD`. A branch created from `origin/main` can track it, so never rely on a bare `git push`.
- Use repository scripts for merged-worktree cleanup; `[gone]` or ancestry alone is insufficient. Preserve dirty, locked, open-PR, harness-managed, and locally divergent worktrees. Inspect untracked source before cleanup: scripts may clear build artifacts. Manually remove an abandoned worktree only after verifying repository, path, ownership, and absence of valuable unpublished work.
- Stop only services, processes, and containers started for this worktree. Never blanket-delete containers or volumes, or stop the shared lock Redis.

### Agent output locations

Write nothing loose beside the repository. Every file an agent produces outside a worktree has one home, so cleanup can find it:

- Evidence (build and test logs, reports, PR and comment body drafts, diffs, validation projects) goes in `<repo-parent>/<repoName>-evidence/<lock-name>/`, for example `C:/git/Reservoir-evidence/issue-42/build.log`. `scripts/Remove-MergedWorktrees.ps1` deletes an entry once its newest file is older than 7 days (`-EvidenceRetentionDays`).
- Throwaway checkouts and baselines are worktrees under `$worktreeRoot`, never plain copies.
- Downloaded SDKs and CLI tools go in `C:/git/toolchains/<name>-<version>/`, shared by every repository.
- Never write to `C:/git` itself or to the shared checkout.

## Implement and ship

- Follow local validation, resource limits, target frameworks, generated-code boundaries, and performance requirements. Use TDD for bugs and behavior changes where feasible; explain exceptions. Run focused and relevant broader checks, including docs and website checks when affected.
- Review the full diff for correctness, reuse, and efficiency (`/simplify` when available, otherwise equivalent review). Preserve performance and contracts, and batch related fixes into one push.
