# Сборка установщика «Спутника»

Нужны .NET SDK 10 и [Inno Setup 7](https://jrsoftware.org/isdl.php). Выполните в PowerShell из корня репозитория. Укажите версию выпуска и путь к вашему `ISCC.exe`.

```powershell
$version = '1.2.0'
$iscc = "$env:LOCALAPPDATA\Programs\Inno Setup 7\ISCC.exe"
$payload = Join-Path $PWD "artifacts\installer-payload-$version"
$out = Join-Path $PWD "artifacts\installer-$version"

dotnet publish .\Sputnik.csproj -c Release -r win-x64 --self-contained false -o $payload
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $iscc "/DAppVersion=$version" "/DPublishDir=$payload" "/DInstallerOutput=$out" .\installer\Sputnik.iss
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
```

Установщик `SputnikSetup.exe` появится в папке `$out`. Он устанавливает приложение для текущего пользователя в `%LOCALAPPDATA%\Programs\Sputnik`, создаёт ярлык в меню «Пуск» и по выбору на рабочем столе. Обновление использует ту же папку и запись в списке приложений.

Установщик проверяет наличие **.NET Desktop Runtime 10 x64** и **Microsoft Edge WebView2 Runtime**. Если среды нет, мастер сообщит об этом и предложит открыть страницу загрузки Microsoft. После установки недостающих компонентов запустите приложение из меню «Пуск».

Удаление доступно в разделе «Установленные приложения» в параметрах Windows. Пользовательские данные в `%LOCALAPPDATA%\Sputnik` сохраняются.
