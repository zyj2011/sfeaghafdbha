@echo off
chcp 65001 >nul
title NewEastSideNEL 桌面快捷方式工具

set "TARGET_EXE=%~dp0publish\NewEastSide.UI.exe"
set "DESKTOP=%USERPROFILE%\Desktop"
set "LNK_NAME=NewEastSideNEL.lnk"

if not exist "%TARGET_EXE%" (
    echo [错误] 找不到主程序: %TARGET_EXE%
    echo 请确认 publish\NewEastSide.UI.exe 存在于正确路径。
    pause
    exit /b 1
)

echo 正在创建桌面快捷方式...
echo 目标: %TARGET_EXE%
echo 位置: %DESKTOP%\%LNK_NAME%

powershell -NoProfile -Command ^
    "$s=(New-Object -ComObject WScript.Shell).CreateShortcut('%DESKTOP%\%LNK_NAME%');" ^
    "$s.TargetPath='%TARGET_EXE%';" ^
    "$s.WorkingDirectory='%~dp0publish';" ^
    "$s.Description='NewEastSideNEL - Netease Launcher';" ^
    "$s.Save();"

if exist "%DESKTOP%\%LNK_NAME%" (
    echo.
    echo [完成] 快捷方式已创建到桌面: %LNK_NAME%
) else (
    echo.
    echo [失败] 快捷方式创建失败，请手动创建。
)

pause
