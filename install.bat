@echo off
echo Windows Screenshot Uploader Installation Script
echo ============================================
echo.

REM Check if Python is installed
python --version >nul 2>&1
if %errorlevel% neq 0 (
    echo ERROR: Python is not installed or not in PATH
    echo Please install Python 3.8+ from https://python.org
    pause
    exit /b 1
)

echo Python found. Installing dependencies...
echo.

REM Install Python dependencies
pip install -r requirements.txt
if %errorlevel% neq 0 (
    echo ERROR: Failed to install dependencies
    pause
    exit /b 1
)

echo.
echo Dependencies installed successfully!
echo.

REM Create config file from template if it doesn't exist
if not exist "config.json" (
    echo Creating config.json from template...
    copy "config.example.json" "config.json"
    echo.
    echo IMPORTANT: Please edit config.json with your server details before running the application!
    echo.
) else (
    echo config.json already exists, skipping creation.
    echo.
)

REM Create startup shortcut
echo Creating startup shortcut...
set "startup_folder=%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup"
set "current_dir=%~dp0"
set "python_script=%current_dir%src\screenshot_uploader.py"

REM Create a VBS script to run Python silently (no console window)
(
echo Set objShell = CreateObject^("Wscript.Shell"^)
echo objShell.Run "python ""%python_script%""", 0, False
) > "%startup_folder%\ScreenshotUploader.vbs"

if exist "%startup_folder%\ScreenshotUploader.vbs" (
    echo Startup shortcut created successfully!
    echo The application will start automatically when you log in.
) else (
    echo WARNING: Could not create startup shortcut. You may need to run as administrator.
)

echo.
echo Installation complete!
echo.
echo Next steps:
echo 1. Edit config.json with your server details
echo 2. Ensure your SSH key is properly configured
echo 3. Run the application: python src\screenshot_uploader.py
echo 4. Or restart Windows to start automatically
echo.

pause