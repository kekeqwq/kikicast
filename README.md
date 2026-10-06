# Kikicast

Windows 原生版 Tinycast，目标是尽量保持跨平台一致的操作逻辑和键盘手感，而不是编译 macOS 工程。

> 当前阶段：P1 持续完善 + P2 基础窗口管理。已有快捷键录制/设置、本地历史、应用搜索、基础计算、动作面板、参数命令库、现有条目全局绑定、自定义窗口尺寸和 32 个可独立绑定的窗口动作；不是完整功能版本。

## 产品目标

- 双击 Ctrl 等修饰键或组合快捷键呼出命令面板。
- 在同一入口搜索应用、执行命令、计算、管理窗口。
- 尽量实现 Tinycast 的其他可适配功能，不实现 Raycast 扩展运行时。
- 使用可靠的 Windows 公共接口；不为表面一致而依赖脆弱的私有系统接口。
- 本地优先，无遥测；网络请求只由明确启用的功能发起。

## 文档

- [功能范围与阶段](docs/product-scope.md)
- [交互规范](docs/interaction-spec.md)
- [技术架构与平台差异](docs/architecture.md)
- [验收基准](docs/acceptance.md)
- [Tinycast / Raycast Windows 源码对齐复核与剩余工作](docs/upstream-alignment-review.md)

## 技术方案

采用 C#、.NET 10 LTS、WPF 与 Win32 互操作；目标 Windows 11 x64 / ARM64。`global.json` 固定稳定 SDK 10.0.401。

## 下载与安装（0.2 预览版）

[GitHub 0.2.1-preview.1](https://github.com/kekeqwq/kikicast/releases/tag/v0.2.1-preview.1) 的目标资产为 **Setup.exe 安装程序，不是免安装 ZIP**：

- x86_64：`Kikicast-0.2.1-preview.1-windows-x86_64-setup.exe`
- aarch64 / ARM64：`Kikicast-0.2.1-preview.1-windows-aarch64-setup.exe`
- 补丁修复呼出后空查询首次 Enter 被 WPF 旧重复状态吞掉；不改焦点策略，仍防长按重复。旧 [0.2.0-preview.1](https://github.com/kekeqwq/kikicast/releases/tag/v0.2.0-preview.1) / [0.1.0-preview.1](https://github.com/kekeqwq/kikicast/releases/tag/v0.1.0-preview.1) 标签/安装包保持不变。

自带 .NET 10 runtime，默认当前用户安装，无需管理员权限；创建开始菜单和 Windows 应用卸载入口。默认不自启；启动后在托盘，双 Ctrl 呼出。先退出旧实例再升级/卸载；卸载保留家目录个人配置。PowerShell 7 仅 Shell/命令功能单独需要。预览版无可信签名，SHA-256 不是签名替代，不应关闭 Windows 安全保护。ARM64 原生与 x64-on-ARM64 仿真验证明确分开，不宣称已通过原生 x64 或完整人工验收。详见 [0.2.1 补丁说明](docs/releases/0.2.1-preview.1.md) / [0.2 功能说明](docs/releases/0.2.0-preview.1.md) / [安装包合同](docs/installer.md)。

## 0.2 预览版新增

Windows/.NET 独立扩展包、官方 GitHub Releases 目录、安装/更新/禁用/卸载、宿主声明式设置和 Extensions 命令/绑定已接入；第一款 RandomWallpaper 的源码在独立 [kikicast.extensions](https://github.com/kekeqwq/kikicast.extensions) 仓库。General 新增默认关闭的 Windows 登录自启，只启动托盘 Kikicast，不运行壁纸命令。详见 [扩展合同](docs/extensions.md) / [自启合同](docs/logon-startup.md)。[RandomWallpaper 0.1.0-preview.3](https://github.com/kekeqwq/kikicast.extensions/releases/tag/v0.1.0-preview.3) 单独安装，默认禁用且没有个人路径；用户已报告本机人工壁纸/删除测试成功。自动化不执行真实壁纸/回收；发布后的导入、原生 x64、真实重新登录、可信签名及稳定版验收仍待完成。

## 构建与运行

```powershell
dotnet build Kikicast.slnx -c Release
dotnet test Kikicast.slnx -c Release
dotnet run --project src/Kikicast.App
```

启动后仅驻留托盘，不显示主界面；默认只绑定双击 Ctrl 呼出/关闭，其余全局动作未绑定。↑↓ 选择，Enter 启动程序或复制算式结果，Esc 返回原窗口。

主界面呼出时只强制定位一次输入焦点，使用已安装的英文键盘并禁用输入法；关闭时恢复呼出前窗口及其输入法状态，不做中英文轮换（若系统没有英文布局会提示添加，不自动改全局输入配置）；中文条目支持全拼、首字母和字符模糊搜索（如微信：`weixin` / `wx`）。跟随系统亮暗主题和强调色，隐藏滚动条。主面板是分层透明窗口：不画外框线或方形底衬，外围只有柔和暗色阴影；面板透光度已降低，使用呼出前的背景快照做毛玻璃（非实时 Acrylic）；图片仅在内存保留，关闭立即释放，从不写磁盘。只模糊背景，文字保持清晰。高对比度或关闭 Windows 透明效果时改为实色。不使用 DWM 亚克力，因为实测会留下方形窗框且没有透出桌面。

- **Ctrl+, 或托盘 → 设置**：录制组合键/双击 Ctrl、Alt、Shift，指定左右侧，保存后立即生效；设置打开时全局热键暂停。
- **空搜索**：Favorites → Suggestions → 类型分组；建议最多 5 个条目，不在下方重复。标题不占选择位。设置可关闭建议，未实现功能不补占位建议。
- **Ctrl+Shift+F**：收藏/取消收藏选中应用或命令。空搜索中 Ctrl+1…9/0 直接运行前十个可见收藏；Ctrl+Alt+↑/↓ 调整收藏顺序；Ctrl+↑/↓ 分组导航，Ctrl+N/P 移动选择。关闭功能或入口缺失不会删除收藏。
- **Open/Empty Recycle Bin**：支持 `open trash [bin]` / `empty trash [bin]` 原查询。清空始终明确确认，默认 No，可 Esc 取消；空站/成功有托盘反馈。调用公开 Shell API，不直接删回收站目录，不提权；可在 Launcher 页录制独立全局绑定，清空仍走同一确认入口。
- **汇率**：`1 usd to cny`、`eur in usd`、`(2+3) usd -> cny` 等 ISO 货币转换；左右转换卡，Enter 复制金额。使用 Frankfurter 每日参考价并显示日期；按需更新、24 小时缓存，失败保留旧价并标明 stale。查询金额从不发给服务端。暂不支持所有货币别名、混合金额表达式和加密货币。
- **pwsh**：输入 `pwsh localsend-cli` 或选中当前查询的 Run Shell Command fallback，**Enter 立即运行，无确认、无二次 Run**。使用真实系统终端（Windows 的默认终端/Console Host），支持交互输入、方向键菜单、密码提示、ANSI/全屏 TUI 和 Ctrl+C；运行结束保留 pwsh 提示符。单独 `pwsh` 打开交互提示符。空的 Run Shell Command 入口/托盘打开编辑器，Enter 执行，Shift+Enter 换行，可设置 Run In 与 profile。默认加载 profile，方便使用自己的 PATH/函数；需要 PowerShell 7，不提权或绕过策略。终端独立运行，退出 Kikicast 不终止命令。临时 Shell 不记录脚本/输入/输出；pwsh/终端自身历史设置仍生效。内嵌终端、Raycast 脚本目录导入待实现。
- **面板内 Ctrl+H**：搜索并复制最近 200 个结果，只跳过连续重复；Ctrl+K 可删除单条，Esc 返回根搜索。
- **Ctrl+K Actions**：在当前窗口内打开可筛选动作面板；执行、收藏/排序、隐藏、复制、单条学习重置、历史删除和 Launcher 设置。Esc 先返回根搜索；Ctrl+Enter 用公开 Shell PIDL 揭示文件，Ctrl+Shift+H 隐藏可恢复条目，PageUp/Down 翻页。菜单和快捷键共用操作入口，不打开额外浮窗。
- **设置 → Launcher**：应用总开关、Actions 开关、按名称/ID/别名过滤条目、别名/可见性/收藏、全局学习重置、清计算历史。别名/可见性/收藏使用 Save item；**Global shortcut** 录制/清除逐应用、保存命令和现有内置动作绑定，使用 Save changes，换条目保留绑定草稿，关闭不自动保存。窗口条目编辑同一旧窗口绑定；隐藏不禁用热键，关闭功能/单条 Enabled 则阻止运行。带参数命令从热键进入参数栏，不自动执行或复用旧值；缺失条目绑定保留可恢复。Settings 入口不可隐藏，托盘也可修复。见 [条目绑定合同](docs/entry-bindings.md)。
- **设置 → Features**：计算器、货币转换、计算历史（显示和记录）、临时 Shell、Shell fallback、系统命令独立开关，Save changes 生效。关闭功能隐藏其入口并阻止执行，保留数据/收藏；关闭货币不发起新汇率请求（已经进行的请求可能完成）。关闭 Shell 会关闭未运行的编辑器，不终止已打开的终端。自定义命令库开关与临时 Shell 独立。
- **设置 → Commands**：独立 `commands.json` 保存 PowerShell 7 命令；默认关闭功能，需要启用并 Save changes。支持新增/编辑/删除、单条 Enabled、Run In（空=家目录，支持 `~`）、每条 Load PowerShell profile（默认关）、名称拼音/模糊搜索、稳定 UUID 收藏与排序。只搜索名称，不搜索脚本。Show in launcher 关闭隐藏普通入口，收藏仍可用。编辑命令立即保存，但选另一条会丢弃未保存编辑。Enter 直接在交互终端运行，不确认、不提权，不写运行日志。支持最多 3 个位置参数（重复名称按位置区分，必填/可选），选择命令时在面板显示字段；Enter 缺必填只聚焦字段，填完直接运行。脚本用 `$args[0]`…读取；值作为安全数据传递，不替换进脚本文本、不记历史，最终启动大小超限会拒绝。JSON 导入有警示并强制将导入项禁用（相同 ID 被替换，名称冲突整批拒绝）；导出含明文脚本，妥善保管。损坏库保留原文件并暂停编辑/导出。
- **设置 → Applications**：默认启用桌面 EXE 自动发现。Kikicast 运行时用窗口显示/前台事件登记独立 EXE，退出程序后仍可搜索；开始菜单已覆盖的目标 EXE 不登记第二版，保留 `.lnk` 参数。删除文件会从搜索移除（文件通知；异常/未覆盖目录最多约 30 秒或下次呼出复核）。支持 Scan running apps、Add EXE、Forget、Refresh Start menu；开关保存生效，入口管理立即生效。Forget 会抑制自动重新发现，Add EXE 可恢复。后台/纯 CLI/没有可见窗口/无法查询的进程不保证发现；手动 Add EXE 可补充。无需提权，不读窗口正文/标题、命令行或私有启动历史。数据独立保存为 `discovered-apps.json`，损坏时不覆盖。
- **设置 → Applications → Include Store / packaged applications**：当前用户 AppsFolder 名称 + 注册 AUMID，最多4096检查项/512新行；桌面宿主 EXE 不冒充应用，同包快捷方式优先保留参数和旧 ID。来源关闭同时阻断旧结果/收藏/全局热键，保留引用；应用图标开关也控制包内 manifest/PNG 的有界懒读取。包目录不用于扫描应用，只读明确资产，缺失/不支持图标降级通用符号。详情与真实激活待验收边界见 [包装应用合同](docs/packaged-applications.md)。
- **设置 → 启用窗口管理**：设置间距后，在面板搜索 `zuobanping`、`juzhong`、`restore`、`next display` 等并执行；操作呼出前的窗口。支持半屏/四分屏/三等分、尺寸、跨屏及单级恢复。
- **设置 → 全局配置 / 窗口管理配置**：General / Launcher / Features / Commands / Applications / Window management / Extensions 七个分类；呼出键及 32 个窗口动作逐行列出，点击对应按钮录制，支持清除。录制内容先进入草稿，保存后才注册/落盘；取消录制或换 Tab 不改动当前绑定，关闭时不自动保存。旧用户配置保留，可在全局页点击“恢复默认快捷键”再保存，仅保留双击 Ctrl；不再自动填入半屏预设。
- **设置 → Custom sizes**：独立 DIP/百分比宽高、九种锚点、DIP 偏移和 Enabled；Keep size draft 后 Save changes 原子保存，Launcher 条目可录制全局键，改名保留 UUID，隐藏/窗口列表显示关闭不禁用热键。Restore 共享首次位置/最大化状态恢复，详见 [自定义尺寸合同](docs/custom-window-sizes.md)。
- **设置 → Repeat half action**：None（默认）/ Sizes（½ → ⅓ → ⅔）/ Displays 互斥。显示器各贡献两个半屏槽，Left/Top 向后，Right/Bottom 向前，按起始屏计步并 wrap；单屏不翻边。切模式/命令、用户移动或屏幕配置变化重开循环，旧 CycleHalfSizes 配置可读，绑定 ID 不变。详见 [显示器循环合同](docs/window-display-cycle.md)。关闭窗口管理仍停用窗口热键。
- **设置 → Window layouts**：[窗口布局](docs/window-layouts.md)支持编辑/预览、捕获、复制、九锚点/比例/DIP偏移、唯一置前标记和实际摆放；Keep entry → Keep layout draft → Save changes，稳定UUID、搜索/收藏/隐藏/逐条Enabled/全局键共享门禁。每布局 Launch missing applications 默认关闭（旧配置也关闭），启用并保存后可启动缺失应用并通过限时窗口事件等候/实际摆放；保留原快捷方式参数、纯启动每个实际应用最多一次；现支持逐条本地文件/文件夹、URI/deeplink及JSON字面argv输入，输入条目只绑定新窗口、逐项事件等待、按app+input去重，主门/来源/当前定义失效即拒绝，失联显示器始终跳过。输入仅直接EXE或注册包公开文件/协议契约（包argv/文件夹、lnk额外输入明确拒绝，不换目标）；显式启用即信任应用自身参数行为，值存明文不存秘密。不会覆盖窗口命令 Restore。托盘可取消剩余工作，不杀已启动程序。
- 配置/历史保存到 `~/.config/kikicast/`（Windows 通常为 `%USERPROFILE%\.config\kikicast\`）；Debug 使用 `kikicast-dev/`。首次缺文件时从旧 LocalAppData 渠道复制，保留旧数据，不覆盖新目录文件。保存配置保留 `.bak`，损坏历史不自动覆盖。手动编辑目前需重启生效。

系统热键注册失败会提示；托盘双击或菜单可打开设置、修复绑定，但不会呼出主界面。普通按键不吞掉；Alt 双击仍可能触发 Windows 自己的菜单行为。

```powershell
# 可选桌面冒烟：独立实例和临时配置，5 秒后正常退出；测试期间请勿操作热键。
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/smoke-test.ps1 -TrayOnly
# 以下显式测试呼出面板/设置（不是正常启动行为）。
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/smoke-test.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/smoke-test.ps1 -Settings
# 验证绑定注册/暂停/恢复，以及自有窗口分屏、尺寸循环和恢复；不改用户窗口。
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/smoke-test.ps1 -WindowManagement
```

### 当前边界

- 搜索覆盖开始菜单 .lnk、手选本地范围、有界 Windows App Paths 注册入口、当前用户 Store/包装应用和已发现/手动添加的 EXE，保留快捷方式参数；加入基于频率/时间衰减的排序，但尚未对齐完整上游排名。已支持常用汉字拼音/首字母与全角、大小写归一化；不保证多音词、罕见字读音。本地 EXE/快捷方式图标已接入，可在 Launcher 关闭；[Store/包装应用](docs/packaged-applications.md)已接入 AppsFolder/AUMID、独立来源门禁、公开激活路由及有界本地 PNG 图标；真实安装应用激活和全部图标变体仍待验收。已加入收藏、建议和分组；学习每次 +100、十天半衰期，最近三个搜索词保留用于排序且 17 天后失效。旧计数只能近似迁移，不能重建每次访问时间；别名、隐藏/恢复和学习重置已接入；现有条目已接入字段分离比较器与 Low/Medium/High 强度设置；已修复 boost 比较的三条目循环并降低排序热路径分配，[生成数据基准与边界](docs/search-order-performance.md)不等于发布性能验收；更多来源和全语言转写仍待补。
- 面板、窗口、应用、保存命令及现有内置动作可配置独立热键；参数命令进入参数栏，禁用门禁共享；Win 双击及未实现模块绑定仍待补。
- 计算支持英文数字、四则、括号、幂、科学计数法和基础法币转换；百分比、单位、尾部表达式待实现。
- 自定义尺寸、显示器循环和已打开窗口布局编辑/预览/捕获/保存/实际摆放已接入；布局缺失应用及文件/URI/字面参数已按默认关闭策略接入；Rooms 尚未实现；跨屏 DPI、固定尺寸/管理员窗口仍需实机回归，不承诺通用全屏。
- 面板目前展开、主屏定位；同窗口 Ctrl+K 动作菜单已实现；多屏跟随、紧凑模式待补。不宣称像素级复刻 Apple 液态玻璃。
- 最新 0.2 Release 编译零警告，506 个测试通过（368 Core + 138 Windows）（含目录迁移、渠道隔离、不覆盖与失败回退）。上一版 ARM64 四种完整桌面冒烟覆盖托盘启动、英文面板、设置、绑定、窗口分屏/恢复、自有目标焦点返回，以及受控条目分组/收藏槽/建议开关和刷新后英文焦点、背景捕获/关闭释放、回收站原查询及只读 native 统计、货币卡、Shell 编辑器、启动参数，以及本机受控 pwsh UTF-8 输出/控制台句柄、自有独立 EXE 自动发现/文件删除通知、真实 `.lnk` 目标读取和去重、自定义命令库/收藏/隐藏/禁用、功能开关及损坏 schema 保留；本轮新增 10 项几何/移动已经自有窗口原生 placement/restore 子集验证，WPF models 子集验证动作/别名/隐藏/参数字段/六分类；字面参数注入测试运行自有 pwsh 通过。经用户授权的自有窗口点击/按键自动化已通过当前四种完整桌面冒烟，截图与焦点/布局恢复证据在 `artifacts/acceptance/`；未更改正常激活策略。合成输入不是物理双 Ctrl/真实 IME/TSF、主题和混合 DPI 的人工验收。
- 显示器循环：两屏/三屏完整槽位、实际边缘及模式/拓扑重置通过 Core 生成数据测试；历史单屏探针验证不翻边；本轮已接两块真实屏（3840×2160、2880×1920，均192 DPI），Ctrl+Alt+F22自有窗口完整验证四个半动作跨屏槽位和wrap，Ctrl+Alt+F21布局真实分布两屏及Restore。用户已拔外屏，本机读回2880×1920/192 DPI单屏，完整单屏窗口回归通过；不是混合DPI、实时失联Restore或重连验收。设置实际选择/Save/重载和写失败回滚已验证。
- 用户授权JeppView仅启动一次：启用探测后记录唯一EXE，名称搜索/退出保留/JSON重载通过，已结束本次新建Job且剩余JeppView进程0。额外摆放被拒绝的原始记录保留；按用户要求，不再测其可能特殊的初始化窗，也不作为通用窗口验收阻塞项。真实双屏/JeppView范围见 [原生验收记录](docs/native-connected-display-acceptance.md)。
- 发布门禁见 [发布检查](docs/release-checklist.md)：`scripts/package-release.ps1` 要求构建/测试/完整桌面冒烟、自包含双架构 apphost复测/受控开始菜单安装升级卸载、Setup.exe、清晰标注预览及 SHA-256；稳定包还要求所有必要项通过、干净源码及可信签名。当前稳定包被未完成项阻止，不伪造发版结果。
- 无独立图标的命令已接入 [彩色类别矢量图标](docs/launcher-category-icons.md)：扩展、窗口管理、布局、自定义命令等统一图标尺寸与标题/副标题对齐，原应用图标优先；不读取额外文件或执行扩展，支持明暗与高对比资源。
- 功能/设置配对的推进矩阵见 [功能设置矩阵](docs/feature-settings-matrix.md)。本地目录扫描范围（手选32项/默认一层、可选0–3层/有界检查/应用总开关）已实现；[Windows App Paths](docs/windows-app-paths.md) 已有独立保存设置、实际目标去重和旧行执行门禁；[现有条目全局绑定](docs/entry-bindings.md)已完成设置/门禁/回滚闭环，Store/AUMID 索引、来源开关、激活路由和本地 PNG 图标已接入；当前宿主只读验证19个注册包装入口，测试未安装包或启动用户 Store 应用，真实激活/更新/卸载仍待签收。P1 尚未完成，不能用测试数量代替 Tinycast 功能覆盖率。

## 上游与许可

参考项目：[abue-ammar/tinycast](https://github.com/abue-ammar/tinycast)。本次基线提交：`5d357d49c91157459e34a5332b6c1772ed110fab`。

上游采用 AGPL-3.0；后续复制或翻译源码时须保留作者、许可及来源声明，并按衍生作品义务提供相应源码。Rooms 部分还有 MIT 第三方通知。已加入 `LICENSE` 和 `NOTICE.md`，记录双击识别策略来源；Rooms 尚未引入。TinyPinyin.Net 拼音依赖及源算法的许可已保存在 `third-party/`，随构建复制到 `licenses/`；发布前仍需完整依赖许可审计。当前不宣称兼容上游二进制、备份文件或全部行为。
