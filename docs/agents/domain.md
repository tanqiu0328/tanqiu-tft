# 领域文档

工程技能探索代码库时，应按照本文件说明读取领域文档

## 开始探索前

- 读取仓库根目录的 `CONTEXT.md`
- 如果根目录存在 `CONTEXT-MAP.md`，则改为读取该文件，并按其指引读取与当前任务相关的各个 `CONTEXT.md`
- 读取 `docs/adr/` 中与即将修改区域相关的 ADR
- 对于 multi-context 仓库，还应检查 `src/<context>/docs/adr/` 中的上下文级决策

如果这些文件不存在，直接继续，不报告缺失，也不预先建议创建。`/domain-modeling` 技能会在术语或架构决策真正明确后按需创建它们

## 文件结构

本仓库采用 single-context 布局：

```text
/
├── CONTEXT.md
├── docs/adr/
│   ├── 0001-event-sourced-orders.md
│   └── 0002-postgres-for-write-model.md
└── src/
```

如果未来转为 multi-context 布局，则由根目录的 `CONTEXT-MAP.md` 指示：

```text
/
├── CONTEXT-MAP.md
├── docs/adr/                          ← 系统级决策
└── src/
    ├── ordering/
    │   ├── CONTEXT.md
    │   └── docs/adr/                  ← 上下文级决策
    └── billing/
        ├── CONTEXT.md
        └── docs/adr/
```

## 使用术语表中的词汇

当输出内容提及领域概念时，包括 Issue 标题、重构提案、假设或测试名称，应使用 `CONTEXT.md` 中定义的术语，不要改用术语表明确排除的同义词

如果所需概念尚未出现在术语表中，应重新判断该词是否脱离项目语言，或将其记录为需要 `/domain-modeling` 补充的真实空白

## 标记与 ADR 的冲突

如果输出内容与现有 ADR 冲突，应明确指出，不要静默覆盖：

> 与 ADR-0007（event-sourced orders）冲突，但值得重新讨论，因为……
