/* ============================================================
 *  sim-core.js —— 模拟器"内核"
 *
 *  这一层是纯逻辑 + 会话状态，逐条对齐真实实现：
 *      BattleNetSwitcher.Core/AccountBook.cs        账号本（邮箱 ↔ 区服）
 *      BattleNetSwitcher.Core/AccountSwitcher.cs    战网配置读写 / 切换账号
 *      BattleNetSwitcher.Core/AppSettings.cs        全局设置
 *      BattleNetSwitcher.Core/SnapshotManager.cs    本地状态快照（v1.1.0 新增）
 *      BattleNetSwitcher.Core/FirewallManager.cs    防火墙规则（模拟）
 *
 *  与真实版的两点差异（都是有意为之）：
 *      1. 所有状态只放在 sessionStorage（关掉标签页即丢失），符合"只会话有效"；
 *         file:// 下浏览器可能禁用 storage，此时自动回落到内存对象。
 *      2. 副作用（杀进程、sleep、netsh、静音、快照复制）用 setTimeout 模拟，
 *         不产生任何真实影响。
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
             */
            clientAccounts: {},
            /** 客户端当前显示的账号（已登录时为邮箱，未登录为 null） */
            clientLoggedIn: null,
            /** 客户端当前所在的区服（登录状态按它区分） */
            clientRegion: null,
            /**
             * 本地状态快照（SnapshotManager 的等价物）。
             * key = 小写邮箱 + "__" + 大写区服（对应 SnapshotPaths.ForEmail）
             * value = { email, region, createdAt, updatedAt, fileCount, uniqueIdCount }
             */
            snapshots: data.buildSeedSnapshots(),
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
        if (!state.snapshots) state.snapshots = {};
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

    /** 会话键 = 小写邮箱 + '@' + 区服代码 */
    function sessionKey(email, region) {
        var mail = String(email || '').trim().toLowerCase();
        if (!mail) return '';
        var code = String(region || selectedRegion() || '').trim().toUpperCase();
        return mail + '@' + code;
    }

    /** 快照键 = 小写邮箱 + '__' + 大写区服（对应 SnapshotPaths.ForEmail） */
    function snapshotKeyOf(email, region) {
        var mail = String(email || '').trim().toLowerCase();
        if (!mail) return '';
        var code = String(region || 'CN').trim().toUpperCase();
        return mail + '__' + code;
    }

    /* ============================================================
     *  五、账号本（对齐 AccountBook.cs）
     * ============================================================ */
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

    function bookContains(email, region) {
        if (isBlank(email) || isBlank(region)) return false;
        return ensureState().accounts.some(function (e) {
            return eq(e.email, email) && eq(e.region, region);
        });
    }

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

    function savedAccountNames() {
        var node = clientNode();
        var raw = node && node.SavedAccountNames;
        if (isBlank(raw)) return [];
        return String(raw).split(',')
            .map(function (x) { return x.trim(); })
            .filter(function (x) { return x.length > 0; });
    }

    function readConfigAccountsAndRegion() {
        var node = clientNode();
        var region = node && node.LoginSettings && node.LoginSettings.SelectedRegion;
        if (isBlank(region)) region = 'CN';
        return { emails: savedAccountNames(), region: String(region).toUpperCase() };
    }

    function currentAccount() {
        var names = savedAccountNames();
        return names.length > 0 ? names[0] : null;
    }

    function selectedRegion() {
        return readConfigAccountsAndRegion().region;
    }

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

    /* ============================================================
     *  七、本地状态快照（对齐 SnapshotManager.cs，v1.1.0 新增）
     *
     *  与真实实现保持一致的几件事：
     *    * 快照以 (邮箱, 区服) 为键，同一邮箱在多个区服各存一份，互不覆盖
     *    * 保存前先关战网（真实版是 CloseBattleNet，模拟版只 sleep）
     *    * 切换账号时先为"源账号"更新快照，再恢复"目标账号"的快照
     *    * 删除账号时清空该邮箱所有区服的快照（对应 SnapshotManager.RemoveAll）
     * ============================================================ */

    /** SnapshotManager.Exists(email, region) */
    function snapshotExists(email, region) {
        var k = snapshotKeyOf(email, region);
        if (!k) return false;
        return !!ensureState().snapshots[k];
    }

    /** SnapshotManager.GetInfo(email, region) */
    function snapshotGetInfo(email, region) {
        var k = snapshotKeyOf(email, region);
        if (!k) return null;
        var s = ensureState().snapshots[k];
        if (!s) return null;
        // 返回副本，避免外部误改
        return {
            email: s.email,
            region: s.region,
            createdAt: s.createdAt,
            updatedAt: s.updatedAt,
            fileCount: s.fileCount,
            uniqueIdCount: s.uniqueIdCount
        };
    }

    /** SnapshotManager.List()：跨邮箱、跨区服 */
    function snapshotList() {
        var st = ensureState();
        var out = [];
        Object.keys(st.snapshots).sort().forEach(function (k) {
            var s = st.snapshots[k];
            if (!s) return;
            out.push({
                email: s.email,
                region: s.region,
                createdAt: s.createdAt,
                updatedAt: s.updatedAt,
                fileCount: s.fileCount,
                uniqueIdCount: s.uniqueIdCount
            });
        });
        return out;
    }

    /** 列出某个邮箱在所有区服的快照 */
    function snapshotListForEmail(email) {
        if (isBlank(email)) return [];
        var mail = String(email).toLowerCase();
        return snapshotList().filter(function (s) {
            return String(s.email).toLowerCase() === mail;
        });
    }

    /**
     * SnapshotManager.Save(email, region)：保存/更新快照。
     * 异步：模拟关战网 → 复制文件 → 导出注册表 → 原子替换。
     */
    async function snapshotSave(email, region, log) {
        var lines = [];
        function L(text) {
            lines.push(text);
            if (log) log(text);
        }

        if (isBlank(email)) throw new Error('邮箱不能为空。');
        var mail = String(email).trim();
        var code = String(region || 'CN').trim().toUpperCase();

        // 真实版在 SaveSnapshot 里先 CloseBattleNet（除非调用方已经关过）
        L('已请求战网正常退出，等待它自行收尾…');
        await sleep(180);
        L('战网已正常退出。');

        L('已复制文件: Battle.net.config');
        L('已导出 UnifiedAuth，共 1 个子键。');

        var st = ensureState();
        var k = snapshotKeyOf(mail, code);
        var now = new Date().toISOString();
        var prev = st.snapshots[k];

        st.snapshots[k] = {
            email: mail,
            region: code,
            createdAt: prev ? prev.createdAt : now,
            updatedAt: now,
            fileCount: 1,
            uniqueIdCount: 1
        };
        persist();
        emit('snapshots');

        L('快照保存完成：' + mail + ' [' + code + ']，1 个文件，1 个 UnifiedAuth 条目。');
        return { ok: true, info: snapshotGetInfo(mail, code), log: lines };
    }

    /**
     * SnapshotManager.Restore(email, region)：把快照覆盖回战网本地位置。
     * 未找到快照时返回 ok:false，不抛异常（与真实版 TryRestoreSnapshot 一致）。
     */
    async function snapshotRestore(email, region, log) {
        var lines = [];
        function L(text) {
            lines.push(text);
            if (log) log(text);
        }

        var mail = String(email || '').trim();
        var code = String(region || 'CN').trim().toUpperCase();

        if (!snapshotExists(mail, code)) {
            L('未找到 ' + mail + ' [' + code + '] 的本地快照，跳过恢复。');
            return { ok: false, log: lines };
        }

        L('已恢复文件: Battle.net.config');
        L('已恢复 UnifiedAuth。');
        await sleep(140);
        L('已恢复 ' + mail + ' [' + code + '] 的本地快照。');
        return { ok: true, log: lines };
    }

    /** SnapshotManager.Remove(email, region)：删除某个区服的快照 */
    function snapshotRemove(email, region) {
        var k = snapshotKeyOf(email, region);
        if (!k) return false;
        var st = ensureState();
        if (!st.snapshots[k]) return false;
        delete st.snapshots[k];
        persist();
        emit('snapshots');
        return true;
    }

    /** SnapshotManager.RemoveAll(email)：删除该邮箱所有区服的快照 */
    function snapshotRemoveAll(email) {
        var mail = String(email || '').trim().toLowerCase();
        if (!mail) return 0;
        var st = ensureState();
        var n = 0;
        Object.keys(st.snapshots).forEach(function (k) {
            var s = st.snapshots[k];
            if (s && String(s.email).toLowerCase() === mail) {
                delete st.snapshots[k];
                n++;
            }
        });
        if (n > 0) { persist(); emit('snapshots'); }
        return n;
    }

    /* ============================================================
     *  八、切换账号（对齐 AccountSwitcher.SwitchAccount，v1.1.0）
     *
     *  执行顺序与真实实现完全一致：
     *    1. 关闭战网（优雅退出）
     *    2. 读 (currentEmail, currentRegion)：
     *       若与目标 (email, region) 不完全相同 → 自动更新 current 的快照
     *    3. 恢复目标 (email, region) 的快照（若存在）
     *    4. 重写 Battle.net.config（SavedAccountNames / SelectedRegion）
     *    5. 以 --setregion 启动战网
     * ============================================================ */
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
        var s = ensureState();

        // 关战网之前先记下"当前登录的账号 + 区服"
        var currentAcc = currentAccount();
        var currentReg = selectedRegion();

        var sameTarget = !isBlank(currentAcc) &&
                         eq(currentAcc, email) &&
                         eq(currentReg, info.code);

        // ---- 1. 关闭战网 ----
        await killBattleNet(L);

        // ---- 2. 自动为"源账号"更新快照 ----
        var snapEnabled = s.settings.useSnapshotOnSwitch !== false;
        var autoSave = s.settings.autoSaveSnapshotOnSwitch !== false;

        if (snapEnabled && autoSave) {
            if (!isBlank(currentAcc) && !sameTarget) {
                L('检测到切换前登录的账号 ' + currentAcc + ' [' + currentReg + ']，' +
                  '正在自动更新其快照…');
                try {
                    await snapshotSave(currentAcc, currentReg, L);
                } catch (e) {
                    L('自动更新当前账号快照失败（不影响切换）：' + (e && e.message ? e.message : e));
                }
            } else if (sameTarget) {
                L('当前登录账号与目标一致（' + currentAcc + ' [' + currentReg + ']），无需更新快照。');
            }
        }

        // ---- 3. 恢复目标账号快照 ----
        var snapshotRestored = false;
        if (snapEnabled && !sameTarget) {
            var r = await snapshotRestore(email, info.code, L);
            snapshotRestored = r.ok;
        }

        // 快照恢复会覆盖 Battle.net.config，需要重新读一次
        if (snapshotRestored) {
            var again = savedAccountNames();
            if (again.length > 0) accounts = again;
            if (!accounts.some(function (a) { return eq(a, email); })) accounts.unshift(email);
        }

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
                    accountChanged: !sameTarget
                });
            } else {
                L('未能找到战网可执行文件。若使用便携版，请在“设置”里指定路径；' +
                    '也可手动打开战网客户端以应用切换。');
            }
        }

        // 收尾
        if (sameTarget) {
            L('当前账号与目标一致，已跳过快照操作。');
        } else if (snapshotRestored) {
            L('已从本地快照恢复登录状态，客户端应会直接向服务端续期，无需浏览器验证。');
            L('若仍被要求重新登录：说明该快照的令牌已被服务端作废，' +
              '请在客户端里重新登录一次并让工具重新保存快照。');
        } else {
            L('未找到该账号在该区服的快照，走常规流程。');
            L('切换完成后，可在客户端里登录一次并点“更新当前快照”保存一份。');
        }

        return { ok: true, log: lines };
    }

    /* ------------------------------------------------------------
     *  登录检测（对应真实版的轮询 SavedAccountNames）
     * ------------------------------------------------------------ */

    function seedLoginBaseline() {
        var st = ensureState();
        st.loginBaseline = savedAccountNames();
        persist();
        return st.loginBaseline.slice();
    }

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

    function registerSavedAccount(email) {
        if (isBlank(email)) return false;
        var mail = String(email).trim();
        var node = clientNode();
        var names = savedAccountNames();

        var alreadyFirst = names.length > 0 && eq(names[0], mail);
        if (alreadyFirst) return false;

        var rest = names.filter(function (x) { return !eq(x, mail); });
        rest.unshift(mail);
        node.SavedAccountNames = rest.join(',');

        persist();
        emit('accounts');
        emit('config');
        return true;
    }

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

    function battleNetPath() {
        var custom = ensureState().settings.battleNetExePath;
        if (!isBlank(custom)) return custom;
        return data.FAKE_FILES.installDir + '\\Battle.net.exe';
    }

    function backupPath() {
        return configPath() + '.backup';
    }

    function configPath() {
        var custom = ensureState().settings.battleNetConfigPath;
        if (!isBlank(custom)) return custom;
        return data.FAKE_FILES.configDir + '\\Battle.net.config';
    }

    /** 快照根目录（对应 SnapshotPaths.RootDir） */
    function snapshotRootPath() {
        var custom = ensureState().settings.snapshotRootPath;
        if (!isBlank(custom)) return custom;
        return data.FAKE_FILES.snapshotDir;
    }

    /* ============================================================
     *  九、全局设置（对齐 AppSettings.cs 与 SettingsPanel.OnSave）
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
     *  十、一键拔线（对齐 NetworkPanel.cs + FirewallManager.cs）
     * ============================================================ */
    function updateNetwork(patch) {
        var s = ensureState();
        for (var k in patch) {
            if (Object.prototype.hasOwnProperty.call(patch, k)) s.network[k] = patch[k];
        }
        persist();
        emit('network', patch);
    }

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
     *  十一、客户端会话（按账号 + 区服记录）
     * ============================================================ */
    function accountState(email, region) {
        var key = sessionKey(email, region);
        var st = ensureState();
        var s = key ? st.clientAccounts[key] : null;
        return {
            loggedIn: !!(s && s.loggedIn),
            keep: !!(s && s.keep)
        };
    }

    function hasSession(email, region) {
        return accountState(email, region).loggedIn;
    }

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

    function clientRegion() {
        var st = ensureState();
        return String(st.clientRegion || selectedRegion() || 'CN').toUpperCase();
    }

    /* ============================================================
     *  十二、对外接口
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
        snapshotRootPath: snapshotRootPath,

        // 本地状态快照
        snapshotExists: snapshotExists,
        snapshotGetInfo: snapshotGetInfo,
        snapshotList: snapshotList,
        snapshotListForEmail: snapshotListForEmail,
        snapshotSave: snapshotSave,
        snapshotRestore: snapshotRestore,
        snapshotRemove: snapshotRemove,
        snapshotRemoveAll: snapshotRemoveAll,
        snapshotKeyOf: snapshotKeyOf,

        // 设置 / 拔线
        updateSettings: updateSettings,
        updateNetwork: updateNetwork,
        setTargetApp: setTargetApp,
        createBlockRule: createBlockRule,
        deleteRule: deleteRule,
        setRuleEnabled: setRuleEnabled,
        randomHex8: randomHex8,

        // 客户端会话
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
