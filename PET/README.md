# Питомцы и исходные референсы

Все восемь питомцев имеют отдельную папку с референсом, темой, опорными кадрами, описаниями анимаций и манифестом готовых ресурсов. [Машиночитаемый каталог](catalog.json) · [Результат проверки структуры](validation.json).


## Скачать на Mac

[**Спутник 0.2.4 для macOS — скачать DMG**](https://github.com/GamoffVad/Sputnik/releases/download/macos-v0.2.4/Sputnik-0.2.4-macOS-Universal.dmg) · [Страница выпуска](https://github.com/GamoffVad/Sputnik/releases/tag/macos-v0.2.4)

Образ подходит для **Apple Silicon и Intel, macOS 13+**. Перенесите Sputnik в «Программы». Ссылки публичные: вход в GitHub не нужен.

**Питомцы:** скачайте нужный файл `.pet` в таблице ниже, затем откройте в приложении «Питомцы» → добавление питомца (+) и выберите файл. Для цветов персонажа включите палитру питомца. Клякса уже встроена в Mac-приложение.

| Питомец | Референс PNG | Анимации | Загрузка |
| --- | --- | ---: | --- |
| [Тканевый ниндзя](cloth-ninja/pet.json) | [941×1671](cloth-ninja/reference.png) | 11 | [.pet](https://github.com/GamoffVad/Sputnik/releases/download/pet-cloth-ninja-v1/cloth-ninja.pet) |
| [HUDI](hudi/pet.json) | [346×720](hudi/reference.png) | 1 | [.pet](https://github.com/GamoffVad/Sputnik/releases/download/v1.3.14/hudi.pet) |
| [Клякса](klyaksa/pet.json) | [384×384](klyaksa/reference.png) | 8 | Встроена в приложение |
| [Рыцарь-дозорный](knight-watchman/pet.json) | [384×384](knight-watchman/reference.png) | 5 | [.pet](https://github.com/GamoffVad/Sputnik/releases/download/v1.3.14/knight-watchman.pet) |
| [Кудряшка](kudryashka/pet.json) | [256×256](kudryashka/reference.png) | 4 | [.pet](https://github.com/GamoffVad/Sputnik/releases/download/v1.3.14/kudryashka.pet) |
| [Тихий ниндзя](quiet-ninja/pet.json) | [1280×720](quiet-ninja/reference.png) | 5 | [.pet](https://github.com/GamoffVad/Sputnik/releases/download/pet-quiet-ninja-v2/quiet-ninja.pet) |
| [Песчаный Дозорный](sand-watchman-clean/pet.json) | [941×1671](sand-watchman-clean/reference.png) | 15 | [.pet](https://github.com/GamoffVad/Sputnik/releases/download/pet-sand-watchman-v2/sand-watchman-clean.pet) |
| [Праздничная ёлка](christmas-tree/pet.json) | [960×960](christmas-tree/reference.png) | 10 | [.pet](https://github.com/GamoffVad/Sputnik/releases/download/v1.3.28.21/Christmas-Tree-10.pet) |

```text
PET/<id>/
  README.md
  pet.json
  reference.png
  theme.json
  package-manifest.json
  keyframes/start.png
  keyframes/end.png
  animations/<id>/animation.json
  source/
  renders/
  exports/<id>.pet
```

Референсы сохранены как RGBA PNG без изменения исходного разрешения. У шести питомцев референс совпадает с первым нативным кадром ресурсов. У тканевого ниндзя и Песчаного Дозорного `reference.png` — референс пользователя 941×1671 для генерации; `keyframes/delivery-start.png` и `delivery-end.png` — общая поза готовых анимаций 960×960. Кадр прежних ресурсов Дозорного 720×720 сохранён отдельно в `reference-legacy-frame.png`. Исходное изображение не заменяется кадром другого размера.

`start.png` и `end.png` побайтово совпадают с основным референсом каждого питомца. Это опорные кадры для будущих анимаций, а не заявление о совпадении концов всех старых роликов. У тихого ниндзя проверены 10 общих концов; у тканевого — 22. У Песчаного Дозорного формат v3 использует один общий PNG в начале и конце всех 15 анимаций; требуется Спутник Windows 1.3.16 или новее.

У тканевого ниндзя готовы 11 согласованных анимаций, включая дополнительный танец. Все итоговые кадры зеркальные; сохранены детали, реквизит, эффекты и мягкая тень. [Описание движений](cloth-ninja/ANIMATIONS.md).

Клякса встроена в приложение: её исходные PNG остаются в `wwwroot/assets/klyaksa`, локальные рабочие копии создаются в `PET/klyaksa/source/bundled`. Семь отдельных `.pet` лежат локально в `exports/` соответствующих питомцев и опубликованы среди файлов релиза рядом с установщиками. В Git хранятся референсы, JSON и инструкции; тяжёлые видео, AE-мастера, рендеры и пакеты исключены из Git.

Для восстановления локальных пакетов скачайте `.pet` из выпуска 1.3.14 в отдельную папку и выполните `python PET/organize.py --packages <папка>`. Скрипт проверяет SHA256, прозрачность и размеры референсов, совпадение опорных кадров и соответствие всех анимаций. Он не масштабирует изображения и не запускает генерацию.
