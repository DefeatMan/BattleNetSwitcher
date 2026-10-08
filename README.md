# BattleNetSwitcher

战网账号快速切换 + 应用一键断网/静音，Windows 桌面小工具。

![platform](https://img.shields.io/badge/平台-Windows%2010%2F11-blue)
![dotnet](https://img.shields.io/badge/.NET-8.0-512BD4)
![license](https://img.shields.io/badge/协议-MIT-green)

## 功能

### 账号切换（GUI / CLI 都支持）

- 按**区服**分组管理账号（国服 / 美服 / 欧服 / 亚服 / 台服）
- 同一个邮箱可以在多个区服各有一条记录，**快照也按区服分别保存，互不覆盖**
- 切换时自动关闭战网 → 修改配置 → 带 `--setregion=XX` 重启战网
- 修改前自动备份 `Battle.net.config` 到 `.backup`
- 本地维护"邮箱 ↔ 区服"映射（`%APPDATA%\BattleNetSwitcher\accounts.json`），不污染战网配置
- 支持**便携版 / 绿色版战网**：手动指定战网程序与配置文件路径
- **关闭战网时让它正常退出**（发退出请求，超时才强杀），
  给它机会把状态与刷新后的令牌好好写盘，而不是被硬杀掉

### 本地状态快照（v1.1.0 新增）

**目的**：减少切换账号时弹出的浏览器验证码。**在登录还新鲜时，把战网客户端的本地状态存一份**，
之后即使客户端清理了旧凭证，也能写回去让客户端直接向服务端续期。

- **一键保存**：战网客户端当前登录某账号并完成验证后，在工具里点
  **“更新当前快照”**，工具会关闭战网、把关键文件 + `UnifiedAuth` 注册表子树复制到本地缓存
- **自动更新**：每次切换账号时（A→B），工具在关闭战网后**自动为 A 更新快照**
  （因为刚下线的瞬间，A 的凭证是最新的），再恢复 B 的快照，然后启动
- **按 (邮箱, 区服) 隔离**：同一邮箱在国服和美服各有独立快照，
  从 A 国服切到 A 美服，会走完整的"更新 A 国服 + 恢复 A 美服"链路，不会串
- **递归备份注册表**：`UnifiedAuth\<8位十六进制ID>` 下所有值类型
  （`REG_SZ` / `REG_DWORD` / `REG_QWORD` / `REG_MULTI_SZ` / `REG_BINARY`）都能原样导出与恢复
- **保护关键键**：整个流程**从不触碰** `EncryptionKey` 和 `CacheDatabase`，
  破坏它们会导致客户端完全无法登录

### 一键拔线（仅 GUI，可关闭）

- 选定任意 exe，点一下立即断网，可同时静音
- 默认 3 秒后自动恢复，时长可调（1 ~ 3600 秒）
- 基于 Windows 防火墙出站规则，无需额外驱动
- **可禁用**：不需要该功能时关闭后，GUI 启动不再请求管理员权限

## 下载

| 文件 | 说明 |
|---|---|
| `BattleNetSwitcher.exe` | 图形界面，自带 .NET 运行时，双击即用 |
| `BattleNetSwitcher.Cli.exe` | 命令行工具，自带 .NET 运行时 |

启用"一键拔线"时需要**管理员权限**（修改防火墙规则必需）；禁用后不需要。

## 在线体验

不想先下载？官网提供了一个纯前端的模拟器，打开浏览器就能玩：

| 页面 | 内容 |
|---|---|
| **[官网首页](https://defeatman.github.io/BattleNetSwitcher/)** | 功能说明、下载 |
| **[UI 模拟](https://defeatman.github.io/BattleNetSwitcher/gui.html)** | 左侧模拟战网客户端的登录页与登录成功页，右侧还原真实 WinForms 主窗口（切换区服 / 切换账号 / 添加账号 / 更新当前快照 / 一键拔线 / 设置 / UAC 启动流程），两边状态联动 |

模拟器只做演示：不读写本地文件、不调用 `netsh`、不联网，数据只存在浏览器会话里。
站点源码在 [`Website/`](Website/)（纯静态零依赖，无构建步骤）。

## GUI 使用

### 账号切换页

- 顶部选好区服，列表里双击账号或点"切换"即可
- 点"添加账号"补录新账号，两种用法：
  - **邮箱填好** → 直接添加（该邮箱必须已在战网客户端勾选"记住密码"）
  - **邮箱留空** → 点"启动战网并等待登录"：工具会以所选区服打开战网，
    你在客户端里登录新号（记得勾"记住密码"），登录成功后邮箱会自动回填
- 点**"更新当前快照"**：以战网客户端当前真正登录的账号 + 区服为准，
  保存/刷新一份本地状态快照（会先关战网）
- 点每行末尾 `×` 从本地记录移除（只删本地记录，不动战网账号；该邮箱所有区服的快照也会一并删除）

### 一键拔线页（默认启用）

- 点"浏览…"从磁盘选 exe，或"选择…"从运行中的进程里挑
- 进程选择窗口支持**按进程名或路径搜索**，回车/双击直接确定
- 勾选是否同时静音，设置秒数，点"一键拔线"

### 关于"启动战网并等待登录"

战网客户端**不对外提供任何"登录完成"通知**（无 API、无 IPC 通道），所以这个功能靠
**轮询** `Battle.net.config` 的 `SavedAccountNames` 实现：

1. 点按钮时先记下"当前已记住的账号"作为基线，再以所选区服启动战网
2. 后台每 800ms 读一次配置，出现基线里没有的邮箱即判定登录成功，回填到邮箱框
3. 超时 3 分钟；期间也可点"刷新检测"手动补一次

实测：**登录成功后约 2 秒配置就被写入**，不需要等用户关闭客户端。
前提是登录时勾了"记住密码"——不勾则这个邮箱永远不会写进配置，工具也无从读取。

### 设置（菜单栏 → 设置 → 首选项…）

- **战网程序路径**：便携版/绿色版战网可手动指定 `Battle.net.exe` 完整路径
- **配置文件路径**：自定义 `Battle.net.config` 位置（留空用默认 `%APPDATA%\Battle.net\Battle.net.config`）
- **切换账号时优先恢复本地快照**：默认开启。关闭后切换退化为"只改配置+重启"
- **保存快照前自动关闭战网客户端**：默认开启。建议保持开启，避免半写状态
- **快照根目录**：留空 = `%APPDATA%\BattleNetSwitcher\Snapshots\`
- **禁用"一键拔线"功能**：勾选后 GUI 启动不再请求管理员权限，只保留账号切换

修改后需重启程序生效。

> 底部状态栏的版本号可点击，跳转到 GitHub Releases 页。

## CLI 用法

```
BattleNetSwitcher.Cli list [--region <区服>]                列出所有（或指定区服的）账号
BattleNetSwitcher.Cli regions                               列出所有有账号的区服
BattleNetSwitcher.Cli switch <邮箱> [--region <区服>]       切换账号
BattleNetSwitcher.Cli <邮箱> [--region <区服>]              同上（简写）
BattleNetSwitcher.Cli add <邮箱> --region <区服>            添加账号到区服
BattleNetSwitcher.Cli remove|rm <邮箱> --region <区服>      从区服移除账号

BattleNetSwitcher.Cli snapshot save <邮箱> [--region <区服>]      保存/更新指定区服的本地状态快照
BattleNetSwitcher.Cli snapshot list [--region <区服>]             列出所有（或指定区服的）快照
BattleNetSwitcher.Cli snapshot remove <邮箱> [--region <区服>]    删除快照（不指定区服 = 删该邮箱所有区服）
BattleNetSwitcher.Cli snapshot restore <邮箱> [--region <区服>]   恢复指定区服的快照（需先手动关战网）

BattleNetSwitcher.Cli version                               显示版本号
BattleNetSwitcher.Cli help                                  显示帮助
```

**区服代码**：`CN` / `US` / `EU` / `KR` / `TW`
**选项**：`--region` 或 `-r`

**示例**：

```bash
bns list                              # 列出所有区服的账号（有快照的会标 [快照]）
bns list --region US                  # 只看美服
bns switch a@b.com --region US        # 切到美服的 a@b.com
bns add b@c.com --region KR           # 把 b@c.com 加入亚服
bns remove b@c.com --region KR        # 从亚服移除

bns snapshot save a@b.com --region US # 保存/更新 a@b.com 美服的快照
bns snapshot list                     # 列出所有快照
bns snapshot list --region CN         # 只看国服的快照
bns snapshot remove a@b.com           # 删除 a@b.com 所有区服的快照
bns snapshot remove a@b.com -r US     # 只删美服那份
bns snapshot restore a@b.com -r US    # 恢复（需先手动关闭战网）
```

> 若邮箱在多个区服有记录，`switch` 必须用 `--region` 指定，否则会报错。

## 本地状态快照（原理与使用）

### 为什么会有浏览器验证码

战网的登录凭证是 **Windows DPAPI 加密的 blob**，存放在：

```
HKCU\Software\Blizzard Entertainment\Battle.net\UnifiedAuth\<8位十六进制ID>
```

- 客户端启动时读取、解密，拿去服务端续期；成功就免密进入，失败就弹浏览器
- 客户端会**主动清理"不活跃"的旧凭证**——离开某账号越久，它的 `UnifiedAuth` 条目越可能被删
- 被删后切回去就只能走网页验证 → 频繁弹验证码

**快照就是"在凭证还新鲜时备份一份"**，切换时写回去，让客户端读到"已验证过"的本地状态，
直接向服务端续期，浏览器全程不参与。

### 快照里有什么

```
%APPDATA%\BattleNetSwitcher\Snapshots\<邮箱安全化>__<区服>\
├── manifest.json      元数据：邮箱、区服、文件清单（含 SHA256）、UnifiedAuth ID 列表
├── registry.json      UnifiedAuth 子树的完整导出（递归，含所有值类型）
└── files\
    └── Battle.net.config 等战网客户端根目录文件
```

**目录名示例**：

```
Snapshots\
├── user_example.com__CN\         ← user@example.com 在国服的快照
├── user_example.com__US\         ← user_example.com 在美服的快照
└── another_at_test.com__CN\
```

### 使用流程

1. **首次登录某账号**：手动在战网客户端里登录（该弹验证码就弹，无法避免）
2. **保存快照**：在工具里点"更新当前快照"，工具会：
   - 优雅关闭战网（让客户端把最新状态写盘）
   - 复制关键文件到快照目录
   - 递归导出 `UnifiedAuth` 到 `registry.json`
3. **正常使用账号**：随便玩，玩完关客户端
4. **切换时自动更新**：点"切换"到别的账号时：
   - 关闭战网
   - **自动为"刚下线的那个账号"更新快照**（此时它的凭证是服务端刚确认过的）
   - 恢复目标账号的对应区服快照
   - 以 `--setregion=XX` 启动
5. **启动后应免验证直连**：若仍弹浏览器 → 该快照的令牌已被服务端作废，需要重新登录并刷新快照

### 什么时候会失效

快照不是万能的，以下情况需要重新登录：

- 服务端主动作废了刷新令牌（如异地登录、改密码、长期不活跃）
- 客户端已经用服务端的最新状态刷新过本地凭证，而快照是更早的版本
- 换 Windows 用户 / 换机器：DPAPI blob 绑定原用户和机器，新环境解不开

### 我们**不**碰什么

- `HKCU\...\Battle.net\EncryptionKey` —— 安装级密钥，动它客户端完全登录不了
- `HKCU\...\Battle.net\CacheDatabase` —— 安装级缓存数据库密钥
- 战网客户端安装目录里除 `Battle.net.config` 之外的任何文件
- `Identity\Identity` —— 设备身份，与账号无关

## 便携版 / 绿色版战网

默认情况下，程序通过 `%APPDATA%\Battle.net\Battle.net.config` 和注册表定位战网。便携版战网需要手动指定：

**方式 A（推荐）**：GUI 菜单栏 → 设置 → 首选项… → 填入：

- 战网程序路径，例如 `D:\Games\Battle.net\Battle.net.exe`
- 配置文件路径，例如 `D:\Games\Battle.net\Battle.net.config`

**方式 B**：手动编辑 `%APPDATA%\BattleNetSwitcher\config.json`：

```json
{
  "battleNetExePath": "D:\\Games\\Battle.net\\Battle.net.exe",
  "battleNetConfigPath": "D:\\Games\\Battle.net\\Battle.net.config",
  "disablePullout": false,
  "useSnapshotOnSwitch": true,
  "autoSaveSnapshotOnSwitch": true,
  "closeBattleNetBeforeSave": true,
  "snapshotRootPath": null
}
```

配置一次后，GUI 和 CLI 都生效。

## 环境要求（自行编译时）

- Windows 10 / 11（x64）
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## 编译

```bash
git clone https://github.com/DefeatMan/BattleNetSwitcher.git
cd BattleNetSwitcher

# GUI
dotnet build BattleNetSwitcher/BattleNetSwitcher.csproj -c Release

# CLI
dotnet build BattleNetSwitcher.Cli/BattleNetSwitcher.Cli.csproj -c Release
```

或直接发布单文件：

```bash
# GUI
dotnet publish BattleNetSwitcher/BattleNetSwitcher.csproj \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true \
  -o bin/Release/publish/gui

# CLI
dotnet publish BattleNetSwitcher.Cli/BattleNetSwitcher.Cli.csproj \
  -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true \
  -o bin/Release/publish/cli
```

> Linux / WSL 也能编译 Windows 目标，见 `Directory.Build.props` 里的 `<EnableWindowsTargeting>true</EnableWindowsTargeting>`。

## 项目结构

```
BattleNetSwitcher/
├── Directory.Build.props              共享属性（版本号、仓库地址）
├── BattleNetSwitcher.Core/            类库
│   ├── AccountBook.cs                 本地"邮箱 ↔ 区服"映射
│   ├── AccountSwitcher.cs             战网配置读写、账号切换、快照调度
│   ├── AppInfo.cs                     版本号与仓库地址
│   ├── AppSettings.cs                 全局设置（战网路径、快照开关）
│   ├── SnapshotManager.cs             本地状态快照：保存 / 恢复 / 枚举 / 迁移
│   ├── Snapshots/                     快照基础设施
│   │   ├── SafeName.cs                邮箱 → 安全目录名
│   │   ├── SnapshotPaths.cs           快照路径计算（<safeEmail>__<REGION>）
│   │   ├── SnapshotInfo.cs            快照运行时视图
│   │   ├── SnapshotManifest.cs        manifest.json DTO
│   │   ├── RegistrySnapshot.cs        registry.json DTO
│   │   └── RegistrySnapshotIO.cs      注册表递归导出/恢复（保护关键键）
│   ├── AudioSessionController.cs      WASAPI 会话静音
│   ├── CoreAudioInterop.cs            Core Audio COM 定义
│   └── FirewallManager.cs             netsh advfirewall 封装
├── BattleNetSwitcher/                 GUI（WinExe）
│   ├── Program.cs
│   ├── app.manifest
│   ├── app.ico
│   └── Forms/
│       ├── MainForm.cs
│       ├── AccountPanel.cs
│       ├── AccountRow.cs
│       ├── AddAccountDialog.cs
│       ├── NetworkPanel.cs
│       ├── ProcessPickerDialog.cs
│       └── SettingsPanel.cs
├── BattleNetSwitcher.Cli/             CLI（Console Exe）
│   ├── Program.cs
│   └── app.manifest
└── Website/                           官网 + 在线模拟器（纯静态，Pages 托管）
    ├── index.html                     首页
    ├── gui.html                        UI 模拟（左侧战网客户端 + 右侧工具窗口）
    └── assets/                        样式与模拟内核
```

## 本地文件位置

| 文件 | 作用 |
|---|---|
| `%APPDATA%\BattleNetSwitcher\accounts.json` | 邮箱 ↔ 区服映射 |
| `%APPDATA%\BattleNetSwitcher\config.json` | 全局设置（战网路径、快照开关等） |
| `%APPDATA%\BattleNetSwitcher\Snapshots\<邮箱>__<区服>\` | 本地状态快照 |
| `%APPDATA%\Battle.net\Battle.net.config` | 战网自身配置（默认位置） |
| `HKCU\Software\Blizzard Entertainment\Battle.net\UnifiedAuth\` | 战网登录凭证（快照来源/目标） |
| `HKCU\Software\BattleNetTool` | 一键拔线页的目标应用 / 秒数 / 上次选中区服 |

> 官网模拟器不使用以上任何位置：它的数据只放在浏览器的 `sessionStorage` 里。

## 关于战网登录状态

**登录凭证不在 `Battle.net.config` 里** —— 那个文件只有邮箱列表和设置：

```
Battle.net.config\Client\
├── SavedAccountNames   "a@x.com,b@y.com"   ← 邮箱列表，首位 = 当前自动登录账号
├── AutoLogin            true
└── LoginSettings\SelectedRegion  CN
```

真正的凭证在注册表 `HKCU\Software\Blizzard Entertainment\Battle.net\`：

| 位置 | 内容 | 与账号有关？ |
|---|---|---|
| `UnifiedAuth\<8位十六进制ID>` | 会话凭证 blob（约 262B） | ✅ 按账号不同（键名会轮换） |
| `Identity\Identity` | 设备身份（64B） | ❌ 与账号无关 |
| `Launch Options\<产品>\`（`WEB_TOKEN` / `ACCOUNT_STATE` / `ACCOUNT` / `ACCOUNT_TS`） | 产品级登录态 | ❌ 实测不随账号变化 |
| `EncryptionKey\CacheDatabase` | **安装级密钥** | ❌ 动它会破坏整个客户端 |

所有 blob 都以 `01000000D08C9DDF0115D1118C7A00C04FC297EB` 开头，这是
**Windows DPAPI（`CryptProtectData`）的固定头部** —— 凭证只能被同一个 Windows 用户解密。

### 它们怎么生效

1. 登录时客户端走 OAuth，从服务端拿到一对令牌：**会话令牌（短命）** 与 **刷新令牌（长命）**
2. 客户端用 DPAPI 加密后写入上面的注册表位置，并把登录时间记进 `ACCOUNT_TS`
3. 每次启动时读取、解密，拿去服务端续期：成功就免密进入，失败就弹登录框
4. **关键：续期是服务端在计时，客户端只负责"到点去换"，而它只在运行期间去换**

### 本工具是怎么应对的

因为"离开客户端越久越可能需要重新登录"是**服务端行为**，本地改不了。
工具只做两件在本地有效的事：

1. **让战网正常退出**：发退出请求（`CloseMainWindow`）并等它自行收尾，超时才强杀 ——
   给它机会把状态与刷新后的令牌好好写盘
2. **在凭证还新鲜时存一份快照**：切 A→B 时先为 A 更新快照、再恢复 B 的快照，
   让 B 起客户端时能读到"已验证过"的本地状态

因此：

- 首次登录某账号仍需手动完成（含验证码），快照只能省**后续切换**的验证
- 长期不活跃后仍会失效（服务端作废刷新令牌），需重新登录一次并刷新快照
- 换 Windows 用户 / 换机器，快照无法复用（DPAPI 限制）

### 已知的外部限制

- **国服**：目前登录需经网页跳转，属运营方（网易）的服务端策略，本地无法绕过
- **跨区服**：不同区服的登录状态相互独立，切换区服本身就是换一个会话
- 若客户端频繁要求重新登录，先确认登录时**勾选了"记住密码"**，否则凭证不会被写入

## 注意事项

- **管理员权限**：启用"一键拔线"时 GUI 启动会弹 UAC。禁用后不再需要
- **杀毒软件**：`netsh advfirewall` 命令可能被拦截，如遇失败请添加信任
- **跨区服需要加速器**：连接美服/欧服/亚服通常需要游戏加速器
- **账号独立**：不同区服的账号数据不互通，需分别拥有对应区服的账号
- **首次运行**：本地账号本为空时，会自动把战网 `SavedAccountNames` 里的账号归到当前 `SelectedRegion`；跨区服的账号需手动 `add` 补录

## 协议

[MIT License](LICENSE)

> 本项目与 Blizzard Entertainment 无任何关联。"Battle.net""战网"是其商标或注册商标。
