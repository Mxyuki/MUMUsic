@echo off
rem Start MUMUsic from the source tree. Add --debug to get the web inspector.
start "" "%~dp0.venv\Scripts\pythonw.exe" "%~dp0MUMUsic.pyw" %*
