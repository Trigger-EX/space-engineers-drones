@echo off
REM Fleet search: which mix of drones (up to 10, any models) beats your ship? Live count, new leaders printed as they appear.
REM Press Enter to stop and save (results\search\leaderboard.csv, then SE_results.zip). Running it again continues the search.
REM   run_search.bat                       vs the first ship in enemies\ (Tech Hunter), 1-10 drones, 10-minute fights
REM   run_search.bat --max-drones 6        run_search.bat --models "Hornet,Gnat,Longbow L"
cd /d "%~dp0"
set /a JOBS=%NUMBER_OF_PROCESSORS%-1
if %JOBS% LSS 1 set JOBS=1
if not exist results\search mkdir results\search
set TRIES=0
:again
out\sim\SimHost.exe search --enemies enemies --cap-seconds 600 --jobs %JOBS% --out results/search %*
set RC=%ERRORLEVEL%
if %RC% GEQ 0 if %RC% LEQ 5 goto done
set /a TRIES+=1
if %TRIES% GTR 50 goto done
echo Simulator crashed (code %RC%); restarting, finished fights are kept.
goto again
:done
call pack_results.bat
