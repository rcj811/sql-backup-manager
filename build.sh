#!/bin/sh
# 在 Linux 用 Mono 編譯 (輸出可在 Windows .NET Framework 4.8 執行)
set -e
cd "$(dirname "$0")"
R=/usr/lib/mono/4.8-api
mkdir -p bin
mcs -target:winexe -codepage:utf8 -optimize+ -nostdlib -noconfig -nowarn:1702 \
 -r:$R/mscorlib.dll -r:$R/System.dll -r:$R/System.Core.dll -r:$R/System.Data.dll -r:$R/System.Drawing.dll \
 -r:$R/System.Windows.Forms.dll -r:$R/System.Xml.dll -r:$R/System.Security.dll \
 -r:/usr/lib/mono/4.6.2-api/System.IO.Compression.dll -r:$R/System.IO.Compression.FileSystem.dll \
 -win32icon:src/app.ico -out:bin/SqlBackupManager.exe src/*.cs
cp src/App.config bin/SqlBackupManager.exe.config
