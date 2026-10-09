@echo off
chcp 65001 >nul
cd /d "%~dp0"
where python >nul 2>nul
if errorlevel 1 (
  echo Python не найден. Установите Python 3.9+ с https://www.python.org/downloads/ ^(галочка "Add python.exe to PATH"^).
  pause
  exit /b 1
)
python -m pip install --quiet --disable-pip-version-check -r requirements.txt
python install.py %*
pause
