# Проверка Windows-интерфейса

Запускать из корня репозитория в интерактивном сеансе Windows с .NET SDK 10. Тесты открывают настоящие WPF-окна. Во время прогона не взаимодействуйте с ними. Тестовые заметки, настройки и изображения создаются в `artifacts`; пользовательские данные не заменяются. API использует подменённый HttpMessageHandler, значки сайтов — временный HTTP-сервер на loopback. Отправки сообщений во внешние сервисы нет.

Общий запуск с журналом: `./tests/Run-Checks.ps1`. Добавьте `-Legacy` для проверки .NET Framework x64/x86. Отдельные команды приведены ниже.

`note-snapping-check` проверяет привязку краёв и центров заметок, четыре направления стыковки, ближайшего соседа, масштаб экрана 100–200%, отрицательные координаты мониторов, реальный обработчик `WM_MOVING`, исключение скрытых окон и сохранение позиции после открытия. Команда: `dotnet run --project tests/note-snapping-check/Check.csproj -c Release -- artifacts/note-snapping-check/<новая-папка>`. Пользовательские заметки не используются.

`notification-check` проверяет компактное окно рядом с питомцем и открытым меню, длинный текст, видимость кнопок, отложенное и выполненное напоминание, закрытие без выполнения, связанную заметку, ошибку сохранения и номер версии в меню. Команда: `dotnet run --project tests/notification-check/Check.csproj -c Release -- artifacts/notification-check/current`. Для сборки Windows 7 добавьте `-p:Win7=true -p:PlatformTarget=x64 -p:SputnikDirectory=<каталог net48>`; x86 проверяется отдельно. Вторым аргументом после каталога отчёта можно передать пакет ёлки: он будет установлен только в тестовую библиотеку.

`memory-check` сравнивает пиксели и альфу кадров после отделения от атласа, порядок кадров, ограничение кэша и освобождение предпросмотра. [Описание и методика измерения](../docs/fixes/windows-memory.md).

`pet-removal-check` работает только с синтетической библиотекой в `artifacts`: удаление и возврат питомца, переключение активного на Кляксу, защита встроенного питомца, проверка путей и конфликта восстановления, сохранность байтов PNG и пользовательской палитры. Команда: `dotnet run --project tests/pet-removal-check/Check.csproj -c Release -- artifacts/pet-removal-check/current`.

`application-icons-check` извлекает настоящий значок из тестового EXE с кириллицей и пробелами в пути, проверяет цветной/монохромный предпросмотр, сохранение и повторное открытие формы, список избранного и сферу, явный выбор значка библиотеки, отсутствующий файл и EXE без значка. Тестовую программу не запускает. Команда: `dotnet run --project tests/application-icons-check/Check.csproj -c Release -- artifacts/application-icons-check/current`.

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
