# App Manager 设计规格

## 概述

一个 Windows 桌面程序，用于集中管理多个本地开发服务程序（Node.js 等）。每个被管理程序拥有独立的 bat 脚本（start/stop/restart），管理工具负责控制这些程序的启停、端口冲突检测、实时状态监控。

## 技术栈

- **框架**: WPF (.NET 8)
- **语言**: C# 12
- **数据存储**: SQLite (via Microsoft.Data.Sqlite)
- **打包**: 单文件发布 (`dotnet publish -p:PublishSingleFile=true`)，目标体积 30-50MB
- **UI 样式**: WPF 原生控件 + 现代扁平风格

---

## 数据模型

### Program 表

| 字段 | 类型 | 说明 |
|------|------|------|
| Id | INTEGER PRIMARY KEY AUTOINCREMENT | 主键 |
| Name | TEXT NOT NULL | 程序名称 |
| StartBat | TEXT | start.bat 路径 |
| StopBat | TEXT | stop.bat 路径 |
| RestartBat | TEXT | restart.bat 路径 |
| ApiPort | INTEGER | API 端口 (如 6000) |
| WebPort | INTEGER | Web/浏览器端口 (如 6001) |
| WsPort | INTEGER | WebSocket 端口 (如 24679) |
| LoginUrl | TEXT | 登录地址 (如 http://localhost:6001) |
| Directory | TEXT | 项目根目录 |
| Status | TEXT | 运行状态: Running / Stopped / Unknown |
| SortOrder | INTEGER | 排序权重，默认 0 |
| CreatedAt | TEXT | 创建时间 (ISO 8601) |
| UpdatedAt | TEXT | 更新时间 (ISO 8601) |

### app.json 格式（扫描导入用）

每个被管理项目可选的 `app.json` 文件：

```json
{
  "name": "PPIOS 产品管理系统",
  "apiPort": 6000,
  "webPort": 6001,
  "wsPort": 24679,
  "loginUrl": "http://localhost:6001",
  "startBat": "start.bat",
  "stopBat": "stop.bat",
  "restartBat": "restart.bat"
}
```

> 所有字段均为可选。扫描时仅导入存在的字段，其余由用户手动补充。

---

## 核心功能

### 1. 程序管理 (CRUD)

- **添加**: 手动填写名称、bat 路径、端口、登录地址等信息
- **编辑**: 修改已有程序的所有字段
- **删除**: 删除程序记录（仅删除数据库记录，不删除实际文件）
- **排序**: 支持拖拽或按钮调整排序

### 2. 扫描导入

- 选择根目录，递归扫描子目录
- 发现 `app.json` 文件则自动导入
- 重复检测：以 Name 或 Directory 判断是否已存在
- 扫描结果预览，用户勾选确认后批量导入

### 3. 启动 / 停止 / 重启

**启动流程**：

1. 端口冲突检测（检查 ApiPort、WebPort、WsPort 是否被占用）
2. 若端口冲突 → 弹窗告警，显示占用进程 PID 和名称
3. 扫描相邻空闲端口（当前端口 ± 范围内查找），给出可用端口建议列表
4. 用户确认后执行 start.bat（通过 `Process.Start` 启动 cmd）
5. 延迟 2 秒后开始轮询验证进程是否成功启动

**停止流程**：

1. 若存在 stop.bat → 执行 stop.bat
2. 否则按窗口标题 `taskkill` 杀进程（窗口标题格式: `{Name}-Server`）
3. 轮询确认进程已退出

**重启流程**：

1. 执行停止 → 等待确认退出 → 执行启动

**进程追踪方式**：

- 启动 bat 时指定窗口标题: `start "{Name}-Server" /min cmd /c "{startBat}"`
- 通过窗口标题匹配找到对应进程
- 定期轮询（2 秒间隔）检查进程存活状态

### 4. 端口冲突检测

**检测时机**：

- 启动前自动检测
- 手动刷新时检测

**检测实现**：

- 使用 `System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners()` 获取所有监听端口
- 对比目标程序的 ApiPort、WebPort、WsPort

**冲突告警内容**：

```
端口 6000 已被占用
  进程: node.exe (PID: 12345)

建议可用端口: 6002, 6003, 6004
```

**建议端口算法**：

- 从目标端口号开始，上下各扫描 10 个端口
- 列出所有空闲端口，取最近的 3 个作为建议

### 5. 实时状态监控

- 每 2 秒轮询所有已记录程序的进程存活状态
- 状态显示于列表：🟢 Running / 🔴 Stopped / ⚪ Unknown
- 程序退出/崩溃自动检测，状态变更为 Stopped

### 6. 系统托盘

- 关闭主窗口 → 最小化到系统托盘
- 托盘图标右键菜单:
  - 显示主窗口
  - 启动全部 / 停止全部
  - 退出
- 托盘图标 ToolTip 显示运行中程序数量

---

## 界面布局

```
┌──────────────────────────────────────────────────┐
│  程序管理器                         [_] [□] [×]  │
├──────────────────┬───────────────────────────────┤
│  [添加] [扫描]   │                               │
│  [刷新] [全部启动]│        详情面板                │
├──────────────────┤                               │
│                  │  名称: PPIOS 产品管理系统      │
│  ┌────────────┐  │  API 端口: 6000               │
│  │🟢 程序A    │  │  Web 端口: 6001    [复制URL]  │
│  │🔴 程序B    │  │  WS 端口:  24679              │
│  │🟢 程序C    │  │  登录地址: http://...         │
│  │⚪ 程序D    │  │  目录: F:\codex\PPISO-2       │
│  │            │  │                               │
│  │            │  │  端口状态: ✓ 全部正常          │
│  │            │  │                               │
│  │            │  │  [启动] [停止] [重启]          │
│  │            │  │  [编辑] [删除]                 │
│  └────────────┘  │                               │
└──────────────────┴───────────────────────────────┘
```

### 左侧列表

- ListBox 显示所有程序
- 每项显示：状态图标 + 程序名称
- 点击选中，右侧显示详情
- 悬停显示完整信息 Tooltip

### 右侧详情面板

- 上半部分：程序详细信息（名称、端口、地址等）
- 登录地址旁有 [复制URL] 按钮
- 端口状态区域：显示每个端口的占用情况
- 下半部分：操作按钮 [启动] [停止] [重启] [编辑] [删除]

---

## 项目结构

```
appmanager/
├── AppManager.sln
├── src/
│   └── AppManager/
│       ├── AppManager.csproj
│       ├── App.xaml / App.xaml.cs
│       ├── MainWindow.xaml / MainWindow.xaml.cs
│       ├── Models/
│       │   └── ProgramEntry.cs
│       ├── Services/
│       │   ├── DatabaseService.cs      # SQLite CRUD
│       │   ├── ProcessService.cs       # 进程管理 (启动/停止/监控)
│       │   ├── PortChecker.cs          # 端口检测
│       │   └── ScannerService.cs       # 目录扫描导入
│       ├── ViewModels/
│       │   ├── MainViewModel.cs        # 主窗口 VM
│       │   └── ProgramViewModel.cs     # 单个程序 VM
│       ├── Views/
│       │   └── （XAML 视图文件）
│       └── Helpers/
│           └── （工具类）
└── docs/
    └── superpowers/
        ├── specs/                      # 设计文档
        └── plans/                      # 实现计划
```

---

## 关键实现细节

### 进程管理

启动 bat 必须指定窗口标题，格式为 `{Name}-Server`：

```csharp
var startInfo = new ProcessStartInfo
{
    FileName = "cmd.exe",
    Arguments = $"/c start \"{name}-Server\" /min cmd /c \"{batPath}\"",
    WorkingDirectory = directory,
    CreateNoWindow = true,
    UseShellExecute = false
};
Process.Start(startInfo);
```

停止时通过窗口标题查找进程：

```csharp
var processes = Process.GetProcesses()
    .Where(p => p.MainWindowTitle.Contains($"{name}-Server"))
    .ToList();
foreach (var p in processes) p.Kill();
```

### 进程存活检测

```csharp
bool IsRunning(string name)
{
    return Process.GetProcesses()
        .Any(p => p.MainWindowTitle.Contains($"{name}-Server") && !p.HasExited);
}
```

### 端口检测

```csharp
var listeners = IPGlobalProperties.GetIPGlobalProperties()
    .GetActiveTcpListeners();
bool isOccupied = listeners.Any(l => l.Port == targetPort);
```

**获取占用进程信息**：使用 `netstat -ano` 命令输出解析，或调用 `GetExtendedTcpTable` (iphlpapi.dll)。

### 复制 URL

使用 `Clipboard.SetText(url)` 一键复制登录地址到剪贴板。

---

## 约束

- 仅支持 Windows 平台
- 不修改被管理程序的端口配置，仅检测和建议
- 不创建 bat 文件，仅使用已有的 bat
- 数据库文件存放于 `%AppData%\AppManager\data.db`

---

## 成功标准

1. 能不限制数量地添加/管理程序
2. 能通过 bat 脚本正确启动、停止、重启程序
3. 能实时显示各程序运行状态（Running/Stopped）
4. 启动前自动检测端口冲突并给出建议
5. 能从目录扫描 app.json 批量导入
6. 支持系统托盘最小化
