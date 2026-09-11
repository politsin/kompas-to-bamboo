# kompas-bambu

Интеграция KOMPAS-3D v24 Home с Bambu Studio: команда в меню KOMPAS экспортирует активную 3D-деталь/сборку в файл для печати и сразу открывает его в Bambu Studio.

Этот README описывает не только текущий результат, но и рабочий процесс разработки. Важный практический момент: KOMPAS читает не XML из git-репозитория, а установленную копию из своей папки `Libs`. После любого изменения меню или RTW-кода нужно пересобрать проект и переустановить библиотеку.

Рабочая интеграция сейчас сделана как RTW-приложение KOMPAS:

- `KompasBambu.rtw` - нативная 64-битная RTW-библиотека, которую видит меню `Приложения`.
- `KompasBambu.xml` - описание команд меню/панелей для KOMPAS.
- `kompas-bambu.exe` - .NET exporter/launcher, который делает реальный экспорт через COM API KOMPAS и запускает Bambu Studio.

RTW-библиотека намеренно тонкая: она только получает команду KOMPAS и запускает `kompas-bambu.exe` с нужным режимом (`step`, `stl`, `dxf-sketch`, `all-sketches-dxf`). Вся CAD-логика лежит в C# exporter, чтобы не тащить экспорт STEP/STL/DXF в C++ RTW-код.

## Что умеет

Команды в KOMPAS:

- `Приложения -> Bambu Studio -> Bambu STEP`
- `Приложения -> Bambu Studio -> Bambu STEP - new window`
- `Приложения -> Bambu Studio -> Bambu STL`
- `Приложения -> Bambu Studio -> Bambu STL - new window`
- `Приложения -> Bambu Studio -> Laser DXF`
- `Приложения -> Bambu Studio -> Export All Sketches DXF`

Поведение по умолчанию:

- формат `Bambu STEP`: STEP AP203;
- формат `Bambu STL`: binary STL с заданными параметрами тесселяции;
- экспортируются только тела;
- результат кладется в папку `print` рядом с исходным файлом KOMPAS;
- имя экспортированного файла совпадает с именем модели;
- STEP передаёт файл в открытый Bambu Studio (`--single-instance`); если он закрыт, запускает его;
- STEP — новое окно принудительно запускает отдельное окно (`--no-single-instance`);
- STL передаёт файл в Bambu Studio без дополнительных флагов и учитывает настройки Bambu Studio;
- STL — новое окно принудительно запускает отдельное окно (`--no-single-instance`).
- `Laser DXF` экспортирует выбранный эскиз, либо первый эскиз верхней детали, в DXF для лазерной резки;
- `Export All Sketches DXF` экспортирует все эскизы верхней детали в отдельные DXF-файлы.

Для STEP используется штатный механизм Bambu Studio: https://github.com/bambulab/BambuStudio/blob/master/src/slic3r/GUI/InstanceCheck.cpp. Открытый экземпляр должен использовать тот же путь к EXE. Если окон несколько, получателя выбирает Bambu Studio.

Пример результата:

```text
C:\Users\polit\YandexDisk\3d\...\print\Держалка к стене.step
```

Активный документ должен быть сохранен хотя бы один раз: папка `print` создается рядом с файлом детали/сборки.

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
- `LIBRARYID()` - возвращает стабильный числовой id;
- `LIBRARYENTRY(unsigned int command)` - вызывается KOMPAS при выборе команды.

`LIBRARYENTRY` мапит команды так:

- `1` -> `kompas-bambu.exe step`
- `3` -> `kompas-bambu.exe step --new-window`
- `2` -> `kompas-bambu.exe stl`
- `4` -> `kompas-bambu.exe stl --new-window`
- `5` -> `kompas-bambu.exe dxf-sketch`
- `6` -> `kompas-bambu.exe all-sketches-dxf`

`rtw/KompasBambu.xml`

XML-описание приложения для UI KOMPAS. Содержит:

- `<application id="APP_KompasBambu" ...>`;
- шесть команд: STEP/STL и отдельные варианты открытия в текущем или новом окне Bambu Studio, DXF выбранного эскиза и пакетный DXF всех эскизов;
- меню `<menu id="APP_KompasBambu">`;
- toolbar trays для `m3d_main` и `a3d_main`.

При установке XML записывается в UTF-16 LE, как штатные XML-файлы KOMPAS.

`Program.cs`

C#/.NET 8 Windows console app. Делает основную работу:

- подключается к запущенному KOMPAS через `KOMPAS.Application.5` и `KOMPAS.Application.7`;
- берет активный 3D-документ;
- строит путь `<папка модели>\print\<имя модели>.step|.stl`;
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
- удаляет ручные дубли `id="KompasBambu.rtw"` и `id="APP_KompasBambu"` из пользовательского `kHome.kit.config`, если они появились после ручного добавления через UI;
- удаляет старый экспериментальный путь `KompasBambuPlugin.dll` и пользовательский `APP_KompasBambu` из `UI_AppPaths.config`.

После установки нужно перезапустить KOMPAS.

Если KOMPAS открыт во время установки, он может держать старую RTW-библиотеку загруженной. Надежный порядок такой:

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

Этот скрипт сравнивает установленный `KompasBambu.xml` с `rtw\KompasBambu.xml`, проверяет пункты меню, регистрацию `APP_KompasBambu` в `Base.kit.config`, отсутствие пользовательских дублей в `%APPDATA%`, hash установленного RTW и наличие `kompas-bambu.exe`.

Для текущей версии в установленном XML должны быть шесть команд:

```xml
<appCommand id="1" title="Bambu STEP" />
<appCommand id="3" title="Bambu STEP - new window" />
<appCommand id="2" title="Bambu STL" />
<appCommand id="4" title="Bambu STL - new window" />
<appCommand id="5" title="Laser DXF" />
<appCommand id="6" title="Export All Sketches DXF" />
```

Если повышенный PowerShell не видит `g++.exe`, можно сначала собрать RTW обычной консолью, а затем установить уже собранные файлы:

```powershell
.\build-rtw.bat
powershell -ExecutionPolicy Bypass -File .\install-rtw-library.ps1 -SkipBuild
```

`-SkipBuild` нужен только как обход проблем окружения. Для обычной разработки лучше запускать установщик без него, чтобы установленная библиотека точно соответствовала исходникам.

## Как добавлять новые команды

Для нового пункта меню нужно менять две части синхронно:

1. `rtw/KompasBambu.xml` - добавить новый `<appCommand id="...">` внутрь каждого нужного `<toolBar>` и включить этот id в `<menu>/<appItem>`.
2. `rtw/KompasBambuRtw.cpp` - добавить такой же id в `LIBRARYENTRY` и передать нужные аргументы в `kompas-bambu.exe`.

После этого обязательная проверка:

```powershell
powershell -ExecutionPolicy Bypass -File .\install-rtw-library.ps1
powershell -ExecutionPolicy Bypass -File .\check-rtw-install.ps1
```

Если забыть второй шаг, пункт появится в меню, но будет делать не то действие. Если забыть переустановку, в KOMPAS вообще не появится новый пункт, даже если git-версия уже правильная.

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
.\dist\kompas-bambu.exe step --out-dir print
.\dist\kompas-bambu.exe dxf-sketch --out-dir laser
.\dist\kompas-bambu.exe all-sketches-dxf --out-dir dfx
.\dist\kompas-bambu.exe step --bambu "C:\Program Files\Bambu Studio\bambu-studio.exe"
```

`export` / `--export-only` только экспортирует файл и не запускает Bambu Studio.

## Удаление

```powershell
powershell -ExecutionPolicy Bypass -File .\uninstall-rtw-library.ps1
```

Удаляет `APP_KompasBambu` из `Base.kit.config` и папку:

```text
C:\Program Files\ASCON\KOMPAS-3D v24 Home\Libs\KompasBambu
```
