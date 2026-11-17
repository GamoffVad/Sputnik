# Сборка установщика «Спутника»

Нужны .NET SDK 10 и [Inno Setup 7](https://jrsoftware.org/isdl.php). Выполните в PowerShell из корня репозитория. Укажите версию выпуска и путь к вашему `ISCC.exe`.

```powershell
$version = '1.3.11'
$iscc = "$env:LOCALAPPDATA\Programs\Inno Setup 7\ISCC.exe"
$payload = Join-Path $PWD "artifacts\installer-payload-$version"
$out = Join-Path $PWD "artifacts\installer-$version"

dotnet publish .\Sputnik.csproj -c Release -r win-x64 --self-contained true -p:Version=$version -o $payload
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& .\installer\Prepare-Payload.ps1 -PublishDir $payload
& $iscc "/DAppVersion=$version" "/DPublishDir=$payload" "/DInstallerOutput=$out" /DVersionedOutput=1 .\installer\Sputnik.iss
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
```

Установщик `SputnikSetup-<версия>-win10-win11-x64.exe` появится в папке `$out`. При первой установке можно выбрать папку. По умолчанию используется `%LOCALAPPDATA%\Programs\Sputnik`. Обновление использует прежнюю папку, ярлыки и запись в списке приложений. В комплекте Клякса, Кудряшка и Рыцарь-дозорный.

Журналы установки сохраняются в `%LOCALAPPDATA%\Sputnik.Desktop\logs`. Файл `.log` содержит этапы установки, файл `.txt` — подробный журнал Inno Setup. Путь к исходному подробному журналу в `%TEMP%` указан в первой строке файла `.log`.

Сборка установщика требует интернета для загрузки зависимостей. Готовый установщик работает офлайн. .NET встроен в приложение, все окна созданы на WPF.

Удаление доступно в разделе «Установленные приложения» в параметрах Windows. Пользовательские данные в `%LOCALAPPDATA%\Sputnik` сохраняются.

## Windows 7 SP1

Отдельная сборка работает на .NET Framework 4.8. В каждом установщике есть автономный пакет .NET Framework 4.8. Для установки .NET Framework может потребоваться пароль администратора и перезагрузка.

На Windows 7 SP1 должны быть установлены обновления [стека обслуживания и поддержки SHA-2](https://support.microsoft.com/help/4474419) и [компонента D3DCompiler](https://support.microsoft.com/help/4019990). Выберите `SputnikSetup-<версия>-win7-x64.exe` для 64-разрядной Windows 7 или `SputnikSetup-<версия>-win7-x86.exe` для 32-разрядной. Клякса выбирается при первом запуске. Папка журналов та же, её можно открыть кнопкой **Журналы установки** в мастере.

Для сборки нужны .NET SDK 10 и Inno Setup 7. Следующая команда собирает оба варианта в `artifacts/win7/v1.3.11`. Параметр `-Architecture x64` или `-Architecture x86` собирает один вариант.

```powershell
.\installer\Build-Windows7.ps1 -Version '1.3.11' `
  -Compiler "$env:LOCALAPPDATA\Programs\Inno Setup 7\ISCC.exe"
```
