@echo off
REM Runs the whole plan (about 1-2 hours on a fast 14-core CPU; stop any time with Ctrl+C, run again to resume).
REM Options:  run.bat -Only verify,full-fresh     run.bat -Jobs 12
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run_plan.ps1" %*
pause
