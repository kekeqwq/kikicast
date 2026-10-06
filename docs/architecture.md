# Windows 技术架构

## 技术决策

- C# / .NET 10 LTS / WPF；Windows 11，x64 + ARM64。
- 通过公共 Win32、Shell、DWM、UI Automation 接口完成系统集成。
- 不使用 Electron，不引入浏览器作为主界面；不引入 Raycast JS 运行时。
- `global.json` 已固定稳定 SDK 10.0.401；三个应用/库工程与两个测试工程已建立。本文其余合同为目标架构，当前原型尚未完整实现。
- 不承诺照搬上游“低于 100 MB”的宣传指标；在发布构建中建立实际内存/延迟基线。

## 项目分层（拟建）

```text
src/
  Kikicast.Core/       纯模型、搜索、计算、模板、布局、动作策略
  Kikicast.Windows/    Win32/Shell/DWM、热键、剪贴板、进程、存储适配
  Kikicast.App/        WPF、状态、协调器、窗口、设置、组合根
 tests/
  Kikicast.Core.Tests/ 算法和虚拟时间测试
  Kikicast.Windows.Tests/ 受控集成测试，默认不改用户桌面/剪贴板
```

Core 不引用 WPF，不取得当前时间/桌面/网络/用户目录；依赖均由输入传入。矩形、显示器、窗口身份使用自己的数据类型，不让 HWND 和 UI 控件泄漏进算法层。

所有长期服务由应用组合根创建/释放；不在各功能里增加静态单例。每个功能由 coordinator 统一执行；UI、热键及其他入口都通过同一动作分派。确认和功能开关不能只放在按钮上。根搜索比较在完整匹配 pass 先判定 boost 撤销，避免逐对例外的排序循环；值型匹配事实/学习命中、无临时LINQ数组和候选内懒折叠降低分配。[生成数据基准](search-order-performance.md)只依赖Core，不采集用户应用/脚本；端到端性能预算仍未验收。

## 核心合同

- `ActionId`：稳定、非显示名称；区分内置命令与用户 UUID。
- `LauncherEntry`：身份、类型、搜索字段、图标引用、动作引用。
- `PaletteRow`：唯一显示/选择来源；卡片、条目、fallback 独立 ID。
- `HotKeyBinding`：组合键 / 双击修饰键；记录侧别及规范化键身份。
- `DoubleTapRecognizer`：事件+单调时间输入，只输出动作候选。
- `WindowSnapshot` / `DisplaySnapshot`：一次操作的几何、工作区、DPI及身份。
- `WindowPlacement`：纯算法输出；系统层检查能力后执行并回读实际结果。
- `FeaturePolicy`：主开关、搜索可见性、逐条启用、绑定状态分开。
- `ExecutionContext`：呼出前目标、参数及一次性模板上下文；不是永远有效的 HWND。

## 热键与生命周期

组合热键使用 `RegisterHotKey`/`WM_HOTKEY`，长按按动作策略防重复。双击使用专门消息循环线程的 `WH_KEYBOARD_LL`；有绑定时才安装鼠标取消监听。

钩子内只做轻量状态处理，不读磁盘/网络、不等待 UI、不运行窗口操作。尽快调用后续钩子并向 UI 队列派发。处理侧别、自动重复、注入标记、会话锁定/解锁和电源状态；退出完整卸载。

录制与运行共用识别器，但不共用“动作已执行”状态。内部冲突与 OS 注册失败分别展示。键码迁移不能直接读取 Carbon 数字值当作 Windows VK。

`EntryBindings`（最多256项）持久化现有应用/保存命令/内置条目稳定 ID；窗口绑定继续使用原枚举键。`ActionBinding`携带 EntryId；原面板/窗口原生注册 ID 不变，条目临时注册槽在公开范围内轮换，双击队列携带配置 generation，组合消息重验键/修饰符。UI 再验当前绑定/功能，并从最新目录/库解析到与 Enter/Actions 共用的 `ExecuteRowAsync`；带参数绑定仅打开同 HWND 参数栏并清旧值，破坏性回收站绑定不绕过确认。绑定草稿由全局 Save 注册/持久化；异常写入恢复旧注册且不发布，缺失/禁用保留引用。见 [绑定合同与证据](entry-bindings.md)。

## 面板及焦点

WPF 无边框窗口，不在任务栏显示；正常启动不显示面板，托盘仅提供退出和设置入口。保存/验证原目标并按操作决定是否恢复。前台激活遵守系统限制，不用无限 `AttachThreadInput` 或重复抢焦点绕过规则。

UI 线程只负责交互。应用索引、图标、文件搜索、HTTP、OCR、脚本输出等有界后台运行；过期搜索结果不可覆盖最新查询。根面板采用 `InputMethod.IsInputMethodEnabled=false`、`EnglishInputSession`（GetKeyboardLayoutList 选择已安装英文布局，ActivateKeyboardLayout 仅切换本线程，ImmAssociateContext 禁用面板上下文），只作用自身 UI 线程和面板 HWND；失焦/关闭恢复此前布局。请求切换语言时拒绝或重新确认英文，保留 composition 防误执行守卫。设置等普通编辑器不强制英文。`WindowActivation.Once` 只在呼出时短时连接输入线程并立即断开；`SourceInputState` 在激活前捕获目标，关闭且目标校验通过后按值恢复。不调用 Windows 8+ 会影响全系统的 LoadKeyboardLayout；没有已安装英文布局时保持输入法禁用并明确提示用户添加英文键盘。

主面板使用 `AllowsTransparency` 分层窗口和圆角半透明填充，不画硬边框；圆角外围仅有独立柔和暗色阴影，没有方形底衬。不再使用 WindowChrome/DWM 背景：实测它们会留下方形窗框，且亚克力没有透出桌面。设置窗口仍用公开 DWM 暗色标题栏属性。新版降低透光度（深色填充 alpha=190，浅色=208），用公开 GDI 在隐藏面板的内框范围捕获一次背景，冻结为内存 BitmapSource；BlurEffect 只用于背景 Image。圆角几何裁剪背景与内容，关闭清空 Image.Source。没有轮询/磁盘截图/内容日志，因此是静态毛玻璃而非实时 Acrylic；受保护/不可捕获桌面使用纯色调透明降级。高对比度或 Windows 禁用透明时实色，不捕获背景。

## 应用索引

扫描用户及公共开始菜单 `.lnk`，用公开 ShellLink 读取目标去重但仍保留 shell 启动入口/参数。事件驱动记录可见桌面 EXE，手动添加也支持；`RunningApplicationDiscovery` 的 callback 不做 IO，单一后台队列/有界生命周期缓存处理查询、目录 watcher 删除与批量 JSON 保存。没有进程轮询/整盘扫描/窗口标题/命令行采集，默认启用并可在 Applications 设置关闭。当前 WinEvent 的已打开进程通过公开 GetPackageFullName 判别，包装身份不作为新自动发现的版本 EXE 登记，避免与 AUMID 重复；不扫描包目录或删旧保存引用。开始菜单目标抑制发现入口，后续索引覆盖亦去掉旧发现副本；删除通知及 30 秒有界文件复核移除无效路径。手选本地目录范围已接入：最多32项、默认下探一层、可选0–3层、20000检查项、不跨重解析点/隐藏/系统项、不扫描网络或盘根；索引刷新合并并发请求，应用总开关停止索引/发现。LocalPathSafety 从本地盘根逐级检查，先拒绝链接祖先再触及子项，128路径段上限；共享于范围/目标规范化、发现/复核/watcher与图标，不以 File.Exists 先穿透网络或 junction。并发特权文件系统修改不在该隐私边界的安全保证内。目录 EXE 与快捷方式实际目标去重，原 `.lnk` 参数入口保留。桌面名称仍来自入口文件名；Store/AUMID 使用公共 AppsFolder 名称和经当前用户包注册验证的独立身份。RegisteredApplicationIndex 只读本机 HKCU/HKLM App Paths 双视图，最多4根/1024检查键/512新行/4096字符路径，未覆盖目标保留最多16注册关键词；有界原生枚举/读取使用 SafeRegistryHandle，不读 PATH 覆盖。实际目标去重优先原快捷方式；独立 IncludeWindowsAppPaths 与应用总门同时控制扫描、查询和旧行执行/Reveal，关闭保留引用。详情见 [App Paths 合同](windows-app-paths.md)。`PackagedApplicationIndex` 通过公共 AppsFolder/IShellItem2/PKEY_AppUserModel_ID、有界4096 Shell项/512新行读取包装应用，用 FindPackagesByPackageFamily/OpenPackageInfo/GetPackageApplicationIds 验证当前用户注册；不读未文档化 Shell package 属性、包注册表或目录树。Package ID 稳定，不以版本目录/通用宿主 EXE 为身份；`.lnk` 的公开 AUMID 元数据去重，旧 ID/参数保留。独立 IncludePackagedApplications gate 在查询/热键/旧行执行再次检查；激活用 IApplicationActivationManager，包行只提供 Copy application ID、不提供虚假文件 Reveal。详情与未验收边界见 [包装应用合同](packaged-applications.md)。本地应用资源图标已接入：ShellLink 同次读取 IconLocation/Index（不 Resolve），可见绑定懒请求、单后台 worker、队列/cache 各256，公开 SHDefExtractIconW 读取64px后冻结内存 BitmapSource，SafeHandle 释放 HICON。自定义图标失败回退实际 EXE/通用符号；拒绝网络/重解析路径链/超过256MiB的资源，只读 exe/dll/ico，大小16–128和索引±65535有界。应用总门与 Show application icons 门关闭后不再开始资源读取；既有读取可完成，无 Shell 扩展调用或磁盘缓存。包装应用图标复用同一缓存/worker与可见绑定门，仅读明确本地 AppxManifest.xml（1MiB、禁DTD/32深/16384节点）和固定7种PNG资产变体（8MiB/4096像素上限），WPF加载后64px冻结；不枚举包目录或调用Shell图标factory，不支持变体降级通用符号。

身份候选为 AUMID、规范化 shortcut 身份和 EXE 路径；同一 EXE 的不同参数快捷方式不能错误合并。索引更新和学习数据独立；索引暂时缺失不立即删除收藏。当前 `SearchProfile` 在索引时缓存 TinyPinyin.Net 1.0.2 的全拼/首字母字段，采用 NFKC、大小写和空白归一化；查询阶段不重复转写。窗口命令复用同一算法，MIT/源算法 Apache-2.0 许可保存在 third-party 并随构建复制。完整 ICU/多音词消歧未实现。

## 功能门禁与命令库

Settings 的六分类覆盖现有功能；[功能设置矩阵](feature-settings-matrix.md) 约束后续功能/设置成对交付，不提供空开关。Features 开关控制候选及执行入口；历史关闭同时停止记录，关闭币种转换不发起新网络请求。关闭 adhoc Shell 关闭未运行编辑器/禁用托盘入口，不影响独立命令库或终端进程。

`SavedCommand` 是稳定 UUID 值记录，名称查询配置通过 identity-keyed weak cache 缓存，改名不会复用旧 profile；脚本不进检索。`SavedCommandStore` 串行事务验证/原子存盘后才发布，不采集输出。`Visible` 与 `Runnable` 分离：launcher visibility 可隐藏普通条目，但收藏/全局绑定仍能触达；feature 或单项关闭拒绝运行。命令库默认关闭，保存命令 profile 默认关闭，工作目录到执行时验证（缺失/文件路径拒绝，不偷偷落回 home）。Windows 路由到 `PowerShellRunner.LaunchInteractive`，无确认/无 EOF，终端独立存活。导入只接受 Kikicast JSON，用户警示后按 UUID 合并并强制禁用导入项，冲突批量拒绝，不执行脚本；文件上限 64 MiB、1000 命令、每条 8192 字符。

Launcher 页管理稳定 ID 的别名/隐藏/收藏，含缺失引用；条目修改串行、设置草稿 Save 不覆盖同期条目管理。Ctrl+K 使用同一 HWND 内的 Actions pane，内部失焦不关闭，外部失焦仍关闭；菜单冻结条目和能力，激活时重验当前条目，菜单与键位共用操作入口。文件揭示使用公开 Shell PIDL，不拼 Explorer 命令行。计算历史 200 条且只跳过连续重复，编辑写失败在没有更新的前提下回滚内存。

最多三个命令参数在同一面板显示，按位置而非名称键控；必填缺失拒绝并聚焦字段。值以转义单引号数据数组传给独立解码的可信 ScriptBlock（`$args`），不文本替换，最终命令也执行大小限制。值仅存当前 UI，切换条目/隐藏清除，不写脚本库/运行历史。受控 pwsh 探针测试包括注入字符和空可选槽。

## 窗口适配

- 进程声明 Per-Monitor V2 DPI awareness。
- 全局物理像素用于跨屏矩形；gap、最小尺寸、合理尺寸上限等 UI 长度明确按目标屏 DPI 换算。
- `GetWindowRect` 与 DWM 可见框语义不同；建立单一边框适配入口，不重复补偿。
- `EnumWindows` 过滤桌面、工具浮层、cloaked 窗口、自己瞬态窗口；最小化窗口按功能决定是否纳入。
- `MonitorFromWindow`/`GetMonitorInfo` 提供工作区；一次操作快照，不混用热插拔前后几何。
- `SetWindowPos` 调整，`GetWindowPlacement`/`ShowWindow` 处理最大化/最小化；回读实际矩形以学习限制并记录 Restore。
- 使用有界调用/`SendMessageTimeout`，不能因第三方窗口卡住 UI。
- 显示器持久身份从 `QueryDisplayConfig` 等公共信息构造并验证；不能持久化 HMONITOR 当永久 ID，也不能声称设备标识跨硬件变化绝对稳定。
- 窗口恢复记录含 PID、进程创建时间、窗口特征、原 placement 和本程序实际修改；每次恢复重验。HWND/PID 复用或身份不确定时跳过。

自定义尺寸已接入：schema-1 `CustomWindowSizes` 最大128，稳定 `window-size:` UUID 和 EntryBindings；DIP/百分比/九锚点/偏移共享核心几何。所有窗口动作使用同一序列化 WindowManager/有界回读/64条 Restore 记录；自定义尺寸不携带 tile/cycle，按当前定义执行、固定及最小尺寸再次锚定。设置定义/快捷键草稿原子 Save，列表显示/逐条 Enabled/主门分离；显式删除才清引用，合并保存不覆盖即时别名/收藏编辑。详情和验收边界见 [自定义尺寸](custom-window-sizes.md)。

已打开窗口布局已接入：schema-1定义/绑定同存settings，64布局/32条每布局/512总条；用户比例/锚点/DIP偏移与机器窗口cookie/运行计划分开。LayoutDisplays只读公共QueryDisplayConfig/设备名，与当前GDI屏边界精确联接，持久身份为monitorDevicePath，不是HMONITOR或虚构硬件UUID。WindowLayoutGeometry的Resolve/Describe供预览/捕获/计划共用；手势级有界EnumWindows读取元数据，不采集标题/正文/argv或轮询。执行重解当前UUID/来源，串行WindowManager摆放逐窗口重新校验拓扑与身份，布局不改tile/cycle/Restore账本；Restore先按身份取原记录，下一非Restore动作仍因漂移刷新基点。窗口/应用主门、来源门、Enabled与搜索显示分离；默认No设置Apply关闭设置后走同一执行门、托盘取消剩余项。逐布局LaunchMissingApplications默认关闭/旧schema读取不重写，已保存的显式启用通过共享ApplicationLauncher保留原lnk路径/参数并再次验证实际EXE/AUMID来源；手势级LayoutWindowEvents三种公共WinEvents/单slot队列/200ms按事件合并，WindowLayoutOpening限时10秒/64次快照、不做常驻或进程轮询。新窗口只绑定初始snapshot外的实际app键与首次cookie；取消不杀应用、无后台延迟执行。自有复制EXE通过生产库存/注册F20真实启动摆放；WindowLayoutInput定义本地文件/文件夹、URI与JSON字面argv字段/深拷贝/语法预算，输入条目不认领旧窗，按app+input精确去重、顺序事件等待且下一提交前另读snapshot排除已观察兄弟窗；EXE ArgumentList直接传值，包用公共单item Shell数组/ActivateForFile/Protocol，不猜默认handler、不追加lnk参数、包argv/文件夹明确拒绝。输入明文可信配置，不索引/学习/诊断其值、不是应用行为沙盒。更广第三方/实包激活与更新/混合DPI/hotplug验收待补，详见 [布局合同](window-layouts.md)。

半屏显示器循环已接入：可空HalfCycleMode优先于旧CycleHalfSizes bool，三模式互斥；窗口内存额外记录起始/落屏与不可变有序工作区/DPI/monitor bounds快照（最多64屏、128步）。真实Bounds排序避免任务栏偏移改变顺序；WindowGeometry.Resolve携带明确目标屏/实际Tile边缘，最小尺寸重锚和后续Next Display不以请求框中心猜屏。模式/命令/观测漂移/拓扑变化重开链，Restore仍保留适当基点，状态不落盘；原生枚举超限/失败拒绝部分快照。原单屏探针保留；用户接入两块实际192 DPI显示器后，注册四半动作完整槽位/wrap及双屏布局/Restore通过自有窗口原生测试，不代替混合DPI/hotplug验收，详见 [显示器循环](window-display-cycle.md)及[原生接屏/JeppView验收范围](native-connected-display-acceptance.md)。

管理员窗口、UAC 安全桌面、游戏等不保证可操作；报告边界，不默认以管理员身份常驻。最大化与填满工作区分开记录，不把它们当原生全屏。

## 0.2 Windows/.NET 扩展与登录自启

见 [扩展协议/存储/门禁/人工边界](extensions.md) 与 [默认关闭登录自启/回滚/卸载归属](logon-startup.md)。扩展逻辑在独立仓库与独立自包含进程；主进程处理静态包元数据、声明式表单、包事务及当前定义执行。不是 Raycast 宿主或安全沙箱；加载/设置/安装/更新/登录不执行壁纸命令，真实设置/锁屏/源图回收只留用户手测。

## 存储与导入

当前配置与文本历史放 `~/.config/kikicast/`，Debug 使用 `kikicast-dev/`；原 LocalAppData 数据按缺文件迁移，保留原件、不覆盖新文件。当前为版本化 `settings.json`、`history.json`、`discovered-apps.json`、`commands.json` 原子写入，渠道隔离；发现表损坏时禁写而非覆盖。后续学习/便携入口/计算历史拆分 JSON，剪贴板计划用 SQLite；笔记/片段计划用可读 Markdown；缓存留 LocalAppData 独立目录，不能清掉用户学习。完整存储对照见 [上游复核](upstream-alignment-review.md)。

备份拟采用 ZIP + manifest + 分类 payload；这不是上游 AppleArchive 的兼容实现。需版本校验、大小/数量限制、路径穿越/链接拒绝、事务化应用和逐类预览。文件本体引用路径与包内 blob 路径区分；不得把本机缓存路径当可移植 blob 路径。

授权开关、凭据、运行恢复账本不导入。命令/外部路径可作为用户内容导入，但必须预览并显式确认，不自动执行。用户 authored 数据损坏时保留原文件/隔离并报告，不照搬剪贴板数据库的删除重建策略。

API 密钥用 Windows 凭据保护机制，默认不进入备份、日志或错误报告。

## 剪贴板、文本与命令

`AddClipboardFormatListener` 事件采集，STA线程读取格式；文件/图片/文本顺序和内部标记统一。剪贴板竞争有限重试。保留期只删除本应用拥有的图片，不删除引用文件；敏感标记及排除应用过滤尽力而为，不能宣称能识别所有密码。

粘贴用受控 `SendInput`，尊重 UIPI；不读取密码框、不监控文本日志。选中文本优先 UI Automation，失败按用户配置提示/替代，不能默认对所有应用发送 Ctrl+C 干扰剪贴板。

进程执行采用独立参数列表及明确 shell 后端；输出限制内存并增量显示。进程树归属用 Job Object 等机制确认；不为结束命令误杀无关进程。ConPTY、Stop 与“关闭窗口但命令继续”需联合验证，不能默认 kill-on-close 破坏合同。

## 测试及许可

先迁移上游纯测试作为行为样本，再做 Windows 集成测试；迁移测试也需保留来源和许可。核心用虚拟时间、固定地区/时区、模拟窗口/显示器进行确定性测试。

关键参考：`DoubleTapDetector`、`LauncherMatch/Order`、`CalcEngine/ExpressionParser`、`WindowPlacementEngine/ActionMemory`、`WindowLayoutGeometry/Plan`、`RoomPlan`、`SnippetTemplateEngine`。

编译通过不等于桌面行为通过。双击、焦点、IME、多 DPI、管理员窗口、剪贴板竞争、崩溃恢复必须实机验证。正式复制/翻译前加入 AGPL-3.0 和 Rooms MIT 通知，记录每个源文件的上游路径与基线提交。
