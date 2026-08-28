# 问题跟踪器：GitHub

本仓库的问题和规格均记录为 GitHub Issues，所有操作使用 `gh` CLI

## 约定

- 创建问题：`gh issue create --title "..." --body "..."`，多行正文使用 heredoc
- 读取问题：`gh issue view <number> --comments`，使用 `jq` 筛选评论并同时获取标签
- 列出问题：`gh issue list --state open --json number,title,body,labels,comments --jq '[.[] | {number, title, body, labels: [.labels[].name], comments: [.comments[].body]}]'`，按需使用 `--label` 和 `--state` 筛选
- 评论问题：`gh issue comment <number> --body "..."`
- 添加或移除标签：`gh issue edit <number> --add-label "..."` 或 `--remove-label "..."`
- 关闭问题：`gh issue close <number> --comment "..."`

仓库信息从 `git remote -v` 推断；在克隆目录中运行时，`gh` 会自动完成此操作

## 将 Pull Request 作为 triage 入口

**PRs as a request surface: no**

如需将外部 PR 作为功能请求，可将该标志改为 `yes`，`/triage` 会读取此设置

启用后，PR 使用与 Issue 相同的标签和状态，并采用对应的 `gh pr` 命令：

- 读取 PR：`gh pr view <number> --comments`，使用 `gh pr diff <number>` 查看差异
- 列出待 triage 的外部 PR：`gh pr list --state open --json number,title,body,labels,author,authorAssociation,comments`，仅保留 `authorAssociation` 为 `CONTRIBUTOR`、`FIRST_TIME_CONTRIBUTOR` 或 `NONE` 的项目
- 评论、设置标签或关闭：使用 `gh pr comment`、`gh pr edit --add-label`、`gh pr edit --remove-label` 和 `gh pr close`

GitHub 的 Issue 和 PR 共用编号空间，因此 `#42` 可能指向任意一种对象。先执行 `gh pr view 42`，失败后再执行 `gh issue view 42`

## 技能要求发布到问题跟踪器时

创建一个 GitHub Issue

## 技能要求获取相关工单时

执行 `gh issue view <number> --comments`

## Wayfinding 操作

供 `/wayfinder` 使用。map 是一个 Issue，其 child Issues 作为具体工单

- Map：带有 `wayfinder:map` 标签的单个 Issue，正文包含 Notes、Decisions-so-far 和 Fog。使用 `gh issue create --label wayfinder:map`
- Child ticket：通过 GitHub sub-issue 关联到 map 的 Issue。使用 sub-issues API；如果仓库未启用 sub-issues，则将 child 添加到 map 正文的任务列表，并在 child 正文顶部写入 `Part of #<map>`。标签使用 `wayfinder:<type>`，其中类型为 `research`、`prototype`、`grilling` 或 `task`。工单被认领后，将其分配给负责开发者
- Blocking：优先使用 GitHub 原生 Issue dependencies。通过 `gh api --method POST repos/<owner>/<repo>/issues/<child>/dependencies/blocked_by -F issue_id=<blocker-db-id>` 添加依赖边。其中 `<blocker-db-id>` 必须是阻塞 Issue 的数字 database id，可通过 `gh api repos/<owner>/<repo>/issues/<n> --jq .id` 获取，不能使用 `#number` 或 `node_id`。GitHub 通过 `issue_dependencies_summary.blocked_by` 报告尚未关闭的阻塞项。如果依赖功能不可用，则在 child 正文顶部添加 `Blocked by: #<n>, #<n>`。所有 blocker 关闭后，工单解除阻塞
- Frontier query：列出 map 中所有未关闭的 child，排除仍有开放 blocker 或已有 assignee 的项目，按 map 中的顺序选择第一个
- Claim：执行 `gh issue edit <n> --add-assignee @me`，这是会话中的首次写操作
- Resolve：执行 `gh issue comment <n> --body "<answer>"`，随后运行 `gh issue close <n>`，最后向 map 的 Decisions-so-far 追加上下文指针与链接
