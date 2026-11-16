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
& $iscc "/DAppVersion=$version" "/DPublishDir=$payload" "/DInstallerOutput=$out" "/DWebViewInstaller=$webview" .\installer\Sputnik.iss
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
```

Установщик `SputnikSetup.exe` появится в папке `$out`. Он устанавливает приложение для текущего пользователя в `%LOCALAPPDATA%\Programs\Sputnik`, создаёт ярлык в меню «Пуск» и по выбору на рабочем столе. Обновление использует ту же папку и запись в списке приложений.

Сборка установщика требует интернета для загрузки зависимостей. Готовый `SputnikSetup.exe` работает офлайн. .NET встроен в приложение. Если WebView2 отсутствует, мастер запускает вложенный автономный установщик Microsoft. При сбое установки WebView2 питомец и заметки доступны, а встроенные настройки и полный чат требуют восстановления WebView2.

Удаление доступно в разделе «Установленные приложения» в параметрах Windows. Пользовательские данные в `%LOCALAPPDATA%\Sputnik` сохраняются.
