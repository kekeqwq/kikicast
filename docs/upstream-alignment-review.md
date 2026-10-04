# Tinycast / Raycast Windows 对齐复核

复核日期：2026-10-03。**这是差距与实施合同，不是完成报告。**

- 原实现基线：Tinycast `5d357d49c91157459e34a5332b6c1772ed110fab`。
- 本次重新读取上游 HEAD：[`fa1c2bb2849dd826abc43b9343abad835607bdcf`](https://github.com/abue-ammar/tinycast/tree/fa1c2bb2849dd826abc43b9343abad835607bdcf)。后续行为移植必须记录具体源文件与提交，不把上游文档中残留的命令数量当事实。
- Raycast 是闭源产品；本次读取官方 Applications、Keyboard Shortcuts、System Commands 文档，没有分析其私有数据库或反编译。用户对独立 EXE 的实测是需求输入，不是其内部实现的证据。

## 持续移植规则

完整源码目录清单是开发待办，而不是只靠用户报告补洞。后续每批主动检查上游的模型、协调器、持久化与键位，先交付可移植闭环，再同步此表；不以命令名称/占位条目代替能力。Windows/.NET 的公开 Shell/COM/Win32、WinRT 与标准 HTTP 适配均算原生可移植；仅 macOS 独有或需私有 Windows API 的行为明确跳过/改为可靠替代，不能把缺少实现称为技术上不可实现。

本轮重新阅读 `CalcCurrency`/`CurrencyRateStore`/`CurrencyFeed`/`CalcFormatter` 与 `ShellCommandRunner`、Custom Commands 文档。法币采用公开 Frankfurter 每日接口，不依赖 Tinycast 使用的非公开 Raycast backend 合同；只发固定 USD 基准请求，24h 缓存、失败30min退避、离线旧价标记。当前仅显式 ISO 对转换，非全部 Quantity 计算。根据用户后续明确需求，pwsh 路径改为**Enter 立即运行，不确认**：选中 shell fallback 直接在系统真实终端启动 PowerShell 7，使用 UTF-16LE EncodedCommand/ArgumentList，默认加载 profile、NoExit，不加 NonInteractive、不重定向/关闭 stdin，不强制 PlainText。原生终端处理交互/密码/方向键/ANSI/全屏 TUI/Ctrl+C；不是在旧只读管道窗加一个输入框。空命令入口是编辑器，Enter 运行、Shift+Enter 换行；启动后终端独立存活，退出 Kikicast 不杀命令。Kikicast 无脚本/输入/输出日志或学习词；终端/pwsh 的自身历史规则不更改。保存命令库/JSON 导入导出本轮已实现；位置参数（最多3个/必填/可选/字面数据）已实现；内嵌 ConPTY 与 Raycast 脚本目录导入仍待实现；保留的非交互管道 runner 仅用于受控测试，不用于用户命令。回收站等独立破坏性系统动作仍保留原确认，不套用此 Shell 约定。

## 距离原计划还有多远

**P1 未达到出口，P2 仅有基础窗口操作，P3/P4 大多未实现。不能称为接近 Tinycast 功能齐全，也没有依据给一个整体完成百分比或工期。**

| 工作流 | 当前实际 | 缺口 |
| --- | --- | --- |
| 呼出与设置 | 托盘、单实例、双击 Ctrl/Alt/Shift、组合键录制、焦点及英文输入会话、JSON 保存 | 现有条目统一绑定已接入；设置搜索、引导及未来模块绑定待补；真实 IME/物理热键仍需验收 |
| 根搜索 | 开始菜单 `.lnk`、独立 EXE 自动发现/手动添加/删除同步/目标去重、拼音/模糊、Favorites/Suggestions/分组、anchor 使用学习与三个搜索词 | 本地手选范围/默认一层、可配0–3层已有功能设置闭环；现有类型字段比较器/匹配强度已接入；更多来源/全语言罗马化待补；Store/AUMID 当前用户来源/来源门禁/公开激活路由及有界PNG图标已接入，真实包激活与更多图标变体仍待验收；本地 EXE/快捷方式资源图标已接入。动作/参数/设置已有受控原生按键与恢复验证，物理焦点/IME 验收仍未完成 |
| 计算 | 基础四则/括号/幂/科学计数法、基础法币转换、200 条连续重复抑制历史/删除/清除 | 百分比/隐式乘法/函数/单位/日期/进制、完整货币/别名/加密货币表达式等 |
| 窗口 | 32 个动作、半屏尺寸/显示器循环、单级恢复、独立绑定；自定义尺寸/九锚点/DIP与百分比/偏移/稳定UUID已接入；None/Sizes/Displays互斥，旧bool/绑定ID不变 | 已打开窗口布局编辑/预览/捕获/保存/实际摆放/统一绑定已接入；逐布局默认关闭的缺失应用启动/共享原lnk安全启动/限时事件等待已接入；文件/文件夹、URI/deeplink、字面argv输入接入直接EXE/包公开契约，lnk额外输入及包文件夹/argv明确拒绝；Rooms、切换器及受控实包/更新匹配待补；用户已接双屏，同为192 DPI，自有四半动作完整tour和跨屏布局/Restore通过；混合DPI/hotplug/第三方窗口验收待做，不模拟macOS全屏或Space |
| 日常工具 | 自定义 PowerShell 命令库/启停/目录/Profile/收藏/名称搜索/JSON 导入导出；Open/Empty Recycle Bin | 剪贴板、Quicklinks、Snippets、Notes、Emoji、文件搜索、其余系统动作；参数和现有类型全局绑定已实现，Raycast 脚本导入待补 |
| 高级功能与发布 | 尚无完整功能闭环 | OCR、AI/Quick Actions/MCP、日历/摄像头、备份、升级、签名、自启动与性能/安全回归 |

现有自动测试只验证已经写出的算法和路径，**测试通过数不能代表上游覆盖率**。本次界面透明修复也不能抵消上述功能缺口。

## 1. 空搜索 Suggestions：最多五个条目，不是五个固定分类

源码：`Launcher/Model/LauncherSuggestions.swift`、`LauncherRankingStore.swift`、`LauncherOrder.swift`，以及 `Launcher/UI/LauncherList.swift`。

上游明确规定：

1. 空查询顺序为 **Favorites → Meetings（启用时）→ Suggestions → 各类型分组**。
2. Suggestions 最多 **5 个条目**，可来自不同类型；不是固定五种分类，不是简单取字母顺序前五个应用。
3. 优先放入最多两个 **五分钟内刚安装、未使用过**的应用/扩展。
4. 再按使用排序选取有学习记录、**没有全局快捷键**的条目。收藏、会议、AI 命令和 Tinycast 自身不参与建议。
5. 不足五个时，按优先级补入启用的内置命令：Clipboard History、Search Files、My Schedule、Search Emoji，随后 Create Quicklink / Create Snippet。关闭的功能不会被推荐。
6. 建议项从下方类型列表移除，不能出现两份。标题不参与选中索引。
7. 频率不是简单总次数：每次访问加 100，**十天半衰期**，最低 1；保留最近三个不同搜索词，最长 64 字符，17 天后搜索词不再影响排序。
8. 通过面板打开才记录搜索词；分类查询只记访问，**全局热键和 fallback 不学习**。

本轮已替换 `Count × decay(lastUsed)`：`LauncherUsage.cs` 使用 anchor 学习曲线与最近三个词，旧计数近似转换，无法重建原始访问时序。`LauncherSections.cs` 提供稳定 ID、收藏/建议/类型的同一平面选择顺序；WPF 分组标题不占索引，空查询不重复条目。绑定窗口动作不进入使用建议；未实现命令不作为冷启动填充。

已重新读取 `LauncherMatch.swift` / `LauncherOrder.swift` / `ranking-test.swift` 并移植现有类型的字段比较器：UTF-16 最佳对齐/词首与 CamelHump/可跳过分隔符，Low/Medium/High（默认 Medium）实际设置落盘；精确别名、长查询精确标题、精确学习词、精确副标题、前缀别名、学习前缀、最多3字延伸、frecency、名称强度/前缀、类型优先级、数字顺序与稳定索引逐层判定。关键词只作准入，不冒充标题；保存命令只索引名称。Boost 纯策略已测试，尚无扩展来源接入，不提供占位开关。本轮复现上游式逐对 boost 例外的三条目循环；Windows 明确改成每次完整匹配 pass/精确别名桶内先决定撤销，再作稳定比较，不假称逐对排序完全一致。三查询64条目反对称/全部三元传递性、混合boost顺序/限额前缀和分配回归通过；热路径去掉LINQ/逐次params数组及重复折叠。[生成Core性能基准](search-order-performance.md)已记录5k/20k本地数据，但不替代端到端性能门禁。标题/拼音/首字母按条目身份缓存，改名不重用旧缓存。

学习词改用与匹配相同的 Latin/宽度/大小写/音标折叠（`微信`→`wei xin`，保留词间空格）；旧词在弱身份缓存中规范化，不修改原历史记录，下一次实际访问才原子发布规范值。旧版本丢掉的空格无法可靠重建，不假装恢复；64字符/3词/17天限额保留。中文罗马化使用 TinyPinyin；并非 Swift 的所有文字转写，排序用确定的数字/大小写/音标比较，不冒充 macOS 本地化排序；更多来源/语言仍待补。Windows 没有统一可靠的安装时间，不能拿首次扫描时间冒充刚安装时间；现有索引不提供安装时间，跳过“刚安装”分支。纯策略支持可信时间，且不把未来时间当新安装。

首批已加入共享条目描述与分组/选择模型、稳定 ID、Suggestions 与 Favorites。收藏用 Ctrl+Shift+F，空查询 Ctrl+1…9/0 对应可见槽，Ctrl+Alt+↑/↓ 排序；缺失/关闭入口保留配置。Ctrl+↑/↓ 分组、Ctrl+N/P 移动已接入。配置写入串行，失败不更新内存，设置保存不覆盖同期收藏。Ctrl+K 同窗口动作与条目管理已接入；字段比较器已接入当前类型；现有类型全局统一绑定已接入，更多来源/语言与未来模块绑定仍待补。

## 2. Open Trash / Empty Trash：提升为近期必做

源码：`SystemActions/Model/SystemAction.swift`、`Service/SystemActionRunner.swift`、`UI/SystemActionCoordinator.swift`。

- 上游显示 **Open Trash / Empty Trash**，清空策略默认确认，macOS 上跟随 Finder 设置。
- Windows 显示可使用 **Open Recycle Bin / Empty Recycle Bin**，同时索引 `open trash`、`open trash bin`、`empty trash`、`empty trash bin`，保证原查询习惯可用。
- 打开：系统 Shell 的回收站命名空间。
- 清空：公开 `SHQueryRecycleBin` / `SHEmptyRecycleBin`，覆盖当前用户可访问的卷；**不递归删除 `$Recycle.Bin`，不提权**。
- 空站是正常无操作结果，不报删除失败；清空在执行确认后才调用接口。
- 全局快捷键与搜索结果共享同一执行/确认入口，危险操作不能绕过确认。真实清空绝不用于自动冒烟测试。

本轮已接入两个可搜索/可收藏的命令，保留 Trash 查询词。`RecycleBinCommand` 是统一确认门：空站不删除，确认默认 No、Esc/Cancel 不删除；`RecycleBinService` 使用公开 Shell API 在专用 STA 线程执行，成功/空站给托盘反馈。自动测试只用假删除接口；桌面冒烟仅做真实只读统计和搜索检查，从未清空用户回收站。现有条目全局绑定及同窗口动作菜单均已接入，共享执行/确认门；其余 P1-C 功能仍不能视为全部完成。

### 功能与设置成对交付

后续每项以 [功能设置矩阵](feature-settings-matrix.md) 约束：功能、可用性门禁、持久化、设置项、异常与验收一同落地，不为未实现模块放占位开关。本轮 Features 页提供 Calculator、Currency、History、adhoc Shell、fallback、System commands 开关；Commands 页独立库开关/launcher visibility 与每条 Enabled、Run In、Load profile。关闭历史同时停止记录但保留既有数据；关闭 Shell 关闭未运行编辑器，不终止已运行终端。

本轮重新核对 `CustomCommand.swift` 与 `docs/features/custom-commands.md`：移植稳定 UUID、名称独占检索、关闭保留数据/引用、单条启停、库与 adhoc 分离、目录/profile 语义。用户要求覆盖上游的确认/EOF 策略，固定采用 Enter 无确认 + 真终端交互；不提供无法兑现的内嵌 Show output/Stop 假开关。Profile 对保存命令默认关，对 adhoc 保持默认开。JSON 导入是 Kikicast 格式（非兼容 macOS zsh/Raycast 格式），有 default-No 警示且导入项强制禁用；相同 UUID 替换，名称冲突整批拒绝。库原子保存/串行更新/失败不发布；损坏 schema 与 JSON 都保留原件并禁写。位置参数与全局快捷键已接入；图标选择/Raycast 脚本目录导入/完整分类备份仍待补。

重新阅读 `docs/features/hotkeys.md` 与 `HotKeys/Model/HotKeyAction.swift` 后，已接入现有应用/保存命令/内置动作统一条目绑定，窗口绑定继续用旧枚举和注册 ID；无新增默认绑定。Settings Launcher 录制/清除草稿、Save changes 统一校验/注册/原子持久化，旧配置缺省空 map、256上限、禁用/缺失保留恢复。隐藏与 Enabled 分离，热键和 Enter/Actions 共用 ExecuteRowAsync，参数热键只进入参数栏/清瞬时旧值，全局入口不学习查询词，回收站仍默认 No 确认。受控 OS 注册失败/锁文件回滚、真实设置录制/Save/reload/冲突双 Ctrl/清除草稿与注册 Ctrl+Alt+F24 历史模式/native 状态恢复通过；不是物理热键/TSF 验收。详情见 [条目绑定](entry-bindings.md)。

## 3. 独立 EXE 与应用发现

Tinycast 源码：`Launcher/Model/SearchScopes.swift`、`Service/AppIndex.swift`、`RunningAppsMonitor.swift`。

- Tinycast 扫描配置的目录或单独 `.app`，普通目录往下一层，不扫整盘；修改范围立即重建索引，呼出时刷新。
- `RunningAppsMonitor` 用 NSWorkspace 维护运行指示与退出动作；**所读源码并不是“自动从所有系统启动历史发现程序”的实现**。
- Raycast 官方 [Applications](https://manual.raycast.com/applications) 支持用户添加 Search Scopes，Windows Deep Search 可向下三层。
- 官方页面未说明它是否/怎样从 UserAssist、Prefetch、系统启动历史发现独立 EXE，不能据此猜测。用户观察到的结果应由我们自己的可靠机制达到。

### Kikicast 的目标来源

| 来源 | 接法 | 保留的语义 |
| --- | --- | --- |
| 开始菜单 `.lnk` | 继续保留 Shell 入口，并解析必要身份信息 | 同 EXE 不同参数的快捷方式不误合并 |
| Store / 包装应用 | 已接入公共 Shell AppsFolder / 当前用户注册 AUMID | 独立来源门禁；不以通用宿主 EXE 为身份/启动目标；真实激活验收待做 |
| 注册入口 | 已接入已文档化的 App Paths | 不把卸载条目当可执行入口 |
| 手动入口与范围 | 添加单个 EXE、目录；默认有界扫描、深度可配置 | 可移除、可隐藏；不递归整盘 |
| 运行中的桌面应用 | 呼出时枚举可见顶层窗口，查询进程完整路径 | 找到不在开始菜单的便携程序 |
| 使用过的独立 EXE | 可说明/关闭的前台窗口事件观察，持久化已发现入口 | 退出后仍可搜索，不依赖已安装程序清单 |

发现与学习必须分开：

- 发现“程序存在”不等于一次启动；切前台次数不能伪装成启动次数。
- 按 PID + 进程创建时间去重一次进程生命周期，避免多窗口、最小化/恢复和 Kikicast 自己恢复目标反复加分。
- 自己、系统宿主、后台无窗口进程不作为便携应用；无法读取的管理员进程跳过，不请求提权。
- **不采集窗口正文、文档标题、进程命令行或按键**；不读未文档化的启动历史注册表，不使用 Prefetch/私有数据库。
- 明确告知：只能记住观察期间/呼出时可见的程序，不能声称知道安装 Kikicast 以前的所有启动。
- 稳定身份去重 `.lnk` / EXE / AUMID 时要保留参数入口；已有快捷方式可作优先启动入口。离线、删除或移动 EXE 不自动删收藏/历史。
- 新发现程序仅在用户 Enter 时执行，不能自动运行；学习日志可清除，关闭观察后已保存入口仍可手动管理。

本轮已实现事件驱动的桌面 EXE 发现：两个公开 WinEvent hook（Foreground / ObjectShow），callback 只投递有界队列，单一后台 worker 用有限查询权限读取路径/进程创建时间，按 PID+创建时间抑制重复处理。启动/手动扫描枚举一次可见顶层窗口，平时不轮询进程列表。1000 条记录、512 个生命周期缓存、最多 64 个目录 watcher、每批最多 64 条事件并合并存盘；超额/异常目录用 30 秒文件存在性复核，呼出时也复核。不是性能基准报告，不声称能捕获极短暂/无窗口/不可查询的程序。

开始菜单先通过公开 IShellLinkW/IPersistFile 读取目标（不 Resolve、不执行），展开环境变量/短路径，再启用后台发现。已覆盖目标不登记，旧发现记录若后来被开始菜单覆盖也移除；实际启动仍使用原 `.lnk` 参数。无法解析的特殊链接/路径别名尚不能保证完全去重，需要后续 Shell/AUMID 身份模型，不按名字猜测合并。已解析但目标删除的 `.lnk` 也从索引剔除。

独立 `discovered-apps.json` 在家目录配置渠道持久化；损坏时原件保留、写暂停。窗口事件仅记存在，不加 frecency。删除/重命名从搜索消失，收藏/学习 ID 保留但不显示无效入口；Forget 另记忽略路径防止自动反弹，手动 Add 可恢复。Applications 设置页支持开关、扫描、添加、Forget、刷新开始菜单。手选本地扫描目录已接入并重读 `SearchScopes.swift`：32 范围上限、最多20000检查项、默认一层子目录、可选0–3层（Windows 适配额外配置，不假装上游已有此设置）、不跨重解析点/隐藏/系统项，不支持网络或盘根；在 Applications 页 Add/Remove 草稿、Save 生效，`~` 家目录缩写可持久化，应用总开关停止索引/事件发现/搜索。目录内 EXE 也按快捷方式实际目标去重、保留 `.lnk` 参数；删文件/移除范围后刷新消失，收藏引用不删除。开始菜单扫描另有10000检查项/12层上限；并发刷新合并后按最新设置再扫描。本地应用资源图标已接入：公开 ShellLink 同次读取 IconLocation/Index，绝不 Resolve/改写/启动；可见 WPF 绑定懒请求、单 worker/队列256/cache256，SHDefExtractIconW 读取64px后冻结内存 BitmapSource，HICON 及时释放，无 Shell 扩展/磁盘缓存。优先快捷方式自定义图标，失败回退实际 EXE，再降级通用符号；拒绝网络、路径链重解析点、超过256MiB文件及非 exe/dll/ico，大小16–128与索引±65535有界。Launcher 的 Show application icons 独立保存，总应用开关同时关闭提取；图标缺失不影响启动参数。Owned 原生提取、损坏/重解析点拒绝、缓存/功能门、真实 checkbox/Save 均验证；混合 DPI 和所有应用视觉不声称通过。共享 LocalPathSafety 从盘根逐段校验（128段上限），拒绝链接祖先后才检查子项；范围、索引目标、图标、发现/保存路径复核/watcher均接入，避免先用 File.Exists 意外穿透网络链接。Windows App Paths 已接入独立设置及门禁、只读本机 HKCU/HKLM 双视图、1024检查键/512新行/4096字符路径上限、实际目标去重和最多16注册关键词；不读取 PATH 覆盖或执行/写注册表。自有 HKCU GUID 根测试与真实 Space/Save 设置往返通过；详情/不模拟的属性见 [App Paths 合同](windows-app-paths.md)。Store/AUMID 现已通过公共 AppsFolder/IShellItem2/PKEY_AppUserModel_ID 和当前用户 FindPackagesByPackageFamily/GetPackageApplicationIds 验证接入；4096检查项/512新行、有界原生缓冲和句柄释放，不读取未文档化 Shell 包属性或包注册数据库。独立 IncludePackagedApplications 实际设置/Save/查询/旧行/全局门禁，稳定 packaged:AUMID 不用版本目录/通用宿主；真实 `.lnk` 读取AUMID元数据去重并保留旧ID/参数。激活路由用 IApplicationActivationManager；PNG 图标只读有界本地 manifest 和7种固定资产路径，不调用Shell图标扩展/枚举目录，缺失降级；已验证19个本机注册入口（仅计数）、受控门禁/PNG与真实checkbox保存。未安装或启动用户包装应用，实际激活/更新/卸载和更多图标变体验收仍待做，见 [包装应用合同](packaged-applications.md)。设置 Applications 页改成可滚动，修复下方按钮裁切；深度下拉/滚动条使用主题资源与圆角，非文本深度选择器局部禁用 IME，普通编辑器不改。真实按键/保存/滚动及本地 junction 后代拒绝测试通过，混合 DPI/高对比度人工验收仍待做。

## 4. 肌肉记忆合同

源码：`Palette/PaletteShortcut.swift`、`PalettePanel.swift`、`LauncherScreen.swift`、`FavoriteSlots.swift`。参照 Raycast 官方 [Keyboard Shortcuts](https://manual.raycast.com/keyboard-shortcuts) 与 [Action Panel](https://manual.raycast.com/action-panel)。

- Tinycast 是主基准，Raycast Windows 用来验证 Windows 习惯，**两者冲突不能悄悄换成 Raycast 键位**。
- 先明确 Command→Ctrl 的平台映射，再逐项解决 Mac Control 与 Command 映射后重叠的组合；不机械把两个修饰键折成同一个 Ctrl。
- 优先补：Ctrl+K 动作菜单、Ctrl+Shift+F 收藏、Ctrl+1…9/0 可见收藏槽、显示位置动作、页面/分组导航、Ctrl+N/P 列表移动、Esc 分层返回、功能模式 Tab 环。
- Raycast 的 Ctrl+F 收藏与 Tinycast 的 Shift+Command+F 不相同，默认继续按 Tinycast 语义；用户可选方案需显式说明。
- 现有 Ctrl+H 计算历史是 Kikicast 临时快捷键，不是已对齐的 Tinycast 固定规则；后续必须列入键位冲突审计。
- Favorites、Suggestions、标题、菜单、参数字段必须共享一个显示/执行模型；标题不是可行动条目。不能把视觉分组加上后让 Enter 操作旁边的条目。
- 已有用户要求保留：双击 Ctrl 默认、强制英文输入、一次聚焦、失焦关闭、系统主题、英文界面。不为了对齐 macOS 把这些改回去。

## 5. Tinycast 真实存储，不是“一个 JSON 包办”

读取：`Platform/AppPaths.swift`、`Settings/AppSettings.swift`、`docs/features/settings-file.md`、`FavoritesStore.swift`、`LauncherRankingStore.swift`、`CalculatorHistoryStore.swift`、`ClipboardStore.swift`、`docs/features/backup.md`。

| 数据 | Tinycast 当前存储 |
| --- | --- |
| 核心偏好、收藏、别名等 | UserDefaults，键有显式归属 |
| 可编辑配置镜像 | 默认关闭的 `~/.config/tinycast/settings.json`；UserDefaults 仍是真源。文件与应用双向同步，坏编辑不覆盖现值 |
| 学习排序 | Application Support 下 `launcher-ranking.json`；串行异步、原子写，清空缓存不应删除学习 |
| 计算历史 | `calculator-history.json`；上限 200，只避免连续重复，不是全历史 distinct |
| 剪贴板 | `clipboard.sqlite3` 与拥有的 `images/`；备份导出才用 JSONL，不代表运行时也是文本 |
| Notes / Snippets | Markdown；可以指定用户内容目录 |
| Quicklinks / 命令 / 布局 / Rooms | 独立存储模块；Rooms 的运行状态不混入可移植配置 |
| 缓存 / 暂存 | 独立 Caches，不进入常规备份 |
| 密钥 | Keychain，不进入配置和备份 |

Kikicast 不需要仿造 UserDefaults，但需要保留这套数据分层和损坏保护。不要把 Tinycast 的可选家目录镜像误称为它的全部状态来源。

### 本次已实施的目录调整

```text
~/.config/kikicast/            Release；Windows 的 ~ 是 UserProfile
  settings.json               当前配置（现有 schema）
  settings.json.bak            上次配置副本
  history.json                当前学习次数与计算历史（现有 schema）
~/.config/kikicast-dev/        Debug，绝不混用 Release
```

- `UserDataDirectory` 从旧 `%LOCALAPPDATA%/Kikicast/` 或 `Kikicast.Dev/` 复制缺少的文件；保留旧文件，**不覆盖新目录已有文件，即使它损坏**。
- 原字节迁移，不用反序列化重写历史或未来版本文件。暂存后逐文件发布；发布失败回退本次新副本并在启动提示，临时继续使用旧目录。
- 冒烟测试仍是独立临时目录；目录与迁移测试只操作受控临时目录。
- 当前读取手动编辑的设置需要重启；**尚无实时文件监视/热键事务重载**。

随着下一批功能增加，再版本化拆分 `state/launcher-ranking.json`、`state/discovered-apps.json`、`state/calculator-history.json`；旧 `history.json` 不会直接丢弃。Notes、Snippets 使用可读 Markdown；大量剪贴板数据使用 SQLite + 自有图片目录，不在每次复制时重写庞大 JSON。密钥走 Windows 凭据保护，缓存留在 LocalAppData 独立目录。布局等用户内容与窗口恢复等机器运行账本分开。这里后半段为计划，不是假装已有这些文件。

## 6. 全部上游 Features 目录的处理去向

| 上游目录 | Kikicast 去向 / 当前状态 |
| --- | --- |
| Launcher | P1 最高优先级补齐建议、收藏、分组、评分、入口发现与动作；部分实现 |
| HotKeys | 现有面板/窗口/应用/命令的稳定 ID、统一录制/绑定、侧别及冲突已接入；Win 双击及未来模块绑定待补 |
| Settings | 家目录 JSON 已接入；搜索、按功能逐项配置、重载/导入保护仍缺 |
| Onboarding | 补默认值、目录、监听范围、权限说明；未实现 |
| Calculator | 基础及 ISO 法币转换已实现；补历史/高级计算/全货币语法，不使用 eval |
| SystemActions | Open/Empty Recycle Bin 已接入搜索/收藏/全局绑定与共享确认；锁屏、电源、媒体、Core Audio 等仍待实现 |
| WindowManagement | 32 项动作、自定义尺寸及[显示器循环](window-display-cycle.md)实现/设置/持久化/绑定/Restore已接入；生成两/三屏完整槽位与真实单屏不翻边/模式切换/观测漂移/原生键路由通过；已打开窗口布局的编辑/捕获/纯计划/实际摆放已配套设置/原子保存/绑定接入；默认关闭的缺失应用启动/保存门禁/取消/生产库存自有EXE已验证；补布局文件URL额外输入、Rooms及混合DPI/hotplug/更广第三方验收 |
| WindowSwitcher | EnumWindows / DWM 过滤、标题/应用搜索与恢复；未实现 |
| Clipboard | AddClipboardFormatListener、SQLite、排除敏感来源、固定与粘贴；未实现 |
| TextInjection | 显式恢复目标、UIPI 检查、有限 SendInput；与剪贴板/片段统一；未实现 |
| CustomCommands | adhoc 与保存库均 Enter 直跑系统交互终端；保存库默认关、稳定 UUID、工作目录/Profile、Enabled、隐藏/收藏、JSON 导入导出已实现；最多3个位置参数已接入/安全字面值执行已验证；全局绑定已接入；Raycast 脚本导入及内嵌 ConPTY 仍待补 |
| Quicklinks | URL/路径/deeplink/参数，不默认执行 Shell；未实现 |
| Snippets | Markdown 模板、手动插入先行，自动展开单独许可；未实现 |
| Notes | 本地 Markdown、保存/搜索/编辑、浮窗；未实现 |
| Emoji | 搜索、常用/固定、复制/插入；未实现 |
| FileSearch | Windows Search + 显式目录的有界扫描，揭示/复制/回收站；未实现 |
| Backup | 自有 ZIP+manifest，分类导入、路径/容量校验、授权不导入；未实现 |
| AI | 用户选择的提供方、密钥、聊天、取消、历史；未实现 |
| QuickActions | 选中文本获取成功才执行，无法安全读取则提示；未实现 |
| MCP | 标准协议、显式授权、工具确认；不是 Raycast 宿主；未实现 |
| Calendar | 先选择可用源（ICS/CalDAV/用户授权服务），会议链接/提醒；未实现 |
| Camera | Media Foundation/公开摄像头接口、显式许可；未实现 |
| Dictionary | 明确本地数据或用户选择服务，不冒充 Windows 自带词典；未实现 |
| MenuSearch | UI Automation 暴露菜单的应用可行性验证，不保证任意应用；未实现 |
| Uninstall | 打开系统已安装应用/注册卸载入口；不自动扫残留或删用户文件；未实现 |
| Updates | 可信发布、签名/校验、渠道隔离、自启动可选；未实现 |
| AppleShortcuts | macOS 专属不做；Windows 自定义命令覆盖可移植工作流 |
| Extensions | 按原范围排除 Raycast runtime/市场，不为凑覆盖率引入 |
| Support | 上游品牌/捐赠功能不移植 |

Windows 缺少可靠公共 API 的系统开关不能只因 Raycast 列出来就硬做。亮暗主题/Night Light 等先提供系统设置入口；蓝牙/无线等需验证公开 Radio API 与权限；不写私有注册表、不模拟通用 F11、不绕过安全桌面。每项退出条件必须写明是“真动作”还是“打开设置”，不能混淆。

## 7. 调整后的交付顺序

1. **P1-A：启动器模型与空列表** — 稳定条目 ID/类型，Favorites + 最多五条 Suggestions + 不重复分组，统一显示/选择；学习曲线、查询词与源码测试对齐。
2. **P1-B：应用范围与发现** — 手动 EXE/目录、运行窗口补索引、持久化便携入口、观察范围开关与说明、Store/AUMID、图标和刷新。
3. **P1-C：系统动作最小闭环** — Open/Empty Recycle Bin，保留 Trash 搜索词、确认、统一动作绑定与成功/无操作反馈。
4. **P1-D：动作/键盘闭环** — Ctrl+K、别名、收藏槽、隐藏/恢复、揭示位置、分组分页与导航；补完整计算历史和可控设置重载。
5. **P2 完成基础出口** — 32 项候选、自定义尺寸及显示器循环已接入，继续真实多屏/混合 DPI 与第三方固定窗口回归；已打开窗口布局编辑/预览/捕获/保存/实际摆放已交付闭环，逐布局默认关闭缺失应用启动和窗口事件等待已交付设置/持久化/统一门禁/故障测试，注册F20自有复制EXE通过生产库存摆放；补文件URL额外布局输入和 Rooms。
6. **P3 日常工具** — 剪贴板 → Quicklinks/自定义命令 → Snippets/Notes → Emoji/文件搜索/高级计算 → 分类备份。
7. **P4 高级与发布** — OCR、AI/Quick Actions/MCP、日历/摄像头/可访问菜单等；可靠性不达标的条目留明确限制及替代入口。

每批完成时更新状态并跑真实键盘验收，不再把搜索器和窗口摆放原型说成接近完整 Tinycast。扩展运行时与 macOS 专属例外保持原约束；其余可可靠适配功能逐个交付，不能静默移出范围。
