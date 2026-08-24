# kompas-bambu

Интеграция KOMPAS-3D v24 Home с Bambu Studio: команда в меню KOMPAS экспортирует активную 3D-деталь/сборку в файл для печати и сразу открывает его в Bambu Studio.

Рабочая интеграция сейчас сделана как RTW-приложение KOMPAS:

- `KompasBambu.rtw` - нативная 64-битная RTW-библиотека, которую видит меню `Приложения`.
- `KompasBambu.xml` - описание команд меню/панелей для KOMPAS.
- `kompas-bambu.exe` - .NET exporter/launcher, который делает реальный экспорт через COM API KOMPAS и запускает Bambu Studio.

RTW-библиотека намеренно тонкая: она только получает команду KOMPAS и запускает `kompas-bambu.exe step` или `kompas-bambu.exe stl`. Вся CAD-логика лежит в C# exporter, чтобы не тащить экспорт STEP/STL в C++ RTW-код.

## Что умеет

Команды в KOMPAS:

- `Приложения -> Bambu Studio -> Bambu STEP`
- `Приложения -> Bambu Studio -> Bambu STL`

Поведение по умолчанию:

- формат `Bambu STEP`: STEP AP203;
- формат `Bambu STL`: binary STL с заданными параметрами тесселяции;
- экспортируются только тела;
- результат кладется в папку `print` рядом с исходным файлом KOMPAS;
- имя экспортированного файла совпадает с именем модели;
- после экспорта запускается Bambu Studio с этим файлом.

Пример результата:

```text
C:\Users\polit\YandexDisk\3d\...\print\Держалка к стене.step
```

Активный документ должен быть сохранен хотя бы один раз: папка `print` создается рядом с файлом детали/сборки.

## Архитектура

`rtw/KompasBambuRtw.cpp`

Нативная DLL с расширением `.rtw`. Экспортирует функции старого RTW API KOMPAS:

- `LIBRARYNAME()` - возвращает имя библиотеки `Bambu Studio`;
- `LIBRARYID()` - возвращает стабильный числовой id;
- `LIBRARYENTRY(unsigned int command)` - вызывается KOMPAS при выборе команды.

`LIBRARYENTRY` мапит команды так:

- `1` -> `kompas-bambu.exe step`
- `2` -> `kompas-bambu.exe stl`

`rtw/KompasBambu.xml`

XML-описание приложения для UI KOMPAS. Содержит:

- `<application id="APP_KompasBambu" ...>`;
- две команды `<appCommand id="1" title="Bambu STEP" />` и `<appCommand id="2" title="Bambu STL" />`;
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

## Установка RTW

Основной установщик:

```powershell
powershell -ExecutionPolicy Bypass -File .\install-rtw-library.ps1
```

Запускать от администратора, потому что файлы копируются в `Program Files`, а регистрация пишется в `ProgramData`.

Что делает установщик:

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

## Ручной запуск

Ту же логику можно проверить без меню KOMPAS:

```powershell
& "C:\Program Files\ASCON\KOMPAS-3D v24 Home\Libs\KompasBambu\kompas-bambu.exe" step
```

Команды exporter:

```powershell
.\dist\kompas-bambu.exe step
.\dist\kompas-bambu.exe stl
.\dist\kompas-bambu.exe step export
.\dist\kompas-bambu.exe step --out-dir print
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
