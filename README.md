# kompas-bambu

Интеграция KOMPAS-3D v24 Home с Bambu Studio: команда в меню KOMPAS экспортирует активную 3D-деталь/сборку в файл для печати и сразу открывает его в Bambu Studio.

Этот README описывает не только текущий результат, но и рабочий процесс разработки. Важный практический момент: KOMPAS читает не XML из git-репозитория, а установленную копию из своей папки `Libs`. После любого изменения меню или RTW-кода нужно пересобрать проект и переустановить библиотеку.

Рабочая интеграция сейчас сделана как RTW-приложение KOMPAS:

- `KompasBambu.rtw` - нативная 64-битная RTW-библиотека, которую видит меню `Приложения`.
- `KompasBambu.xml` - описание команд меню/панелей для KOMPAS.
- `kompas-bambu.exe` - .NET exporter/launcher, который делает реальный экспорт через COM API KOMPAS и запускает Bambu Studio.

RTW-библиотека намеренно тонкая: она только получает команду KOMPAS и запускает `kompas-bambu.exe` с нужным режимом (`step`, `stl`, `dxf-sketch`). Вся CAD-логика лежит в C# exporter, чтобы не тащить экспорт STEP/STL/DXF в C++ RTW-код.

## Что умеет

Команды в KOMPAS:

- `Приложения -> Bambu Studio -> Bambu STEP`
- `Приложения -> Bambu Studio -> Bambu STEP — новое окно`
- `Приложения -> Bambu Studio -> Bambu STL`
- `Приложения -> Bambu Studio -> Bambu STL — новое окно`
- `Приложения -> Bambu Studio -> Laser DXF`

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
- `Laser DXF` экспортирует выбранный эскиз, либо первый эскиз верхней детали, в DXF для лазерной резки.

Для STEP используется штатный механизм Bambu Studio: https://github.com/bambulab/BambuStudio/blob/master/src/slic3r/GUI/InstanceCheck.cpp. Открытый экземпляр должен использовать тот же путь к EXE. Если окон несколько, получателя выбирает Bambu Studio.

Пример результата:

```text
C:\Users\polit\YandexDisk\3d\...\print\Держалка к стене.step
```

Активный документ должен быть сохранен хотя бы один раз: папка `print` создается рядом с файлом детали/сборки.

Для лазера результат пишется отдельно:

```text
C:\Users\polit\YandexDisk\3d\...\laser\Деталь-Эскиз1.dxf
```

DXF-экспорт эскиза фильтрует геометрию под резку:

- берутся только 2D-кривые со стилем `1`, то есть основная линия KOMPAS;
- тонкие/вспомогательные линии со стилем `2` не попадают в DXF;
- сейчас копируются отрезки, окружности и дуги;
- сплайны, эллипсы, размеры, текст, осевые и прочие неподдержанные объекты пропускаются.

Исходный эскиз не редактируется: exporter открывает его как 2D-документ, считывает поддержанные кривые, создает временный 2D-фрагмент и сохраняет уже этот временный фрагмент через `ksSaveToDXF`.

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

`rtw/KompasBambu.xml`

XML-описание приложения для UI KOMPAS. Содержит:

- `<application id="APP_KompasBambu" ...>`;
- пять команд: STEP/STL и отдельные варианты открытия в текущем или новом окне Bambu Studio, плюс DXF эскиза для лазерной резки;
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

Запускать от администратора, потому что файлы копируются в `Program Files`, а регистрация пишется в `ProgramData`.

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
- удаляет ручной дубль `id="KompasBambu.rtw"` из пользовательского `kHome.kit.config`, если он появился после ручного добавления через UI.

После установки нужно перезапустить KOMPAS.

Если KOMPAS открыт во время установки, он может держать старую RTW-библиотеку загруженной. Надежный порядок такой:

1. Закрыть KOMPAS.
2. Запустить `install-rtw-library.ps1` от администратора.
3. Открыть KOMPAS.
4. Проверить меню `Приложения -> Bambu Studio`.

Если менялись команды меню, проверять надо не только файлы в git, а установленную копию:

```powershell
Get-Content "C:\Program Files\ASCON\KOMPAS-3D v24 Home\Libs\KompasBambu\KompasBambu.xml"
```

Именно этот XML читает KOMPAS. Если в репозитории уже есть новые `<appCommand>`/`<appItem>`, а в установленном XML их нет, значит после изменений не запускали `install-rtw-library.ps1` или установка не смогла перезаписать файлы. После изменения `rtw/KompasBambuRtw.cpp` надо также убедиться, что обновился установленный `KompasBambu.rtw`; если KOMPAS держит RTW-файл открытым, закрой KOMPAS и повтори установку.

Быстрая проверка установленного состояния:

```powershell
$lib = "C:\Program Files\ASCON\KOMPAS-3D v24 Home\Libs\KompasBambu"
Select-String -Path "$lib\KompasBambu.xml" -Pattern "appCommand|appItem|Bambu"
Get-Item "$lib\KompasBambu.rtw", "$lib\kompas-bambu.exe" | Select-Object FullName, Length, LastWriteTime
```

Для текущей версии в установленном XML должны быть пять команд:

```xml
<appCommand id="1" productID="APP_KompasBambu" title="Bambu STEP" />
<appCommand id="3" productID="APP_KompasBambu" title="Bambu STEP — новое окно" />
<appCommand id="2" productID="APP_KompasBambu" title="Bambu STL" />
<appCommand id="4" productID="APP_KompasBambu" title="Bambu STL — новое окно" />
<appCommand id="5" productID="APP_KompasBambu" title="Laser DXF" />
```

Если повышенный PowerShell не видит `g++.exe`, можно сначала собрать RTW обычной консолью, а затем установить уже собранные файлы:

```powershell
.\build-rtw.bat
powershell -ExecutionPolicy Bypass -File .\install-rtw-library.ps1 -SkipBuild
```

`-SkipBuild` нужен только как обход проблем окружения. Для обычной разработки лучше запускать установщик без него, чтобы установленная библиотека точно соответствовала исходникам.

## Как добавлять новые команды

Для нового пункта меню нужно менять две части синхронно:

1. `rtw/KompasBambu.xml` - добавить новый `<appCommand id="...">` и включить этот id в нужные `<appItem>`.
2. `rtw/KompasBambuRtw.cpp` - добавить такой же id в `LIBRARYENTRY` и передать нужные аргументы в `kompas-bambu.exe`.

После этого обязательная проверка:

```powershell
powershell -ExecutionPolicy Bypass -File .\install-rtw-library.ps1
Get-Content "C:\Program Files\ASCON\KOMPAS-3D v24 Home\Libs\KompasBambu\KompasBambu.xml"
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
.\dist\kompas-bambu.exe step export
.\dist\kompas-bambu.exe step --out-dir print
.\dist\kompas-bambu.exe dxf-sketch --out-dir laser
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
