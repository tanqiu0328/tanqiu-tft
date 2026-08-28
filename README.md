# 紫云院妙妙屋

本地离线保存和查看云顶之弈阵容一图流的 Windows 桌面应用

## 开发

需要 .NET 10 SDK 和 Windows 11

```powershell
dotnet restore TanqiuTft.sln
dotnet test TanqiuTft.sln
dotnet run --project src/TanqiuTft.App/TanqiuTft.App.csproj
```

首次启动会在用户文档目录的 `紫云院妙妙屋\阵容库` 中创建默认阵容库

## 发布

```powershell
dotnet publish src/TanqiuTft.App/TanqiuTft.App.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish
```

发布产物自带 .NET 运行时，可在未安装 .NET 的 Windows 11 x64 电脑上离线运行

## Agent skills

### Issue tracker

问题与规格通过 GitHub Issues 跟踪。详见 `docs/agents/issue-tracker.md`

### Triage labels

使用五个默认 triage 标签。详见 `docs/agents/triage-labels.md`

### Domain docs

采用 single-context 领域文档布局。详见 `docs/agents/domain.md`
