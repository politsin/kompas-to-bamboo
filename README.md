# kompas-bambu

Интеграция KOMPAS-3D v24 Home с Bambu Studio: команда в меню KOMPAS экспортирует активную 3D-деталь/сборку в файл для печати и сразу открывает его в Bambu Studio.

Этот README описывает не только текущий результат, но и рабочий процесс разработки. Важный практический момент: KOMPAS читает не XML из git-репозитория, а установленную копию из своей папки `Libs`. После любого изменения меню или RTW-кода нужно пересобрать проект и переустановить библиотеку.

Рабочая интеграция сейчас сделана как RTW-приложение KOMPAS:

- `KompasBambu.rtw` - нативная 64-битная RTW-библиотека, которую видит меню `Приложения`.
- `KompasBambu.xml` - описание команд меню/панелей для KOMPAS.
- `kompas-bambu.exe` - .NET exporter/launcher, который делает реальный экспорт через COM API KOMPAS и запускает Bambu Studio.

RTW-библиотека намеренно тонкая: она только получает команду KOMPAS и запускает `kompas-bambu.exe` с нужным режимом (`step`, `stl`, `dxf-sketch`, `all-sketches-dxf`). Вся CAD-логика лежит в C# exporter, чтобы не тащить экспорт STEP/STL/DXF в C++ RTW-код.

Практический вывод из отладки меню: одного XML недостаточно. KOMPAS также смотрит экспортируемые функции RTW и кеширует собранные UI-ресурсы в пользовательском `resources.bin`. Установщик поэтому пересобирает RTW, копирует XML и удаляет этот кеш, чтобы меню строилось заново.

## ОБЯЗАТЕЛЬНЫЙ ПРОЦЕСС ДЛЯ ДОБАВЛЕНИЯ ПУНКТА В МЕНЮ KOMPAS

**ЕСЛИ ДОБАВИТЬ КОМАНДУ ТОЛЬКО В `rtw/KompasBambu.xml`, В МЕНЮ ОНА НЕ ПОЯВИТСЯ.** KOMPAS читает установленную копию из `Libs`, загружает RTW и может показывать сохранённое меню из `resources.bin`.

После любого изменения меню выполнять строго в таком порядке:

1. **ПОЛНОСТЬЮ ЗАКРЫТЬ KOMPAS-3D.**
2. Добавить команду во все три места XML: верхний `<appCommand>`, оба `<toolBar>` и все три `<menu>`.
3. Добавить тот же id в `LIBRARYENTRY` файла `rtw/KompasBambuRtw.cpp`.
4. **ЗАПУСТИТЬ УСТАНОВЩИК БЕЗ `-SkipBuild`:**

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\install-rtw-library.ps1
   ```

5. **ОБЯЗАТЕЛЬНО ЗАПУСТИТЬ ПРОВЕРКУ:**

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\check-rtw-install.ps1
   ```

6. Открыть KOMPAS и проверить `Приложения -> Bambu Studio`.

**УСТАНОВЩИК КОПИРУЕТ XML/RTW В `Program Files`, ОБНОВЛЯЕТ РЕГИСТРАЦИЮ ПРОФИЛЯ И УДАЛЯЕТ `%APPDATA%\ASCON\KOMPAS-3D\24\resources.bin`. ИМЕННО УДАЛЕНИЕ ЭТОГО КЭША ЗАСТАВЛЯЕТ KOMPAS ПОСТРОИТЬ НОВОЕ МЕНЮ.**

## Что умеет

Команды в KOMPAS:

- `Приложения -> Bambu Studio -> Bambu STEP`
- `Приложения -> Bambu Studio -> Bambu STEP - new window`
- `Приложения -> Bambu Studio -> Bambu STL`
- `Приложения -> Bambu Studio -> Bambu STL - new window`
- `Приложения -> Bambu Studio -> Laser DXF`
- `Приложения -> Bambu Studio -> Export All Sketches DXF`
- `Приложения -> Bambu Studio -> CAM STEP`

Поведение по умолчанию:

- формат `Bambu STEP`: STEP AP203;
- формат `Bambu STL`: binary STL с заданными параметрами тесселяции;
- экспортируются только тела;
- FDM-результат кладется в папку `fdm` рядом с исходным файлом KOMPAS;
- `CAM STEP` экспортирует STEP AP203 в папку `cnc` рядом с исходным файлом KOMPAS и не запускает Bambu Studio;
- имя экспортированного файла совпадает с именем модели;
- `Bambu STEP` и `Bambu STL` запускают Bambu Studio через `--single-instance`, то есть передают файл в уже открытое окно; если окна нет, Bambu Studio запускается;
- `Bambu STEP - new window` и `Bambu STL - new window` запускают Bambu Studio через `--no-single-instance`, то есть открывают отдельное окно;
- `Laser DXF` экспортирует выбранный эскиз, либо первый эскиз верхней детали, в DXF для лазерной резки;
- `Export All Sketches DXF` экспортирует все эскизы верхней детали в отдельные DXF-файлы.

Для передачи файла в текущее окно мост использует штатный протокол Bambu Studio `WM_COPYDATA` с тем же форматом аргументов, что и `InstanceCheck.cpp`; `WM_DROPFILES` не используется, потому что его получение ещё не означает импорт модели. Для нового окна Bambu Studio 2.8.2.61 запускается с одним путём к модели: её флаги `--single-instance` и `--no-single-instance` завершаются с кодом `-2`. Открытый экземпляр должен использовать тот же путь к EXE.

Пример результата:

```text
C:\Users\polit\YandexDisk\3d\...\fdm\Держалка к стене.step
```

Активный документ должен быть сохранен хотя бы один раз: папка `fdm` или `cnc` создается рядом с файлом детали/сборки.

Для лазера результат пишется отдельно:

```text
C:\Users\polit\YandexDisk\3d\...\laser\Деталь-Эскиз1.dxf
C:\Users\polit\YandexDisk\3d\...\dfx\01-Деталь-Эскиз1.dxf
C:\Users\polit\YandexDisk\3d\...\dfx\02-Деталь-Эскиз2.dxf
```

DXF-экспорт эскиза фильтрует геометрию под резку:

- берутся только 2D-кривые со стилем `1`, то есть основная линия KOMPAS;
- тонкие/вспомогательные линии со стилем `2` не попадают в DXF;
- сейчас копируются отрезки, окружности и дуги;
- сплайны, эллипсы, размеры, текст, осевые и прочие неподдержанные объекты пропускаются.

Исходный эскиз не редактируется: exporter открывает его как 2D-документ, считывает поддержанные кривые, создает временный 2D-фрагмент и сохраняет уже этот временный фрагмент через `ksSaveToDXF`.

Пакетный режим `Export All Sketches DXF` делает то же самое для каждого эскиза верхней детали. Файлы нумеруются префиксом `01-`, `02-` и так далее, чтобы одинаковые имена эскизов не перетирали друг друга. Папка по умолчанию для пакетного режима называется `dfx`.

## Архитектура

`rtw/KompasBambuRtw.cpp`

Нативная DLL с расширением `.rtw`. Экспортирует функции старого RTW API KOMPAS:

- `LIBRARYNAME()` - возвращает имя библиотеки `Bambu Studio`;
- `LIBRARYNAMEW()` и `DisplayLibraryNameW()` - wide-версии имени для современного UI KOMPAS;
- `LIBRARYID()` - возвращает стабильный числовой id;
- `LIBRARYPROTECTNUMBER()`, `LibToolBarId()`, `LibraryBmpBeginID()` - совместимые RTW exports, которые есть у штатных RTW-библиотек KOMPAS и помогают UI-слою корректно обработать приложение;
- `LIBRARYENTRY(unsigned int command)` - вызывается KOMPAS при выборе команды.

`LIBRARYENTRY` мапит команды так:

- `1` -> `kompas-bambu.exe step`
- `3` -> `kompas-bambu.exe step --new-window`
- `2` -> `kompas-bambu.exe stl`
- `4` -> `kompas-bambu.exe stl --new-window`
- `5` -> `kompas-bambu.exe dxf-sketch`
- `6` -> `kompas-bambu.exe all-sketches-dxf`
- `7` -> `kompas-bambu.exe cam`

`rtw/KompasBambu.xml`

XML-описание приложения для UI KOMPAS. Содержит:

- `<application id="APP_KompasBambu" ...>`;
- семь команд: STEP/STL и отдельные варианты открытия в текущем или новом окне Bambu Studio, DXF выбранного эскиза, пакетный DXF всех эскизов и CAM STEP;
- меню `<menu id="APP_KompasBambu">`;
- toolbar trays для `m3d_main` и `a3d_main`.

При установке XML записывается в UTF-16 LE, как штатные XML-файлы KOMPAS.

`Program.cs`

C#/.NET 8 Windows console app. Делает основную работу:

- подключается к запущенному KOMPAS через `KOMPAS.Application.5` и `KOMPAS.Application.7`;
- берет активный 3D-документ;
- строит FDM-путь `<папка модели>\fdm\<имя модели>.step|.stl` и CAM-путь `<папка модели>\cnc\<имя модели>.step`;
- экспортирует через API5 `AdditionFormatParam` + `SaveAsToAdditionFormat`;
- использует API7 converter/document path как fallback;
- запускает Bambu Studio.

В режиме `dxf-sketch` exporter:

- получает эскиз через `ksDocument3D.GetSelectionMng()`; если выбранного эскиза нет, берет первый `o3d_sketch` из `GetPart(pTop_Part).EntityCollection(o3d_sketch)`;
- открывает эскиз через `ksSketchDefinition.BeginEditEx(true)` или fallback `BeginEdit()`;
- проходит объекты 2D-итератором `GetIterator().ksCreateIterator(ALL_OBJ, 0)`;
- читает стиль через `ksDocument2D.ksGetObjectStyle(ref)` и оставляет только основной стиль `1`; тонкий стиль `2` отбрасывается;
- читает параметры через `ksGetObjParam` и структуры `ko_LineSegParam`, `ko_CircleParam`, `ko_ArcByAngleParam`;
- создает временный фрагмент `lt_DocFragment`;
- сохраняет его в DXF через `ksDocument2D.ksSaveToDXF(path)`.

В режиме `all-sketches-dxf` exporter:

- берет все эскизы из `GetPart(pTop_Part).EntityCollection(o3d_sketch)`;
- для каждого эскиза применяет ту же фильтрацию основной геометрии;
- сохраняет результат в `<папка модели>\dfx\NN-<имя модели>-<имя эскиза>.dxf`.

Лог последнего запуска пишется в:

```text
%TEMP%\kompas-bambu.log
```

## Структура репозитория

Рабочие файлы:

- `Program.cs` - exporter/launcher;
- `KompasBambu.csproj` - проект .NET exporter;
- `bridge/` - независимое приложение, которое получает готовый STEP/STL и работает с Bambu Studio;
- `rtw/KompasBambuRtw.cpp` - native RTW shim;
- `rtw/KompasBambu.xml` - UI-команды KOMPAS;
- `build-rtw.bat` - сборка RTW;
- `install-rtw-library.ps1` - установка RTW-интеграции;
- `check-rtw-install.ps1` - проверка, что установленная в KOMPAS копия совпадает с текущей сборкой;
- `uninstall-rtw-library.ps1` - удаление RTW-интеграции;
- `README.md` - это описание.

Каталоги `bin`, `obj`, `dist`, `rtw/bin`, `_sdk_*` являются локальными сборочными/справочными артефактами и не версионируются.

## Сборка

Предпосылки:

- Windows x64;
- KOMPAS-3D v24 Home;
- .NET 8 SDK;
- MinGW с `g++.exe` в `PATH` или в стандартной папке Scoop `%USERPROFILE%\scoop\apps\mingw\current\bin`;
- Bambu Studio.

Exporter:

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -o dist
```

RTW:

```bat
build-rtw.bat
```

Для RTW используется `g++.exe` из MinGW. Результат сборки:

```text
rtw\bin\KompasBambu.rtw
```

Обычно вручную эти две команды запускать не нужно: `install-rtw-library.ps1` делает это сам перед копированием файлов в KOMPAS.

## Установка RTW

Основной установщик:

```powershell
powershell -ExecutionPolicy Bypass -File .\install-rtw-library.ps1
```

Если команда запущена из обычного PowerShell, установщик сам откроет elevated PowerShell через UAC. Это нужно, потому что файлы копируются в `Program Files`, а регистрация пишется в `ProgramData`.

Что делает установщик:

- пересобирает exporter через `dotnet publish`;
- пересобирает RTW через `build-rtw.bat`;
- копирует `KompasBambu.rtw`, `KompasBambu.xml` и файлы `kompas-bambu.exe` в:

```text
C:\Program Files\ASCON\KOMPAS-3D v24 Home\Libs\KompasBambu
```

- добавляет приложение в:

```text
C:\ProgramData\ASCON\KOMPAS-3D\24\Base.kit.config
```

как:

```xml
<Application libType="lbt_rtw_ocx" path="KompasBambu\KompasBambu.rtw" id="APP_KompasBambu" libName="Bambu Studio" displayName="Bambu Studio" autostart="false" />
```

- удаляет старые экспериментальные `KompasBambu.kit.config` и `KompasBambuDummy.kit.config`;
- удаляет старый ручной дубль `id="KompasBambu.rtw"` из пользовательского `kHome.kit.config`;
- добавляет или обновляет `id="APP_KompasBambu"` в пользовательском `kHome.kit.config`, потому что меню конкретной установки KOMPAS берется из пользовательского профиля;
- удаляет старый экспериментальный путь `KompasBambuPlugin.dll` из `UI_AppPaths.config`;
- добавляет или обновляет `APP_KompasBambu` в `UI_AppPaths.config`.
- удаляет пользовательский кеш UI-ресурсов:

```text
%APPDATA%\ASCON\KOMPAS-3D\24\resources.bin
```

Этот файл KOMPAS пересоздает при следующем запуске. Если его оставить после изменения XML/RTW, меню `Приложения` может показывать старый набор пунктов, хотя установленный XML уже правильный.

**ПОСЛЕ УСТАНОВКИ НУЖНО ПЕРЕЗАПУСТИТЬ KOMPAS.**

**НЕ ЗАПУСКАТЬ УСТАНОВКУ ПРИ ОТКРЫТОМ KOMPAS:** он может держать старую RTW-библиотеку загруженной. Надежный порядок такой:

1. Закрыть KOMPAS.
2. Запустить `install-rtw-library.ps1`.
3. Запустить `check-rtw-install.ps1`.
4. Открыть KOMPAS.
5. Проверить меню `Приложения -> Bambu Studio`.

Если менялись команды меню, проверять надо не только файлы в git, а установленную копию:

```powershell
Get-Content "C:\Program Files\ASCON\KOMPAS-3D v24 Home\Libs\KompasBambu\KompasBambu.xml"
```

Именно этот XML читает KOMPAS. Команды должны быть объявлены внутри `<toolBar>`, как в штатных XML-файлах KOMPAS; верхнеуровневые `<appCommand>` могут пройти файловую проверку, но не попасть в меню приложения. Если в репозитории уже есть новые `<appCommand>`/`<appItem>`, а в установленном XML их нет, значит после изменений не запускали `install-rtw-library.ps1` или установка не смогла перезаписать файлы. После изменения `rtw/KompasBambuRtw.cpp` надо также убедиться, что обновился установленный `KompasBambu.rtw`; если KOMPAS держит RTW-файл открытым, закрой KOMPAS и повтори установку.

Быстрая проверка установленного состояния:

```powershell
powershell -ExecutionPolicy Bypass -File .\check-rtw-install.ps1
```

Этот скрипт сравнивает установленный `KompasBambu.xml` с `rtw\KompasBambu.xml`, проверяет пункты меню, регистрацию `APP_KompasBambu` в `Base.kit.config`, правильную пользовательскую регистрацию в `%APPDATA%`, hash установленного RTW и наличие `kompas-bambu.exe`.

Дополнительно проверяются обязательные exports в `KompasBambu.rtw` и отсутствие старого `resources.bin`. Если `resources.bin` найден, это не рабочее состояние после установки: закрой KOMPAS и повтори установку, чтобы UI-ресурсы построились из актуального XML.

Для текущей версии в установленном XML должны быть семь команд:

```xml
<appCommand id="1" title="Bambu STEP" />
<appCommand id="3" title="Bambu STEP - new window" />
<appCommand id="2" title="Bambu STL" />
<appCommand id="4" title="Bambu STL - new window" />
<appCommand id="5" title="Laser DXF" />
<appCommand id="6" title="Export All Sketches DXF" />
<appCommand id="7" title="CAM STEP" />
```

Если повышенный PowerShell не видит `g++.exe`, можно сначала собрать RTW обычной консолью, а затем установить уже собранные файлы:

```powershell
.\build-rtw.bat
powershell -ExecutionPolicy Bypass -File .\install-rtw-library.ps1 -SkipBuild
```

`-SkipBuild` нужен только как обход проблем окружения. Для обычной разработки лучше запускать установщик без него, чтобы установленная библиотека точно соответствовала исходникам.

## Как добавлять новые команды

**ИСПОЛЬЗОВАТЬ ОБЯЗАТЕЛЬНЫЙ ПРОЦЕСС В НАЧАЛЕ README.** Короткая контрольная последовательность:

1. Закрыть KOMPAS.
2. Добавить команду в `rtw/KompasBambu.xml`.
3. Добавить такой же command id в `rtw/KompasBambuRtw.cpp`.
4. Запустить `install-rtw-library.ps1`.
5. Запустить `check-rtw-install.ps1`.
6. Открыть KOMPAS и проверить `Приложения -> Bambu Studio`.

В XML новый пункт должен быть описан в трех местах:

1. Верхнеуровневый `<appCommand id="..." title="..." />`.
2. Ссылка `<appCommand id="..." />` внутри каждого нужного `<toolBar>`.
3. Ссылка `<appItem id="..." />` внутри каждого нужного `<menu>`.

В C++ тот же id должен быть обработан в `LIBRARYENTRY`. RTW также должен продолжать экспортировать совместимый набор функций:

```text
LIBRARYENTRY
LIBRARYID
LIBRARYNAME
LIBRARYNAMEW
DisplayLibraryNameW
LIBRARYPROTECTNUMBER
LibToolBarId
LibraryBmpBeginID
```

Обязательная проверка после изменений:

```powershell
powershell -ExecutionPolicy Bypass -File .\install-rtw-library.ps1
powershell -ExecutionPolicy Bypass -File .\check-rtw-install.ps1
```

**ЕСЛИ В KOMPAS ВИДНО МЕНЬШЕ ПУНКТОВ, ЧЕМ В XML, НЕ ПРОВЕРЯТЬ ИСХОДНИКИ — ПРОВЕРЯТЬ УСТАНОВЛЕННОЕ СОСТОЯНИЕ:**

```powershell
powershell -ExecutionPolicy Bypass -File .\check-rtw-install.ps1
```

**ЧЕК ОБЯЗАН ПОДТВЕРДИТЬ** установленный XML, hash установленного `KompasBambu.rtw`, обязательные exports и отсутствие `%APPDATA%\ASCON\KOMPAS-3D\24\resources.bin`. **ЕСЛИ `resources.bin` ОСТАЛСЯ, KOMPAS МОЖЕТ ПОКАЗЫВАТЬ СТАРОЕ МЕНЮ ДАЖЕ ПРИ ПРАВИЛЬНОМ XML.**

Если забыть обработчик в `LIBRARYENTRY`, пункт может появиться в меню, но не будет выполнять нужное действие. Если забыть переустановку, в KOMPAS вообще не появится новый пункт, даже если git-версия уже правильная.

## Ручной запуск

Ту же логику можно проверить без меню KOMPAS:

```powershell
& "C:\Program Files\ASCON\KOMPAS-3D v24 Home\Libs\KompasBambu\kompas-bambu.exe" step
```

Команды exporter:

```powershell
.\dist\kompas-bambu.exe step
.\dist\kompas-bambu.exe step --new-window
.\dist\kompas-bambu.exe stl
.\dist\kompas-bambu.exe stl --new-window
.\dist\kompas-bambu.exe dxf-sketch
.\dist\kompas-bambu.exe all-sketches-dxf
.\dist\kompas-bambu.exe step export
.\dist\kompas-bambu.exe step --out-dir fdm
.\dist\kompas-bambu.exe cam
.\dist\kompas-bambu.exe cam --out-dir cnc
.\dist\kompas-bambu.exe dxf-sketch --out-dir laser
.\dist\kompas-bambu.exe all-sketches-dxf --out-dir dfx
.\dist\kompas-bambu.exe step --bambu "C:\Program Files\Bambu Studio\bambu-studio.exe"
```

`export` / `--export-only` только экспортирует файл и не запускает Bambu Studio.

## Отдельный Bambu bridge

KOMPAS-часть отвечает за экспорт: FDM-команды создают STEP/STL в `fdm` и передают локальному bridge задание через named pipe. В запросе есть поле `manufacturingTarget: "fdm"`, чтобы bridge в следующем этапе мог маршрутизировать задания по технологии. `CAM STEP` создаёт STEP в `cnc` и bridge не вызывает. Логика поиска окна Bambu, передачи файла и запуска отдельного окна находится в самостоятельном Windows-приложении:

```text
%LOCALAPPDATA%\KompasBambu\Bridge\kompas-bambu-bridge.exe
```

Bridge запускается автоматически при первой задаче и остаётся отдельным процессом. Он пишет структурированный JSONL-журнал рядом с исполняемым файлом: `%LOCALAPPDATA%\KompasBambu\Bridge\bridge-YYYY-MM-DD.jsonl`; результат последней задачи лежит там же в `bridge-status.json`.

## Удаление

```powershell
powershell -ExecutionPolicy Bypass -File .\uninstall-rtw-library.ps1
```

Удаляет `APP_KompasBambu` из `Base.kit.config` и папку:

```text
C:\Program Files\ASCON\KOMPAS-3D v24 Home\Libs\KompasBambu
```

