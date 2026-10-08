@echo off
chcp 65001 >nul
REM ============================================================
REM  发布为单个 exe（免安装、自带 .NET 运行时、无需管理员权限）
REM  需要安装 .NET 8 SDK：https://dotnet.microsoft.com/download/dotnet/8.0
REM  输出：publish\Dlt645Reader.exe
REM ============================================================
cd /d "%~dp0"

echo [1/3] 运行单元测试...
dotnet test tests\Dlt645.Core.Tests -c Release
if errorlevel 1 (
    echo 单元测试失败，已停止发布。
    pause
    exit /b 1
)

echo [2/3] 发布单文件 exe...
dotnet publish src\Dlt645.App -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:DebugType=none -p:DebugSymbols=false ^
  -o publish
if errorlevel 1 (
    echo 发布失败。
    pause
    exit /b 1
)

echo [3/3] 完成：%~dp0publish\Dlt645Reader.exe
pause
