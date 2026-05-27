# AAAA_game

Мобильный экшен-файтинг на Unity 6 (URP) с горизонтальной ориентацией. Управление: виртуальный джойстик слева + жесты справа (tap = лёгкий удар, double-tap = тяжёлый, hold = блок, swipe = уклонение, flick = парирование).

## ?? Архитектура

```
Game.Core      ? EventBus (типобезопасная событийная шина)
Game.Combat    ? Health, Stamina, Hitbox, Hurtbox, AttackData
Game.Input     ? GestureRecognizer, VirtualJoystick, TouchInputProvider
Game.Characters? Fighter, StateMachine, States (Idle/Move/Attack/Block/Parry/Dodge/Hitstun)
Game.UI        ? ArenaBootstrap (авто-сборка сцены), FighterHUD, WorldSpaceHealthBar
Game.Editor    ? BuildScript (сборка APK)
```

Слои развязаны через `asmdef` и общение через `EventBus`. `Combat` ничего не знает про `UI`.

## ?? Запуск

1. Открыть проект в Unity **6000.2.8f1** (или совместимой 6000.2.x).
2. Открыть сцену `Assets/_Project/Scenes/Arena.unity`.
3. Нажать **Play**. На сцене должен быть один `GameObject` с компонентом `ArenaBootstrap` — он автоматически создаст Canvas, джойстик, HUD и подключит ввод.

В окне **Game** выбрать разрешение `2340 x 1080 Landscape` (или 16:9 landscape), чтобы видеть мобильный layout.

## ?? Управление

| Действие | Жест |
|---|---|
| Движение | Левый виртуальный джойстик |
| Лёгкая атака | Тап в правой половине экрана |
| Тяжёлая атака | Двойной тап |
| Блок | Удержание пальца |
| Уклонение | Свайп в направлении |
| Парирование | Резкий короткий flick |

## ?? Сборка APK

### Требования
- Установлен **Android Build Support** в Unity Hub (с OpenJDK, Android SDK, NDK).
- Подключённый Android-телефон с USB Debugging (для `Build And Run`) — опционально.

### Способ 1: Через меню Unity
1. Открыть проект.
2. `Tools ? Build ? Android APK`.
3. Скрипт сам пропишет нужные Player Settings (landscape, IL2CPP, ARM64, package name = `com.aaaa.game`) и соберёт APK в `Builds/Android/AAAA_game.apk`.

### Способ 2: Через CLI (для CI или из консоли)
```cmd
"C:\Program Files\Unity\Hub\Editor\6000.2.8f1\Editor\Unity.exe" ^
  -quit -batchmode -nographics ^
  -projectPath "%CD%" ^
  -buildTarget Android ^
  -executeMethod Game.EditorTools.BuildScript.BuildAndroid ^
  -logFile build.log
```

Готовый APK появится в `Builds/Android/AAAA_game.apk`. Размер: 50–80 МБ. Первая сборка IL2CPP занимает 10–30 минут.

### Установка на телефон
```cmd
adb install -r Builds\Android\AAAA_game.apk
```
Или просто скопировать `.apk` на устройство и открыть через файловый менеджер.

## ?? Player Settings (которые ставит BuildScript)

| Параметр | Значение |
|---|---|
| Package Name | `com.aaaa.game` |
| Orientation | Landscape Left (с разрешённым Landscape Right) |
| Min API Level | Android 7.0 (API 24) |
| Scripting Backend | IL2CPP |
| Target Architectures | ARMv7 + ARM64 |
| Color Space | Linear |
| Render Outside Safe Area | ? (поддержка выемок/dynamic island) |

## ?? Git LFS

В репозитории настроен Git LFS (`.gitattributes` от GameCI). Бинарные ассеты (`.fbx`, `.png`, `.psd`, `.wav`, `.mp4` и т.д.) автоматически идут через LFS. Перед клонированием убедитесь, что у вас установлен `git lfs`:
```cmd
git lfs install
git clone https://github.com/prefff/AAAA_game.git