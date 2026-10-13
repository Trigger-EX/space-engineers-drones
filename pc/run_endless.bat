@echo off
REM Endless simulation: fights every matchup round after round with a live count. Press Enter to stop and save
REM (fights in progress finish; Enter twice = stop at once). Running it again continues with new seeds.
REM   run_endless.bat                 your ships in enemies\ vs 1..24 of every drone ("1vX")
REM   run_endless.bat --kind 1v1      any other selection instead (same options as SimHost run)
cd /d "%~dp0"
set /a JOBS=%NUMBER_OF_PROCESSORS%-1
if %JOBS% LSS 1 set JOBS=1
set SEL=--enemies enemies --kind enemy --enemy-counts 1,2,3,4,6,8,12,16,24
if not "%~1"=="" set SEL=--enemies enemies %*
if not exist results\endless mkdir results\endless
set TRIES=0
:again
out\sim\SimHost.exe run --endless %SEL% --jobs %JOBS% --out results/endless
set RC=%ERRORLEVEL%
if %RC% GEQ 0 if %RC% LEQ 5 goto done
set /a TRIES+=1
if %TRIES% GTR 50 goto done
echo Simulator crashed (code %RC%); restarting, finished fights are kept.
goto again
:done
call pack_results.bat
