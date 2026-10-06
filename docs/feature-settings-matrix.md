# Feature / Settings delivery matrix

本表是后续交付约束，不是完整实现报告。源码基线、移植边界和总体缺口见 [上游复核](upstream-alignment-review.md)。

每项交付必须同时有：真实功能闭环、设置/条目管理、所有入口统一门禁、版本化持久化、失败保留数据、自动测试与人工验收边界。未实现功能不提供无效占位开关；默认只双击 Ctrl，普通设置不暗中覆盖用户绑定。禁用保留数据和绑定，隐私敏感采集默认关闭；操作或导入不能由加载配置自动触发。

## 当前可操作设置

| 功能 | 设置 | 实现边界 |
| --- | --- | --- |
| 呼出、建议 | General：录制/清除/重置呼出键、Suggestions | 主题跟随系统；外观偏好仍待补；现有条目统一绑定已接入 Launcher |
| 登录自启（0.2） | General：Start Kikicast when I sign in to Windows，默认关闭 | 当前用户 Run、保存/读回/失败回滚、渠道隔离；不运行扩展；实际登录与新卸载代码实机验收待做，见 [合同](logon-startup.md) |
| Windows/.NET 扩展（0.2） | Extensions：总门/显示门、官方目录、安装/更新/导入/启停/卸载、各插件宿主渲染设置；Launcher：绑定/收藏/隐藏 | 独立进程/版本架构协议校验/原子存储/保存不执行；SDK1 声明式 boolean/UUID 文件夹集合；非沙箱，无可信签名。RandomWallpaper 真实设置/锁屏/回收由用户手测，见 [合同](extensions.md) |
| 应用发现/范围 | Applications：发现开关、Scan、Add EXE、Forget、Refresh index、Include Windows App Paths、Include Store / packaged applications、Add/Remove folder、Child-folder depth；Launcher：应用总开关 | 本地手选目录 + 默认一层，可选0–3层；最多32范围/20000检查项；拒绝网络/盘根/重解析点；快捷方式实际目标去重并保留参数；总开关停止索引/观察/搜索；App Paths 来源独立门禁/只读1024键/512新行/实际目标去重，Store/AUMID 当前用户只读来源/独立门禁/公开激活路由已接入，真实安装应用激活验收待做 |
| 计算器 | Features：Calculator | 基础安全表达式；高级量纲/日期待补 |
| 货币 | Features：Currency conversion | 依赖 Calculator；ISO 法币/公开日价/缓存；不是 crypto/交易报价 |
| 计算历史 | Features：Calculation history；Launcher：清除；Actions：单条删除 | 200 条、只跳过连续重复；关闭停止记录并隐藏，保留既有数据 |
| 临时 Shell | Features：Run Shell Command、query fallback | Enter 无确认、真实交互终端；关闭会关闭未运行编辑器，绝不杀已运行终端 |
| 自定义命令 | Commands：库开关、launcher visibility、CRUD、Enabled、Run In、Profile、JSON import/export | 库默认关；profile 默认关；收藏仍可运行隐藏条目，禁用条目不可运行；仅名称检索；最多 3 个位置参数/必填/可选已接入，运行值不持久化；全局绑定共享执行门禁，带参数时仅打开参数栏 |
| 系统命令 | Features：System commands | 目前只有 Open / Empty Recycle Bin；可录制条目全局绑定，Empty 所有入口保留独立确认 |
| 窗口管理 | Window management：开关、launcher display、32 项逐条绑定、gap、Repeat half action (None/Sizes/Displays)、Custom sizes CRUD/Enabled、Window layouts 编辑/预览/捕获/复制/Enabled/显示/Launch missing applications 默认关闭；Launcher：尺寸及布局条目绑定 | 32 项动作、自定义尺寸及半屏显示器槽位循环已接入；旧尺寸循环可读/原绑定ID保留；已打开窗口布局的编辑/预览/捕获/保存/实际摆放/取消已接入；缺失应用启动默认关闭的逐布局选项已接入共享安全启动/限时事件等待/取消不杀程序；已支持保存的文件/URI/字面 argv 输入及新窗口顺序等待，Rooms 待补，同DPI双屏自有窗口通过，混合DPI/hotplug验收待做 |
| 收藏/排序/别名/隐藏 | Launcher：条目管理、别名、可见性、收藏、学习重置、Low/Medium/High 匹配强度；Ctrl+K：共享动作 | 现有类型接入字段分离/UTF-16 动态对齐/别名及学习比较器；名称/关键词不混成一个串，保存命令不索引脚本；现有条目 Global shortcut 录制/清除、Save changes、缺失恢复、关闭门禁已接入；更多来源/全语言罗马化仍待补 |

条目全局绑定草稿（最多256个 EntryBindings）使用 Save changes；窗口条目复用原 WindowBindings，默认无新增绑定；条目隐藏不禁用，功能关闭阻断执行但保留绑定。详情见 [条目绑定合同](entry-bindings.md)。自定义尺寸先 Keep size draft 再 Save changes，与其快捷键原子保存；显式删除并保存清理该尺寸引用。Feature switches 与 Applications 目录范围草稿使用 Save changes；Commands CRUD/import 和 Applications Add EXE/Forget 立即生效。Commands 明文脚本只保存到库/用户主动导出文件，不进搜索词或运行日志。Kikicast JSON 导入不等于支持 macOS zsh/Raycast 脚本目录。

## 待实现配对计划

| 模块 | 对应设置/管理验收目标 | 状态 |
| --- | --- | --- |
| 应用图标 | Launcher：Show application icons；默认开启，可独立持久化 | 已接入：只对可见绑定请求资源；单 worker/队列256/cache256，公开 SHDefExtractIconW、本地包 manifest/PNG 固定资产路径、64px 冻结图像/失败通用图标及快捷方式目标回退；关闭阻止新读取，无磁盘缓存。更多包资产变体/混合 DPI 人工验收待做 |
| Launcher 完整行为 | Windows App Paths 注册入口及其独立设置已接入；Ctrl+K/别名/隐藏/本地目录范围/学习重置已接入；现有字段比较器/匹配强度已接入；更多来源字段、全语言罗马化待补；Store/AUMID 来源/设置/激活路由/有界 PNG 图标已接入，真实包激活与更多资产变体验收待做；现有类型统一绑定已接入；范围深度已可配置0–3层；本地 EXE/快捷方式图标已接入 | 待补 |
| 自定义命令进阶 | 最多 3 个位置参数/必填/可选及全局绑定已接入；可信脚本目录导入、输出策略 | 后两项待补；不牺牲已确认的真实交互终端行为 |
| 窗口进阶 | [自定义尺寸](custom-window-sizes.md)、[显示器循环](window-display-cycle.md)、[已打开窗口布局](window-layouts.md)及统一条目绑定已接入；默认关闭的缺失应用与文件/文件夹、URI、字面argv输入/顺序新窗口事件等待已接入直接EXE或包公开契约（lnk额外输入、包argv/文件夹明确拒绝）；Rooms、逐应用规则 | 后几项待补；历史单屏不翻边及当前真实同DPI双屏四动作tour/布局/Restore通过；Core生成多屏不代替混合DPI/hotplug/第三方窗口验收 |
| Clipboard | 启用采集、容量/保留期、格式、敏感来源排除、固定、清除、粘贴绑定 | 未实现；默认关闭 |
| Quicklinks | 功能/可见性、URL/路径编辑、参数、单项启停、默认浏览器、导入导出、绑定 | 未实现 |
| Snippets | 功能/可见性、内容管理、参数、触发/粘贴策略、绑定 | 未实现 |
| Notes | 功能、保存位置、管理/导入导出、绑定 | 未实现 |
| Emoji | 功能/可见性、语言/肤色、收藏/最近、复制/粘贴、绑定 | 未实现 |
| 文件搜索 | 启用、允许目录/排除项、数量/索引策略、打开/揭示、绑定 | 未实现；禁止默认整盘扫描 |
| 高级计算 | 精度/格式、单位/日期/时区、币种/缓存、历史管理 | 待补；公开数据源与失败降级明确 |
| 其余系统动作 | 每类启用/绑定、破坏性确认策略、音量/媒体/电源/锁屏等公共 API | 待补；真实动作与仅打开系统设置区分 |
| OCR / Quick Actions | 启用、语言/区域/权限、失败提示、绑定、敏感内容策略 | 未实现 |
| AI / MCP | 服务/模型/密钥、启停、发送边界、超时/取消、工具权限 | 未实现；不默认发送桌面/剪贴板 |
| 日历/摄像头/菜单 | 公共 API/权限、来源管理、功能/绑定、明确不可用降级 | 未实现；不模拟私有 macOS API |
| 备份/设置基础设施 | 分类导入导出、可执行内容警示、引导、搜索、实时文件重载、外观、自启动、更新 | 登录自启在0.2接入，实机登录/卸载验收待做；其余待补 |

不能因测试通过就将某模块标为完整。IME、物理快捷键、DPI、外观、CLI 输入/TUI 和第三方程序仍需实机验收。可靠 Windows API 不存在的 macOS 专属项明确不移植，不算作已经实现。
