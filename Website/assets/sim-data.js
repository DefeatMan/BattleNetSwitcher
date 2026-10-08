/* ============================================================
 *  sim-data.js —— 区服定义与演示数据
 *
 *  区服表逐项对齐 BattleNetSwitcher.Core/AccountSwitcher.cs 里的 RegionInfo.All：
 *      Code / DisplayName / DefaultLocale
 *  （唯一区别：Core 里是 enum + RegionInfo，这里用普通对象表示，方便前端使用。）
 * ============================================================ */
(function (global) {
    'use strict';

    var BNS = global.BNS || (global.BNS = {});

    /** 与 RegionInfo.All 完全一致（顺序也不能变：GetActiveRegions 依赖这个顺序）。 */
    var REGIONS = [
        { code: 'CN', displayName: '国服 (CN)', locale: 'zhCN', short: '国服' },
        { code: 'US', displayName: '美服 (US)', locale: 'enUS', short: '美服' },
        { code: 'EU', displayName: '欧服 (EU)', locale: 'enGB', short: '欧服' },
        { code: 'KR', displayName: '亚服 (KR)', locale: 'koKR', short: '亚服' },
        { code: 'TW', displayName: '台服 (TW)', locale: 'zhTW', short: '台服' }
    ];

    function tryFromCode(code) {
        if (!code) return null;
        var upper = String(code).toUpperCase();
        for (var i = 0; i < REGIONS.length; i++) {
            if (REGIONS[i].code === upper) return REGIONS[i];
        }
        return null;
    }

    /** 对应 RegionInfo.FromCode：未知代码回落到 All[0]（CN）。 */
    function fromCode(code) {
        return tryFromCode(code) || REGIONS[0];
    }

    function codes() {
        return REGIONS.map(function (r) { return r.code; });
    }

    /* ------------------------------------------------------------
     *  演示数据
     * ------------------------------------------------------------ */

    /**
     * 账号本（等价于 %APPDATA%\BattleNetSwitcher\accounts.json）。
     *
     * 演示数据只保留两个区服、一共 5 条记录，但刻意让
     * player@example.com 在国服与美服各有一条 —— 这是本工具最核心的使用场景之一：
     * 战网自身只存 SavedAccountNames（一串邮箱），并不记录"哪个邮箱属于哪个区服"，
     * 所以需要本地账号本维护这份映射，跨区服切号也必须显式指定区服。
     *
     *   player@example.com  →  CN + US   （同一邮箱，两个区服）
     *   demo@example.com    →  CN
     *   alt.cn@example.com  →  CN
     *   alt.us@example.com  →  US
     *
     * 由此可以演示 CLI 的这条分支：
     *   switch player@example.com              →  报错，要求用 --region 指定区服
     *   switch player@example.com --region US  →  成功
     */
    var SEED_ACCOUNTS = [
        // 国服
        { email: 'player@example.com', region: 'CN' },
        { email: 'demo@example.com', region: 'CN' },
        { email: 'alt.cn@example.com', region: 'CN' },
        // 美服
        { email: 'player@example.com', region: 'US' },
        { email: 'alt.us@example.com', region: 'US' }
    ];

    /**
     * 战网自身配置（等价于 %APPDATA%\Battle.net\Battle.net.config）。
     * SavedAccountNames 是逗号分隔的字符串，首位即"当前自动登录账号"。
     */
    var SEED_CONFIG = {
        Client: {
            SavedAccountNames: 'player@example.com,demo@example.com,alt.cn@example.com,alt.us@example.com',
            LoginSettings: {
                // 与 AccountSwitcher.TrySetConfigRegion 写入的值保持一致
                AllowedRegions: 'CN;US;EU;KR;TW',
                AllowedLocales: 'zhCN;deDE;enGB;enUS;esMX;esES;frFR;itIT;plPL;ptBR;ruRU;koKR;zhTW',
                SelectedRegion: 'CN'
            }
        }
    };

    /** 全局设置（等价于 %APPDATA%\BattleNetSwitcher\config.json）。 */
    var SEED_SETTINGS = {
        battleNetExePath: null,
        battleNetConfigPath: null,
        disablePullout: false,
        // v1.1.0 快照相关
        useSnapshotOnSwitch: true,
        autoSaveSnapshotOnSwitch: true,
        closeBattleNetBeforeSave: true,
        snapshotRootPath: null
    };

    /** 一键拔线页的持久化设置（等价于注册表 HKCU\Software\BattleNetTool）。 */
    var SEED_NETWORK = {
        appPath: '',
        procName: '',
        ruleName: '',
        pullSeconds: 3,
        mute: true
    };

    /**
     * 种子快照（等价于 %APPDATA%\BattleNetSwitcher\Snapshots\<safeEmail>__<REGION>\）。
     *
     * 键格式与 Core 的 SnapshotPaths.ForEmail 一致：小写邮箱 + "__" + 大写区服。
     * 每个账号在它所在的每个区服各有一份 —— 这是本工具 v1.1.0 的核心能力：
     * 切换账号时自动更新"源账号"的快照、恢复"目标账号"的快照，
     * 让客户端读到"已验证过"的本地状态直接续期，避免浏览器验证码。
     *
     * 时间用"相对现在 N 小时前"，这样无论何时打开页面都像是最近保存过。
     */
    function buildSeedSnapshots() {
        var now = Date.now();
        var H = 3600 * 1000;

        function entry(email, region, hoursAgo) {
            return {
                email: email,
                region: region,
                createdAt: new Date(now - (hoursAgo + 24) * H).toISOString(),
                updatedAt: new Date(now - hoursAgo * H).toISOString(),
                fileCount: 1,
                uniqueIdCount: 1
            };
        }

        return {
            'player@example.com__cn': entry('player@example.com', 'CN', 2),
            'player@example.com__us': entry('player@example.com', 'US', 5),
            'demo@example.com__cn':   entry('demo@example.com',   'CN', 3),
            'alt.cn@example.com__cn': entry('alt.cn@example.com', 'CN', 26),
            'alt.us@example.com__us': entry('alt.us@example.com', 'US', 48)
        };
    }

    /**
     * 「浏览…」对话框里的假文件系统。
     * installDir 作为战网默认安装目录出现在设置页的浏览对话框里。
     */
    var FAKE_FILES = {
        // 全部为虚构路径：用户名统一用 demo，游戏目录用 D:\Games
        installDir: 'C:\\Program Files (x86)\\Battle.net',
        configDir: 'C:\\Users\\demo\\AppData\\Roaming\\Battle.net',
        snapshotDir: 'C:\\Users\\demo\\AppData\\Roaming\\BattleNetSwitcher\\Snapshots',
        files: [
            'C:\\Program Files (x86)\\Battle.net\\Battle.net.exe',
            'C:\\Program Files (x86)\\Battle.net\\Battle.net Launcher.exe',
            'C:\\Program Files (x86)\\Battle.net\\Battle.net.config',
            'C:\\Users\\demo\\AppData\\Roaming\\Battle.net\\Battle.net.config',
            'D:\\Games\\Battle.net\\Battle.net.exe',
            'D:\\Games\\Battle.net\\Battle.net.config'
        ]
    };

    /**
     * 「选择…」（进程选择对话框）里的假进程列表。
     * 对应 ProcessPickerDialog：进程名 / PID / 可执行文件路径。
     */
    var SEED_PROCESSES = [
        // 名称是真实的进程名（战网与自家游戏），PID 与路径都是编造的
        { name: 'Battle.net.exe', pid: 1024, path: 'C:\\Program Files (x86)\\Battle.net\\Battle.net.exe' },
        { name: 'Battle.net Helper.exe', pid: 1036, path: 'C:\\Program Files (x86)\\Battle.net\\Battle.net Helper.exe' },
        { name: 'Wow.exe', pid: 2048, path: 'D:\\Games\\World of Warcraft\\_retail_\\Wow.exe' },
        { name: 'WowClassic.exe', pid: 2064, path: 'D:\\Games\\World of Warcraft\\_classic_\\WowClassic.exe' },
        { name: 'Overwatch.exe', pid: 3072, path: 'D:\\Games\\Overwatch\\Overwatch.exe' },
        { name: 'Diablo IV.exe', pid: 3088, path: 'D:\\Games\\Diablo IV\\Diablo IV.exe' },
        { name: 'Hearthstone.exe', pid: 4096, path: 'D:\\Games\\Hearthstone\\Hearthstone.exe' },
        { name: 'StarCraft II.exe', pid: 4112, path: 'D:\\Games\\StarCraft II\\Support64\\SC2_x64.exe' },
        { name: 'explorer.exe', pid: 512, path: 'C:\\Windows\\explorer.exe' },
        { name: 'notepad.exe', pid: 6144, path: 'C:\\Windows\\System32\\notepad.exe' },
        { name: 'mspaint.exe', pid: 6160, path: 'C:\\Windows\\System32\\mspaint.exe' },
        { name: 'calc.exe', pid: 6176, path: 'C:\\Windows\\System32\\calc.exe' },
        { name: 'Taskmgr.exe', pid: 6208, path: 'C:\\Windows\\System32\\Taskmgr.exe' }
    ];

    /**
     * 客户端模拟里展示的游戏（登录成功后出现）。
     * 只作为占位展示，不绑定具体区服。
     */
    var GAME_CARDS = [
        { title: 'World of Warcraft', sub: '正式服 · 11.0.5', color: '#1d4e89' },
        { title: 'Overwatch 2', sub: '第 13 赛季', color: '#b0472b' },
        { title: 'Diablo IV', sub: '憎恨之躯', color: '#5a1f24' },
        { title: '炉石传说', sub: '标准模式', color: '#7a5c1e' }
    ];

    BNS.data = {
        REGIONS: REGIONS,
        tryFromCode: tryFromCode,
        fromCode: fromCode,
        codes: codes,
        SEED_ACCOUNTS: SEED_ACCOUNTS,
        SEED_CONFIG: SEED_CONFIG,
        SEED_SETTINGS: SEED_SETTINGS,
        SEED_NETWORK: SEED_NETWORK,
        buildSeedSnapshots: buildSeedSnapshots,
        FAKE_FILES: FAKE_FILES,
        SEED_PROCESSES: SEED_PROCESSES,
        GAME_CARDS: GAME_CARDS
    };
})(typeof window !== 'undefined' ? window : this);
