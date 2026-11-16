# Сборка установщика «Спутника»

Нужны .NET SDK 10 и [Inno Setup 7](https://jrsoftware.org/isdl.php). Выполните в PowerShell из корня репозитория. Укажите версию выпуска и путь к вашему `ISCC.exe`.

```powershell
$version = '1.2.0'
$iscc = "$env:LOCALAPPDATA\Programs\Inno Setup 7\ISCC.exe"
$payload = Join-Path $PWD "artifacts\installer-payload-$version"
$out = Join-Path $PWD "artifacts\installer-$version"

$webview = Join-Path $PWD 'artifacts/MicrosoftEdgeWebView2RuntimeInstallerX64.exe'
New-Item -ItemType Directory -Force (Split-Path $webview) | Out-Null
Invoke-WebRequest 'https://go.microsoft.com/fwlink/?linkid=2124701' -OutFile $webview
$signature = Get-AuthenticodeSignature -LiteralPath $webview
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'CN=Microsoft Corporation') {
    throw 'Некорректная подпись установщика WebView2.'
}

dotnet publish .\Sputnik.csproj -c Release -r win-x64 --self-contained true -o $payload
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& .\installer\Prepare-Payload.ps1 -PublishDir $payload
& $iscc "/DAppVersion=$version" "/DPublishDir=$payload" "/DInstallerOutput=$out" "/DWebViewInstaller=$webview" .\installer\Sputnik.iss
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
```

Установщик `SputnikSetup.exe` появится в папке `$out`. При первой установке можно выбрать папку. По умолчанию используется `%LOCALAPPDATA%\Programs\Sputnik`. Обновление использует прежнюю папку, ярлыки и запись в списке приложений. В комплекте Клякса и Кудряшка.

Журналы установки сохраняются в `%LOCALAPPDATA%\Sputnik.Desktop\logs`. Файл `.log` содержит этапы установки, файл `.txt` — подробный журнал Inno Setup. Путь к исходному подробному журналу в `%TEMP%` указан в первой строке файла `.log`.

Сборка установщика требует интернета для загрузки зависимостей. Готовый `SputnikSetup.exe` работает офлайн. .NET встроен в приложение. Если WebView2 отсутствует, мастер запускает вложенный автономный установщик Microsoft. При сбое установки WebView2 питомец и заметки доступны, а встроенные настройки и полный чат требуют восстановления WebView2.

Удаление доступно в разделе «Установленные приложения» в параметрах Windows. Пользовательские данные в `%LOCALAPPDATA%\Sputnik` сохраняются.

## Windows 7 SP1

Отдельная сборка работает на .NET Framework 4.8. В каждом установщике есть автономный пакет .NET Framework 4.8 и подписанный Microsoft WebView2 109 для соответствующей разрядности. Мастер устанавливает недостающие компоненты из комплекта. Для установки .NET Framework может потребоваться пароль администратора и перезагрузка.

На Windows 7 SP1 должны быть установлены обновления [стека обслуживания и поддержки SHA-2](https://support.microsoft.com/help/4474419) и [компонента D3DCompiler](https://support.microsoft.com/help/4019990). Выберите `SputnikSetup-win7-x64.exe` для 64-разрядной Windows 7 или `SputnikSetup-win7-x86.exe` для 32-разрядной. Клякса выбирается при первом запуске. Папка журналов та же, её можно открыть кнопкой **Журналы установки** в мастере.

Для сборки нужны .NET SDK 10, Inno Setup 7 и 7-Zip. Следующая команда собирает оба варианта в `artifacts/win7/v1.3.3`. Параметр `-Architecture x64` или `-Architecture x86` собирает один вариант.

```powershell
.\installer\Build-Windows7.ps1 -Version '1.3.3' `
  -Compiler "$env:LOCALAPPDATA\Programs\Inno Setup 7\ISCC.exe" `
  -SevenZip "$env:ProgramFiles\7-Zip\7z.exe"
```
