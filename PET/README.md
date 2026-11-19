# Питомцы и исходные референсы

Все семь питомцев имеют отдельную папку с референсом, темой, опорными кадрами, описаниями анимаций и манифестом готовых ресурсов. [Машиночитаемый каталог](catalog.json) · [Результат проверки структуры](validation.json).

| Питомец | Референс PNG | Анимации | Загрузка |
| --- | --- | ---: | --- |
| [Тканевый ниндзя](cloth-ninja/pet.json) | [941×1671](cloth-ninja/reference.png) | 11 | [.pet](https://github.com/GamoffVad/Sputnik/releases/download/v1.3.14/cloth-ninja.pet) |
| [HUDI](hudi/pet.json) | [346×720](hudi/reference.png) | 1 | [.pet](https://github.com/GamoffVad/Sputnik/releases/download/v1.3.14/hudi.pet) |
| [Клякса](klyaksa/pet.json) | [384×384](klyaksa/reference.png) | 8 | [В приложении](https://github.com/GamoffVad/Sputnik/releases/download/v1.3.14/Sputnik-1.3.14-Windows-10-11-x64.exe) |
| [Рыцарь-дозорный](knight-watchman/pet.json) | [384×384](knight-watchman/reference.png) | 5 | [.pet](https://github.com/GamoffVad/Sputnik/releases/download/v1.3.14/knight-watchman.pet) |
| [Кудряшка](kudryashka/pet.json) | [256×256](kudryashka/reference.png) | 4 | [.pet](https://github.com/GamoffVad/Sputnik/releases/download/v1.3.14/kudryashka.pet) |
| [Тихий ниндзя](quiet-ninja/pet.json) | [1280×720](quiet-ninja/reference.png) | 5 | [.pet](https://github.com/GamoffVad/Sputnik/releases/download/v1.3.14/quiet-ninja.pet) |
| [Песчаный дозорный (чёткий контур)](sand-watchman-clean/pet.json) | [720×720](sand-watchman-clean/reference.png) | 5 | [.pet](https://github.com/GamoffVad/Sputnik/releases/download/v1.3.14/sand-watchman-clean.pet) |

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

Референсы сохранены как RGBA PNG без изменения исходного разрешения. У шести существующих питомцев референс совпадает с первым нативным кадром актуальных ресурсов. У тканевого ниндзя `reference.png` — исходный референс 941×1671 для генерации; `keyframes/delivery-start.png` и `delivery-end.png` — общая зеркальная поза готовых анимаций 960×960. Исходное изображение не заменяется кадром другого размера.

`start.png` и `end.png` побайтово совпадают с основным референсом каждого питомца. Это опорные кадры для будущих анимаций, а не заявление о совпадении концов всех старых роликов. У тихого ниндзя проверены 10 общих концов; у тканевого — 22.

У тканевого ниндзя готовы 11 согласованных анимаций, включая дополнительный танец. Все итоговые кадры зеркальные; сохранены детали, реквизит, эффекты и мягкая тень. [Описание движений](cloth-ninja/ANIMATIONS.md).

Клякса встроена в приложение: её исходные PNG остаются в `wwwroot/assets/klyaksa`, локальные рабочие копии создаются в `PET/klyaksa/source/bundled`. Шесть отдельных `.pet` лежат локально в `exports/` соответствующих питомцев и опубликованы среди файлов релиза рядом с установщиками. В Git хранятся референсы, JSON и инструкции; тяжёлые видео, AE-мастера, рендеры и пакеты исключены из Git.

Для восстановления локальных пакетов скачайте `.pet` из выпуска 1.3.14 в отдельную папку и выполните `python PET/organize.py --packages <папка>`. Скрипт проверяет SHA256, прозрачность и размеры референсов, совпадение опорных кадров и соответствие всех анимаций. Он не масштабирует изображения и не запускает генерацию.
