# Windows App Paths source

这是 Windows 原生应用来源适配，不是 Tinycast 的 macOS `.app` 索引或 Store/AUMID 支持。

- Applications 的 **Include Windows App Paths** 默认开启，随 Save changes 原子持久化为 `IncludeWindowsAppPaths`。应用总开关同时停止该来源；关闭来源不删除收藏、别名、学习或图标偏好。
- 只读本机 HKCU/HKLM 的 `Software\Microsoft\Windows\CurrentVersion\App Paths`，64/32 位视图，用户优先；同注册名称大小写去重。较高优先级的无效记录不会回退同名机器记录。
- 四个根、1024 检查键、512 新应用有界；原始/展开路径最多4096字符。注册字符串使用固定缓冲原生读取，拒绝超长、错误类型、控制字符、错误 UTF-16、相对/远程/设备路径、不存在或路径链含重解析点的文件。
- 只接受无名 `REG_SZ`/`REG_EXPAND_SZ` 的完整 `.exe` 目标；支持整路径引号及有界环境变量展开。忽略 `Path` 覆盖、其他值、卸载注册表及参数。生产不写注册表、不查询远程注册表、不遍历目标目录、不启动应用或提权。
- 实际目标与开始菜单快捷方式、目录 EXE 去重，优先保留 `.lnk` 及其原始参数。新注册来源行使用 EXE 文件名；最多16个注册名称作为普通关键词，而非用户主动别名/标题匹配。已经被其他来源覆盖的目标不另附注册关键词。
- 启动直接使用已验证的本地 EXE；不模拟 Shell 按注册名称查找时的 PATH 注入或其他 App Paths 扩展属性。入口、动作中的启动及 Reveal 共享应用/来源门禁，并在外部动作前复核本地路径；异步重建尚未完成时的旧注册行同样不能执行。
- 启动、手动 Refresh index、保存来源/范围设置时重读；不轮询注册表、不承诺实时注册变更通知。关闭来源不影响独立开始菜单/手选范围/发现来源的同一 EXE。

## 验证边界

Windows 测试仅写 GUID 命名的 `HKCU\Software\KikicastTests\…` 夹具并清理，不改真实 App Paths。覆盖引号/展开、原值/文件字节不变、目标去重、用户优先、无效/缺失/远程/参数/超长/错误类型、枚举/结果/关键词上限、失效根和调用方 handle 保留。索引关闭断言来源 callback 根本不调用。

WPF 受控模型覆盖真实根查询关键词、重建前旧行拒绝执行、关闭不误伤独立本地来源；真实 Space/Save 自动化覆盖 checkbox 落盘及恢复。测试不启动注册应用，也不代替第三方安装器、x64 包、物理热键、IME/TSF 或最终验收。

公共 API 合同参考：[Application Registration](https://learn.microsoft.com/en-us/windows/win32/shell/app-registration)、[RegEnumKeyExW](https://learn.microsoft.com/en-us/windows/win32/api/winreg/nf-winreg-regenumkeyexw)、[RegQueryValueExW](https://learn.microsoft.com/en-us/windows/win32/api/winreg/nf-winreg-regqueryvalueexw)。
