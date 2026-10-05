/* ============================================================
 *  sim-client.js —— 左侧"战网登录页"模拟
 *
 *  按 account.battlenet.com.cn/login 的结构与配色还原（见 client.css 顶部注释），
 *  登录页只保留真实页面最核心的四项：
 *      标志 / 电子邮箱或手机号码 / 密码 / 保持登录状态 / 登录
 *  （官方页面上还有「或 / 网易游戏登录 / 协议勾选 / 无法登录？」等，这里不渲染）
 *
 *  与本项目联动的部分：
 *      * 地区选择 = LoginSettings.SelectedRegion（切换后带 --setregion 重启）
 *      * 工具点"切换"→ 走「正在关闭 → 正在以 --setregion 启动 → 回到登录页，邮箱已填好」
 *      * 点"登录"→ 进入登录成功页
 *
 *  两个页面：
 *      login    —— 登录页
 *      loggedin —— 登录成功页（账号卡片 / 进入游戏 / 最近游玩）
 * ============================================================ */
(function (global) {
    'use strict';

    var BNS = global.BNS || (global.BNS = {});
    var core = BNS.core;
    var data = BNS.data;

    function el(tag, cls, text) {
        var node = document.createElement(tag);
        if (cls) node.className = cls;
        if (text !== undefined && text !== null) node.textContent = text;
        return node;
    }

    function initialOf(email) {
        if (!email) return '?';
        var ch = String(email).trim().charAt(0);
        return ch ? ch.toUpperCase() : '?';
    }

    function hash(s) {
        var h = 0;
        for (var i = 0; i < s.length; i++) {
            h = ((h << 5) - h) + s.charCodeAt(i);
            h |= 0;
        }
        return h;
    }

    /* 标志：真实页面 .logo 是 240×40 的横向图（alt = "战网登录"）。
       这里用几何图形 + 文字拼一个同尺寸的横向锁定，不使用官方素材。 */
    function blizzardMark(size) {
        return '<svg width="' + size + '" height="' + size + '" viewBox="0 0 64 64" aria-hidden="true">' +
            '<defs><linearGradient id="blz" x1="0" y1="0" x2="0" y2="1">' +
            '<stop offset="0" stop-color="#8ed0ff"/><stop offset="1" stop-color="#0d86e8"/>' +
            '</linearGradient></defs>' +
            '<path d="M32 3 L60 32 L32 61 L4 32 Z" fill="none" stroke="url(#blz)" stroke-width="3.4"/>' +
            '<path d="M32 12 L52 32 L32 52 L12 32 Z" fill="none" stroke="url(#blz)" stroke-width="2.2" opacity=".75"/>' +
            '<path d="M32 22 L42 32 L32 42 L22 32 Z" fill="url(#blz)"/>' +
            '</svg>';
    }

    /** 240×40 的横向标志区 */
    function buildBrand() {
        var box = el('div', 'bn-brand');
        var lockup = el('div', 'lockup');
        lockup.innerHTML = blizzardMark(32) +
            '<span class="txt">战网登录</span>';
        box.appendChild(lockup);
        return box;
    }

    /**
     * @param {HTMLElement} host 容器
     * @param {{onChange?: Function}} [options]
     */
    function create(host, options) {
        options = options || {};
        var notify = options.onChange || function () { };

        /** 'login' | 'loggedin' */
        var view = 'loggedin';
        /** 客户端当前"已登录"的邮箱 */
        var loggedInEmail = null;
        /**
         * 客户端当前所在区服。
         * 登录状态按「账号 + 区服」区分：同一个邮箱在国服与美服是两个独立账号，
         * 在国服登录了不代表美服也登录。
         */
        var currentRegion = core.clientRegion();
        /** 本次空白登录是否要把登录成功的账号登记进"记住密码"列表 */
        var pendingRegister = false;
        /** 覆盖层：null | {msg, sub} */
        var overlay = null;
        /** 登录页里正在输入的邮箱 */
        var draftEmail = '';
        var remember = true;
        var showPassword = false;
        /** 密码不是演示重点，直接给一个默认值 */
        var DEFAULT_PASSWORD = '12345678';
        var password = DEFAULT_PASSWORD;
        var busy = false;
        var timers = [];

        function clearTimers() {
            timers.forEach(function (t) { clearTimeout(t); });
            timers = [];
        }

        function later(fn, ms) {
            var id = setTimeout(fn, ms);
            timers.push(id);
            return id;
        }

        /* ------------------------------------------------------------
         *  外部行为
         * ------------------------------------------------------------ */

        /**
         * 工具点"切换"后：客户端被关闭并以新区服重新启动。
         *
         * keepSession    = 被切到的这个账号已有登录状态 → 直接进已登录页
         * blankLogin     = 由"添加账号 → 启动战网等待登录"发起：不预填邮箱，
         *                  停在空白登录页让用户登录一个新号
         * registerOnLogin = 该次登录成功后把账号登记进"记住密码"列表
         *                  （真实客户端登录时就是这么做的），供调用方检测
         */
        function relaunch(regionCode, email, args, keepSession, blankLogin, registerOnLogin) {
            clearTimers();
            var info = data.fromCode(regionCode);
            busy = true;
            overlay = {
                msg: '正在关闭战网客户端…',
                sub: 'KillBattleNet() → 已终止 Battle.net.exe (PID 7312)'
            };
            render();

            // 客户端已经按目标区服启动了，把"当前区服"落进状态。
            // 注意：启动战网**不**改配置里的 SelectedRegion，所以不能只靠内存变量
            // —— render() 会读配置并把它覆盖掉，导致登录被记到错误的区服上。
            core.setClientRegion(info.code);

            later(function () {
                overlay = {
                    msg: blankLogin
                        ? '正在以 [' + info.displayName + '] 打开登录页…'
                        : (keepSession
                            ? '正在以 [' + info.displayName + '] 重启并恢复会话…'
                            : '正在以 [' + info.displayName + '] 重新启动…'),
                    sub: 'Battle.net.exe ' + args
                };
                render();
            }, 700);

            later(function () {
                overlay = null;
                busy = false;
                draftEmail = blankLogin ? '' : (email || '');
                pendingRegister = !!blankLogin && !!registerOnLogin;
                password = DEFAULT_PASSWORD;
                showPassword = false;
                remember = true;

                if (keepSession) {
                    // 这个「账号 + 区服」已有登录状态：直接落到已登录页，不需要再输密码
                    view = 'loggedin';
                    loggedInEmail = core.currentAccount() || email || '';
                    core.setClientLoggedIn(loggedInEmail, info.code);
                    currentRegion = info.code;
                    render();
                    notify('该账号已保持登录状态，直接切换到 ' + loggedInEmail);
                } else {
                    view = 'login';
                    loggedInEmail = null;
                    currentRegion = info.code;
                    render();
                    notify(blankLogin
                        ? '已打开 [' + info.displayName + '] 的登录页，登录后会自动识别新账号'
                        : '客户端已回到登录页（地区：' + info.displayName + '）');
                }
            }, 1700);
        }

        /** 登出：只作废「当前账号 + 当前区服」，其它账号/其它区服都不受影响 */
        function logout() {
            clearTimers();
            var who = loggedInEmail || core.currentAccount();
            if (who) core.clearSession(who, currentRegion);
            view = 'login';
            overlay = null;
            busy = false;
            loggedInEmail = null;
            draftEmail = who || core.currentAccount() || '';
            password = DEFAULT_PASSWORD;
            render();
            notify('已登出 ' + (who || '') + '（这个账号下次切换需要重新登录）');
        }

        function login() {
            if (busy) return;
            var email = (draftEmail || '').trim();
            if (!email) {
                notify('请输入电子邮箱或手机号码', 'warn');
                return;
            }
            busy = true;
            overlay = { msg: '正在登录…', sub: 'POST /oauth/authorize → ' + email };
            render();

            later(function () {
                overlay = null;
                busy = false;
                view = 'loggedin';

                // 顺序很重要：以下两个核心调用都会 emit('accounts')/emit('config')，
                // 从而同步触发订阅里的 syncFromState()，而它会把 loggedInEmail
                // 重置为 core.currentAccount()。所以必须在它们**之后**再落
                // loggedInEmail，否则刚登录的账号会被上一个号覆盖掉。
                //
                // 注意：无论有没有勾"保持登录状态"，这次登录都已经发生了，
                // 会话必须记下来；勾选框只决定这个会话**是否保持**。
                // 原来的写法在没勾时直接 clearSession，等于"登录了但没登录"，是错的。
                core.setSession(email, currentRegion, remember);

                // 来自"添加账号 → 等待登录"的会话：把账号登记进"记住密码"列表，
                // 好让添加账号窗口轮询到它（真实客户端也是登录时写入并置顶）
                if (pendingRegister && remember) {
                    core.registerSavedAccount(email);
                    pendingRegister = false;
                }

                loggedInEmail = email;

                render();
                notify(remember
                    ? '已登录并保持登录状态：' + email
                    : '已登录：' + email + '（未勾选"保持登录状态"，切换后需重新登录）');
            }, 950);
        }

        function setView(next) {
            view = next;
            render();
        }

        /* ------------------------------------------------------------
         *  渲染
         * ------------------------------------------------------------ */
        function render() {
            var cfg = core.readConfigAccountsAndRegion();
            // 纯渲染，不要在这里覆盖"客户端当前区服"：
            // 它由 relaunch() 在启动客户端时写入，切换区服靠它区分登录状态
            currentRegion = core.clientRegion();
            var info = data.fromCode(currentRegion);
            var saved = cfg.emails;
            var current = core.currentAccount();

            host.innerHTML = '';
            var page = el('div', 'bn-page');

            page.appendChild(buildBrand());

            var card = el('div', 'bn-card');
            if (view === 'login') {
                card.appendChild(buildLogin(info, saved));
            } else {
                card.appendChild(buildWelcome(loggedInEmail || current, info));
            }
            if (overlay) {
                var ov = el('div', 'bn-overlay');
                ov.appendChild(el('div', 'spinner'));
                ov.appendChild(el('div', 'msg', overlay.msg));
                if (overlay.sub) ov.appendChild(el('div', 'sub', overlay.sub));
                card.appendChild(ov);
            }
            page.appendChild(card);

            host.appendChild(page);
            if (BNS.ui && BNS.ui.notifyLayout) BNS.ui.notifyLayout();
        }

        function escapeHtml(s) {
            return String(s).replace(/[&<>"']/g, function (c) {
                return ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c];
            });
        }

        /* ---- 登录页 ---- */
        function buildLogin(info, saved) {
            var wrap = el('div');
            wrap.style.cssText = 'display:flex;flex-direction:column;flex:1;min-height:0';

            /* 电子邮箱或手机号码 */
            var fMail = el('div', 'bn-field');
            var mail = document.createElement('input');
            mail.type = 'text';
            mail.autocomplete = 'username';
            mail.value = draftEmail;
            mail.disabled = busy;
            mail.placeholder = '电子邮箱或手机号码';
            mail.oninput = function () { draftEmail = mail.value; };
            mail.onkeydown = function (e) { if (e.key === 'Enter') login(); };
            fMail.appendChild(mail);
            wrap.appendChild(fMail);

            /* 密码 */
            var fPwd = el('div', 'bn-field');
            var pwdWrap = el('div', 'bn-pwd-wrap');
            var pwd = document.createElement('input');
            pwd.type = showPassword ? 'text' : 'password';
            pwd.autocomplete = 'current-password';
            pwd.value = password;
            pwd.disabled = busy;
            pwd.placeholder = '请输入密码';   // 有默认值时不会显示
            pwd.oninput = function () { password = pwd.value; };
            pwd.onkeydown = function (e) { if (e.key === 'Enter') login(); };
            pwdWrap.appendChild(pwd);

            var toggle = el('button', 'bn-pwd-toggle', showPassword ? '🙈' : '👁');
            toggle.type = 'button';
            toggle.title = showPassword ? '隐藏密码' : '显示密码';
            toggle.disabled = busy;
            toggle.onclick = function (e) {
                e.stopPropagation();
                showPassword = !showPassword;
                render();
                var box = host.querySelector('.bn-pwd-wrap input');
                if (box) { box.focus(); }
            };
            pwdWrap.appendChild(toggle);
            fPwd.appendChild(pwdWrap);
            wrap.appendChild(fPwd);

            /* 保持登录状态 */
            var keep = el('label', 'bn-check');
            var cb = document.createElement('input');
            cb.type = 'checkbox';
            cb.checked = remember;
            cb.onchange = function () { remember = cb.checked; };
            keep.appendChild(cb);
            keep.appendChild(el('span', 'box'));
            var keepBody = el('span');
            keepBody.appendChild(el('span', 'label', '保持登录状态'));
            keepBody.appendChild(el('div', 'note',
                '您将在战网启动时登录，登出或在其他设备上登录前都将保持登录状态。'));
            keep.appendChild(keepBody);
            wrap.appendChild(keep);

            /* 登录 */
            var submit = el('button', 'bn-submit', '登录');
            submit.type = 'button';
            submit.disabled = busy;
            submit.onclick = login;
            wrap.appendChild(submit);

            // 真实页面在按钮下面还有「或 / 网易游戏登录 / 无法登录？」等，
            // 为了保持"只有邮箱 + 密码 + 保持登录状态 + 登录"的干净形态，这里不渲染。

            return wrap;
        }

        /* ---- 登录成功页：只显示账号信息 + 登出 ---- */
        function buildWelcome(email, info) {
            var wrap = el('div', 'bn-welcome');
            var name = email || '（未登录）';
            // 该账号登录时是否勾了"保持登录状态"（决定下面显示哪个标签）
            var kept = core.accountState(name, currentRegion).keep;

            var card = el('div', 'bn-account-card');
            var avatar = el('div', 'bn-avatar', initialOf(name));
            avatar.appendChild(el('div', 'on'));
            card.appendChild(avatar);

            var meta = el('div', 'meta');
            meta.appendChild(el('div', 'name', name));
            meta.appendChild(el('div', 'line',
                'BattleTag 模拟 · #' + (Math.abs(hash(name)) % 9000 + 1000)));
            var tags = el('div', 'tags');
            tags.appendChild(el('span', 'bn-tag blue', info.displayName));
            tags.appendChild(kept
                ? el('span', 'bn-tag ok', '保持登录状态')
                : el('span', 'bn-tag', '未保持登录状态'));
            meta.appendChild(tags);
            card.appendChild(meta);
            wrap.appendChild(card);

            var out = el('button', 'bn-logout', '登出');
            out.type = 'button';
            out.onclick = logout;
            wrap.appendChild(out);

            return wrap;
        }

        /* ------------------------------------------------------------
         *  状态变化 / 交互
         * ------------------------------------------------------------ */
        function syncFromState() {
            if (overlay) return;
            if (view === 'loggedin') {
                loggedInEmail = core.currentAccount();
                render();
                return;
            }
            if (!draftEmail) draftEmail = core.currentAccount() || '';
            render();
        }

        var unsubscribe = core.subscribe(function (topic, payload) {
            if (topic === 'client' && payload && payload.action === 'relaunch') {
                // 被切到的账号若已有登录状态 → 直接进已登录；否则回登录页
                relaunch(payload.region, payload.email, payload.args,
                    core.hasSession(payload.email, payload.region));
                return;
            }
            if (topic === 'client' && payload && payload.action === 'launch') {
                // 添加账号发起的空白登录：不预填邮箱，登录成功后登记账号
                relaunch(payload.region, '', payload.args, false, true,
                    payload.registerOnLogin);
                return;
            }
            if (topic === 'accounts' || topic === 'config' || topic === 'reset' || topic === 'settings') {
                if (topic === 'reset') {
                    view = 'loggedin';
                    overlay = null;
                        draftEmail = '';
                    loggedInEmail = core.currentAccount();
                }
                syncFromState();
            }
        });

        // 初始状态
        draftEmail = core.currentAccount() || '';
        loggedInEmail = core.currentAccount();
        view = core.hasSession(loggedInEmail, currentRegion) ? 'loggedin' : 'login';
        render();

        return {
            render: render,
            relaunch: relaunch,
            // 调试出口：便于自动化验证读取内部状态（不影响正常功能）
            __dbg: function () {
                return {
                    view: view, busy: busy, overlay: overlay ? overlay.msg : null,
                    loggedInEmail: loggedInEmail, currentRegion: currentRegion,
                    pendingTimers: timers.length
                };
            },
            logout: logout,
            login: login,
            setView: setView,
            destroy: function () {
                clearTimers();
                unsubscribe();
            },
            get view() { return view; }
        };
    }

    BNS.client = { create: create };
})(typeof window !== 'undefined' ? window : this);
