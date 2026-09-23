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
      Localization/ IStringCatalog.cs  LocaleTable.cs
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
      Localization/ Localizer.cs
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
  Resources/
    Localization/   en.txt  ru.txt            (локали: ключ = строка, см. 5.4c)
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
    float Sample(double cx, double cy, int maxIterations, CancellationToken token);
}

public interface IEscapeSamplerDD
{
    float Sample(in DoubleDouble cx, in DoubleDouble cy, int maxIterations, CancellationToken token);
}

// Рендерер реализует это и передаёт определению; определение зовёт обратно со своей
// структурой. Обобщённый визитёр — нужен ровно затем, чтобы горячий цикл остался мономорфным.
public interface ICpuPassHost
{
    void Run<TSampler>(TSampler sampler) where TSampler : struct, IEscapeSamplerD;
    void RunExtended<TSampler>(TSampler sampler) where TSampler : struct, IEscapeSamplerDD;
    void RunPerturbed<TSampler>(TSampler sampler) where TSampler : struct, IPerturbationSampler; // см. 4.8
}
```

**Отмена возвращает, а не бросает.** Семплер проверяет токен раз в несколько сотен итераций и
возвращает любое значение; проход после каждого сэмпла сам смотрит на токен и не пишет в буфер
значение отменённого прохода. `Parallel.For` запускается без токена в `ParallelOptions`. Жест
отменяет рендер несколько раз в секунду, а исключение платилось на каждом воркере и ещё раз на
главном потоке в `DrainTask`.

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

### 4.5 Буфер escape-значений и раскраска

CPU-рендерер копит **escape-значения** (`float[]`), а не сразу `Color32[]`. Цвет применяется
один раз на опубликованный проход.

```csharp
public interface IColorMapper
{
    void MapRange(float[] escapeValues, Color32[] target, int start, int count,
                  PaletteData palette, in ColoringSettings settings);
}
```

Диапазон, а не весь буфер, и массивы, а не `Span`: вызывающий делит картинку по потокам, а
`Span` нельзя захватить лямбдой, которая это сделает. Один виртуальный вызов на кусок бесплатен,
на пиксель — нет.

Смена палитры или режима раскраски = повторный маппинг буфера (миллисекунды), без пересчёта
фрактала (секунды на глубоком зуме). Именно поэтому `SessionChange.Palette` и `Coloring` не
поднимают `renderDirty` у презентера: он зовёт `SetColoring` у обоих CPU-слоёв, и те
перекрашивают то, что уже посчитано.

**Контракт семплера.** `IEscapeSamplerD/DD` возвращают `float` — непрерывный escape-счёт, либо
**отрицательное** значение, если точка не вышла за bailout. Отрицательное — маркер интерьера,
на который завязан весь путь раскраски; возвращать `maxIterations` нельзя. Непрерывность даёт
`EscapeMath.Smooth`:

```
nu = i + 1 - log2( log(|z|^2) / log(bailout) )
```

Bailout при этом поднят далеко выше четвёрки, решающей принадлежность (Мандельброт: 65536): при
маленьком bailout приближение плохое и полосы возвращаются. Цена — пара лишних итераций на
выходящий пиксель.

`ColoringSettings`: `Smooth` (брать дробную часть), `Mode` (`Linear` / `Logarithmic`),
`CycleLength`, `Offset`, `InteriorColor`. Логарифмический режим по умолчанию: экран на старте —
почти сплошь малые счётчики, и линейная развёртка отдаёт им один цвет на всё поле. Оба режима
совпадают в нуле и на одном полном обороте палитры.

**Инвариант:** формула позиции в палитре живёт в двух местах — `EscapeColorMapper.MapRange` и
`FractalCommon.hlsl:FractalEscapeColor` — и они обязаны совпадать. Бэкенд переключается под
зрителем на середине зума, и палитра, съезжающая в этот момент, читается как сбой.

Палитры: `PaletteData` (256 стопов, циклическая выборка) + `PaletteLibrary` — список в коде, по
той же причине, что и `FractalCatalog`. Каждая палитра заканчивается тем же цветом, с которого
начинается, иначе на каждом обороте виден жёсткий шов. `PaletteAsset` решено не заводить:
пользовательские палитры хранятся в JSON через `PaletteCatalog` (см. 4.6).

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

**Как сделано (этап 7).** Поля `iterations` в DTO нет: бюджет выводится из масштаба в
`FractalSession`, а сохранённый бюджет перекрыл бы более удачный после обновления. Палитра
хранится по `Id` и ищется в `PaletteCatalog`; если её больше нет, остаётся текущая. Все числа
пишутся в инвариантной культуре: на устройстве с русской локалью иначе получится «0,5».
`FractalSession.Capture()` / `Apply(dto, catalog, palettes)` — единственная точка входа, и
`Apply` использует тот же `Normalize` (зажим масштаба и бюджет), что и `SetView`. Документы
хранятся в `persistentDataPath/*.json` через `IAppStorage`. Запись атомарная (временный файл
+ замена), потому что убить приложение посреди записи на Android — обычный сценарий.

**Палитры — без ScriptableObject.** Встроенные остаются кодом (`PaletteLibrary`) по той же
причине, что и `FractalCatalog`. Пользовательские создаются на телефоне, где базы ассетов нет,
поэтому хранятся как опорные цвета (`PaletteDto`), а не как 256 запечённых цветов.
`PaletteData.Stops` сохраняет эти точки.

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

### 4.8 Пертурбация: глубина по цене fp64

Портировано из движка WPF-проекта (`FractalExplorerWPF/Core/Rendering/MandelbrotFamilyRenderer.DeepZoom.cs`
и `.Bla.cs`). Раньше ниже `ExtendedPrecisionScale` (1e-12) каждый пиксель итерировался в
double-double, а это в 15–20 раз дороже fp64 на итерацию. Отсюда был «обрыв» плавности на
этой глубине, и отсюда же ограничение «во время жеста только проходы 16 и 8».

Как устроено:

- `IPerturbationSampler.BuildReference` один раз на запрос считает опорную орбиту центра в
  `DoubleDouble` и хранит её как `double[]` (`ReferenceOrbit`, переиспользуется между запросами).
  Для диапазона `decimal` (до ~1e-28) этого достаточно, `BigFloat` из WPF не нужен.
- Пиксель итерирует только отклонение `d' = 2Zd + d² + dc` в fp64; `dc` — экранное смещение,
  умноженное на масштаб, в fp64 оно точно на любой глубине.
- **Rebasing (Zhuoran)**: при `|z| < |d|`, при исчерпании орбиты или по критерию Pauldelbrot
  (`|z|² < 1e-6·|Z|²`) выполняется `d = z − Z₀`, индекс опоры сбрасывается в 0. Второй опорной
  точки не нужно. В орбите всегда есть `Z₀` и `Z₁`, даже если центр сразу убегает.
- **BLA** (`ComplexBlaTable`, только для z²+c): пирамида линейных шагов `d' = A·d + B·dc`, допуск
  2⁻⁵² как в WPF. Выигрыш заметен у пикселей рядом с минибротами (там `d` долго остаётся малым)
  и на самой большой глубине.
- **Burning Ship** складывает компоненты по модулю, поэтому отклонение сворачивается покомпонентно
  (`FoldedDelta`). Условие rebasing там тоже **покомпонентное**: `|z_r| < |d_r|` или
  `|z_i| < |d_i|`. Проверено против double-double на граничной точке: с одним условием по модулю
  на 1e-21 было неверно 5% кадра, на 1e-23 — 50%; с покомпонентным условием ошибок нет. BLA для
  Burning Ship не сделан: линейная часть там вещественная 2×2, в WPF это отдельная таблица
  `RealBlaTable`.

Выбор тира остаётся за определением: при `extendedPrecision` Мандельброт и Burning Ship зовут
`host.RunPerturbed(...)`. Семплеры `*SamplerDD` оставлены как эталон для сверки. Рендерер
сообщает фактический тир (`FractalCpuRenderer.ActivePrecision` → `RenderStatus.Precision`),
HUD показывает `perturbation`.

Сверка (сетка 96×54, эталон — DD, 2048 итераций, точки на границе найдены бисекцией):
Мандельброт на 1e-13…1e-22 совпадает, кроме <1% пикселей. Эти пиксели плохо обусловлены: сам DD
меняет результат при сдвиге на 0,001 пикселя. Ускорение на одном потоке — ×5–7, на 1e-22 до ×30.

### 4.9 Бюджет потоков и темп кадров

`CpuWorkerBudget` (`Rendering/Cpu`) — один на все CPU-рендереры.

- **Резерв ядер.** На мобильных при ≥4 ядрах свободными остаются два: главный поток Unity и
  рендер-поток. На десктопе резервируется одно. Раньше главный рендер брал «все ядра минус одно»,
  а широкий слой брал ещё 1–2 поверх. Во время зум-аута, когда работают оба, вычислительных
  потоков было больше, чем ядер.
- **Ранги воркеров.** Первый воркер каждого рендерера никогда не паркуется. Дальше в очереди
  стоят воркеры широкого слоя, за ними остальные воркеры главного рендера. Когда широкий слой
  простаивает, его доля отдаётся главному рендеру.
- **Регулятор** (`Regulate`, вызывается из `FractalPresenter.Tick`). Работает только во время
  жеста на CPU-пути. Если сглаженное время кадра больше 1.3× цели дольше 0.1 с, паркуется
  один воркер. Если меньше 1.1× дольше 0.5 с, воркер возвращается. Ниже половины бюджета
  регулятор не опускает, чтобы медленный GPU не превращал его в «один поток навсегда». Воркер
  паркуется на границе тайла 64×64, так что запаздывание ограничено одним тайлом.
- HUD показывает `fps`, худший кадр за 0.1 с и `workers N/M`. Если на устройстве картинка
  дёргается при `workers M/M`, дело не в конкуренции за ядра.

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

**Как сделано (этап 7).** Модуль, который что-то предлагает остальным, объявляет интерфейс в
`App` (`IBookmarkService`, `IScreenshotService`) и регистрирует себя в
`AppServices.Provide<T>()` в `Initialize`. Экраны берут сервис через `Get<T>()`. Только так UI
может сохранить закладку, не ссылаясь на сборку `Modules`, а эту ссылку запрещают asmdef. Порядок
модулей в `AppBootstrap` важен: `StateStoreModule` восстанавливает сессию раньше всех, а
`UiRouter` идёт последним, после модулей, чьи сервисы нужны его экранам. Отдельного
`SettingsModule` нет: настройки интерфейса и разрешения живут в сессии и сохраняются
`StateStoreModule`.

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

Плюс одна строка в массиве `FractalCatalog.Definitions`. Перевод имени и параметров — по желанию,
строками `fractal.<id>` / `fractal.<id>.<ключ>` в файлах локалей (см. 5.4c); без них показываются
`DisplayName` и `Label`. Ассеты-определения появятся вместе с
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
| 5 | ✅ **Сделано.** Буфер escape-значений + `IColorMapper`/`EscapeColorMapper` + `PaletteData`/`PaletteLibrary` (5 палитр) + `ColoringSettings` (smooth, Linear/Logarithmic, cycle, offset, interior); `IEscapeSamplerD/DD` возвращают `float` с отрицательным маркером интерьера; bailout поднят до 65536 (Мандельброт) и 256 (Burning Ship); GPU и CPU считают позицию в палитре по одной формуле. Не делалось: `PaletteAsset` как ScriptableObject и редактор палитр — уходит в Этап 7 | средний |
| 6 | ✅ **Сделано (2026-09-14).** `UiRouter` (модуль), `UiScreen`, матовое стекло (`BackdropBlur`, `GlassPanel`, `UiSprites`, `UiTheme`), блокировка жестов через `PointerOverUi`. Масштаб UI: `ScreenScale.Density` × `UserScale` (0.6…2.5). Внизу справа панель из трёх кнопок (скриншот / закладки / настройки, иконки процедурные) и одна открытая панель за раз. Настройки собираются из блоков в 1–3 колонки; блок PARAMETERS генерируется по `FractalParameterDescriptor` (`SliderRow`, у Bool — Off/On), значение применяется при отпускании пальца. Экраны `BookmarksScreen` и `PaletteEditorScreen` (цвета по кругу в HSV, длина цикла и смещение, живой предпросмотр, Cancel откатывает). Виджеты `SliderRow`, `ActionRow`, `PointerUpRelay`. См. 5.4b | низкий |
| 7 | ✅ **Сделано (2026-09-14).** `StateStoreModule` (восстановление при старте, сохранение через 1.5 с после изменений и сразу при потере фокуса), `BookmarksModule` (`IBookmarkService`), `ScreenshotModule` (`IScreenshotService`: ждёт окончания рендера, PNG кодируется вне главного потока, на Android 10+ файл попадает в галерею через MediaStore). `FractalStateDto` + `StateCodec` в `Core/State`, `IAppStorage`/`FileAppStorage`, `PaletteCatalog` (встроенные + пользовательские палитры в JSON), реестр сервисов `AppServices.Provide/Get`. Не сделано: снимок в высоком разрешении с суперсэмплингом (сейчас сохраняется разрешение рендера) | низкий |
| 8 | ✅ **Сделано.** Burning Ship: два семплера-структуры, определение с параметром `bailout`, `BurningShip.shader` на общем include, строка в каталоге. Проверено: обе точности CPU, GPU, смена фрактала через `FractalSession.SetDefinition`, параметр доходит до ядра и до материала | низкий |
| 9 | Burst + Jobs в `ProgressivePass` (см. заметку в CLAUDE.md) | отдельная задача |
| 10 | ✅ **Сделано.** Композиция кадров (см. 4.7): `FramePlacement`, `ViewMotion`, `FrameCompositor` + `FrameComposite.shader`, `WideFieldLayer`; репроекция и маска непокрытых блоков удалены; оверскан стал функцией скорости при постоянном размере буфера; полосы в CPU-проходе заменены тайлами 64×64 из общего курсора | средний |
| 11 | 🔶 **Частично (2026-09-14).** Инерция (`ViewInertia`), замер стоимости сэмпла и бюджет времени рендера во время движения, прогноз вида для инерции, удерживаемый кадр с растворением, сглаживание грубых проходов, частота кадров = частота панели. См. 5.6. Не сделано: прогноз во время ведения пальцем (эталон тоже предсказывает только в режиме инерции), автозум по долгому нажатию и двойному тапу | средний |
| 13 | ✅ **Сделано (2026-09-23).** Мультиязычный фундамент: `IStringCatalog` + `LocaleTable` в `Core/Localization`, `Localizer` в `App/Localization` (`AppServices.Strings`), локали — текстовые файлы `Assets/Resources/Localization/<код>.txt` (`en` — эталон и запасной, `ru` — первый перевод). Язык — `InterfaceSettings.Language` (пусто = язык устройства), сохраняется `StateStoreModule`, выбирается блоком LANGUAGE в настройках; смена языка перестраивает UI так же, как смена масштаба. Все строки экранов, тосты скриншота и имена закладок идут через каталог; имена фракталов, параметров и встроенных палитр — по ключам-соглашениям с откатом на собственное имя. HUD намеренно не переводится. См. 5.4c | низкий |
| 12 | 🔶 **Частично (2026-09-14).** `PrecisionTier.Perturbation`, `IPerturbationSampler`, `ReferenceOrbit`, `ComplexBlaTable`; Мандельброт — пертурбация + rebasing + BLA, Burning Ship — пертурбация + покомпонентный rebasing (см. 4.8). Глубина теперь стоит примерно как fp64. Вместе с этим — `CpuWorkerBudget` и отмена без исключений (4.9). Осталось: потолок `decimal` (~1e-28) для центра вида (нужен свой тип координат, и `FloatExp` для `d` за 1e-300 из WPF), адаптивный бюджет итераций, `RealBlaTable` для Burning Ship | отдельная задача |

Этапы 0–1 — фундамент, делаются подряд. Этап 10 вынесен вперёд намеренно: артефакты по краям при
отдалении — самый заметный дефект картинки, а его решение задаёт форму слоя показа, на который
потом опираются этапы 5, 7 и 11. Этапы 5–8 независимы друг от друга.

---

### 5.4a Адаптивный интерфейс

Два прогона на устройстве дали один и тот же отчёт — «всё слишком мелко» — и три разные причины.

**1. `CanvasScaler` перебивал всю арифметику.** В сцене стоял режим Scale-With-Screen-Size с
эталоном 3440×1444 и Shrink-матчингом, то есть канвас домножал все наши размеры на
`min(w/3440, h/1444)` — около **0.3** на телефоне. Весь слой `UiTheme` считает размеры в
устройственных пикселях, поэтому это была вторая, противоречащая шкала поверх первой.
`AppBootstrap.EnsureUi` теперь принудительно ставит ConstantPixelSize со `scaleFactor = 1` —
именно в коде, а не правкой сцены, чтобы допущение не разъехалось снова. HUD печатает
`canvas x<factor>`; всё, кроме `1.00`, значит, что скейлер вернулся.

**2. Плотность.** `ScreenScale.Density` — единственное определение «сколько пикселей в
миллиметре», общее для `UiTheme` и слоя жестов (эти сборки друг друга не видят). Берётся
**максимум** из заявленного `Screen.dpi` и того, что следует из разрешения
(`короткая сторона / 380 dp`): второе — пол, который кривые метаданные производителя не пробьют.

**3. Две шкалы вместо одной.**

| Шкала | Что размеряет | Чем ограничена |
|---|---|---|
| `UiTheme.Scale` | «хром»: кнопка настроек, HUD | долей короткой стороны экрана (`ToggleShortEdgeFraction`) |
| `UiTheme.PanelScale` | всё внутри панели настроек | дополнительно — высотой, оставшейся под панель |

Разделение нужно потому, что кнопка может быть любой, какая нравится, а список опций обязан
остаться списком. Внутри панели — только `PanelPx` / `PanelInset`, никогда `Px`.

**Что делает панель адаптивной:**

- **Колонки.** Ширина экрана делится на натуральную ширину панели; телефон в портрете получает
  одну колонку, планшет или окно на десктопе — до трёх. Секции раскладываются по колонкам
  «в самую короткую», иначе одна колонка уезжает вниз, пока соседняя наполовину пуста.
- **Ограниченные отступы.** `UiTheme.PanelInset(container, dp, maxFraction)` — отступ в dp растёт
  без предела вместе со шкалой, а контейнер нет. Именно так отступы и колонка под маркер съели
  строку целиком, и в пункте меню осталось две буквы.
- **Подгонка текста.** `UiFactory.CreateText(..., fitToRect: true)` включает `resizeTextForBestFit`
  с нижней границей 45% от заданного кегля. Последняя линия обороны: строку всегда лучше показать
  чуть мельче, чем показать «Mand».
- **Прокрутка.** Что не поместилось по высоте — скроллится; вьюпорт — сама область содержимого
  стеклянной панели, уже лежащая в её скруглённой маске.

**Настройка INTERFACE SIZE** — множитель поверх плотности, лесенка XS…XXL вокруг 1.0. Он остаётся
даже теперь, когда скейлер починен: плотность экрана — не то же самое, что вкус и зрение
владельца, и ни одна формула этого не знает.

### 5.4b Экраны, виджеты и перестройка (этап 6)

- **Экраны живут дольше своих GameObject'ов.** `UiRouter` создаёт экраны один раз. Поворот
  экрана, смена масштаба интерфейса или `UiScreen.NeedsRebuild` пересоздают только объекты, а
  открытый экран открывается снова без анимации (`OpenImmediately`). Поэтому редактируемая
  палитра не теряется при повороте телефона.
- **Панель меняет форму — значит перестройка, а не обновление.** `SettingsScreen` просит
  перестройку, когда сменился фрактал (другие параметры) или число палитр. `BookmarksScreen`
  просит её при изменении списка.
- **Параметр фрактала применяется при отпускании пальца** (`SliderRow.onCommitted` через
  `PointerUpRelay`). Каждое изменение параметра сбрасывает кадр, и если применять его на каждом
  шаге перетаскивания, на экране были бы одни грубые проходы. Цвет, наоборот, применяется вживую
  (`onChanged`), потому что это перекраска буфера, а не рендер.
- **Значения из сессии в контрол переносятся только при реальном изменении**
  (`ParameterControl.lastSeen`). Если синхронизировать каждый кадр, слайдер будет прыгать назад
  под пальцем.
- `PointerUpRelay`, а не `EventTrigger`: `EventTrigger` реализует все интерфейсы указателя и
  перехватывает drag/scroll, так что панель переставала прокручиваться, если жест начинался на
  слайдере.
- Редактор палитры никогда не перезаписывает встроенную палитру: Save создаёт пользовательскую.
  Любое закрытие без Save откатывает и палитру, и раскраску (`OnOpenChanged(false)`).

### 5.4c Локализация (этап 13)

**Правило одно: в коде нет строк, которые читает пользователь.** Код держит ключ
(`settings.title`), файл локали говорит, как это звучит на выбранном языке.

| Что | Где | Зачем именно там |
|---|---|---|
| `IStringCatalog` (`Language`, `TryGet`, `Get`) и расширения `Format`, `GetOr`, `FractalName`, `ParameterLabel`, `PaletteName` | `Core/Localization` | Контракт поиска виден любому слою; соглашения об именах ключей контента живут в одном месте |
| `LocaleTable` — разбор одного файла | `Core/Localization` | Чистый C# без Unity, проверяется вне редактора |
| `Localizer` — все локали, выбор активной, откат, отчёт о пробелах | `App/Localization` | Знает про сессию (язык — её настройка) и про `Resources` |
| Файлы `en.txt`, `ru.txt` | `Assets/Resources/Localization` | Грузятся `Resources.LoadAll`, имя файла = код языка |

**Формат файла** — `ключ = значение`, по строке на запись, разрез по первому `=`, `#` —
комментарий, `\n` — перенос строки, `{0}` заполняет код. Не JSON: `JsonUtility` не читает словари,
а массив объектов ключ/значение — втрое больше текста, в котором переводчик может ошибиться. Не
Unity Localization package: он тянет Addressables, редактор таблиц и асинхронную загрузку ради
пятидесяти строк, а пользовательские палитры уже показали, что всё, что правится вне редактора,
у нас текстовое.

**Откат.** Ключа нет в активной локали — берётся английский. Нет и там — на экран выводится сам
ключ (заметно, а не пусто), в development-сборке один раз пишется предупреждение. При старте
development-сборка перечисляет, каких ключей не хватает каждой локали относительно `en` и какие в
ней лишние. `en.txt` обязан содержать каждый ключ, который спрашивает код.

**Язык** — `InterfaceSettings.Language`: код (`"ru"`) или пусто — «как в системе»
(`Application.systemLanguage` → код, а если файла для него нет — английский). Сохраняется в
`settings.json` полем `language`; старые файлы читаются как пустое. `Localizer` подписан на
`SessionChange.Interface` и сам переключает активную таблицу. `UiRouter` сравнивает
`Strings.Language` с языком, на котором строил, и при расхождении перестраивает UI целиком — тот же
путь, что у смены масштаба интерфейса. Поэтому экраны читают строки **только во время `OnBuild`** и
ничего не знают о смене языка; кэшировать строку между перестройками нельзя.

**Контент со своим именем** — фрактал, его параметры, встроенная палитра — переводится по ключу-
соглашению с откатом на собственное имя: `fractal.<id>`, `fractal.<id>.<ключ параметра>`,
`palette.<id>`. Перевод необязателен: новый фрактал работает и без строки в файлах локалей, так что
рецепт раздела 6 не удлиняется. Пользовательские палитры не переводятся — это имя, которое дал
пользователь. Имя закладки и имя новой палитры («Своя 3») записываются на языке, действовавшем в
момент сохранения, и дальше считаются данными пользователя.

**Числа** форматируются в инвариантной культуре во всех языках (`Format` тоже): десятичная точка в
«x2.4e+09» понятна всем, а сохранённое имя не должно зависеть от культуры устройства. Культурное
форматирование (`CultureInfo("ru-RU")`) не заводилось сознательно: данные культур под IL2CPP на
Android — отдельный риск, а выигрыш — запятая вместо точки.

**HUD не переводится.** Это отладочное табло (`fps`, `iter`, `dpi`, `workers`), его сверяют с логами
и между устройствами; перевод сделал бы это сравнение хуже.

**Шрифт.** `LegacyRuntime.ttf` — динамический шрифт; кириллица берётся из него или из системного
отката. Для языков с другой письменностью (CJK, арабский) проверить на устройстве и при нужде
добавить шрифт-откат в `UiTheme.Font`.

**Добавить язык** = положить `Assets/Resources/Localization/<код>.txt` с `language.name = <имя на
этом языке>`. Он сам появится в LANGUAGE; если его кода нет в `Localizer.SystemLanguageCode`,
добавить туда строку, иначе «как в системе» его не выберет. **Добавить строку** = ключ в код, запись
в `en.txt` и в остальные локали (чего не хватает — покажет лог при старте).

Не сделано, по мере необходимости: множественное число (`1 закладка / 2 закладки / 5 закладок` —
понадобится правило CLDR на язык, `Plural(key, n)`), направление письма справа налево.

### 5.5 Жесты: мёртвая зона и что значит «взаимодействие»

Проверка на устройстве вскрыла две связанные вещи.

**Любое касание считалось жестом.** `FractalGestureInput` возвращал `IsInteracting = true` на
любой палец на стекле, а порог панорамы был `sqrMagnitude > 0.01`, то есть 0.1 пикселя. Дрожь
пальца — несколько пикселей за кадр, поэтому касание сразу читалось как перетаскивание: вид
уползал, презентер запрашивал рендер с расширенным полем, и картинка падала до прохода 16×16.

Что сделано:

- Каждый жест проходит **мёртвую зону в dp** (`TouchSlopDp = 8`, как в Android) прежде чем
  включиться. Зона — условие старта, а не пофреймовый фильтр: медленное перетаскивание иначе бы
  запиналось. Накопленный до включения сдвиг **не применяется**, иначе картинка прыгает на
  ширину зоны в момент старта.
- `IsInteracting` теперь значит «пользователь двигает вид», а не «палец на экране». Смена этого
  флага сама по себе больше не сбрасывает готовый кадр: `ShouldRequestCpuRender` перезапрашивает
  рендер только при реальном изменении вида либо при завершении жеста, если предыдущий запрос был
  с расширенным полем (его надо переснять в полном разрешении).
- Смена числа пальцев начинает жест заново: центр и разброс скачком меняются, и перенос старого
  «включённого» состояния утащил бы вид за ними.

**Грубый проход не заменяет более чёткий кадр.** Сначала это решалось через `minimumPublishStep`
(проходы грубее шага 4 не публиковались, пока старый кадр покрывает вид). С 2026-09-14 этот
механизм заменён удерживаемым кадром (`RetainedFrame`, см. 5.6): публикуется каждый проход, а
более чёткий старый кадр копируется и лежит поверх, пока его не превзойдут.

### 5.6 Плавность движения: инерция, бюджет, прогноз, удержание кадра

Сделано по разбору `FadeMoveController` / `SmoothMovementDeceleration` / `PictureView` эталонного
приложения (`RESEARCH-mandelbrot-browser.md` §4–5). После теста на устройстве вывод был «работает
хорошо, но плавности зума, как у конкурента, нет, и нет инерции». Причин оказалось пять.

1. **Инерция** (`Core/View/ViewInertia`). Жесты (`FractalGestureInput`) пишут кольцевой буфер
   сдвигов. При отпускании скорость считается как сумма сдвигов за последние 100 мс, делённая на
   окно, поэтому палец, остановившийся перед отпусканием, почти ничего не бросает. Скорость
   затухает как `v0·e^(−kt)`, где `k = −ln(0.002)/T`. Зум затухает в логарифме. Пороги старта и
   остановки взяты у эталона, в коротких рёбрах экрана в секунду для панорамы. Для короткого жеста
   (<0.3 с, «тап») порог старта выше. Панорама бросается только с одного пальца, зум и поворот —
   с двух, вокруг последней точки щипка. Если после щипка поднят один палец, второй игнорируется
   до отпускания, иначе он начнёт панораму и погасит инерцию (пальцы никогда не отрываются
   одновременно). Любое касание картинки (`FractalGestureFrame.Touching`, в том числе палец без
   движения) или любое внешнее изменение вида (закладка, сброс, смена фрактала) инерцию
   останавливает. Длительность — настройка INERTIA (Off/Short/Medium/Long = 0/2/5/10 с,
   `InterfaceSettings.InertiaSeconds`, по умолчанию 5).
2. **Бюджет времени рендера во время движения.** Рендерер сам меряет стоимость сэмпла
   (`RecordThroughput`, только проходы ≥16k сэмплов: на грубых почти всё время уходит на
   пробуждение потоков, и оценка раздувалась). Запрос с `timeBudgetSeconds` берёт проходы, пока
   оценка влезает в бюджет, но первый проход берётся всегда. Во время жеста или инерции бюджет
   0.2 с, в покое 0. Раньше во время зума текущий рендер доходил до шага 1, и между кадрами
   картинка подолгу «плыла».
3. **Прогноз** (`IViewForecast`, реализует `ViewInertia`). Траектория инерции задана формулой,
   поэтому `Predict` точен. Запрос нацелен на момент `now + 1.5·бюджет` — середину жизни кадра, от
   прихода до прихода следующего. Поле расширяется (`CoveringFieldFactor`), пока кадр не покроет
   вид и в начале, и в конце этого окна. Перезапуск во время инерции — только если идущий рендер
   не бюджетный или завис (>4 бюджетов). Проверка «покрывает ли идущий рендер текущий вид» здесь
   неверна: при приближении текущий вид шире будущего кадра, и такая проверка перезапускала
   рендер каждый кадр, так что за всё движение не публиковалось ничего (найдено в тесте).
4. **Удерживаемый кадр** (`Rendering/RetainedFrame`, третий слой `FrameCompositor`,
   `_LayerAlpha` в шейдере). Перед заливкой нового кадра рендерер поднимает
   `FrameReplacing(view, step)`, и презентер копирует уходящий кадр на GPU (`Graphics.Blit`).
   Чёткость сравнивается как шаг выборки на плоскости `step × scale`, ограниченный снизу
   `scale` текущего вида: деталь мельче пикселя экрана не видна и без mip-уровней только
   рябит. Без этого ограничения кадр с большей глубины навсегда оставался поверх после зума на
   отдаление. Если уходящий кадр чётче (с допуском 25%, шаги отличаются в 2 раза), копия лежит
   сверху, пока новый не догонит. Иначе она растворяется за 0.15 с. Смена палитры, фрактала,
   бэкенда и размера сбрасывает копию.
5. **Сглаживание грубых проходов** (`FractalCpuRenderer.MapInterpolated`). Проход с шагом >1
   раскрашивается как билинейная интерполяция цветов сэмплов, а не блоками. У эталона грубые
   фазы — маленькие битмапы, растянутые с фильтрацией, отсюда «размытие вместо пикселей».
   Буфер escape-значений по-прежнему хранит блоки: его читают последующие проходы и перекраска.

Плюс частота кадров: на телефоне `targetFrameRate` равен частоте панели (60–144 вместо
фиксированных 60). Регулятор потоков при этом целится не выше 90 Гц.

Проверено в Play mode через eval (`runInBackground = true`, иначе редактор без фокуса не крутит
кадры): броски зума, зума на отдаление и панорамы на глубине fp64. Кадры приходят каждые
~100 мс сериями 16→4→2 и в момент прихода покрывают вид; удержание и растворение работают;
после остановки идёт полный рендер. На устройстве не проверялось.

---

## 8. Открытые вопросы


- **Тесты.** `com.unity.test-framework` в `manifest.json` нет. Edit-mode тесты на
  `StateCodec` (round-trip сохранения) и на семплеры (эталонные значения итераций)
  дёшевы и окупятся при добавлении фракталов. Требует добавления пакета.
- **Точность.** `HighPrecision` на `decimal` ограничен ~1e-28, `DoubleDouble` — примерно 1e-30,
  а `MaximumIterations = 2048` упирается ещё раньше: на масштабе 1e-24 картинка становится
  сплошным «интерьером» задолго до того, как кончится точность. То есть текущий предел глубины —
  бюджет итераций, а не арифметика. Разбор в `RESEARCH-mandelbrot-browser.md` §7.3. Пертурбация
  (этап 12, см. 4.8) уже сделала глубину дешёвой; осталось поднять потолок итераций и сделать
  бюджет адаптивным, по предыдущему кадру.
- **Артефакт на переключении GPU -> CPU.** При уходе глубже `gpuMinimumScale` оба CPU-слоя
  помечаются несостоятельными (`DiscardPublished`), поэтому первый CPU-кадр приходит с нуля и
  виден скачок к цвету интерьера. Лечится засевом CPU-буфера содержимым GPU-текстуры в момент
  переключения (`AsyncGPUReadback` или `ReadPixels` один раз на переключение) — композитору
  тогда есть что разместить сразу. Не срочно: замечено пользователем как «не супер критично».
- **Julia как режим, а не отдельный фрактал.** У Мандельброта и Жюлиа общая итерация,
  разный старт (`z0`/`c`). Разумно сделать `JuliaDefinition` отдельным `Id`, но
  переиспользовать семплер через параметр — проверить на Этапе 7.
