/* ============================================================
 *  app.js —— 站点公共脚本
 *  导航高亮、版本号注入、等比缩放舞台、Toast、模拟控制条
 * ============================================================ */
(function (global) {
    'use strict';

    var BNS = global.BNS || (global.BNS = {});

    /* ------------------------------------------------------------
     *  元信息（发布到 Pages 时会用仓库里的真实值替换占位符）
     * ------------------------------------------------------------ */
    function meta(name, fallback) {
        var el = document.querySelector('meta[name="' + name + '"]');
        var v = el ? el.getAttribute('content') : '';
        if (!v || v.indexOf('__') === 0) return fallback;
        return v;
    }

    var version = meta('bns-version', '1.0.0');
    var repo = meta('bns-repo', 'https://github.com/DefeatMan/BattleNetSwitcher');
    var releasesUrl = repo.replace(/\/+$/, '') + '/releases';

    BNS.app = {
        version: version,
        repo: repo,
        releasesUrl: releasesUrl
    };

    /** 把页面里所有 data-bns-version / data-bns-releases 填充上真实值 */
    function injectMeta() {
        var vs = document.querySelectorAll('[data-bns-version]');
        for (var i = 0; i < vs.length; i++) vs[i].textContent = 'v' + version;

        var links = document.querySelectorAll('[data-bns-releases]');
        for (var j = 0; j < links.length; j++) {
            links[j].setAttribute('href', releasesUrl);
        }

        var repos = document.querySelectorAll('[data-bns-repo]');
        for (var k = 0; k < repos.length; k++) {
            repos[k].setAttribute('href', repo);
        }
    }

    /* ------------------------------------------------------------
     *  等比缩放：把固定宽度的模拟窗口塞进任意容器
     *  inner 可以传元素，也可以传"返回元素的函数"（重绘后元素会被替换）
     *
     *  每次测量前必须先把 scale 归零：否则这次拿到的 offsetWidth 是
     *  "上一次缩放之后"的宽度，新渲染出的更高内容就会按旧尺寸算高度，
     *  被 overflow:hidden 直接裁掉（登录页面比登录成功页高 200px 就会中招）。
     * ------------------------------------------------------------ */
    function fitStage(innerOrGetter, holder, maxScale) {
        if (!innerOrGetter || !holder) return function () { };
        maxScale = maxScale || 1;

        var getter = (typeof innerOrGetter === 'function')
            ? innerOrGetter
            : function () { return innerOrGetter; };

        var EVENT = 'bns:stagefit';
        var measuring = false;

        function apply() {
            var inner = getter();
            if (!inner) return;

            var avail = holder.clientWidth;
            if (avail <= 0) return;

            // 只量不改：
            // 绝不能为了测量先把 transform 置 none / 把 holder 高度置 auto ——
            // 那会让文档高度"瞬间撑高又缩回"，Chromium 的文档滚动范围就此算错，
            // 表现为整页滚不动（滚轮/PageDown/scrollTo 全部无效）。
            // 这里用当前已渲染状态的几何量反推"自然尺寸"，
            // 并且只在算出的数值真的变化时才写回样式。
            var scale = parseFloat(holder.style.getPropertyValue('--scale')) ||
                        parseFloat(inner.style.getPropertyValue('--scale')) || 1;
            if (!isFinite(scale) || scale <= 0) scale = 1;

            var naturalW = inner.offsetWidth / scale;
            var naturalH = inner.offsetHeight / scale;
            if (!naturalW || !naturalH) return;

            var nextScale = Math.min(maxScale, avail / naturalW);
            var nextHeight = Math.ceil(naturalH * nextScale);

            if (Math.abs(nextScale - scale) > 0.0005) {
                inner.style.setProperty('--scale', String(nextScale));
                inner.style.setProperty('--w', naturalW + 'px');
            }
            if (holder.style.height !== nextHeight + 'px') {
                holder.style.height = nextHeight + 'px';
            }
        }

        /**
         * 下一帧再测一次，并在尺寸稳定后再补一次。
         * 窗口拖动缩放时，resize 事件可能在布局尚未落定时触发，
         * 测到的自然宽度是中间值，算出来的高度就会短一截（内容被裁）。
         * 尾部防抖可以兜住这种情况。
         */
        var settleTimer = null;
        function schedule() {
            if (global.requestAnimationFrame) {
                global.requestAnimationFrame(apply);
            } else {
                setTimeout(apply, 16);
            }
            if (settleTimer) clearTimeout(settleTimer);
            settleTimer = setTimeout(apply, 140);
        }

        /**
         * 内容主体 = 被缩放元素的第一个子元素（.win / .bn-page）。
         * 它不跟着缩放变尺寸，所以可以安全地交给 ResizeObserver 观察。
         */
        function contentOf(inner) {
            if (!inner) return null;
            return inner.firstElementChild || inner;
        }

        apply();

        // 监听主体内容的尺寸变化（重绘、字体、图片都可能改变它的高度）。
        // 绝不能观察被缩放的那个元素：缩放会改变它的 border box，
        // ResizeObserver 会立刻再次触发 → 反馈环，控制台刷满
        // "ResizeObserver loop completed with undelivered notifications"。
        if (global.ResizeObserver) {
            try {
                var observed = null;
                var ro = new ResizeObserver(function () {
                    if (measuring) return;
                    // 放到下一帧再测，避免在 RO 投递过程中同步改布局
                    if (global.requestAnimationFrame) global.requestAnimationFrame(apply);
                    else apply();
                });
                var watch = function () {
                    if (observed) { ro.unobserve(observed); observed = null; }
                    var content = contentOf(getter());
                    if (content) { ro.observe(content); observed = content; }
                };
                watch();
                // 重绘会换掉内部元素，换完要重新挂观察
                holder.addEventListener(EVENT, watch);
            } catch (e) { /* 忽略 */ }
        }

        holder.addEventListener(EVENT, function () { apply(); });
        global.addEventListener('resize', schedule);
        if (document.fonts && document.fonts.ready) {
            document.fonts.ready.then(schedule).catch(function () { });
        }
        setTimeout(apply, 60);
        setTimeout(apply, 400);

        apply.schedule = schedule;

        return apply;
    }

    /* ------------------------------------------------------------
     *  重绘通知：模拟器每次重绘后都会叫一声，舞台据此重新测量高度。
     *
     *  注意：不能靠"包装 return 出去的 render()"来做这件事 ——
     *  模拟器内部的按钮直接调模块私有的 render()，包装外层函数收不到通知。
     *  所以由模拟器自己显式 notifyLayout()。
     * ------------------------------------------------------------ */
    var layoutListeners = [];

    function onLayout(fn) {
        layoutListeners.push(fn);
        return function off() {
            var i = layoutListeners.indexOf(fn);
            if (i >= 0) layoutListeners.splice(i, 1);
        };
    }

    function notifyLayout() {
        for (var i = 0; i < layoutListeners.length; i++) {
            try { layoutListeners[i](); } catch (e) { /* 忽略单个监听器异常 */ }
        }
    }

    /* ------------------------------------------------------------
     *  Toast
     * ------------------------------------------------------------ */
    function toastHost() {
        var host = document.querySelector('.toast-host');
        if (!host) {
            host = document.createElement('div');
            host.className = 'toast-host';
            document.body.appendChild(host);
        }
        return host;
    }

    function toast(text, kind, ms) {
        var host = toastHost();
        var node = document.createElement('div');
        node.className = 'toast' + (kind ? ' ' + kind : '');
        node.textContent = text;
        host.appendChild(node);

        setTimeout(function () {
            node.style.transition = 'opacity .28s, transform .28s';
            node.style.opacity = '0';
            node.style.transform = 'translateY(6px)';
            setTimeout(function () {
                if (node.parentNode) node.parentNode.removeChild(node);
            }, 300);
        }, ms || 2600);
    }

    /* ------------------------------------------------------------
     *  导航高亮
     * ------------------------------------------------------------ */
    function markActiveNav() {
        var path = global.location.pathname.split('/').pop() || 'index.html';
        var links = document.querySelectorAll('.nav-links a[data-page]');
        for (var i = 0; i < links.length; i++) {
            if (links[i].getAttribute('data-page') === path) {
                links[i].classList.add('active');
            }
        }
    }

    /* ------------------------------------------------------------
     *  启动
     * ------------------------------------------------------------ */
    function ready(fn) {
        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', fn);
        } else {
            fn();
        }
    }

    ready(function () {
        injectMeta();
        markActiveNav();
    });

    BNS.ui = {
        fitStage: fitStage,
        toast: toast,
        ready: ready,
        onLayout: onLayout,
        notifyLayout: notifyLayout
    };
})(typeof window !== 'undefined' ? window : this);
