/* ============================================================
 *  sim-core.js —— 模拟器"内核"
 *
 *  这一层是纯逻辑 + 会话状态，逐条对齐真实实现：
 *      BattleNetSwitcher.Core/AccountBook.cs        账号本（邮箱 ↔ 区服）
 *      BattleNetSwitcher.Core/AccountSwitcher.cs    战网配置读写 / 切换账号
 *      BattleNetSwitcher.Core/AppSettings.cs        全局设置
 *      BattleNetSwitcher.Core/FirewallManager.cs    防火墙规则（模拟）
 *
 *  与真实版的两点差异（都是有意为之）：
 *      1. 所有状态只放在 sessionStorage（关掉标签页即丢失），符合"只会话有效"；
 *         file:// 下浏览器可能禁用 storage，此时自动回落到内存对象。
 *      2. 副作用（杀进程、sleep、netsh、静音）用 setTimeout 模拟，不产生任何真实影响。
 * ============================================================ */
(function (global) {
    'use strict';

    var BNS = global.BNS || (global.BNS = {});
    var data = BNS.data;

    var KEY = 'bns.sim.state.v1';
    var SCHEMA = 1;

    /* ============================================================
     *  一、存储层（sessionStorage，带内存兜底）
     * ============================================================ */
    var memoryFallback = null;
    var storageWorks = null;

    function storageAvailable() {
        if (storageWorks !== null) return storageWorks;
        try {
            var probe = '__bns_probe__';
            global.sessionStorage.setItem(probe, '1');
            global.sessionStorage.removeItem(probe);
            storageWorks = true;
        } catch (e) {
            storageWorks = false;
        }
        return storageWorks;
    }

    function readRaw() {
        if (storageAvailable()) {
            try { return global.sessionStorage.getItem(KEY); } catch (e) { /* 继续回落 */ }
        }
        return memoryFallback;
    }

    function writeRaw(text) {
        memoryFallback = text;
        if (storageAvailable()) {
            try { global.sessionStorage.setItem(KEY, text); } catch (e) { /* 忽略 */ }
        }
    }

    function deepClone(value) {
        return JSON.parse(JSON.stringify(value));
    }

    /* ============================================================
     *  二、状态定义
     * ============================================================ */
    function buildFreshState() {
        return {
            schema: SCHEMA,
            /** accounts.json 的等价物 */
            accounts: deepClone(data.SEED_ACCOUNTS),
            /** Battle.net.config 的等价物 */
            config: deepClone(data.SEED_CONFIG),
            /** config.json 的等价物 */
            settings: deepClone(data.SEED_SETTINGS),
            /** 注册表 HKCU\Software\BattleNetTool 的等价物 */
            network: deepClone(data.SEED_NETWORK),
            /** GUI 记忆的上次选中区服（注册表 LastUiRegion） */
            lastUiRegion: 'CN',
            /**
             * 是否以管理员身份运行（决定状态栏文案 + 拔线能否执行）。
             * 默认 true：模拟器不弹 UAC，按"用户已同意提权"处理。
             */
            isAdmin: true,
            /**
             * 各账号的登录状态（客户端模拟用）。
             *
             * 注意 key 是「小写邮箱 + '@' + 区服代码」：
             * 战网里同一个邮箱可以在多个区服各有账号，它们是彼此独立的账号，
             * 所以登录状态也必须按 账号+区服 分别记录，不能只用邮箱。
             *
             *   key   = "邮箱小写@CN" 之类的组合键
             *   value = { loggedIn: 该账号在该区服是否已登录,
             *             keep:     登录时是否勾了"保持登录状态" }
             *
             * 语义：
             *   * 登录后 loggedIn = true；点"登出"只把这一个账号+区服置回未登录
             *   * 只有 keep = true 的账号，工具切过去才会直接进已登录；
             *     keep = false 的账号切过去要重新输密码
             */
            clientAccounts: {},
            /** 客户端当前显示的账号（已登录时为邮箱，未登录为 null） */
            clientLoggedIn: null,
            /** 客户端当前所在的区服（登录状态按它区分） */
            clientRegion: null,
            /**
             * 用于"添加账号 → 等待登录"的基线：进入等待前已记住的邮箱集合。
             * 轮询 SavedAccountNames 时，只有不在基线里的才算"新登录的账号"。
             */
            loginBaseline: null,
            /** 会话事件日志 */
            log: []
        };
    }

    /** 账号本里"邮箱 + 区服"的所有组合（初始种子要用） */
    function seededPairs(accounts, names, fallbackRegion) {
        var pairs = [];
        var seen = {};

        // 优先用账号本：同一个邮箱在 US / EU 会被正确拆成两条
        (accounts || []).forEach(function (e) {
            if (!e || !e.email || !e.region) return;
            var key = sessionKey(e.email, e.region);
            if (!key || seen[key]) return;
            seen[key] = true;
            pairs.push({ email: e.email, region: String(e.region).toUpperCase() });
        });

        if (pairs.length > 0) return pairs;

        // 账号本为空时，退回用 Battle.net.config 的 SavedAccountNames + SelectedRegion
        var region = String(fallbackRegion || 'CN').toUpperCase();
        (names || []).forEach(function (email) {
            var key = sessionKey(email, region);
            if (!key || seen[key]) return;
            seen[key] = true;
            pairs.push({ email: email, region: region });
        });
        return pairs;
    }

    /**
     * 初始局面：战网里"已记住密码"的**所有账号**都处于「保持登录状态」，
     * 当前显示的是列表里的第一个。
     * 也就是说，一打开这个页面，切到任意一个（账号 + 区服）都已经是登录状态，
     * 不需要再输密码。
     *
     * 注意区服必须取自账号本，而不是 LoginSettings.SelectedRegion ——
     * 否则 US / EU 的账号会被错记成当前区服，切过去就变成"未登录"。
     */
    function buildSeededState() {
        var fresh = buildFreshState();
        var loginRegion = String(
            (fresh.config.Client.LoginSettings || {}).SelectedRegion || 'CN'
        ).toUpperCase();

        var names = String(
            (fresh.config.Client && fresh.config.Client.SavedAccountNames) || ''
        ).split(',').map(function (x) { return x.trim(); }).filter(Boolean);

        var pairs = seededPairs(fresh.accounts, names, loginRegion);
        pairs.forEach(function (p) {
            fresh.clientAccounts[sessionKey(p.email, p.region)] = { loggedIn: true, keep: true };
        });
        if (pairs.length > 0) {
            fresh.clientLoggedIn = pairs[0].email;
            fresh.clientRegion = loginRegion;
        }
        return fresh;
    }

    var state = null;

    function loadState() {
        var raw = readRaw();
        if (!raw) return buildSeededState();
        try {
            var parsed = JSON.parse(raw);
            if (!parsed || parsed.schema !== SCHEMA) return buildSeededState();
            // 补齐可能缺失的字段（版本升级容错）
            var fresh = buildSeededState();
            for (var k in fresh) {
                if (!(k in parsed)) parsed[k] = fresh[k];
            }
            return parsed;
        } catch (e) {
            return buildSeededState();
        }
    }

    function ensureState() {
        if (!state) state = loadState();
        return state;
    }

    function persist() {
        writeRaw(JSON.stringify(ensureState()));
    }

    function resetState() {
        state = buildSeededState();
        persist();
        emit('reset');
    }

    /* ============================================================
     *  三、极简事件总线：GUI / 客户端模拟 / CLI 三方共用一份状态
     * ============================================================ */
    var listeners = [];

    function subscribe(fn) {
        listeners.push(fn);
        return function unsubscribe() {
            var i = listeners.indexOf(fn);
            if (i >= 0) listeners.splice(i, 1);
        };
    }

    /** topic 形如 'accounts' / 'config' / 'settings' / 'network' / 'client' / 'reset' / '*' */
    function emit(topic, payload) {
        var snapshot = listeners.slice();
        for (var i = 0; i < snapshot.length; i++) {
            try { snapshot[i](topic, payload); } catch (e) { /* 单个订阅者异常不影响其它 */ }
        }
    }

    /* ============================================================
     *  四、小工具
     * ============================================================ */
    function sleep(ms) {
        return new Promise(function (resolve) { setTimeout(resolve, ms); });
    }

    function isBlank(value) {
        return value === null || value === undefined || String(value).trim() === '';
    }

    function eq(a, b) {
        return String(a).toLowerCase() === String(b).toLowerCase();
    }

    function pushLog(text, kind) {
        var s = ensureState();
        s.log.push({ text: text, kind: kind || 'info', at: Date.now() });
        if (s.log.length > 200) s.log.splice(0, s.log.length - 200);
    }

    /* ============================================================
     *  五、账号本（对齐 AccountBook.cs）
     * ============================================================ */

    /** AccountBook.GetEmailsForRegion */
    function emailsForRegion(region) {
        if (isBlank(region)) return [];
        var out = [];
        ensureState().accounts.forEach(function (e) {
            if (!eq(e.region, region)) return;
            var exists = out.some(function (x) { return eq(x, e.email); });
            if (!exists) out.push(e.email);
        });
        return out;
    }

    /** AccountBook.GetRegionsForEmail */
    function regionsForEmail(email) {
        if (isBlank(email)) return [];
        var out = [];
        ensureState().accounts.forEach(function (e) {
            if (!eq(e.email, email)) return;
            var code = String(e.region).toUpperCase();
            if (out.indexOf(code) < 0) out.push(code);
        });
        return out;
    }

    /** AccountBook.GetActiveRegions：按 RegionInfo.All 顺序，历史遗留代码排到末尾 */
    function activeRegions() {
        var set = {};
        ensureState().accounts.forEach(function (e) {
            if (!isBlank(e.region)) set[String(e.region).toUpperCase()] = true;
        });

        var result = [];
        data.REGIONS.forEach(function (r) {
            if (set[r.code]) {
                result.push(r.code);
                delete set[r.code];
            }
        });

        Object.keys(set).sort().forEach(function (code) { result.push(code); });
        return result;
    }

    /** AccountBook.Contains */
    function bookContains(email, region) {
        if (isBlank(email) || isBlank(region)) return false;
        return ensureState().accounts.some(function (e) {
            return eq(e.email, email) && eq(e.region, region);
        });
    }

    /** AccountBook.Add：重复返回 false */
    function bookAdd(email, region) {
        if (isBlank(email) || isBlank(region)) return false;
        var trimmed = String(email).trim();
        var code = String(region).trim().toUpperCase();
        if (bookContains(trimmed, code)) return false;

        ensureState().accounts.push({ email: trimmed, region: code });
        persist();
        emit('accounts');
        return true;
    }

    /** AccountBook.Remove */
    function bookRemove(email, region) {
        if (isBlank(email) || isBlank(region)) return false;
        var s = ensureState();
        var before = s.accounts.length;
        s.accounts = s.accounts.filter(function (e) {
            return !(eq(e.email, email) && eq(e.region, region));
        });
        if (s.accounts.length === before) return false;
        persist();
        emit('accounts');
        return true;
    }

    /**
     * AccountBook.EnsureInitialized：本地账号本为空时，
     * 把 Battle.net.config 的 SavedAccountNames 全归到当前 SelectedRegion。
     */
    function ensureInitialized() {
        var s = ensureState();
        if (s.accounts.length > 0) return false;
        var names = savedAccountNames();
        if (names.length === 0) return false;

        var region = selectedRegion();
        names.forEach(function (email) { bookAdd(email, region); });
        return true;
    }

    /* ============================================================
     *  六、战网配置（对齐 AccountSwitcher.cs）
     * ============================================================ */

    function clientNode() {
        return ensureState().config.Client;
    }

    /** AccountSwitcher.LoadAccounts：解析 SavedAccountNames */
    function savedAccountNames() {
        var node = clientNode();
        var raw = node && node.SavedAccountNames;
        if (isBlank(raw)) return [];
        return String(raw).split(',')
            .map(function (x) { return x.trim(); })
            .filter(function (x) { return x.length > 0; });
    }

    /** AccountSwitcher.ReadConfigAccountsAndRegion */
    function readConfigAccountsAndRegion() {
        var node = clientNode();
        var region = node && node.LoginSettings && node.LoginSettings.SelectedRegion;
        if (isBlank(region)) region = 'CN';
        return { emails: savedAccountNames(), region: String(region).toUpperCase() };
    }

    /** 当前自动登录账号 = SavedAccountNames 首位 */
    function currentAccount() {
        var names = savedAccountNames();
        return names.length > 0 ? names[0] : null;
    }

    function selectedRegion() {
        return readConfigAccountsAndRegion().region;
    }

    /** AccountSwitcher.TrySetConfigRegion */
    function trySetConfigRegion(code, log) {
        try {
            var node = clientNode();
            if (!node) return;
            if (!node.LoginSettings) node.LoginSettings = {};
            node.LoginSettings.AllowedRegions = 'CN;US;EU;KR;TW';
            node.LoginSettings.AllowedLocales =
                'zhCN;deDE;enGB;enUS;esMX;esES;frFR;itIT;plPL;ptBR;ruRU;koKR;zhTW';
            node.LoginSettings.SelectedRegion = code;
            if (log) log('已写入区域配置: ' + code);
        } catch (e) {
            if (log) log('写入区域配置失败（不影响账号切换）: ' + e.message);
        }
    }

    /** 模拟 KillBattleNet：杀掉进程 + 固定 500ms 等待 */
    function killBattleNet(log) {
        log('已关闭战网进程。');
        return sleep(500);
    }

    /**
     * AccountSwitcher.SwitchAccount 的异步版。
     * 逐步回调 log(line)，让 GUI 能像真实程序一样把日志一行行打出来。
     * @returns {Promise<{ok:boolean, error?:string, log:string[]}>}
     */
    async function switchAccount(email, regionCode, restart, log) {
        var lines = [];
        function L(text) {
            lines.push(text);
            if (log) log(text);
        }

        if (isBlank(email)) throw new Error('目标邮箱不能为空。');

        var accounts = savedAccountNames();
        if (accounts.length === 0) {
            throw new Error('配置中没有保存任何账号。请先在战网客户端中登录一次该账号（勾选“记住密码”）。');
        }
        if (!accounts.some(function (a) { return eq(a, email); })) {
            throw new Error('账号 ' + email + ' 不在已保存列表中。' +
                '请先在战网客户端中登录一次该账号（勾选“记住密码”）。');
        }

        var info = data.fromCode(regionCode);
        var wasCurrent = eq(currentAccount(), email) && eq(selectedRegion(), info.code);

        await killBattleNet(L);

        var again = savedAccountNames();
        if (again.length > 0) accounts = again;
        if (!accounts.some(function (a) { return eq(a, email); })) accounts.unshift(email);

        L('已备份原配置到: ' + backupPath());

        // 目标账号已在首位时不要重写 SavedAccountNames：
        // 这份列表由客户端自己维护，我们每次无脑重排会让注册表里的登录凭证
        // 与配置对不上，反而把本来还有效的会话搞失效。
        var namesChanged = accounts.length === 0 || !eq(accounts[0], email);
        if (namesChanged) {
            accounts = accounts.filter(function (a) { return !eq(a, email); });
            accounts.unshift(email);
            clientNode().SavedAccountNames = accounts.join(',');
            L('已把目标账号置顶到 SavedAccountNames。');
        } else {
            L('目标账号已在 SavedAccountNames 首位，保持不变（避免打断已有会话）。');
        }

        trySetConfigRegion(info.code, L);

        persist();
        emit('accounts');
        emit('config');

        L('已将账号 ' + email + ' 设置为 [' + info.displayName + '] 的默认登录账号。');

        // AccountSwitcher.SwitchAccount 里的 AccountBook.Add
        bookAdd(email, info.code);

        if (restart) {
            var exe = battleNetPath();
            if (exe) {
                var args = '--setregion=' + info.code + ' --setlanguage=' + info.locale;
                await sleep(220);
                L('已以 [' + info.displayName + '] 重新启动战网客户端。');
                L('  路径: ' + exe);
                L('  参数: ' + args);
                emit('client', {
                    action: 'relaunch',
                    region: info.code,
                    email: email,
                    args: args,
                    // 账号确实换了：客户端可以沿用"保持登录状态"直接进已登录
                    accountChanged: !wasCurrent
                });
                L(wasCurrent
                    ? '账号未变，客户端保持登录状态。'
                    : '已切换会话，客户端保持登录状态。');
            } else {
                L('未能找到战网可执行文件。若使用便携版，请在“设置”里指定路径；' +
                    '也可手动打开战网客户端以应用切换。');
            }
        }

        return { ok: true, log: lines };
    }

    /* ------------------------------------------------------------
     *  登录检测（对应真实版的轮询 SavedAccountNames）
     * ------------------------------------------------------------ */

    /** 记下当前已记住的邮箱作为基线，返回基线快照 */
    function seedLoginBaseline() {
        var st = ensureState();
        st.loginBaseline = savedAccountNames();
        persist();
        return st.loginBaseline.slice();
    }

    /** 相对基线新增的邮箱（= 等待期间新登录并勾了"记住密码"的账号） */
    function newSinceBaseline() {
        var st = ensureState();
        var base = st.loginBaseline || [];
        var lower = {};
        base.forEach(function (x) { lower[String(x).toLowerCase()] = true; });
        var out = [];
        savedAccountNames().forEach(function (x) {
            if (!lower[String(x).toLowerCase()] && out.indexOf(x) < 0) out.push(x);
        });
        return out;
    }

    /**
     * 把一个邮箱登记进战网的"记住密码"列表
     * （等价于真实客户端在登录成功时写入 SavedAccountNames）。
     *
     * 关键：真实客户端是把这个账号**移到首位**，而不是追加到末尾。
     * "当前自动登录账号 = SavedAccountNames 首位"是本工具的核心约定
     * （见 AccountSwitcher.SwitchAccount 里的 unshift），所以这里也必须置顶，
     * 否则 currentAccount() 不会指向刚登录的账号，界面就会显示成上一个号。
     */
    function registerSavedAccount(email) {
        if (isBlank(email)) return false;
        var mail = String(email).trim();
        var node = clientNode();
        var names = savedAccountNames();

        var alreadyFirst = names.length > 0 && eq(names[0], mail);
        if (alreadyFirst) return false;

        // 去重后置顶（已在列表里也要移到最前，与客户端一致）
        var rest = names.filter(function (x) { return !eq(x, mail); });
        rest.unshift(mail);
        node.SavedAccountNames = rest.join(',');

        persist();
        emit('accounts');
        emit('config');
        return true;
    }

    /**
     * AccountSwitcher.LaunchForRegion 的异步版：
     * 直接以指定区服启动战网，**不改动** SavedAccountNames / SelectedRegion，
     * 客户端停在登录页，由用户在客户端里登录一个新号。
     *
     * registerOnLogin = 登录成功后把该账号登记进"记住密码"列表，
     *                   好让调用方（添加账号窗口）能轮询检测到。
     */
    async function launchForRegion(regionCode, log) {
        var lines = [];
        function L(text) {
            lines.push(text);
            if (log) log(text);
        }

        var info = data.fromCode(regionCode);
        L('正在以 [' + info.displayName + '] 启动战网客户端（不改变当前默认登录账号）。');

        var exe = battleNetPath();
        if (!exe) {
            var msg = '未能找到战网可执行文件。若使用便携版，请在【设置】里指定战网程序路径。';
            L(msg);
            throw new Error(msg);
        }

        var args = '--setregion=' + info.code + ' --setlanguage=' + info.locale;
        await sleep(320);

        L('已以 [' + info.displayName + '] 启动战网客户端。');
        L('  路径: ' + exe);
        L('  参数: ' + args);
        L('请在客户端里登录该区服的账号（记得勾选"记住密码"）。');

        emit('client', {
            action: 'launch',
            region: info.code,
            args: args,
            registerOnLogin: true
        });

        return { ok: true, log: lines };
    }

    /** AccountSwitcher.GetBattleNetPath：自定义路径优先，否则"注册表"里的默认路径 */
    function battleNetPath() {
        var custom = ensureState().settings.battleNetExePath;
        if (!isBlank(custom)) return custom;
        return data.FAKE_FILES.installDir + '\\Battle.net.exe';
    }

    /** 备份文件路径（= 配置文件路径 + .backup），避免在日志里硬编码用户名 */
    function backupPath() {
        return configPath() + '.backup';
    }

    /** AccountSwitcher.GetConfigPath */
    function configPath() {
        var custom = ensureState().settings.battleNetConfigPath;
        if (!isBlank(custom)) return custom;
        return data.FAKE_FILES.configDir + '\\Battle.net.config';
    }

    /* ============================================================
     *  七、全局设置（对齐 AppSettings.cs 与 SettingsPanel.OnSave）
     * ============================================================ */
    function updateSettings(patch) {
        var s = ensureState();
        for (var k in patch) {
            if (Object.prototype.hasOwnProperty.call(patch, k)) s.settings[k] = patch[k];
        }
        persist();
        emit('settings', patch);
    }

    /* ============================================================
     *  八、一键拔线（对齐 NetworkPanel.cs + FirewallManager.cs）
     * ============================================================ */
    function updateNetwork(patch) {
        var s = ensureState();
        for (var k in patch) {
            if (Object.prototype.hasOwnProperty.call(patch, k)) s.network[k] = patch[k];
        }
        persist();
        emit('network', patch);
    }

    /** NetworkPanel.SetTargetApp：切换目标时清理旧规则 */
    function setTargetApp(path) {
        var s = ensureState();
        if (s.network.ruleName) {
            pushLog('已删除旧防火墙规则: ' + s.network.ruleName);
        }
        var procName = String(path).split('\\').pop().replace(/\.exe$/i, '');
        s.network.appPath = path;
        s.network.procName = procName;
        s.network.ruleName = 'BattleNetTool_' + procName + '_' + randomHex8();
        persist();
        emit('network');
    }

    function randomHex8() {
        var out = '';
        for (var i = 0; i < 8; i++) {
            out += Math.floor(Math.random() * 16).toString(16);
        }
        return out;
    }

    /**
     * FirewallManager.CreateBlockRule 的模拟。
     * 真实实现是 netsh advfirewall firewall add rule ...
     */
    function createBlockRule(ruleName, appPath) {
        if (isBlank(ruleName)) throw new Error('规则名不能为空。');
        if (isBlank(appPath)) throw new Error('应用路径不能为空。');
        if (!ensureState().isAdmin) {
            throw new Error('netsh 执行失败（退出码 1）：请求的操作需要提升。\r\n' +
                '提示：修改防火墙规则需要以【管理员身份】运行本程序。');
        }
        return 'advfirewall firewall add rule name="' + ruleName + '" ' +
            'dir=out action=block program="' + appPath + '" enable=yes profile=any';
    }

    function deleteRule(ruleName) {
        if (isBlank(ruleName)) return null;
        return 'advfirewall firewall delete rule name="' + ruleName + '"';
    }

    function setRuleEnabled(ruleName, enabled) {
        if (isBlank(ruleName)) return null;
        return 'advfirewall firewall set rule name="' + ruleName + '" new enable=' +
            (enabled ? 'yes' : 'no');
    }

    /* ============================================================
     *  九、客户端会话（按账号记录）
     * ============================================================ */
    /**
     * 会话键 = 小写邮箱 + '@' + 区服代码。
     * 同一个邮箱在 US / EU 是两个不同的战网账号，登录状态不能共享。
     */
    function sessionKey(email, region) {
        var mail = String(email || '').trim().toLowerCase();
        if (!mail) return '';
        var code = String(region || selectedRegion() || '').trim().toUpperCase();
        return mail + '@' + code;
    }

    /**
     * 读某个账号在某个区服的登录状态。
     * region 不传时用当前 SelectedRegion。
     */
    function accountState(email, region) {
        var key = sessionKey(email, region);
        var st = ensureState();
        var s = key ? st.clientAccounts[key] : null;
        return {
            loggedIn: !!(s && s.loggedIn),
            keep: !!(s && s.keep)
        };
    }

    /** 该账号在该区服是否处于登录状态 */
    function hasSession(email, region) {
        return accountState(email, region).loggedIn;
    }

    /**
     * 登录成功后记录该账号的状态。
     * keep 就是登录时"保持登录状态"勾选框的值。
     */
    function setSession(email, region, keep) {
        var key = sessionKey(email, region);
        if (!key) return;
        var st = ensureState();
        st.clientAccounts[key] = { loggedIn: true, keep: !!keep };
        st.clientLoggedIn = email;
        st.clientRegion = String(region || selectedRegion() || '').toUpperCase();
        persist();
        emit('client');
    }

    /** 登出：只把这一个「账号 + 区服」置回未登录（其它账号、其它区服都不受影响） */
    function clearSession(email, region) {
        var key = sessionKey(email, region);
        if (!key) return;
        var st = ensureState();
        if (st.clientAccounts[key]) {
            st.clientAccounts[key] = { loggedIn: false, keep: false };
        }
        if (sessionKey(st.clientLoggedIn || '', st.clientRegion) === key) {
            st.clientLoggedIn = null;
        }
        persist();
        emit('client');
    }

    /**
     * 设置"客户端当前所在区服"。
     * 启动战网（--setregion）只影响客户端自身，**不写**配置里的 SelectedRegion，
     * 所以必须单独记录 —— 否则登录会被记到配置的区服上，而不是实际打开的那个。
     */
    function setClientRegion(region) {
        if (isBlank(region)) return;
        var st = ensureState();
        st.clientRegion = String(region).trim().toUpperCase();
        persist();
    }

    function setClientLoggedIn(email, region) {
        var st = ensureState();
        st.clientLoggedIn = email || null;
        if (region) st.clientRegion = String(region).toUpperCase();
        persist();
    }

    /** 客户端当前所在区服（没设过时跟随 SelectedRegion） */
    function clientRegion() {
        var st = ensureState();
        return String(st.clientRegion || selectedRegion() || 'CN').toUpperCase();
    }

    /* ============================================================
     *  十、对外接口
     * ============================================================ */
    BNS.core = {
        // 状态
        state: ensureState,
        persist: persist,
        reset: resetState,
        subscribe: subscribe,
        emit: emit,

        // 工具
        sleep: sleep,
        isBlank: isBlank,
        eq: eq,

        // 账号本
        emailsForRegion: emailsForRegion,
        regionsForEmail: regionsForEmail,
        activeRegions: activeRegions,
        bookContains: bookContains,
        bookAdd: bookAdd,
        bookRemove: bookRemove,
        ensureInitialized: ensureInitialized,

        // 战网配置
        savedAccountNames: savedAccountNames,
        readConfigAccountsAndRegion: readConfigAccountsAndRegion,
        currentAccount: currentAccount,
        selectedRegion: selectedRegion,
        switchAccount: switchAccount,
        launchForRegion: launchForRegion,
        seedLoginBaseline: seedLoginBaseline,
        newSinceBaseline: newSinceBaseline,
        registerSavedAccount: registerSavedAccount,
        battleNetPath: battleNetPath,
        configPath: configPath,
        backupPath: backupPath,

        // 设置 / 拔线
        updateSettings: updateSettings,
        updateNetwork: updateNetwork,
        setTargetApp: setTargetApp,
        createBlockRule: createBlockRule,
        deleteRule: deleteRule,
        setRuleEnabled: setRuleEnabled,
        randomHex8: randomHex8,

        // 客户端会话（按账号）
        accountState: accountState,
        setClientRegion: setClientRegion,
        clientRegion: clientRegion,
        sessionKey: sessionKey,
        hasSession: hasSession,
        setSession: setSession,
        clearSession: clearSession,
        setClientLoggedIn: setClientLoggedIn,

        // 日志
        pushLog: pushLog
    };
})(typeof window !== 'undefined' ? window : this);
