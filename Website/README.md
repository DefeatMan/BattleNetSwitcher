# Website —— 项目官网 + 在线模拟器

纯静态站点（HTML / CSS / 原生 JS，**零依赖、零构建**），
用 GitHub Pages 托管，也可以在本地直接用浏览器打开。

## 文件结构

```
Website/
├── index.html              官网首页：功能 / 下载 / 使用详情
├── gui.html                UI 模拟：左侧战网登录页 + 右侧 WinForms 主窗口（并排，无外框）
├── favicon.ico             站点图标（取自 GUI 项目的 app.ico）
└── assets/
    ├── common.css          设计令牌、导航、页脚、布局
    ├── sim.css             WinForms 控件模拟（对齐 Forms/*.cs 的实际尺寸与颜色）
    ├── client.css          战网客户端模拟（登录页 / 登录成功页）
    ├── app.js              导航、版本号注入、等比缩放舞台、Toast
    ├── sim-data.js         区服定义与演示数据（区服表与 Core/AccountSwitcher.cs 一致）
    ├── sim-core.js         模拟内核：账号本 / 战网配置 / 快照 / 切换账号 / 防火墙（对齐 Core/*.cs）
    ├── sim-client.js       战网客户端状态机（登录页 ↔ 登录成功页）
    ├── sim-gui.js          WinForms 界面与对话框（含添加账号、更新当前快照、进程选择、一键拔线）
    ├── sim-boot.js         按 data-page 挂载模拟器
    ├── app.ico             原样复制自 BattleNetSwitcher/app.ico（页面 favicon）
    ├── app-icon.png        从 app.ico 提取的 64×64，用于导航栏与模拟窗口标题栏
    └── app-icon-256.png    从 app.ico 提取的 256×256，用作 apple-touch-icon
```

> 图标改版时，把 `BattleNetSwitcher/app.ico` 复制到 `Website/favicon.ico` 与
> `Website/assets/app.ico`，再重新导出两个 PNG 即可。

## 本地预览

站点没有构建步骤，但**建议用本地 HTTP 服务**打开
（`file://` 下部分浏览器会禁用 `sessionStorage`，会话状态会退化成内存存储）：

```bash
cd Website
python3 -m http.server 8765
# 然后浏览器打开 http://127.0.0.1:8765/
```

直接双击 `index.html` 也能用。

## 模拟器说明

- **只做模拟**：不会结束进程、不会读写 `Battle.net.config`、不会调用 `netsh`、不会联网，
  也不会启动战网客户端。
- **状态只在会话里**：数据写在 `sessionStorage`（键 `bns.sim.state.v1`），
  刷新或关掉标签页即恢复初始演示数据。用户新加的账号同样只存在于本次会话。
- **两侧共用状态**：左边的战网客户端模拟与右边的工具窗口读写同一份会话数据，
  工具里切账号/切区服，左侧立刻跟着变。
- **登录状态按账号各存一份**（`clientAccounts`：`{loggedIn, keep}`），不是全局开关：
  - 登录后该账号 `loggedIn = true`；点"登出"只把这个账号置回未登录
  - 工具切到某账号时：**该账号已有登录状态 → 直接进已登录页**（不用再输密码）；
    否则回登录页，邮箱已预填
  - **手动登录时只有勾了"保持登录状态"才会落成持久登录状态**，否则切走再切回需要重新登录
  - 初始种子局面：账号本里**所有**（邮箱 + 区服）组合都处于"保持登录状态"
- **添加账号对话框为非模态**（真实版同样如此）：选好区服后邮箱可以留空，
  点"启动战网并等待登录"会以该区服打开右侧的客户端模拟（停在**空白登录页**）；
  在那边登录并勾选"记住密码"后，该邮箱会被写进模拟的 `SavedAccountNames`，
  对话框轮询到新增项即自动回填邮箱框 —— 对应真实版 `AddAccountDialog` 的检测逻辑
- 密码框默认填 `12345678`（演示用，密码不是重点）
- **左侧登录页按官方页面还原**：尺寸与配色通过 CDP 读取真实页面的 computed style 得到
  （容器 416px、标志 240×40、输入框 40px 高 / 底 `#e8f0fe` / 边框 `#a9a9ac`、
  主按钮 `#0074e0`、页面底色 `#000e2b`、正文 `rgba(255,255,255,.84)` 等）。
  只保留核心四项：电子邮箱或手机号码 / 密码 / 保持登录状态 / 登录；
  登录成功页只显示账号信息与登出。
- **登录页背景为原创**：官方是 `background-xl-cropped.jpg` 美术图，
  这里用纯 CSS 渐变自绘同色调的星空/极光，**不使用官方图片素材**；
  Blizzard 标志用 SVG 几何图形近似，地区图标用色块表示。

### 本地状态快照的模拟（v1.1.0）

`sim-core.js` 增加了与 `SnapshotManager.cs` 对齐的快照逻辑：

- **快照按 (邮箱, 区服) 二维键存储**（对应真实版的
  `%APPDATA%\BattleNetSwitcher\Snapshots\<safeEmail>__<REGION>\`）。
  同一邮箱在国服与美服各有独立快照，互不覆盖。
- **切换账号时自动更新源账号快照**：
  `switchAccount()` 会在关闭战网后读取当前登录的 (email, region)，
  若与目标不同，先 `snapshotSave(source)` 再 `snapshotRestore(target)`。
  完整日志会出现在切换进度对话框里。
- **种子快照**：`SEED_SNAPSHOTS`（由 `data.buildSeedSnapshots()` 生成）给
  5 条演示账号各自预置了一份快照，时间戳是"相对现在 N 小时前"。
  这样一打开页面，切换任意账号都能看到"自动更新源 + 恢复目标"的效果。
- **快照相关 API**（挂到 `BNS.core` 上）：
  `snapshotExists` / `snapshotGetInfo` / `snapshotList` / `snapshotListForEmail`
  / `snapshotSave` / `snapshotRestore` / `snapshotRemove` / `snapshotRemoveAll`
  / `snapshotKeyOf` / `snapshotRootPath`。
- **账号页状态行**：若当前登录账号在该区服已有快照，会显示"当前账号快照：YYYY-MM-DD HH:mm"；
  若没有，会显示"当前账号无快照（建议点'更新当前快照'保存一份）"。
- **"更新当前快照"按钮**：位于账号页底部按钮行（"添加账号"右侧）。
  操作对象由 `Battle.net.config` 的 `SavedAccountNames[0]` + `SelectedRegion` 决定，
  **不是用户点的那一行** —— 避免点错行导致保存到错账号。

演示数据只保留 **两个区服（CN / US）、一共 5 条记录**，
其中 `player@example.com` 在两个区服各有一条，用来演示跨区服：

| 邮箱 | 区服 | 初始快照 |
|---|---|---|
| `player@example.com` | CN、US | 有（两个区服各一份） |
| `demo@example.com` | CN | 有 |
| `alt.cn@example.com` | CN | 有 |
| `alt.us@example.com` | US | 有 |

用它可以触发 CLI 的这条分支（真实程序）：

```
BattleNetSwitcher.Cli switch player@example.com
错误：账号 player@example.com 在多个区服有记录，请用 --region 指定：
  CN, US
```

也可以演示：

```
BattleNetSwitcher.Cli snapshot list
BattleNetSwitcher.Cli snapshot save player@example.com --region US
BattleNetSwitcher.Cli snapshot remove player@example.com --region US
```

## 部署（GitHub Pages）

`.github/workflows/pages.yml` 负责发布，触发条件：

- 推送 `v*` 标签（与 `release.yml` 同口径，所以**发 Release 就会更新官网**）
- 在 GitHub 上发布 Release（`release: published`）
- 手动 `workflow_dispatch`

流程：从 `Directory.Build.props` 读取 `<Version>` 与 `<RepositoryUrl>` →
替换站点里的 `__APP_VERSION__` / `__REPO_URL__` 占位符 → 上传 `Website/` → 部署到 Pages。

**首次使用需要做两步设置：**

1. 仓库 `Settings` → `Pages` → `Build and deployment` → `Source` 选择 **GitHub Actions**
2. 仓库 `Settings` → `Environments` → **`github-pages`** → `Deployment branches and tags`
   → 规则改成 **`All branches and tags`**

第 2 步很容易漏。`github-pages` 环境是 GitHub 在启用 Pages 时自动创建的，
默认只允许**默认分支**部署；而上方触发器里有 `push tags v*`，
这时 `ref` 是**标签**而不是分支，会被保护规则挡下，报错是：

```
Tag "v1.0.1" is not allowed to deploy to github-pages due to environment protection rules.
The deployment was rejected or didn't satisfy other protection rules.
```

`build` 阶段不受影响（它只负责打包上传 artifact），**只有 `deploy` 阶段会失败**，
表现为该任务 2 秒左右就结束、且 job 页面上看不到部署链接。

**这个失败不影响 `Release` workflow 出包**——两者权限独立，exe 照常生成，只是官网没更新。

之后访问 `https://<用户名>.github.io/BattleNetSwitcher/` 即可。

## 维护约定

- 修改官网文案直接改 HTML；修改模拟行为请对照真实的
  `BattleNetSwitcher.Core` / `BattleNetSwitcher` 里的实现。
- 新增区服时，同时更新 `assets/sim-data.js` 的 `REGIONS` 与
  `BattleNetSwitcher.Core/AccountSwitcher.cs` 的 `RegionInfo.All`（顺序也要一致）。
- 演示账号请使用通用示例邮箱（`*@example.com`），不要写真实个人账号。
- 快照相关的目录命名、API 名、错误文案要与
  `BattleNetSwitcher.Core/SnapshotManager.cs` / `Snapshots/SnapshotPaths.cs`
  保持同步，方便两边对照阅读。
