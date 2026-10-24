# Order Trader v1.0 — 交叉编译（MinGW-w64）
# 产物：dist/OrderTrader.exe（单文件，静态链接，无额外 DLL 依赖）

CXX      := x86_64-w64-mingw32-g++
WINDRES  := x86_64-w64-mingw32-windres

SRC_DIR  := src
BUILD    := build
DIST     := dist
TARGET   := $(DIST)/OrderTrader.exe
RES_OBJ  := $(BUILD)/app.res.o

SOURCES  := $(wildcard $(SRC_DIR)/*.cpp) $(wildcard $(SRC_DIR)/*/*.cpp)
OBJECTS  := $(patsubst $(SRC_DIR)/%.cpp,$(BUILD)/%.o,$(SOURCES))
DEPS     := $(OBJECTS:.o=.d)

CXXFLAGS ?= -std=c++17 -O2 -DUNICODE -D_UNICODE -D_WIN32_WINNT=0x0A00 -Wall -Wextra -MMD -MP
LDFLAGS  := -mwindows -static -static-libgcc -static-libstdc++ -Wl,-s
LDLIBS   := -lgdi32 -luser32 -lshell32

VERSION  := 1.0
PKG_DIR  := $(DIST)/order-trader-v$(VERSION)
PKG_ZIP  := $(DIST)/order-trader-v$(VERSION)-win-x64.zip
RUNTIME  := ../runtime/python-win-x64
# 与内置运行时同版本（3.11.x）的 Python，仅用于预编译字节码
PY311    ?= python3.11

.PHONY: all clean package runtime pycache

all: $(TARGET)

pycache:
	@if command -v $(PY311) >/dev/null 2>&1 && [ -d "$(RUNTIME)/Lib/site-packages" ]; then \
		echo "预编译 Python 字节码（加速首次启动）..."; \
		$(PY311) -m compileall -q -j 8 --invalidation-mode unchecked-hash "$(RUNTIME)/Lib/site-packages" || \
		echo "跳过字节码预编译：$(PY311) 不可用"; \
	fi

runtime:
	bash scripts/build-runtime.sh

$(TARGET): $(OBJECTS) $(RES_OBJ)
	@mkdir -p $(DIST)
	$(CXX) $(OBJECTS) $(RES_OBJ) $(LDFLAGS) $(LDLIBS) -o $@
	@echo "已生成 $@"

$(BUILD)/%.o: $(SRC_DIR)/%.cpp
	@mkdir -p $(dir $@)
	$(CXX) $(CXXFLAGS) -I$(SRC_DIR) -c $< -o $@

$(RES_OBJ): res/app.rc res/app.manifest res/app.ico
	@mkdir -p $(BUILD)
	$(WINDRES) -I res -i res/app.rc -o $@

package: $(TARGET) pycache
	rm -rf $(PKG_DIR)
	mkdir -p $(PKG_DIR)/bridge $(PKG_DIR)/config $(PKG_DIR)/docs
	cp $(TARGET) $(PKG_DIR)/
	cp bridge/tqsdk_bridge.py $(PKG_DIR)/bridge/
	cp config/order-trader.json $(PKG_DIR)/config/
	cp docs/使用说明.txt $(PKG_DIR)/docs/ 2>/dev/null || true
	cp tools/检查环境.bat $(PKG_DIR)/
	if [ -d "$(RUNTIME)" ]; then \
		echo "打包内置 Python 运行时..."; \
		cp -a "$(RUNTIME)" $(PKG_DIR)/python; \
	else \
		echo "警告：未找到 $(RUNTIME)，发布包需目标机安装 Python 3 与 tqsdk"; \
	fi
	cd $(DIST) && rm -f $(notdir $(PKG_ZIP)) && zip -qr $(notdir $(PKG_ZIP)) $(notdir $(PKG_DIR))
	@echo "完成：$(PKG_ZIP)"

clean:
	rm -rf $(BUILD) $(DIST)

-include $(DEPS)
