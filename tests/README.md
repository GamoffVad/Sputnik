# Проверка Windows-интерфейса

Запускать из корня репозитория в интерактивном сеансе Windows с .NET SDK 10. Тесты открывают настоящие WPF-окна. Во время прогона не взаимодействуйте с ними. Тестовые заметки, настройки и изображения создаются в `artifacts`; пользовательские данные не заменяются. API использует подменённый HttpMessageHandler, значки сайтов — временный HTTP-сервер на loopback. Отправки сообщений во внешние сервисы нет.

Общий запуск с журналом: `./tests/Run-Checks.ps1`. Добавьте `-Legacy` для проверки .NET Framework x64/x86. Отдельные команды приведены ниже.

```powershell
dotnet build Sputnik.csproj -c Release
dotnet run --project tests/workspace-check/Check.csproj -c Release -- --components
dotnet run --no-build --project tests/workspace-check/Check.csproj -c Release
dotnet run --no-build --project tests/workspace-check/Check.csproj -c Release -- --appearance-only
dotnet run --no-build --project tests/workspace-check/Check.csproj -c Release -- --notes-only
dotnet run --no-build --project tests/workspace-check/Check.csproj -c Release -- --sphere-preview-only
dotnet run --no-build --project tests/workspace-check/Check.csproj -c Release -- --glass-icons-only
dotnet run --no-build --project tests/workspace-check/Check.csproj -c Release -- --clipping-only
dotnet run --project tests/fixed-area-check/Check.csproj -c Release
dotnet run --project tests/neutral-pose-check/Check.csproj -c Release
```

`--components` проверяет смену страниц попиксельным сравнением, CRUD избранного и напоминаний, форматирование и сохранение заметок, ссылки отправки, ответы API, импорт питомцев, загрузку значков, горячие клавиши и состояния быстрого чата. Остальные режимы проверяют размеры окон, автоцвет, сохранение настроек, размещение меню, сферы и анимации. Временная комбинация Ctrl+Alt+Shift+F24 снимается после теста; автозапуск и пользовательские сочетания не меняются.

Проверка сборки для Windows 7 на установленном .NET Framework 4.8:

```powershell
dotnet build Sputnik.csproj -c Release -p:Win7=true -p:PlatformTarget=x64 -p:BaseIntermediateOutputPath=artifacts/win7/obj-x64/
dotnet run --project tests/legacy-smoke/Check.csproj -c Release -p:PlatformTarget=x64
dotnet build Sputnik.csproj -c Release -p:Win7=true -p:PlatformTarget=x86 -p:BaseIntermediateOutputPath=artifacts/win7/obj-x86/
dotnet run --project tests/legacy-smoke/Check.csproj -c Release -p:PlatformTarget=x86
```

Этот прогон не заменяет проверку на настоящих Windows 7/10, реальных API-провайдерах и клиентах соцсетей. Файловые диалоги, автозапуск после входа в Windows и аппаратные конфигурации нескольких мониторов требуют отдельной ручной проверки.

Проверка общей опоры в `fixed-area-check` использует Кляксу из приложения и шесть пакетов `PET/<id>/exports/<id>.pet`. Импорт выполняется в отдельную тестовую библиотеку в `artifacts`, пользовательская библиотека и `workspace.json` не изменяются. Проверяется реальное переключение каждого идентификатора без подмены отсутствующего питомца Кляксой.

`neutral-pose-check` импортирует пакет v3 с 42 записями и проверяет байтовую сохранность исходных PNG, отклонение некорректных путей, областей и архивов сверх лимита. Настоящий WPF-рендер сравнивает все пиксели первых и последних кадров для двух разных исходных разрешений и viewport на чёрном/белом фоне в четырёх размерах отображения. Промежуточные кадры проверяются отдельно, чтобы исключить замену всей анимации статичным изображением.

Переходы проверяются на перекрывающихся непрозрачных и полупрозрачных цветных участках: premultiplied-цвет и альфа должны смешиваться линейно, фон остаётся прозрачным. Проверяются границы `blendFrames` и сохранение явного нуля.
