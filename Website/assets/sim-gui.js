/* ============================================================
 *  sim-gui.js —— 左侧"BattleNetSwitcher"桌面程序模拟
 *
 *  逐项对齐真实实现：
 *      Forms/MainForm.cs        三标签页 + 状态栏（版本号可点，跳 Releases）
 *      Forms/AccountPanel.cs    区服下拉 / 账号列表 / 添加 / 刷新 / 自动重启
 *      Forms/AccountRow.cs      当前行蓝底 + ● 当前 + 切换 + ×，双击整行切换
 *      Forms/AddAccountDialog   邮箱 + 区服，校验必须在 SavedAccountNames 中
 *      Forms/NetworkPanel.cs    目标应用 / 同时静音 / 秒数 / 一键拔线 / 倒计时
 *      Forms/ProcessPickerDialog  搜索 + 回车确定 + 双击选中
 *      Forms/SettingsPanel.cs   两条路径 + 禁用一键拔线
 *      Program.cs               启动时按需提权（模拟器里默认已是管理员）
 * ============================================================ */
(function (global) {
    'use strict';

    var BNS = global.BNS || (global.BNS = {});
    var core = BNS.core;
    var data = BNS.data;

    /* ============================================================
     *  DOM 小工具
     * ============================================================ */
    function el(tag, cls, text) {
        var node = document.createElement(tag);
        if (cls) node.className = cls;
        if (text !== undefined && text !== null) node.textContent = text;
        return node;
    }

    function clear(node) {
        while (node.firstChild) node.removeChild(node.firstChild);
    }

    function btn(label, cls, onClick) {
        var b = el('button', 'wf-btn' + (cls ? ' ' + cls : ''), label);
        b.type = 'button';
        if (onClick) b.onclick = onClick;
        return b;
    }

    /** 下拉框（模拟 ComboBox，选中值支持任意类型） */
    function dropdown(items, selectedValue, onChange, disabled) {
        var wrap = el('div', 'wf-select');
        wrap.tabIndex = 0;
        var valueEl = el('span');
        var caret = el('span');
        caret.style.cssText = 'position:absolute;right:8px;top:7px;font-size:8px;color:#555';
        caret.textContent = '▼';
        wrap.style.position = 'relative';
        wrap.appendChild(valueEl);
        wrap.appendChild(caret);

        /**
         * 当前显示的值。
         * 必须自己维护一份：只读入参 selectedValue 的话，用户选了新项之后
         * 显示文字不会跟着变（WinForms 的 ComboBox 是会立即变的）。
         */
        var current = selectedValue;

        function labelOf(v) {
            for (var i = 0; i < items.length; i++) {
                if (items[i].value === v) return items[i].label;
            }
            return items.length > 0 ? items[0].label : '';
        }

        function show(v) {
            current = v;
            valueEl.textContent = labelOf(v);
        }

        show(selectedValue);
        if (disabled) {
            wrap.style.opacity = '.6';
            wrap.style.cursor = 'not-allowed';
            return wrap;
        }

        wrap.onclick = function (e) {
            e.stopPropagation();
            openList();
        };
        wrap.onkeydown = function (e) {
            if (e.key === 'Enter' || e.key === ' ' || e.key === 'ArrowDown') {
                e.preventDefault();
                openList();
            }
        };

        function openList() {
            if (document.querySelector('.wf-droplist')) return;
            var rect = wrap.getBoundingClientRect();
            var list = el('div', 'wf-droplist');
            list.style.cssText =
                'left:' + rect.left + 'px;top:' + (rect.bottom + 1) + 'px;' +
                'min-width:' + rect.width + 'px;';

            items.forEach(function (item) {
                var row = el('div', 'wf-dropitem' + (item.value === current ? ' on' : ''),
                    item.label);
                row.onclick = function (e) {
                    e.stopPropagation();
                    close();
                    if (item.value === current) return;
                    show(item.value);          // 先刷新显示，再通知外部
                    onChange(item.value);
                };
                list.appendChild(row);
            });

            document.body.appendChild(list);
            function close() {
                if (list.parentNode) list.parentNode.removeChild(list);
                document.removeEventListener('click', close, true);
                document.removeEventListener('keydown', onKey, true);
            }
            function onKey(e) {
                if (e.key === 'Escape') { e.stopPropagation(); close(); }
            }
            setTimeout(function () {
                document.addEventListener('click', close, true);
                document.addEventListener('keydown', onKey, true);
            }, 0);
        }

        return wrap;
    }

    /* ============================================================
     *  模态对话框（对应 WinForms 的 Form / MessageBox）
     * ============================================================ */
    function dialog(title, bodyBuilder, buttonsBuilder, opts) {
        opts = opts || {};
        var layer = el('div', 'modal-layer');
        var dlg = el('div', 'dlg' + (opts.dlgClass ? ' ' + opts.dlgClass : ''));

        var bar = el('div', 'dlg-titlebar');
        bar.appendChild(el('span', null, title));
        var closeBtn = el('div', 'wbtn', '✕');
        bar.appendChild(closeBtn);
        dlg.appendChild(bar);

        var body = el('div', 'dlg-body');
        dlg.appendChild(body);
        layer.appendChild(dlg);
        document.body.appendChild(layer);

        var api = {
            layer: layer,
            dlg: dlg,
            body: body,
            close: function () {
                if (layer.parentNode) layer.parentNode.removeChild(layer);
                document.removeEventListener('keydown', onKey, true);
                if (opts.onClose) opts.onClose();
            },
            setBusy: function (busy) {
                var bs = dlg.querySelectorAll('.wf-btn, .wf-select, .wf-input');
                for (var i = 0; i < bs.length; i++) bs[i].disabled = !!busy;
            }
        };

        closeBtn.onclick = api.close;
        if (!opts.noDismiss) {
            layer.onclick = function (e) { if (e.target === layer) api.close(); };
        }
        function onKey(e) {
            if (e.key === 'Escape' && !opts.noDismiss) api.close();
        }
        document.addEventListener('keydown', onKey, true);

        bodyBuilder(body, api);
        if (buttonsBuilder) body.appendChild(buttonsBuilder(api));

        return api;
    }

    /** 对应 MessageBox 样式：图标 + 文本 + 按钮 */
    function messageBox(iconKind, text, buttons, opts) {
        opts = opts || {};
        return new Promise(function (resolve) {
            var glyph = { info: 'i', warn: '!', error: '✕', question: '?' }[iconKind] || 'i';
            var api = dialog(opts.title || '提示', function (body) {
                var row = el('div', 'mb-row');
                row.appendChild(el('div', 'mb-icon ' + iconKind, glyph));
                var msg = el('div', 'dlg-msg' + (opts.mono ? ' mono' : ''), text);
                row.appendChild(msg);
                body.appendChild(row);
            }, function (api2) {
                var bar = el('div', 'dlg-buttons');
                buttons.forEach(function (b) {
                    var node = btn(b.label, b.primary ? 'primary' : '', function () {
                        api2.close();
                        resolve(b.value);
                    });
                    bar.appendChild(node);
                    if (b.primary) setTimeout(function () { node.focus(); }, 30);
                });
                return bar;
            }, opts);
            void api;
        });
    }

    function alertBox(text, opts) {
        return messageBox((opts && opts.kind) || 'info', text,
            [{ label: '确定', value: 'ok', primary: true }], opts);
    }

    function confirmBox(text, opts) {
        return messageBox('question', text, [
            { label: '是', value: 'yes', primary: true },
            { label: '否', value: 'no' }
        ], opts);
    }

    /** 进度对话框（模拟切换过程中被阻塞的窗口，附执行日志） */
    function progressDialog(title, task) {
        return new Promise(function (resolve, reject) {
            var lines = [];
            var pre = el('div', 'dlg-msg mono');
            pre.style.maxHeight = '180px';
            pre.style.minHeight = '90px';
            pre.textContent = '正在执行…';

            var bar = el('div');
            bar.style.cssText = 'height:6px;background:#dcdcdc;border-radius:3px;overflow:hidden;margin-top:12px';
            var inner = el('div');
            inner.style.cssText =
                'height:100%;width:30%;background:linear-gradient(90deg,#8a8a8a,#3a3a3a);' +
                'transition:width .25s';
            bar.appendChild(inner);

            var api = dialog(title, function (body) {
                body.appendChild(pre);
                body.appendChild(bar);
            }, null, { noDismiss: true, dlgClass: 'progress-dlg' });

            var closed = false;
            function finish(err, value) {
                if (closed) return;
                closed = true;
                inner.style.width = '100%';
                setTimeout(function () {
                    api.close();
                    if (err) reject(err); else resolve(value);
                }, 260);
            }

            task(function (line) {
                lines.push(line);
                pre.textContent = lines.join('\n');
                pre.scrollTop = pre.scrollHeight;
                inner.style.width = Math.min(96, 30 + lines.length * 9) + '%';
            }).then(function (value) {
                finish(null, { value: value, log: lines });
            }, function (err) {
                finish(err);
            });
        });
    }

    /** 模拟 OpenFileDialog */
    function openFileDialog(title, filterList, files) {
        return new Promise(function (resolve) {
            var selected = files.length > 0 ? files[0] : null;

            dialog(title, function (body) {
                var pathRow = el('div', 'fo-path');
                pathRow.appendChild(el('span', 'wf-label', '文件名(N):'));
                var box = el('div', 'wf-input');
                box.style.display = 'flex';
                box.style.alignItems = 'center';
                box.style.fontFamily = 'var(--mono)';
                box.style.fontSize = '11.5px';
                box.textContent = selected || '请选择一个文件';
                pathRow.appendChild(box);
                body.appendChild(pathRow);

                var listBox = el('div', 'fo-list');
                files.forEach(function (f) {
                    var item = el('div', 'fo-item' + (f === selected ? ' selected' : ''));
                    item.appendChild(el('span', 'fi', '📄'));
                    item.appendChild(el('span', null, f));
                    item.onclick = function () {
                        selected = f;
                        box.textContent = f;
                        var all = listBox.querySelectorAll('.fo-item');
                        for (var i = 0; i < all.length; i++) all[i].classList.remove('selected');
                        item.classList.add('selected');
                    };
                    item.ondblclick = function () { onOk(); };
                    listBox.appendChild(item);
                });
                body.appendChild(listBox);

                var hint = el('div', 'wf-hint', '类型：' + filterList);
                hint.style.marginTop = '8px';
                body.appendChild(hint);
            }, function (api2) {
                var bar = el('div', 'dlg-buttons');
                bar.appendChild(btn('确定', 'primary', function () {
                    onOk();
                }));
                bar.appendChild(btn('取消', '', function () {
                    api2.close();
                    resolve(null);
                }));
                api2._ok = onOk;
                function onOk() {
                    if (!selected) return;
                    api2.close();
                    resolve(selected);
                }
                return bar;
            }, { dlgClass: 'file-open' });
        });
    }

    /* ============================================================
     *  主渲染
     * ============================================================ */
    function render(root, clientCtx, options) {
        options = options || {};
        var s = core.state();
        var ui = {
            tab: 'account',
            /** 拔线过程状态 */
            pull: { phase: 'idle', remain: 0, total: 0, timer: null },
            switchBusy: false,
            chain: null,
            savedFlash: ''
        };

        function notify(text, kind) {
            if (options.onNotify) options.onNotify(text, kind);
        }

        function toast(text, kind) {
            notify(text, kind || 'info');
        }

        /* ---------------- 账号页 ---------------- */
        function buildAccountPanel() {
            var panel = el('div', 'tab-panel' + (ui.tab === 'account' ? ' active' : ''));

            // 区服行
            var row = el('div', 'ap-region-row');
            row.appendChild(el('span', 'wf-label', '区服：'));

            var codes = core.activeRegions();
            var selected = codes.indexOf(s.lastUiRegion) >= 0 ? s.lastUiRegion : (codes[0] || null);

            var items = codes.map(function (code) {
                var info = data.tryFromCode(code);
                return { value: code, label: info ? info.displayName : code };
            });

            var combo = dropdown(items, selected, function (value) {
                s.lastUiRegion = value;
                core.persist();
                renderAll();
            }, ui.switchBusy);
            combo.style.width = '200px';
            combo.style.marginRight = '12px';
            row.appendChild(combo);
            row.appendChild(el('span', 'wf-hint', '（切换时通过 --setregion 启动参数生效）'));
            panel.appendChild(row);

            // 账号列表
            var list = el('div', 'account-list');
            var cfg = core.readConfigAccountsAndRegion();

            if (!selected) {
                list.appendChild(el('div', 'list-empty', '还没有任何账号。点击下方"添加账号"加入第一个。'));
            } else {
                var emails = core.emailsForRegion(selected);
                if (emails.length === 0) {
                    var info0 = data.tryFromCode(selected);
                    list.appendChild(el('div', 'list-empty',
                        '区服 [' + (info0 ? info0.displayName : selected) + '] 暂无账号。'));
                } else {
                    emails.forEach(function (email) {
                        var isCurrent = cfg.emails.length > 0 &&
                            core.eq(cfg.emails[0], email) &&
                            core.eq(selected, cfg.region);
                        list.appendChild(buildAccountRow(email, isCurrent, selected));
                    });
                }
            }
            panel.appendChild(list);

            // 状态文字
            var infoSel = selected ? data.tryFromCode(selected) : null;
            var statusText = !selected
                ? ''
                : '区服 [' + (infoSel ? infoSel.displayName : selected) + '] 下共 ' +
                  core.emailsForRegion(selected).length + ' 个账号。';
            panel.appendChild(el('div', 'ap-status', statusText));

            // 按钮行
            var buttons = el('div', 'ap-buttons');
            var addBtn = btn('添加账号', 'add', onAddAccount);
            addBtn.style.height = '32px';
            addBtn.style.width = '120px';
            addBtn.disabled = ui.switchBusy;

            var refreshBtn = btn('刷新列表', 'refresh', function () {
                toast('已刷新区服与账号列表');
                renderAll();
            });
            refreshBtn.style.height = '32px';
            refreshBtn.style.width = '120px';
            refreshBtn.disabled = ui.switchBusy;

            var chk = el('label', 'wf-check');
            var cb = document.createElement('input');
            cb.type = 'checkbox';
            cb.checked = ui.chain !== false;
            cb.disabled = ui.switchBusy;
            cb.onchange = function () { ui.chain = cb.checked; };
            chk.appendChild(cb);
            chk.appendChild(el('span', null, '切换后自动重启战网'));

            buttons.appendChild(addBtn);
            buttons.appendChild(refreshBtn);
            buttons.appendChild(chk);
            panel.appendChild(buttons);

            return panel;
        }

        function buildAccountRow(email, isCurrent, regionCode) {
            // 真实的 AccountRow 只有：邮箱 / ● 当前 / 切换 / ×（没有跨区服标记）
            var row = el('div', 'acct-row' + (isCurrent ? ' current' : ''));
            row.appendChild(el('div', 'email', email));

            var flag = el('div', 'flag', '● 当前');
            row.appendChild(flag);

            var sw = btn('切换', '', function (e) {
                e.stopPropagation();
                onSwitch(email, regionCode);
            });
            sw.disabled = ui.switchBusy;
            row.appendChild(sw);

            var rm = btn('×', 'remove', function (e) {
                e.stopPropagation();
                onRemove(email, regionCode);
            });
            rm.disabled = ui.switchBusy;
            row.appendChild(rm);

            row.ondblclick = function () {
                if (ui.switchBusy) return;
                onSwitch(email, regionCode);
            };

            return row;
        }

        /* ---------------- 一键拔线页 ---------------- */
        function buildNetworkPanel() {
            var disabled = !!s.settings.disablePullout;
            var panel = el('div', 'tab-panel' + (ui.tab === 'network' ? ' active' : ''));

            // 目标应用行
            var top = el('div', 'np-top');
            top.appendChild(el('span', 'wf-label', '目标应用：'));

            var pathBox = el('div', 'wf-input');
            pathBox.style.display = 'flex';
            pathBox.style.alignItems = 'center';
            pathBox.style.overflow = 'hidden';
            pathBox.style.whiteSpace = 'nowrap';
            pathBox.style.textOverflow = 'ellipsis';
            pathBox.style.fontFamily = 'var(--mono)';
            pathBox.style.fontSize = '11.5px';
            pathBox.textContent = s.network.appPath || '';
            top.appendChild(pathBox);

            // WinForms 里 _networkPanel.Enabled = false 会让所有子控件一起变灰不可点
            var busy = ui.pull.phase === 'pulling' || disabled;
            var browse = btn('浏览…', '', function () {
                openFileDialog('选择要绑定的应用',
                    '可执行文件 (*.exe)|*.exe',
                    data.FAKE_FILES.files.filter(function (f) { return /\.exe$/i.test(f); })
                ).then(function (file) {
                    if (!file) return;
                    core.setTargetApp(file);
                    toast('已绑定目标应用：' + file.split('\\').pop());
                    renderAll();
                });
            });
            browse.disabled = busy;
            var pick = btn('选择…', '', function () {
                pickProcess().then(function (path) {
                    if (!path) return;
                    core.setTargetApp(path);
                    toast('已绑定目标应用：' + path.split('\\').pop());
                    renderAll();
                });
            });
            pick.disabled = busy;
            top.appendChild(browse);
            top.appendChild(pick);
            panel.appendChild(top);

            // 分组框
            var grp = el('div', 'wf-group np-group');
            grp.appendChild(el('span', 'wf-group-title', '一键拔线'));

            var muteChk = el('label', 'wf-check np-mute');
            var muteCb = document.createElement('input');
            muteCb.type = 'checkbox';
            muteCb.checked = !!s.network.mute;
            muteCb.disabled = busy;
            muteCb.onchange = function () {
                core.updateNetwork({ mute: muteCb.checked });
            };
            muteChk.appendChild(muteCb);
            muteChk.appendChild(el('span', null, '同时静音该应用的声音'));
            grp.appendChild(muteChk);

            grp.appendChild(el('span', 'wf-label np-sec-label', '断网时长（秒）：'));

            var num = el('div', 'wf-num np-seconds');
            var numInput = document.createElement('input');
            numInput.type = 'number';
            numInput.min = '1';
            numInput.max = '3600';
            numInput.value = String(s.network.pullSeconds || 3);
            numInput.disabled = busy;
            numInput.onchange = function () {
                var v = parseInt(numInput.value, 10);
                if (isNaN(v)) v = 3;
                v = Math.max(1, Math.min(3600, v));
                numInput.value = String(v);
                core.updateNetwork({ pullSeconds: v });
            };
            num.appendChild(numInput);
            var spin = el('div', 'spin');
            var up = el('span', null, '▲');
            var down = el('span', null, '▼');
            up.onclick = function () { numInput.stepUp(); numInput.onchange(); };
            down.onclick = function () { numInput.stepDown(); numInput.onchange(); };
            spin.appendChild(up);
            spin.appendChild(down);
            num.appendChild(spin);
            grp.appendChild(num);

            var pullBtn = btn('一键拔线', 'primary np-pull', onPullCable);
            pullBtn.disabled = !s.network.appPath || busy;
            grp.appendChild(pullBtn);

            var cd = el('div', 'np-countdown' + (ui.pull.phase === 'createFailed' ? ' err' : ''));
            if (disabled) {
                cd.textContent = '';
            } else if (ui.pull.phase === 'pulling') {
                cd.textContent = '已断网，' + ui.pull.remain + ' 秒后自动恢复…';
            } else if (ui.pull.phase === 'done') {
                cd.textContent = '已恢复。';
            } else if (ui.pull.phase === 'restoreFailed') {
                cd.textContent = '恢复失败：' + ui.pull.error;
            } else if (ui.pull.phase === 'createFailed') {
                cd.textContent = ui.pull.error;
            } else {
                cd.textContent = '';
            }
            grp.appendChild(cd);
            panel.appendChild(grp);

            panel.appendChild(el('div', 'wf-hint np-hint',
                '提示：修改防火墙规则需要管理员权限（清单已自动请求）。'));
            if (!s.isAdmin && !disabled) {
                var adminNote = el('div', 'wf-hint', '当前以普通用户模式运行 → netsh 将返回"需要提升"。');
                adminNote.style.color = 'var(--win-orange)';
                panel.appendChild(adminNote);
            }

            return panel;
        }

        /* ---------------- 设置页 ---------------- */
        function buildSettingsPanel() {
            var panel = el('div', 'tab-panel' + (ui.tab === 'settings' ? ' active' : ''));

            panel.appendChild(el('div', 'wf-label sp-field-label', '战网程序路径：'));
            var exeRow = el('div', 'sp-row exe');
            var exeInput = document.createElement('input');
            exeInput.className = 'wf-input';
            exeInput.value = s.settings.battleNetExePath || '';
            exeInput.placeholder = '留空 = 自动从注册表查找';
            exeInput.oninput = function () { exeInput.dataset.touched = '1'; };
            exeRow.appendChild(exeInput);
            exeRow.appendChild(btn('浏览…', '', function () {
                openFileDialog('选择战网可执行文件',
                    '战网程序 (Battle.net*.exe)|Battle.net*.exe|可执行文件 (*.exe)|*.exe',
                    data.FAKE_FILES.files.filter(function (f) { return /\.exe$/i.test(f); })
                ).then(function (f) {
                    if (f) exeInput.value = f;
                });
            }));
            panel.appendChild(exeRow);

            panel.appendChild(el('div', 'wf-label sp-field-label', '配置文件路径：'));
            var cfgRow = el('div', 'sp-row cfg');
            var cfgInput = document.createElement('input');
            cfgInput.className = 'wf-input';
            cfgInput.value = s.settings.battleNetConfigPath || '';
            cfgInput.placeholder = '留空 = %APPDATA%\\Battle.net\\Battle.net.config';
            cfgRow.appendChild(cfgInput);
            cfgRow.appendChild(btn('浏览…', '', function () {
                openFileDialog('选择 Battle.net.config',
                    '战网配置 (Battle.net.config)|Battle.net.config|所有文件 (*.*)|*.*',
                    data.FAKE_FILES.files
                ).then(function (f) {
                    if (f) cfgInput.value = f;
                });
            }));
            cfgRow.appendChild(btn('恢复默认', '', function () {
                cfgInput.value = '';
            }));
            panel.appendChild(cfgRow);

            var hint = el('div', 'wf-hint sp-hint',
                '留空 = 使用默认位置。便携版 / 绿色版战网请手动指定。');
            panel.appendChild(hint);

            var disChk = el('label', 'wf-check');
            var disCb = document.createElement('input');
            disCb.type = 'checkbox';
            disCb.checked = !!s.settings.disablePullout;
            disChk.appendChild(disCb);
            disChk.appendChild(el('span', null, '禁用"一键拔线"功能（GUI 启动不再请求管理员权限）'));
            panel.appendChild(disChk);

            panel.appendChild(el('div', 'wf-hint sp-restart-note',
                '※ 修改后需要重启程序才能完全生效。'));

            var saveRow = el('div', 'sp-btn-row');
            var saveBtn = btn('保存', 'primary', function () {
                var exe = exeInput.value.trim();
                var cfg = cfgInput.value.trim();

                if (exe && data.FAKE_FILES.files.indexOf(exe) < 0) {
                    alertBox('指定的战网程序路径不存在。', { kind: 'warn', title: '提示' });
                    return;
                }
                if (cfg && data.FAKE_FILES.files.indexOf(cfg) < 0) {
                    alertBox('指定的配置文件路径不存在。', { kind: 'warn', title: '提示' });
                    return;
                }

                var oldDisable = !!core.state().settings.disablePullout;
                core.updateSettings({
                    battleNetExePath: exe || null,
                    battleNetConfigPath: cfg || null,
                    disablePullout: disCb.checked
                });
                ui.savedFlash = '已保存 ✓';
                renderAll();

                var needRestart = oldDisable !== disCb.checked;
                if (needRestart) {
                    confirmBox('禁用"一键拔线"状态的变更需要重启程序才能生效。\r\n是否现在重启？',
                        { title: '需要重启' }).then(function (r) {
                        if (r === 'yes') {
                            toast('已重启程序（模拟）：普通用户模式启动', 'ok');
                        }
                    });
                }
            });
            saveBtn.style.width = '120px';
            saveBtn.style.height = '32px';
            saveRow.appendChild(saveBtn);
            if (ui.savedFlash) {
                saveRow.appendChild(el('span', 'sp-saved', ui.savedFlash));
            }
            panel.appendChild(saveRow);

            return panel;
        }

        /* ---------------- 组装窗口 ---------------- */
        function renderAll() {
            s = core.state();
            clear(root);

            var win = el('div', 'win');

            // 标题栏
            var bar = el('div', 'win-titlebar');
            var ico = el('img', 'ico16');
            ico.src = 'assets/app-icon.png';
            ico.alt = '';
            ico.width = 16;
            ico.height = 16;
            bar.appendChild(ico);
            bar.appendChild(el('span', 'title', '战网账号切换 & 一键拔线'));
            bar.appendChild(el('div', 'spacer'));
            ['—', '▢', '✕'].forEach(function (g) {
                bar.appendChild(el('div', 'wbtn' + (g === '✕' ? ' close' : ''), g));
            });
            win.appendChild(bar);

            var body = el('div', 'win-body');

            // 标签头
            var tabs = el('div', 'tabs');
            var disabled = !!s.settings.disablePullout;
            var tabDefs = [
                { key: 'account', label: '账号切换' },
                { key: 'network', label: disabled ? '一键拔线（已禁用）' : '一键拔线', disabled: disabled },
                { key: 'settings', label: '设置' }
            ];
            tabDefs.forEach(function (def) {
                var t = el('div', 'tab' + (ui.tab === def.key ? ' active' : '') +
                    (def.disabled ? ' disabled' : ''), def.label);
                // 对应真实实现：面板 Enabled=false + 文字用 SystemColors.GrayText，
                // 但 tab 本身仍然可以切过去看
                if (def.disabled) t.style.color = 'var(--win-dim)';
                t.onclick = function () {
                    ui.tab = def.key;
                    ui.savedFlash = '';
                    renderAll();
                };
                tabs.appendChild(t);
            });
            body.appendChild(tabs);

            var panels = el('div', 'tab-panels');
            panels.appendChild(buildAccountPanel());
            panels.appendChild(buildNetworkPanel());
            panels.appendChild(buildSettingsPanel());
            body.appendChild(panels);
            win.appendChild(body);

            // 状态栏
            var status = el('div', 'statusbar');
            var ver = el('span', 'ver link', 'v' + (options.version || '1.0.0'));
            ver.title = '点击查看 GitHub 发布页（模拟）';
            ver.onclick = function () {
                if (options.releasesUrl && !options.simulateLinks) {
                    window.open(options.releasesUrl, '_blank', 'noopener');
                } else {
                    toast('打开 Releases 页：' + (options.releasesUrl || '(未配置 RepositoryUrl)'));
                }
            };
            status.appendChild(ver);
            status.appendChild(el('span', 'spacer'));

            var mode = el('span', 'mode');
            if (disabled) {
                mode.classList.add('off');
                mode.textContent = '已禁用拔线';
            } else if (s.isAdmin) {
                mode.classList.add('admin');
                mode.textContent = '管理员模式';
            } else {
                mode.classList.add('user');
                mode.textContent = '普通用户';
            }
            status.appendChild(mode);
            win.appendChild(status);

            root.appendChild(win);
            if (options.fit) options.fit();
            // 通知舞台重新测量
            if (BNS.ui && BNS.ui.notifyLayout) BNS.ui.notifyLayout();
        }

        /* ============================================================
         *  账号切换交互
         * ============================================================ */
        function onSwitch(email, regionCode) {
            if (ui.switchBusy) return;
            var info = data.fromCode(regionCode);
            var restart = ui.chain !== false;

            confirmBox(
                '确定切换到 ' + email + ' 吗？\r\n' +
                '区服：' + info.displayName + '\r\n' +
                '将关闭战网、修改配置并重新启动。',
                { title: '确认切换' }
            ).then(function (r) {
                if (r !== 'yes') return;

                ui.switchBusy = true;
                renderAll();

                var liveBox = null;
                var lines = [];

                progressDialog('正在切换账号', function (log) {
                    return core.switchAccount(email, regionCode, restart, function (line) {
                        lines.push(line);
                        log(line);
                        if (liveBox) void liveBox;
                    });
                }).then(function (res) {
                    ui.switchBusy = false;
                    ui.tab = 'account';
                    renderAll();
                    return alertBox(res.log.join('\r\n'), { title: '完成', mono: true });
                }, function (err) {
                    ui.switchBusy = false;
                    renderAll();
                    return alertBox(err && err.message ? err.message : String(err),
                        { title: '切换失败', kind: 'error' });
                });
            });
        }

        function onRemove(email, regionCode) {
            if (ui.switchBusy) return;
            confirmBox(
                '确定要从本区服移除账号 ' + email + ' 吗？\r\n' +
                '（仅移除本地记录，不会影响战网账号本身）',
                { title: '确认移除' }
            ).then(function (r) {
                if (r !== 'yes') return;
                if (core.bookRemove(email, regionCode)) {
                    toast('已移除 ' + email + '（' + regionCode + '）', 'ok');
                }
                renderAll();
            });
        }

        function onAddAccount() {
            if (ui.switchBusy) return;
            var preset = s.lastUiRegion || 'CN';
            var items = data.REGIONS.map(function (r) {
                return { value: r.code, label: r.displayName };
            });
            var chosen = preset;

            var input = null;
            var statusEl = null;
            var launchBtn = null;
            var apiRef = null;
            var pollTimer = null;

            function stopPolling() {
                if (pollTimer) { clearTimeout(pollTimer); pollTimer = null; }
                if (launchBtn) {
                    launchBtn.disabled = false;
                    launchBtn.textContent = '启动战网并等待登录';
                }
            }

            function setStatus(text, kind) {
                if (!statusEl) return;
                statusEl.textContent = text;
                statusEl.className = 'add-status' + (kind ? ' ' + kind : '');
            }

            /** 轮询"战网已记住的账号"，发现新增就回填邮箱（对应真实版的 SavedAccountNames 轮询） */
            function pollForNewAccount(waitedMs) {
                var added = core.newSinceBaseline();
                if (added.length > 0) {
                    input.value = added[0];
                    setStatus('✅ 检测到新登录的账号：' + added[0] +
                        (added.length > 1 ? '（另有 ' + (added.length - 1) + ' 个：' +
                            added.slice(1).join('、') + '）' : '') +
                        '，点"确定"加入本地账号本。', 'ok');
                    stopPolling();
                    return;
                }

                if (waitedMs >= 30000) {
                    stopPolling();
                    setStatus('等待超时。请确认在客户端登录时勾选了"记住密码"；' +
                        '登录完成后也可以点"刷新检测"。', 'warn');
                    return;
                }

                if (waitedMs % 4000 < 400) {
                    setStatus('等待登录完成… 已等待 ' + Math.round(waitedMs / 1000) + ' 秒\r\n' +
                        '（登录时请勾选"记住密码"，否则无法被检测到）');
                }

                pollTimer = setTimeout(function () {
                    pollForNewAccount(waitedMs + 400);
                }, 400);
            }

            /** 手动刷新：读一次当前已记住的账号，把新增的填进去 */
            function rescan(manual) {
                var added = core.newSinceBaseline();
                if (added.length > 0) {
                    input.value = added[0];
                    setStatus('✅ 检测到新登录的账号：' + added[0], 'ok');
                    stopPolling();
                    return;
                }
                if (manual) {
                    var saved = core.savedAccountNames();
                    setStatus(saved.length === 0
                        ? '战网里还没有记住任何账号。请在客户端登录并勾选"记住密码"。'
                        : '没有检测到新增账号（战网已记住 ' + saved.length + ' 个）。' +
                          '如果你登录的是已经记住过的账号，直接点"确定"即可。', 'warn');
                }
            }

            function launchAndWait() {
                core.seedLoginBaseline();          // 记下基线
                stopPolling();
                launchBtn.disabled = true;
                launchBtn.textContent = '等待登录中…';
                setStatus('正在以 [' + data.fromCode(chosen).displayName + '] 打开登录页…');

                core.launchForRegion(chosen, function (line) {
                    setStatus(line);
                }).then(function () {
                    setStatus('已打开 ' + data.fromCode(chosen).displayName +
                        ' 的登录页。请在右侧客户端模拟里输入该区服的账号并勾选"记住密码"，' +
                        '登录成功后这里会自动填入。');
                    pollForNewAccount(0);
                }, function (err) {
                    setStatus('启动失败：' + (err && err.message ? err.message : String(err)), 'err');
                    stopPolling();
                });
            }

            function submit() {
                var email = input.value.trim();

                // 邮箱留空时先自动补一次（用户可能刚登录完）
                if (!email) {
                    rescan(false);
                    email = input.value.trim();
                }

                if (!email) {
                    alertBox(
                        '请输入邮箱，或点"启动战网并等待登录"用客户端登录一个新号。\r\n\r\n' +
                        '注意：登录时请勾选"记住密码"，否则工具无法读取到该账号。',
                        { title: '提示' }
                    );
                    return;
                }

                core.ensureInitialized();
                var saved = core.savedAccountNames();
                var exists = saved.some(function (x) { return core.eq(x, email); });
                if (!exists) {
                    alertBox(
                        '邮箱 ' + email + ' 未在战网中保存。\r\n\r\n' +
                        '请先在战网客户端里用该账号登录一次，' +
                        '并勾选"记住密码"，然后再回到这里添加。',
                        { title: '账号未保存', kind: 'warn' }
                    );
                    return;
                }

                var code = chosen;
                stopPolling();
                apiRef.close();
                if (core.bookAdd(email, code)) {
                    s.lastUiRegion = code;
                    core.persist();
                    toast('已添加 ' + email + ' 到 ' + data.fromCode(code).displayName, 'ok');
                } else {
                    alertBox(email + ' 已经在 ' + code + ' 区服中。', { title: '提示' });
                }
                renderAll();
            }

            apiRef = dialog('添加账号到区服', function (b) {
                var grid = el('div', 'add-grid');

                grid.appendChild(el('label', 'wf-label', '区服：'));
                grid.appendChild(dropdown(items, chosen, function (v) { chosen = v; }, false));

                grid.appendChild(el('label', 'wf-label', '邮箱：'));
                input = document.createElement('input');
                input.className = 'wf-input';
                input.placeholder = '可留空，用下面的按钮登录新号';
                input.onkeydown = function (e) {
                    if (e.key === 'Enter') { e.preventDefault(); submit(); }
                };
                grid.appendChild(input);

                b.appendChild(grid);
                b.appendChild(el('div', 'add-hint',
                    '邮箱必须已在战网客户端登录过并勾选"记住密码"；' +
                    '留空则用下面按钮直接开该区服的战网去登录一个新号。'));

                var row = el('div', 'add-actions');
                launchBtn = btn('启动战网并等待登录', '', launchAndWait);
                row.appendChild(launchBtn);
                var rescanBtn = btn('刷新检测', '', function () { rescan(true); });
                row.appendChild(rescanBtn);
                b.appendChild(row);

                statusEl = el('div', 'add-status');
                b.appendChild(statusEl);

                setTimeout(function () { input.focus(); }, 40);
            }, function (api2) {
                var bar = el('div', 'dlg-buttons');
                bar.appendChild(btn('确定', 'primary', submit));
                bar.appendChild(btn('取消', '', function () {
                    stopPolling();
                    api2.close();
                }));
                return bar;
            }, { dlgClass: 'add-account', onClose: stopPolling });
        }

        /* ============================================================
         *  一键拔线交互
         * ============================================================ */
        function pickProcess() {
            var procs = data.SEED_PROCESSES.slice();

            return new Promise(function (resolve) {
                var filter = '';
                var selIdx = 0;

                function filteredList() {
                    if (!filter) return procs;
                    var f = filter.toLowerCase();
                    return procs.filter(function (p) {
                        return p.name.toLowerCase().indexOf(f) >= 0 ||
                            p.path.toLowerCase().indexOf(f) >= 0;
                    });
                }

                function currentSelection() {
                    var list = filteredList();
                    if (list.length === 0) return null;
                    return list[Math.min(selIdx, list.length - 1)];
                }

                dialog('从运行中的进程选择', function (body, api) {
                    var searchRow = el('div', 'pp-search-row');
                    searchRow.appendChild(el('span', 'wf-label', '搜索：'));
                    var input = document.createElement('input');
                    input.className = 'wf-input';
                    input.placeholder = '按进程名或路径过滤…';
                    searchRow.appendChild(input);
                    var count = el('div', 'pp-count');
                    searchRow.appendChild(count);
                    body.appendChild(searchRow);

                    body.appendChild(el('div', 'pp-tip',
                        '按进程名或路径匹配，不区分大小写；双击直接选中。'));

                    var listBox = el('div', 'pp-list');
                    var head = el('div', 'pp-head');
                    ['进程名', 'PID', '可执行文件路径'].forEach(function (h) {
                        head.appendChild(el('span', null, h));
                    });
                    listBox.appendChild(head);
                    var rowsHost = el('div');
                    listBox.appendChild(rowsHost);
                    body.appendChild(listBox);

                    function draw() {
                        var list = filteredList();
                        if (selIdx >= list.length) selIdx = list.length - 1;
                        if (selIdx < 0) selIdx = 0;

                        clear(rowsHost);
                        if (list.length === 0) {
                            rowsHost.appendChild(el('div', 'pp-empty', '没有匹配的进程。'));
                        }
                        list.forEach(function (p, i) {
                            var r = el('div', 'pp-row' + (i === selIdx ? ' selected' : ''));
                            r.appendChild(el('span', null, p.name));
                            r.appendChild(el('span', null, String(p.pid)));
                            r.appendChild(el('span', null, p.path));
                            r.onclick = function () { selIdx = i; draw(); };
                            r.ondblclick = function () { pick(list[i]); };
                            rowsHost.appendChild(r);
                        });
                        count.textContent = '共 ' + list.length + ' 个进程';
                    }

                    function pick(p) {
                        api.close();
                        resolve(p ? p.path : null);
                    }

                    input.oninput = function () { filter = input.value; selIdx = 0; draw(); };
                    input.onkeydown = function (e) {
                        var list = filteredList();
                        if (e.key === 'Enter') {
                            e.preventDefault();
                            pick(list[selIdx] || null);
                        } else if (e.key === 'ArrowDown') {
                            e.preventDefault();
                            selIdx = Math.min(selIdx + 1, list.length - 1);
                            draw();
                        } else if (e.key === 'ArrowUp') {
                            e.preventDefault();
                            selIdx = Math.max(selIdx - 1, 0);
                            draw();
                        }
                    };

                    draw();
                    setTimeout(function () { input.focus(); }, 40);
                }, function (api2) {
                    var bar = el('div', 'dlg-buttons');
                    bar.appendChild(btn('刷新', '', function () {
                        toast('已刷新运行中的进程列表（模拟）');
                    }));
                    bar.appendChild(btn('确定', 'primary', function () {
                        var p = currentSelection();
                        api2.close();
                        resolve(p ? p.path : null);
                    }));
                    bar.appendChild(btn('取消', '', function () {
                        api2.close();
                        resolve(null);
                    }));
                    return bar;
                }, { dlgClass: 'proc-picker' });
            });
        }

        function onPullCable() {
            if (ui.pull.phase === 'pulling') return;
            s = core.state();

            // 全程内联：真实程序这里只在分组框里显示倒计时，不弹任何窗口
            if (!s.network.appPath) {
                ui.pull.phase = 'createFailed';
                ui.pull.error = '请先点击"浏览…"或"选择…"指定目标应用。';
                renderAll();
                return;
            }

            var ruleName = s.network.ruleName;
            var procName = s.network.procName;
            var mute = !!s.network.mute;
            var seconds = s.network.pullSeconds || 3;

            // FirewallManager.CreateBlockRule：普通用户模式下会抛 netsh 权限错误
            try {
                core.createBlockRule(ruleName, s.network.appPath);
            } catch (err) {
                ui.pull.phase = 'createFailed';
                ui.pull.error = (err && err.message ? err.message : String(err))
                    .split('\r\n')[0];
                renderAll();
                return;
            }

            ui.pull.phase = 'pulling';
            ui.pull.total = seconds;
            ui.pull.remain = seconds;
            renderAll();

            var left = seconds;
            function tick() {
                ui.pull.remain = left;
                renderAll();
                if (left <= 0) {
                    finish();
                    return;
                }
                left -= 1;
                ui.pull.timer = setTimeout(tick, 1000);
            }

            function finish() {
                try {
                    core.setRuleEnabled(ruleName, false);
                    core.deleteRule(ruleName);
                    ui.pull.phase = 'done';
                    renderAll();
                    toast('已恢复网络：' + procName + '.exe', 'ok');
                } catch (err) {
                    ui.pull.phase = 'restoreFailed';
                    ui.pull.error = err && err.message ? err.message : String(err);
                    renderAll();
                }
            }

            tick();
        }

        /* ============================================================
         *  启动
         *
         *  真实程序在启用"一键拔线"时会请求管理员权限（UAC）。
         *  模拟器不弹这个对话框：容易让人以为是真弹窗，
         *  也可能被理解成"要不要允许某个未知程序提权"，有歧义。
         *  这里直接按"用户已同意"处理，默认以管理员模式运行。
         * ============================================================ */
        function boot() {
            s = core.state();
            s.isAdmin = true;
            core.persist();
            renderAll();
        }

        /** 外部（页面上的模拟控制条）可调用的接口 */
        var controller = {
            render: renderAll,
            boot: boot,
            setTab: function (t) { ui.tab = t; renderAll(); },
            setAdmin: function (v) {
                core.state().isAdmin = !!v;
                core.persist();
                renderAll();
            },
            /** 模拟"重启程序"：按当前设置重新走一遍启动流程 */
            restart: function () {
                var st = core.state();
                // 重启后按当前设置决定以什么模式运行
                st.isAdmin = !st.settings.disablePullout;
                core.persist();
                renderAll();
            }
        };

        renderAll();
        return controller;
    }

    BNS.gui = { render: render };
})(typeof window !== 'undefined' ? window : this);
