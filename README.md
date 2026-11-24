# TouchpadGuard

**敲键盘时自动忽略触摸板**，解决笔记本打字时手掌蹭到触摸板导致光标乱跑、误点的问题。

Windows 自带的「精密触摸板」设置里没有「打字时禁用触摸板」这一项，厂商驱动没装的话也没有对应开关。
这个小工具用一个全局低级钩子补上：**只要你在敲键盘，就忽略指点设备的移动、点击和滚轮；停手约 0.35 秒后自动恢复。**

## 特性

- 常驻托盘，11 KB 单文件，无依赖、不联网、不需要管理员权限、不装驱动
- 恢复延迟可调：200 / 350 / 500 / 800 ms
- 正在按住鼠标拖拽（如拖选文字）时不会打断
- 静默处理按键，不影响输入法或其他快捷键
- 进程退出后钩子自动卸载，不会残留

## 构建

需要 Windows 10/11（自带 .NET Framework 4.x），无需安装 SDK：

```powershell
pwsh -File .\build.ps1
# 产物: .\bin\TouchpadGuard.exe
```

或手动调用编译器：

```powershell
csc /target:winexe /optimize+ /out:TouchpadGuard.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll TouchpadGuard.cs
```

## 使用

1. 运行 `TouchpadGuard.exe`，托盘出现图标（Windows 11 可能收在 `^` 溢出区）。
2. 右键托盘图标：

   | 菜单项 | 说明 |
   | --- | --- |
   | 打字时忽略触摸板/鼠标 | 总开关，双击托盘图标也可以快速切换 |
   | 停手后恢复延迟 | 200 / 350 / 500 / 800 ms |
   | 开机自动启动 | 写入 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` |
   | 退出 | 卸载钩子并结束进程 |

## 自检

```powershell
pwsh -File .\test-guard.ps1
```

脚本会模拟「按一个键后立刻移动鼠标」（应被忽略），再等 1.2 秒移动（应生效），输出 `True` / `True` 即正常。

## 实现原理

- `SetWindowsHookEx(WH_KEYBOARD_LL)` 记录最近一次按键时间戳；
- `SetWindowsHookEx(WH_MOUSE_LL)` 在该时间戳之后的保护窗口内返回 `1` 吞掉事件；若检测到鼠标键仍处于按下状态（拖拽中）则放行；
- 睡眠唤醒、切换桌面后自动补装钩子。

## 限制

- 保护窗口内**所有**指点设备都会被忽略（系统层面无法区分一次移动来自触摸板还是外接鼠标）。正常打字时无感，只有在打字同时不停甩外接鼠标时才会有轻微延迟。
- 只处理输入事件，不修改任何系统设置。

## 可选的系统侧增强（与该工具独立）

- 调低触摸板灵敏度可增强固件级手掌抑制：
  `Set-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad' AAPThreshold 3`（`0`=最敏感 … `3`=低；默认 `2`）
- 关闭「单指轻点以单击」，改用实体按键，可避免手掌轻扫造成误点。

## 设置存放位置

| 项 | 位置 |
| --- | --- |
| 开关、延迟 | `HKCU\Software\TouchpadGuard`（`Enabled`、`DelayMs`） |
| 开机启动 | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` → `TouchpadGuard` |

## 卸载

1. 托盘右键 → 退出；
2. 删除程序目录；
3. `Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' TouchpadGuard`
