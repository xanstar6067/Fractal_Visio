# Архитектура FractalApp: расширяемый шаблон

Документ описывает целевую структуру проекта: как добавлять новые фракталы, палитры,
меню и модули (сохранение изображения, сохранение состояния, настройки) без правки
существующего кода рендеринга и UI.

Статус: **план**. Текущий код (8 файлов в `Assets/Scripts/Fractal/`) переносится в эту
структуру поэтапно, см. раздел «Этапы миграции».

---

## 1. Проблема текущего кода

| Что | Где сейчас | Почему мешает расширению |
|---|---|---|
| Формула Мандельброта | `MandelbrotFloat.shader`, `FractalCpuKernels.EvaluateDouble`, `EvaluateExtended` | Три копии в трёх местах; новый фрактал = правка всех трёх файлов |
| Состояние вида | поле `view` внутри `FractalSceneController` | Ни UI, ни модуль сохранения к нему не подступится |
| Палитра | `BuildDefaultGradient()` — хардкод в контроллере | Нельзя ни выбрать, ни сохранить, ни отредактировать |
| Раскраска | `ResolveColor()` жёстко «iteration × 0.021» | Нет smooth-раскраски, орбитальных ловушек, инверсии |
| Геометрия экрана | `Screen.width/height` прямо в `ScreenToFractal`, `PanByPixels` | Нельзя отрендерить кадр 4096×4096 в файл — сохранение картинки упирается в это |
| UI | `EnsureUi()` создаёт RawImage и два `Text` кодом | Меню/настройки некуда вешать |
| Выбор бэкенда | `RequestRender()` знает про оба рендерера и пороги | Каждый новый фрактал/точность добавляет ветку в тот же метод |

Ничего из этого не «плохой код» — это нормальный прототип. Расширение требует разрезать
его по швам.

---

## 2. Слои и зависимости

Строгое однонаправленное дерево, каждый слой — отдельный `.asmdef` (быстрая перекомпиляция,
физически запрещённые обратные зависимости):

```
Core ──▶ (никого)
Rendering ──▶ Core
Fractals ──▶ Core                 (определения фракталов и ядра)
Gestures ──▶ Core
App ──▶ Core, Rendering            (фракталы видит только через IFractalDefinition)
UI ──▶ Core, App
Modules ──▶ Core, App
Bootstrap ──▶ всё вышеперечисленное
```

Ключевое решение: **`Rendering` не знает про `Fractals`.** Определение фрактала само
поставляет рендереру ядро (делегат прохода) и биндер материала. Поэтому добавление
фрактала физически не может потребовать правки движка рендеринга.

Второе: **композиционный корень — отдельная сборка `Bootstrap`.** Связывать слои может только
тот, кто видит их все, а если посадить `AppBootstrap` в `App`, то `App` придётся сослаться на
`Modules` — ровно тот цикл, ради запрета которого и заводились `.asmdef`. `Bootstrap` содержит
единственный MonoBehaviour сцены и больше ничего.

---

## 3. Структура папок

```
Assets/
  Scripts/
    Core/                              FractalVisio.Core.asmdef
      Math/         HighPrecision.cs  DoubleDouble.cs
      View/         ViewState.cs  Viewport.cs  ViewNavigator.cs
      Fractal/      IFractalDefinition.cs  FractalParameterSet.cs
                    FractalParameterDescriptor.cs  PrecisionTier.cs
      Rendering/    IEscapeSampler.cs  CpuPassRunner.cs  RenderRequest.cs
                    IFractalRenderer.cs  IFrameCapture.cs
      Coloring/     PaletteData.cs  ColoringSettings.cs  IColorMapper.cs
      State/        FractalStateDto.cs  StateCodec.cs
    Fractals/                          FractalVisio.Fractals.asmdef
      FractalCatalog.cs  FractalDefinitionAsset.cs
      Mandelbrot/   MandelbrotDefinition.cs  MandelbrotSamplers.cs
      BurningShip/  ...
      Julia/        ...
    Rendering/                         FractalVisio.Rendering.asmdef
      BackendSelector.cs  RenderTargetSet.cs
      Gpu/          GpuBlitRenderer.cs
      Cpu/          CpuProgressiveRenderer.cs  ProgressiveGrid.cs  BandSplitter.cs
      Coloring/     PaletteAsset.cs  EscapeColorMapper.cs
    Gestures/                          FractalVisio.Gestures.asmdef
      FractalGestureInput.cs  GestureFrame.cs
    App/                               FractalVisio.App.asmdef
      FractalSession.cs  FractalPresenter.cs  AppServices.cs
      IAppModule.cs  RenderStatus.cs
    Bootstrap/                         FractalVisio.Bootstrap.asmdef
      AppBootstrap.cs                  (единственный MonoBehaviour на сцене)
    Modules/                           FractalVisio.Modules.asmdef
      Screenshot/   ScreenshotModule.cs
      State/        StateStoreModule.cs  BookmarksModule.cs
      Settings/     SettingsModule.cs
      Hud/          HudModule.cs
    UI/                                FractalVisio.UI.asmdef
      UiRouter.cs  UiScreen.cs
      Screens/      HudScreen.cs  MainMenuScreen.cs  SettingsScreen.cs
                    PaletteScreen.cs  BookmarksScreen.cs
      Widgets/      ParameterSliderWidget.cs  PaletteSwatchWidget.cs
  Shaders/
    Common/         FractalCommon.hlsl        (маппинг экран→плоскость, поворот, палитра)
    Mandelbrot.shader   BurningShip.shader   Julia.shader
  Settings/
    Fractals/       Mandelbrot.asset  ...     (FractalDefinitionAsset)
    Palettes/       Aurora.asset  Fire.asset  Grayscale.asset  (PaletteAsset)
  Scenes/
    Fractal_Manager.unity
```

---

## 4. Контракты Core

### 4.1 Вид и вьюпорт

`FractalView` переименовывается в `ViewState` и дополняется:

```csharp
public struct ViewState
{
    public HighPrecision x, y, scale;
    public double rotation;      // радианы
    public int iterations;
}

// Геометрия цели рендера. Заменяет обращения к Screen.* в математике.
public readonly struct Viewport
{
    public readonly int Width, Height;
    public double Aspect => (double)Width / Height;
}
```

`ViewNavigator` — чистый статический класс, переносит из контроллера `ScreenToFractal`,
`PanByPixels`, `ApplyTwoFinger`, приняв `Viewport` параметром. Это разблокирует
офлайн-рендер в файл любого разрешения.

### 4.2 Определение фрактала

```csharp
public interface IFractalDefinition
{
    string Id { get; }                       // "mandelbrot" — стабильный ключ для сохранений
    string DisplayName { get; }
    ViewState DefaultView { get; }
    IReadOnlyList<FractalParameterDescriptor> Parameters { get; }
    PrecisionTier SupportedPrecision { get; }   // Float | Double | DoubleDouble (флаги)

    // GPU
    string ShaderName { get; }
    double GpuMinimumScale { get; }
    void BindMaterial(Material material, in FractalParameterSet parameters);

    // CPU: вызывает host.Run / host.RunExtended ровно один раз, отдавая свой семплер
    void RunCpuPass(ICpuPassHost host, in FractalParameterSet parameters, bool extendedPrecision);
}
```

### 4.3 Параметры фрактала

```csharp
public enum ParameterKind { Double, Int, Bool, Complex }

public readonly struct FractalParameterDescriptor
{
    public readonly string Key, Label;
    public readonly ParameterKind Kind;
    public readonly double Min, Max, Default;
    public readonly bool Logarithmic;        // подсказка для слайдера
}

public struct FractalParameterSet   // плоский массив double, индексы = порядок дескрипторов
{
    public double this[int index] { get; set; }
    public double Get(string key);
    public static FractalParameterSet Defaults(IFractalDefinition definition);
}
```

**Зачем именно так:** экран настроек строится автоматически по `Parameters`. Новый фрактал
с параметрами `power`, `bailout`, `julia.re`, `julia.im` получает рабочий UI без единой
строки в `SettingsScreen`.

### 4.4 CPU-ядра без потери скорости

Виртуальный вызов на пиксель недопустим. Универсальный проход параметризуется
структурой-семплером, JIT/IL2CPP специализирует его по типу значения:

```csharp
public interface IEscapeSamplerD
{
    int Sample(double cx, double cy, int maxIterations, CancellationToken token);
}

public interface IEscapeSamplerDD
{
    int Sample(in DoubleDouble cx, in DoubleDouble cy, int maxIterations, CancellationToken token);
}

// Рендерер реализует это и передаёт определению; определение зовёт обратно со своей
// структурой. Обобщённый визитёр — нужен ровно затем, чтобы горячий цикл остался мономорфным.
public interface ICpuPassHost
{
    void Run<TSampler>(TSampler sampler) where TSampler : struct, IEscapeSamplerD;
    void RunExtended<TSampler>(TSampler sampler) where TSampler : struct, IEscapeSamplerDD;
}
```

Почему callback, а не «определение возвращает делегат»: тип семплера известен только фракталу,
а цикл прохода живёт в рендерере. Обратный вызов — единственный способ дать компилятору
инстанцировать цикл по конкретному типу, не заставляя `Rendering` знать про `Fractals`. Цена —
один виртуальный вызов на рендер, дальше только специализированный код.

Прогрессивная сетка (шаги 16→1), раздача тайлов, отмена, публикация кадра — всё это
остаётся в `Rendering` **один раз** и не дублируется по фракталам. Фрактал реализует
только тело итерации.

Пример стороны фрактала целиком:

```csharp
public readonly struct MandelbrotSamplerD : IEscapeSamplerD
{
    public int Sample(double cx, double cy, int maxIterations, CancellationToken token)
    {
        // текущее тело EvaluateDouble, без изменений
    }
}
```

Это же место — будущий вход для Burst: `CpuPassRunner` меняется на планировщик
`IJobParallelFor` без изменений в определениях фракталов (см. заметку в CLAUDE.md).

### 4.5 Буфер итераций и раскраска

Ключевое изменение относительно текущего кода: CPU-рендерер копит **буфер итераций**
(`float[]`, дробные значения для smooth-раскраски), а не сразу `Color32[]`.

```csharp
public interface IColorMapper
{
    void Map(ReadOnlySpan<float> iterations, Span<Color32> target,
             int maxIterations, in PaletteData palette, in ColoringSettings settings);
}
```

Смена палитры или режима раскраски = повторный маппинг буфера (миллисекунды), без
пересчёта фрактала (секунды на глубоком зуме). Без этого модуль палитр будет
неприятно тормозить именно там, где он интереснее всего.

`ColoringSettings`: `Mode` (Escape / Smooth / OrbitTrap / Distance), `CycleLength`,
`Offset`, `InteriorColor`, `Invert`.

### 4.6 Сохраняемое состояние

```csharp
[Serializable]
public sealed class FractalStateDto
{
    public int version = 1;
    public string fractal;         // IFractalDefinition.Id
    public string centerX;         // decimal как строка: JsonUtility не умеет decimal,
    public string centerY;         // а double потерял бы точность глубокого зума
    public string scale;
    public double rotation;
    public int iterations;
    public string palette;         // PaletteAsset.Id
    public ColoringSettingsDto coloring;
    public ParameterValueDto[] parameters;   // key + value, устойчиво к смене порядка
}
```

Правило: `version` инкрементируется при несовместимом изменении, `StateCodec` держит
апгрейд старых версий. Параметры пишутся по строковому ключу, а не по индексу.

### 4.7 Композиция кадров: устранение артефактов по краям

Разбор чужого приложения (`RESEARCH-mandelbrot-browser.md`) показал, что задача решается не
подбором ширины полей, а разделением трёх вещей, которые у нас были слиты в один буфер:

| Что | Кто владеет |
|---|---|
| Что посчитано | кадр + его собственный `ViewState` (`FractalCpuRenderer.PublishedView`) |
| Где зритель сейчас | `FractalSession.View` |
| Как одно показать через другое | `FramePlacement` → `FrameCompositor` (GPU) |

**Что было не так.** `ReprojectFrame` варпил единственный буфер на месте: при выходе за границы
брался `clamp`, то есть крайний пиксель размазывался по вновь открывшейся области. Полосы были
лишь самым заметным симптомом; глубже лежали два других. Варп деструктивен и повторялся каждый
кадр жеста, поэтому панорама в 60 кадров давала 60 nearest-neighbour пересэмплирований уже
пересэмплированного. И он был полнокадровым `Parallel.For` по каждому пикселю — на тех же ядрах,
которые в этот момент считали фрактал.

**Как сделано.**

```csharp
// Core/View/FramePlacement.cs — display uv -> frame uv, одно аффинное отображение
// на пан + зум + поворот. Разность центров вычитается в decimal (там выигрывается
// точность глубокого зума), делится уже в double (частное — экранного порядка).
public static FramePlacement Resolve(
    in ViewState frameView, double frameAspect,
    in ViewState currentView, double displayAspect);

public Vector4 UvRow0 { get; }   // uvFrame.x = dot(UvRow0.xyz, float3(uvDisplay, 1))
public Vector4 UvRow1 { get; }
public float Overhang { get; }   // аналог computeClip: <= 0 значит «покрывает целиком»
```

Композитор (`Rendering/FrameCompositor.cs`, `Shaders/FrameComposite.shader`) кладёт два слоя
за два блита: сначала широкий грубый фон, затем резкий кадр поверх с альфой по покрытию.
Пиксель, не покрытый ничем, получает цвет интерьера. `RawImage` показывает результат целиком,
`uvRect` больше не участвует в слежении за жестом.

**Широкий слой** (`Rendering/WideFieldLayer.cs`) существует ради зум-аута и только ради него.
Пан и зум-ин просят область, которая в последнем кадре уже есть — достаточно его разместить.
Зум-аут просит область, которой не считали никогда, и никакие поля вокруг одного кадра не
покроют жест, удваивающий поле за несколько сотен миллисекунд. Слой намеренно плохой: длинная
сторона `WideLongEdge` (192–320 px), `WideWorkers` (1–2) воркера, поле `WideFieldFactor` = ×8,
перерисовка только когда `Overhang` подходит к нулю или собственный масштаб уехал от требуемого.

**Оверскан теперь функция движения, а не константа.** `ViewMotion` копит сглаженную скорость
`d(ln scale)/dt` и отвечает, во сколько раз шире экрана надо считать, чтобы кадр был ещё
актуален через `FieldLookaheadSeconds` (0.35 с). Диапазон — `CpuFieldBase`…`CpuFieldMax`
(1.08…2.6). Прежние 1.08–1.12× были рассчитаны на панораму и применялись к зум-ауту, где нужно
2–4×.

Ключевое: **буфер не растёт.** `ResolveCpuBuffer` даёт постоянный размер на разрешение экрана,
а `ResolveCpuViewport(buffer, fieldFactor)` уменьшает видимую часть внутри него. Расширение поля
стоит разрешения во время жеста, а не времени и не памяти — и это правильный обмен, потому что
во время жеста картинка и так движется. Так же устроен `scaleOverhead` в источнике.

**Публикация только на границе прохода.** Пока первый проход нового запроса не покрыл буфер
целиком, текстура держит предыдущий кадр вместе с его `PublishedView` — корректная картинка
другого вида вместо наполовину корректной картинки этого. `DiscardPublished()` вызывается, когда
кадр перестаёт быть картинкой чего бы то ни было полезного: смена фрактала, параметров, палитры
или бэкенда.

**Поля считаются только грубыми проходами** (`step >= MarginStepThreshold`, сейчас 4): проходы
step 2 и 1 — это ~80% стоимости рендера, а поля видно лишь в момент жеста. Видимый прямоугольник
приходит в рендерер через `Viewport` и выравнивается наружу по сетке 16, иначе сэмплы в полях и
в видимой части попали бы в разные точки и на границе появился бы шов.

**Инварианты:**

- Рендерер **не варпит свои пиксели**. Опубликованный кадр — картинка одного `ViewState`;
  следовать за жестом — задача композитора. Возврат репроекции вернёт и накопление ресэмплинга,
  и трату ядер посреди жеста.
- Композиция — только CPU-путь. GPU считает вид заново каждый кадр, непокрытых пикселей там не
  бывает; `MobileRenderProfile.ResolveViewport` (GPU-цели) возвращает вьюпорт без полей.
- `fieldFactor` живёт в `Viewport` и только там. Ни `ViewNavigator`, ни ядра фракталов, ни
  шейдеры о полях не знают — они получают уже расширенный вьюпорт и считают его обычным.
- Матрица передаётся в шейдер **двумя `float4`, а не `float4x4`**: у матричной униформы порядок
  строк и столбцов зависит от соглашения компилятора, у скалярного произведения — нет.

---

## 5. App-слой

### 5.1 FractalSession — единственный владелец состояния

```csharp
[Flags] public enum SessionChange { None=0, View=1, Definition=2, Parameters=4,
                                    Palette=8, Coloring=16, Quality=32 }

public sealed class FractalSession
{
    public IFractalDefinition Definition { get; }
    public FractalParameterSet Parameters { get; }
    public ViewState View { get; }
    public PaletteAsset Palette { get; }
    public ColoringSettings Coloring { get; }
    public QualitySettings Quality { get; }

    public event Action<SessionChange> Changed;

    public void SetDefinition(string id);   // сбрасывает вид и параметры на дефолты
    public void SetView(in ViewState view);
    public void SetParameter(string key, double value);
    public void SetPalette(PaletteAsset palette);
    public void Apply(FractalStateDto state);
    public FractalStateDto Capture();
}
```

Всё остальное — читатели и подписчики. `Changed` с флагами позволяет презентеру
различать «нужен полный пересчёт» (`View | Definition | Parameters`) и «достаточно
перекрасить» (`Palette | Coloring`).

### 5.2 FractalPresenter — единственный MonoBehaviour рендера

Наследник текущего `FractalSceneController`, но только про рендер: подписан на сессию,
владеет `RenderTargetSet` (interactive/settled RT + CPU Texture2D), спрашивает
`BackendSelector` какой бэкенд взять, отдаёт запрос рендереру, обновляет `RawImage`.
Из него уходят: жесты (в `Input`), HUD (в модуль), создание UI (в `UI`), палитра (в `Coloring`).

Реализует `IFrameCapture` для модуля скриншотов:

```csharp
public interface IFrameCapture
{
    Task<Texture2D> CaptureAsync(Viewport viewport, int supersample,
                                 IProgress<float> progress, CancellationToken token);
}
```

### 5.3 Модули

```csharp
public interface IAppModule
{
    string Id { get; }
    void Initialize(AppServices services);
    void Tick();                     // вызывается бутстрапом после презентера
    void Shutdown();
}

public sealed class AppServices
{
    public FractalSession Session { get; }
    public IRenderStatusSource Render { get; }   // состояние рендера для HUD и прогресса
    public Transform UiRoot { get; }
    // дальше по мере роста: FractalCatalog, PaletteLibrary, IFrameCapture, IStateStore, IUiRouter
}
```

Тип называется `AppServices`, а не `AppContext`: `System.AppContext` существует, и в любом
файле с `using System;` имя становится неоднозначным. Та же ловушка, что и с `Input` — см.
раздел про `Gestures`.

`AppBootstrap` держит список модулей и поднимает их при инициализации. Добавить модуль =
реализовать интерфейс и добавить одну строку в список. Модули не знают друг о друге и не
получают ни рендерер, ни другой модуль.

Стартовый набор: `HudModule`, `ScreenshotModule`, `StateStoreModule`, `BookmarksModule`,
`SettingsModule`.

### 5.4 UI

`IUiRouter` — стек экранов: HUD всегда внизу, меню/настройки кладутся сверху.
`PointerOverUi` из роутера гасит жесты, пока открыта панель (сейчас `FractalGestureInput`
читает касания напрямую и будет конфликтовать с любой кнопкой).

Экраны читают `FractalSession` и вызывают его сеттеры. Прямых ссылок на рендереры нет.
`SettingsScreen` генерирует контролы по `Definition.Parameters` — см. 4.3.

**Решено: uGUI.** UI Toolkit пришлось бы подмешивать к уже существующему uGUI-канвасу с
`RawImage` вывода и HUD, а главное — эффект матового стекла требует показать размытую копию
экрана внутри панели, что в uGUI делается обычным `RawImage` с `uvRect`, а в UI Toolkit
упирается в отсутствие простого способа отдать элементу произвольную текстуру с кропом.

### Матовое стекло

Фон приложения — одна текстура, которой мы владеем, поэтому блюр честный, а не нарисованный:

1. `BackdropBlur` кропает её по `uvRect` (поля оверскана внутрь стекла попадать не должны),
   ужимает до 384 пикселей по длинной стороне и прогоняет разделимый гауссиан двумя
   проходами возрастающего радиуса — это доли миллисекунды.
2. Каждая панель показывает свой кусок этой общей размытой текстуры: `RawImage.uvRect`
   считается из экранного прямоугольника панели. Поэтому стекло следует за картинкой, а не
   выглядит наклейкой.
3. Сверху — затемняющий тинт, волосяная рамка и блик по верхней кромке; всё вместе скруглено
   маской из процедурного nine-slice спрайта (`UiSprites`), никаких импортированных ассетов.

Тинт непрозрачнее, чем хочется «по красоте»: на жёлтой полосе фрактала панель с alpha 0.6
уходила в бледно-зелёный и белый текст пропадал. Проверено на скриншотах в Play-режиме.

---

## 6. Рецепт: добавить новый фрактал

Три файла, ноль правок в существующем коде:

1. `Assets/Scripts/Fractals/BurningShip/BurningShipSamplers.cs` — структуры
   `BurningShipSamplerD` / `BurningShipSamplerDD` с телом итерации.
2. `Assets/Scripts/Fractals/BurningShip/BurningShipDefinition.cs` — `Id`, `DisplayName`,
   `DefaultView`, дескрипторы параметров, `RunCpuPass`, `BindMaterial`.
3. `Assets/Shaders/BurningShip.shader` — `#include "Common/FractalCommon.hlsl"`, только
   функция итерации.

Плюс одна строка в массиве `FractalCatalog.Definitions`. Ассеты-определения появятся вместе с
меню выбора фрактала; интерфейс, который видит остальной код, от этого не изменится.
Меню выбора фрактала, экран настроек, сохранение состояния, скриншоты и палитры
начинают работать автоматически.

**Критерий приёмки архитектуры:** второй фрактал добавляется ровно этими шагами. Если
понадобилась правка `CpuProgressiveRenderer`, `FractalPresenter` или `SettingsScreen` —
абстракция протекла и её надо чинить до появления третьего фрактала.

**Результат проверки (Burning Ship, Этап 8).** Критерий выдержан: ни рендереры, ни презентер,
ни сессия, ни HUD не изменились ни на строку. Помимо трёх файлов и строки в каталоге
потребовалось ровно два дополнения, и оба — не спецслучаи:

- `DoubleDouble.Abs` / `Negate` в `Core/Math` — примитив арифметики, которого просто не было,
  потому что Мандельброту модуль не нужен. Это пополнение математики, а не правка движка;
  следующему фракталу с `abs` уже ничего добавлять не придётся.
- `AppBootstrap.startupFractalId` — поле в инспекторе, чтобы выбрать стартовый фрактал по `Id`
  до появления меню. Разовое, не на каждый фрактал.

---

## 7. Этапы миграции

Каждый этап оставляет проект компилируемым и визуально идентичным. Порядок не случаен:
сначала швы, потом расширения.

| Этап | Содержание | Риск |
|---|---|---|
| 0 | ✅ **Сделано.** Папки + `.asmdef` (`Core`, `Rendering`, `Gestures`, `App`), перенос 8 файлов вместе с `.meta`, namespaces по слоям, `DoubleDouble`/`MobileRenderProfile`/оба рендерера сделаны `public`. Проект компилируется, компоненты в сцене на месте | низкий |
| 1 | ✅ **Сделано.** `ViewState`, `Viewport`, `ViewNavigator` в `Core/View`; вся математика жестов ушла из контроллера, `Screen.*` остался только в сборке `DisplayViewport` и размеров текстур | низкий |
| 2 | ✅ **Сделано, затем заменено Этапом 10.** Оверскан: поля в `Viewport`, `ResolveCpuViewport`, `ViewNavigator.ForViewport`, `uvRect` в контроллере, маска непокрытых блоков, поля только в грубых проходах. Поля и `MarginStepThreshold` остались; репроекция и маска непокрытых блоков удалены | средний |
| 3 | ✅ **Сделано.** `FractalSession` (владелец вида и `RenderQuality`, события `SessionChange`), `AppServices`, `IAppModule`, `RenderStatus`/`IRenderStatusSource`; `FractalSceneController` разделён на `FractalPresenter` (обычный класс, только рендер) и `AppBootstrap` (MonoBehaviour сцены, композиционный корень); HUD вынесен в `HudModule` | средний |
| 4 | ✅ **Сделано.** `IFractalDefinition`, `ICpuPassHost`, `IEscapeSamplerD/DD`, `FractalParameterSet`/дескрипторы, `PrecisionTier`; `MandelbrotDefinition` + два семплера-структуры + `FractalCatalog`; CPU-проход обобщён по типу семплера, GPU-рендерер берёт шейдер и уникформы у определения; `Shaders/Common/FractalCommon.hlsl` | средний |
| 5 | Буфер итераций + `IColorMapper` + `PaletteAsset`; палитры как ассеты. Сюда же уходит smooth-раскраска: `IEscapeSamplerD/DD` начинают возвращать дробный номер итерации вместо `int` — контракт трогает каждый фрактал, поэтому делать до появления третьего | средний |
| 6 | 🔶 **Частично.** `UiRouter` (модуль), `UiScreen`, `SettingsScreen` с выбором фрактала, матовое стекло (`BackdropBlur`, `GlassPanel`, `UiSprites`, `UiTheme`), блокировка жестов через `PointerOverUi`. Осталось: авто-генерация контролов по `FractalParameterDescriptor`, экраны палитр и закладок | низкий |
| 7 | Модули: `ScreenshotModule`, `StateStoreModule`, `BookmarksModule` | низкий |
| 8 | ✅ **Сделано.** Burning Ship: два семплера-структуры, определение с параметром `bailout`, `BurningShip.shader` на общем include, строка в каталоге. Проверено: обе точности CPU, GPU, смена фрактала через `FractalSession.SetDefinition`, параметр доходит до ядра и до материала | низкий |
| 9 | Burst + Jobs в `ProgressivePass` (см. заметку в CLAUDE.md) | отдельная задача |
| 10 | ✅ **Сделано.** Композиция кадров (см. 4.7): `FramePlacement`, `ViewMotion`, `FrameCompositor` + `FrameComposite.shader`, `WideFieldLayer`; репроекция и маска непокрытых блоков удалены; оверскан стал функцией скорости при постоянном размере буфера; полосы в CPU-проходе заменены тайлами 64×64 из общего курсора | средний |
| 11 | Предиктивный рендер: оценка времени рендера по предыдущему результату, выбор целевого прохода и рендер точки, где вид будет к моменту прилёта кадра (аналог `MovementPredictor`, см. `RESEARCH-mandelbrot-browser.md` §4) | средний |
| 12 | Пертурбация + BLA как отдельный `PrecisionTier`: опорная орбита в managed-коде, дельта в fp64, детекция глитчей. Снимает потолок `decimal` и делает глубину дешёвой — см. `RESEARCH-mandelbrot-browser.md` §7 | отдельная задача |

Этапы 0–1 — фундамент, делаются подряд. Этап 10 вынесен вперёд намеренно: артефакты по краям при
отдалении — самый заметный дефект картинки, а его решение задаёт форму слоя показа, на который
потом опираются этапы 5, 7 и 11. Этапы 5–8 независимы друг от друга.

---

## 8. Открытые вопросы


- **Тесты.** `com.unity.test-framework` в `manifest.json` нет. Edit-mode тесты на
  `StateCodec` (round-trip сохранения) и на семплеры (эталонные значения итераций)
  дёшевы и окупятся при добавлении фракталов. Требует добавления пакета.
- **Точность.** `HighPrecision` на `decimal` ограничен ~1e-28, `DoubleDouble` — примерно 1e-30,
  а `MaximumIterations = 2048` упирается ещё раньше: на масштабе 1e-24 картинка становится
  сплошным «интерьером» задолго до того, как кончится точность. То есть текущий предел глубины —
  бюджет итераций, а не арифметика. Разбор в `RESEARCH-mandelbrot-browser.md` §7.3; лечение —
  Этап 12 (пертурбация) плюс адаптивный бюджет итераций от предыдущего кадра.
- **Артефакт на переключении GPU -> CPU.** При уходе глубже `gpuMinimumScale` оба CPU-слоя
  помечаются несостоятельными (`DiscardPublished`), поэтому первый CPU-кадр приходит с нуля и
  виден скачок к цвету интерьера. Лечится засевом CPU-буфера содержимым GPU-текстуры в момент
  переключения (`AsyncGPUReadback` или `ReadPixels` один раз на переключение) — композитору
  тогда есть что разместить сразу. Не срочно: замечено пользователем как «не супер критично».
- **Julia как режим, а не отдельный фрактал.** У Мандельброта и Жюлиа общая итерация,
  разный старт (`z0`/`c`). Разумно сделать `JuliaDefinition` отдельным `Id`, но
  переиспользовать семплер через параметр — проверить на Этапе 7.
