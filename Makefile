# ============================================================
#  BattleNetSwitcher — 顶层构建 Makefile
#
#  用法：
#    make                 = make all，构建 GUI + CLI（debug 产物，供开发）
#    make gui             仅构建 GUI
#    make cli             仅构建 CLI
#    make publish         发布全部 4 种变体（CI 用这个）
#    make publish-gui-sc  GUI 自包含单文件
#    make publish-cli-sc  CLI 自包含单文件
#    make publish-gui-fxdep  GUI 依赖运行时
#    make publish-cli-fxdep  CLI 依赖运行时
#    make publish-package-fxdep  把 fxdep 两个目录压成 zip
#    make clean           清理 build/ 与 obj/bin
#
#  变量覆盖：
#    make publish CONFIG=Debug
#    make publish RID=win-arm64
# ============================================================

DOTNET      ?= dotnet
CONFIG      ?= Release
RID         ?= win-x64

BUILD_DIR   := build
GUI_OUT     := $(BUILD_DIR)/gui
CLI_OUT     := $(BUILD_DIR)/cli
PUBLISH_DIR := $(BUILD_DIR)/publish

GUI_PROJ    := BattleNetSwitcher/BattleNetSwitcher.csproj
CLI_PROJ    := BattleNetSwitcher.Cli/BattleNetSwitcher.Cli.csproj

DOTNET_FLAGS := -c $(CONFIG) -r $(RID)

# 自包含单文件参数（gui-sc / cli-sc 共用）
PUBLISH_SC_FLAGS := --self-contained true \
                    -p:PublishSingleFile=true

.DEFAULT_GOAL := all

.PHONY: all gui cli publish \
        publish-gui-sc publish-cli-sc publish-gui-fxdep publish-cli-fxdep \
        publish-package-fxdep \
        clean help

# ------------------------------------------------------------
# 构建（开发用）
# ------------------------------------------------------------
all: gui cli

gui:
	@echo ">>> 构建 GUI: $(GUI_PROJ)  [$(CONFIG)/$(RID)] -> $(GUI_OUT)"
	$(DOTNET) build "$(GUI_PROJ)" $(DOTNET_FLAGS) -o "$(GUI_OUT)"

cli:
	@echo ">>> 构建 CLI: $(CLI_PROJ)  [$(CONFIG)/$(RID)] -> $(CLI_OUT)"
	$(DOTNET) build "$(CLI_PROJ)" $(DOTNET_FLAGS) -o "$(CLI_OUT)"

# ------------------------------------------------------------
# 发布（生成发布物，与 CI 一致）
# ------------------------------------------------------------
publish: publish-gui-sc publish-cli-sc publish-gui-fxdep publish-cli-fxdep

publish-gui-sc:
	@echo ">>> 发布 GUI（自包含单文件）-> $(PUBLISH_DIR)/gui-sc"
	$(DOTNET) publish "$(GUI_PROJ)" $(DOTNET_FLAGS) $(PUBLISH_SC_FLAGS) \
	  -o "$(PUBLISH_DIR)/gui-sc"

publish-cli-sc:
	@echo ">>> 发布 CLI（自包含单文件）-> $(PUBLISH_DIR)/cli-sc"
	$(DOTNET) publish "$(CLI_PROJ)" $(DOTNET_FLAGS) $(PUBLISH_SC_FLAGS) \
	  -o "$(PUBLISH_DIR)/cli-sc"

publish-gui-fxdep:
	@echo ">>> 发布 GUI（依赖运行时）-> $(PUBLISH_DIR)/gui-fxdep"
	$(DOTNET) publish "$(GUI_PROJ)" $(DOTNET_FLAGS) \
	  --self-contained false -o "$(PUBLISH_DIR)/gui-fxdep"

publish-cli-fxdep:
	@echo ">>> 发布 CLI（依赖运行时）-> $(PUBLISH_DIR)/cli-fxdep"
	$(DOTNET) publish "$(CLI_PROJ)" $(DOTNET_FLAGS) \
	  --self-contained false -o "$(PUBLISH_DIR)/cli-fxdep"

# 把两个 fxdep 目录打成 zip（供 Release 上传，目录形态无法单附件上传）
publish-package-fxdep: publish-gui-fxdep publish-cli-fxdep
	@echo ">>> 打包 fxdep -> $(PUBLISH_DIR)/BattleNetSwitcher-fxdep.zip"
	cd "$(PUBLISH_DIR)" && zip -r BattleNetSwitcher-fxdep.zip gui-fxdep cli-fxdep
	@ls -la "$(PUBLISH_DIR)/BattleNetSwitcher-fxdep.zip"

# ------------------------------------------------------------
# 清理
# ------------------------------------------------------------
clean:
	@echo ">>> 清理 $(BUILD_DIR)/ 与项目 obj/bin"
	-$(DOTNET) clean "$(GUI_PROJ)" $(DOTNET_FLAGS)
	-$(DOTNET) clean "$(CLI_PROJ)" $(DOTNET_FLAGS)
	-rm -rf "$(BUILD_DIR)"

# ------------------------------------------------------------
# 帮助
# ------------------------------------------------------------
help:
	@echo "BattleNetSwitcher 构建目标："
	@echo "  make                     构建 GUI + CLI（开发产物）"
	@echo "  make all                 同上"
	@echo "  make gui                 仅构建 GUI (WinForms)"
	@echo "  make cli                 仅构建 CLI"
	@echo "  make publish             发布全部 4 种变体（CI 使用）"
	@echo "  make publish-gui-sc      GUI 自包含单文件"
	@echo "  make publish-cli-sc      CLI 自包含单文件"
	@echo "  make publish-gui-fxdep   GUI 依赖运行时"
	@echo "  make publish-cli-fxdep   CLI 依赖运行时"
	@echo "  make publish-package-fxdep  打包 fxdep 目录为 zip"
	@echo "  make clean               清理 build/ 与 obj/bin"
	@echo "  make help                显示本帮助"
	@echo ""
	@echo "可选变量（命令行覆盖）："
	@echo "  CONFIG=Debug|Release     默认 Release"
	@echo "  RID=win-x64|win-arm64    默认 win-x64"
	@echo "  DOTNET=dotnet            可指定 dotnet 路径"
