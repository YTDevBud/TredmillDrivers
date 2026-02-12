@echo off
REM ═══════════════════════════════════════════════════════════════
REM  Build the Treadmill Driver OpenXR API Layer
REM ═══════════════════════════════════════════════════════════════
REM  Run this from a "Developer Command Prompt for VS" (or
REM  "x64 Native Tools Command Prompt for VS 2022").
REM
REM  Alternatively, use CMake:
REM    mkdir build && cd build
REM    cmake .. -G "Visual Studio 17 2022" -A x64
REM    cmake --build . --config Release
REM ═══════════════════════════════════════════════════════════════

setlocal

set SRC=%~dp0treadmill_layer.cpp
set DEF=%~dp0treadmill_layer.def
set OUT=%~dp0bin

if not exist "%OUT%" mkdir "%OUT%"

echo.
echo Building treadmill_layer.dll ...
echo.

cl.exe /nologo /LD /O2 /std:c++17 /EHsc /MT ^
    /I"%~dp0." ^
    "%SRC%" ^
    /Fe:"%OUT%\treadmill_layer.dll" ^
    /Fo:"%OUT%\treadmill_layer.obj" ^
    /link /DEF:"%DEF%" /OUT:"%OUT%\treadmill_layer.dll" ^
    kernel32.lib shell32.lib

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo  BUILD FAILED. Make sure you are running from a
    echo  Developer Command Prompt with x64 tools.
    echo.
    pause
    exit /b 1
)

echo.
echo  ✓  Built successfully:  %OUT%\treadmill_layer.dll
echo.

REM Clean up intermediate files
if exist "%OUT%\treadmill_layer.obj" del "%OUT%\treadmill_layer.obj"
if exist "%OUT%\treadmill_layer.exp" del "%OUT%\treadmill_layer.exp"
if exist "%OUT%\treadmill_layer.lib" del "%OUT%\treadmill_layer.lib"

pause
