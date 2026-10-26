# 发布前的隐私检查

每次更新代码和上传发布包都执行检查。公开目录只保留通用代码、说明、模板和虚构示例。个人总账、真实任务和课表、聊天身份、机器路径、服务器信息、凭证、运行日志、截图与安装配置保存在公开目录之外。

## 推送前

先人工检查改动和文件清单，再检查工作树、暂存区及本地可达提交历史：

```text
python tools/audit_release.py . --git-index --git-history
```

如需检查使用者特有的名字、地址或项目线索，将这些字符串放进**仓库外**的JSON数组文件，通过`--private-values-file`传入。不要把词表、扫描时命中的原文或带敏感绝对路径的报告复制到仓库。

启用此仓库自带的推送门禁：

```text
git config core.hooksPath .githooks
```

可以在本地Git配置中设置`workflow.auditPython`为可用Python程序、`workflow.privateValuesFile`为外置词表。两项配置不属于公开源码。门禁失败时先修正问题，再重试推送。

## 发布包

```text
python scripts/package.py
python tools/audit_release.py . --archives artifacts
```

打包器仅选择允许发布的源码，并检查选中文本与最终ZIP内容。需要外置私人词表时，对最后一条命令同样传入`--private-values-file`。上传前核对三个ZIP清单与`SHA256SUMS.txt`，上传后核对文件大小及可用的远端校验值。

GitHub自动检查再次审查完整历史和最终包。它发生在推送后，本地检查和人工复核必须先完成。检查可拦截已知格式与已提供的私密词，但不能证明所有文字都不含个人线索。

仓库作者与上游项目的公开署名、来源链接、版权及许可证需要保留。它们不替代个人数据审查。
