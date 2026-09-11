@echo off
title Subnautica VR - Lancement 6DOF (LordMadTrix)
chcp 65001 >nul
echo Demarrage de Subnautica en Realite Virtuelle 6DOF...
tasklist | findstr /i "vrserver.exe" >nul || start "" "steam://run/250820"
start "" "SubnauticaVR-Setup.exe"
exit
