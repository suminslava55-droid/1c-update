@echo off
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0fake-psexec.ps1" %*
