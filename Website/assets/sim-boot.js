/* ============================================================
 *  sim-boot.js —— 按页面把模拟器挂起来
 *
 *  左边战网客户端模拟与右边工具窗口共用同一份会话状态（sessionStorage），
 *  所以工具里切换账号后，左侧客户端会立刻跟着变。
 * ============================================================ */
(function (global) {
    'use strict';

    var BNS = global.BNS || (global.BNS = {});
    var core = BNS.core;
    var ui = BNS.ui;
    var app = BNS.app;

    function $(sel) { return document.querySelector(sel); }

    function els(sel) { return Array.prototype.slice.call(document.querySelectorAll(sel)); }

    /* ============================================================
     *  GUI 页面
     * ============================================================ */
    function bootGui() {
        var host = $('#gui-host');
        var holder = $('#gui-stage');
        if (!host || !holder) return;

        var clientHost = $('#client-host');
        var clientHolder = $('#client-stage');

        function guiInner() { return host.firstElementChild; }
        function clientInner() {
            return clientHost ? clientHost.firstElementChild : null;
        }

        // 先建客户端，这样切换账号时能立刻联动
        var client = clientHost
            ? BNS.client.create(clientHost, {
                onChange: function (text) { ui.toast(text, 'ok'); }
            })
            : null;

        // fitGui 先声明（render 内部会回调 options.fit），稍后赋值
        var fitGui = function () { };

        if (global.BNS) BNS.__client = client;   // 调试出口（自动化验证用）

        var gui = BNS.gui.render(host, { client: client }, {
            version: app.version,
            releasesUrl: app.releasesUrl,
            // 站内点击版本号时只弹提示，避免在演示页里跳走
            simulateLinks: true,
            fit: function () { if (fitGui.schedule) fitGui.schedule(); },
            onNotify: function (text, kind) { ui.toast(text, kind); }
        });

        fitGui = ui.fitStage(guiInner, holder, 1);
        var fitClient = clientHost
            ? ui.fitStage(clientInner, clientHolder, 1)
            : function () { };

        // 工具窗口重绘 → 重新测量
        ui.onLayout(function () { if (fitGui.schedule) fitGui.schedule(); });
        fitGui();

        // 客户端重绘 → 重新测量（登录页 / 登录成功页的高度差很大）
        if (client) {
            ui.onLayout(function () { if (fitClient.schedule) fitClient.schedule(); });
            fitClient();
        }

        /* ---------------- 兜底：页面上如果放了重置按钮 ---------------- */
        var resetBtn = document.querySelector('[data-ctl="reset"]');
        if (resetBtn) {
            resetBtn.onclick = function () {
                core.reset();
                ui.toast('已重置演示数据', 'warn');
                gui.render();
            };
        }

        // 首次进入走一遍启动流程（默认已是管理员，不弹 UAC）
        setTimeout(function () { gui.boot(); }, 120);
    }

    /* ============================================================
     *  CLI 页面
     * ============================================================ */

    /* ============================================================
     *  启动
     * ============================================================ */
    ui.ready(function () {
        var page = document.body.getAttribute('data-page');
        try {
            if (page === 'gui') bootGui();
        } catch (err) {
            if (global.console) console.error('[BattleNetSwitcher 官网] 模拟器初始化失败：', err);
            var host = $('#gui-host');
            if (host) {
                host.innerHTML = '<div class="notice warn">模拟器初始化失败：' +
                    String(err && err.message ? err.message : err) + '</div>';
            }
        }
    });
})(typeof window !== 'undefined' ? window : this);
