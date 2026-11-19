# Отдельные питомцы

Установщик Windows включает только **Кляксу**. Остальные питомцы находятся в **Assets / Файлы** [последнего релиза](https://github.com/GamoffVad/Sputnik/releases/latest), рядом с установщиками:

| Файл | Питомец |
| --- | --- |
| [hudi.pet](https://github.com/GamoffVad/Sputnik/releases/download/v1.3.14/hudi.pet) | HUDI |
| [kudryashka.pet](https://github.com/GamoffVad/Sputnik/releases/download/v1.3.14/kudryashka.pet) | Кудряшка |
| [sand-watchman-clean.pet](https://github.com/GamoffVad/Sputnik/releases/download/v1.3.14/sand-watchman-clean.pet) | Песчаный дозорный, чёткий контур |
| [knight-watchman.pet](https://github.com/GamoffVad/Sputnik/releases/download/v1.3.14/knight-watchman.pet) | Рыцарь-дозорный |
| [quiet-ninja.pet](https://github.com/GamoffVad/Sputnik/releases/download/v1.3.14/quiet-ninja.pet) | Тихий ниндзя |
| [cloth-ninja.pet](https://github.com/GamoffVad/Sputnik/releases/download/v1.3.14/cloth-ninja.pet) | Тканевый ниндзя, 11 анимаций |

[Все семь питомцев с исходными референсами и структурой проекта](../../PET/README.md). Клякса входит в приложение и не требует отдельного `.pet`.

Скачайте `.pet`, откройте двойным щелчком либо через **Питомцы → Добавить питомца** и подтвердите установку в предпросмотре. У каждого пакета своя палитра. Флажок **Применить палитру питомца** включает цвета приложения из пакета. Для повторного импорта доступно обновление с резервной копией предыдущей версии в `%LOCALAPPDATA%\Sputnik\pet-backups`.

`bundled-source.json` описывает исходные ресурсы прежней библиотеки для воспроизводимой сборки отдельных пакетов; он не включается в приложение. `Build-Pet-Packages.ps1` переносит исходные PNG без изменения разрешения. Большие `.pet` хранятся в файлах релиза, а не в Git.

Пакет «Тихий ниндзя» использует формат 2 с областью отображения исходных кадров 1280×720. Для него нужен **Спутник Windows 1.3.13 или новее**; рекомендуется актуальная 1.3.14. Поддержка этого пакета в macOS 0.2.2 отсутствует.

«Тканевый ниндзя» проверен в **Спутнике Windows 1.3.14**; macOS 0.2.2 его не поддерживает. [Отдельный выпуск питомца, превью и QA](https://github.com/GamoffVad/Sputnik/releases/tag/pet-cloth-ninja-v1).
