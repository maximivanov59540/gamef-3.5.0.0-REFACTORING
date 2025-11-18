# АРХИТЕКТУРНЫЙ АНАЛИЗ КОДОВОЙ БАЗЫ
## City-Building Game Project (gamef-3.5.0.0-REFACTORING)

**Дата анализа:** 2025-11-18  
**Общая статистика:** 91 C# файл | 22 Singleton | 5 God Classes | ~16,340 строк кода

---

## 1. GOD CLASSES (файлы >500 строк с множеством ответственностей)

### 1.1 КРИТИЧЕСКИЕ (>1200 строк)

#### 📍 BuildingResourceRouting.cs - **1,375 строк**
**Путь:** `/Construction/Core/BuildingResourceRouting.cs`

**Ответственности (6+):**
- ✘ Маршрутизация Input ресурсов (выбор источника)
- ✘ Маршрутизация Output ресурсов (выбор назначения)
- ✘ Auto-discovery ближайших складов (BFS на сетке дорог)
- ✘ Round-Robin распределение между потребителями
- ✘ Координация с другими производителями (избежание дублирования)
- ✘ Управление состоянием маршрутов (кэширование, refresh)
- ✘ Priority modes (preferDirectSupply, preferDirectDelivery)

**Зависимости:** BuildingRegistry, GridSystem, ResourceCoordinator, RoadManager  
**Публичные методы:** 7

**Проблемы:**
- ❌ Смешивает логику маршрутизации с бизнес-логикой распределения
- ❌ Сложность O(n²) в методах поиска маршрутов
- ❌ Большое количество конфигурационных полей (12+ параметров)
- ❌ Интеграция с 4+ синглтонами ➜ high coupling

**Рекомендация:**
```
Разделить на 3 класса:
1. RoutingResolver         - выбор маршрутов
2. ConsumerSelector        - выбор потребителей (round-robin)
3. ProducerCoordinator     - координация между производителями
```

---

#### 📍 BuildingManager.cs - **1,306 строк**
**Путь:** `/Construction/Core/BuildingManager.cs`

**Ответственности (8+):**
- ✘ Building placement (с валидацией)
- ✘ Building deletion (с 50% refund)
- ✘ Building movement/relocation
- ✘ Building copying (одиночное и групповое)
- ✘ Building rotation (с поддержкой size swap)
- ✘ Blueprint mode (тип ресурсов не тратятся)
- ✘ Building upgrade (tier upgrade логика)
- ✘ Ghost building preview (визуальная обратная связь)
- ✘ Resource validation и cost checking

**Зависимости:** ResourceManager, PopulationManager, GridSystem, PlayerInputController, 
NotificationManager, EconomyManager, MoneyManager, BlueprintManager

**Публичные методы:** 25 (⚠️ ОЧЕНЬ МНОГО)

**Проблемы:**
- ❌ Фасад для слишком большого количества операций
- ❌ Смешивает placement, validation, refund логику
- ❌ Зависит от 8 различных менеджеров (star-shaped coupling)
- ❌ Содержит как UI (SelectionManager) так и game logic
- ❌ Copy/Move операции содержат дублированный код

**Рекомендация:**
```
Разделить на 4 класса:
1. BuildingPlacer         - placement + validation
2. BuildingRemover        - deletion + refund calculation
3. BuildingTransformer    - move, rotate, copy, upgrade
4. BuildingValidator      - grid checks, resource checks
```

---

#### 📍 CartAgent.cs - **1,262 строк**
**Путь:** `/Economy/Warehouse/CartAgent.cs`

**Ответственности (7+):**
- ✘ State machine (6 состояний: Idle → Loading → Delivering → Unloading...)
- ✘ Управление грузовыми слотами (3 слота × 5 единиц)
- ✘ Pathfinding на дорожной сети (интеграция LogisticsPathfinder)
- ✘ Движение к целям (корутины + Vector3 интерполяция)
- ✘ Загрузка/разгрузка ресурсов (synchronization с inventories)
- ✘ Запрос ресурсов к производителям
- ✘ Обработка ошибок (stuck detection, no-path handling)

**Зависимости:** GridSystem, RoadManager, BuildingRegistry, BuildingResourceRouting

**Проблемы:**
- ❌ State machine занимает 500+ строк в одном классе
- ❌ Грузовая логика (CargoSlot) встроена в CartAgent
- ❌ Движение, pathfinding и inventory management смешаны
- ❌ Сложный жизненный цикл корутин (трудно отследить)

**Рекомендация:**
```
Разделить на 3 класса:
1. CartAgent              - только state machine (контроллер)
2. CartMovement           - pathfinding и движение
3. CartInventory          - грузовые слоты и их логика
```

---

### 1.2 СРЕДНИЕ (500-700 строк)

#### 📍 GroupOperationHandler.cs - **620 строк**
**Путь:** `/Construction/GroupOps/GroupOperationHandler.cs`

**Ответственности (5+):**
- ✘ Group selection (box selection логика)
- ✘ Batch copy операции (с offset расчетом и ротацией)
- ✘ Batch move операции (lifting/placing buildings)
- ✘ Batch delete операции
- ✘ Ghost pool management (для preview)
- ✘ Rotation mathematics

**Зависимости:** GridSystem, PlayerInputController, BuildingManager, RoadManager

**Проблемы:**
- ❌ Смешивает selection, preview, execution логику
- ❌ Дублирует код из BuildingManager (EnterBuildMode, etc.)
- ❌ Ghost pool - это отдельная concern

**Рекомендация:**
```
Разделить на 2 класса:
1. GroupSelector          - selection + validation
2. GroupExecutor          - batch operations (copy, move, delete)
```

---

#### 📍 RoadCoverageVisualizer.cs - **564 строк**
**Путь:** `/Construction/Roads/RoadCoverageVisualizer.cs`

**Ответственности (4+):**
- ✘ Visualization (материалы, цвета)
- ✘ Road tile rendering
- ✘ Building outline rendering
- ✘ Fade animations (корутины)
- ✘ Source management (multiple coverage sources)

**Зависимости:** GridSystem, RoadManager

**Проблемы:**
- ❌ Чистая визуализация с 4+ responsibilities
- ❌ Управление материалами, цветами и анимациями в одном месте
- ❌ Сложная логика объединения эффективности от разных источников

**Рекомендация:**
```
Разделить на 2 класса:
1. CoverageCalculator    - расчет эффективности от источников
2. CoverageRenderer      - материалы, цвета, анимации
```

---

## 2. SINGLETON КЛАССЫ (22 ВСЕГО)

### Полный список Singletons:

| Класс | Файл | Зависимости |
|-------|------|-------------|
| **ResourceManager** | `/Economy/Systems/ResourceManager.cs` | MoneyManager |
| **MoneyManager** | `/Economy/Money/MoneyManager.cs` | EconomyManager |
| **EconomyManager** | `/Economy/Systems/EconomyManager.cs` | MoneyManager, TaxManager |
| **PopulationManager** | `/Economy/Systems/PopulationManager.cs` | WorkforceManager, ResourceManager |
| **WorkforceManager** | `/Economy/Systems/WorkforceManager.cs` | PopulationManager |
| **EventManager** | `/Economy/Event/EventManager.cs` | HappinessManager, PopulationManager |
| **HappinessManager** | `/Economy/Taxation/HappinessManager.cs` | TaxManager, PopulationManager |
| **TaxManager** | `/Economy/Taxation/TaxManager.cs` | MoneyManager, HappinessManager |
| **RoadManager** | `/Construction/Roads/RoadManager.cs` | GridSystem |
| **AuraManager** | `/Economy/Aura/AuraManager.cs` | RoadManager |
| **ResourceCoordinator** | `/Economy/Storage/ResourceCoordinator.cs` | GridSystem, RoadManager |
| **LogisticsManager** | `/Economy/Storage/LogisticsManager.cs` | GridSystem, RoadManager |
| **BuildingRegistry** | `/Economy/Systems/BuildingRegistry.cs` | - |
| **PlayerInputController** | `/Construction/Input/PlayerInputController.cs` | 13+ других |
| **SelectionManager** | `/Construction/Core/SelectionManager.cs` | GridSystem, PlayerInputController, AuraManager |
| **BuildingManager** | `/Construction/Core/BuildingManager.cs` | ❌ **НЕ Singleton** (FindFirstObjectByType) |
| **BlueprintManager** | `/Construction/Core/BlueprintManager.cs` | GridSystem |
| **BuildOrchestrator** | `/Construction/Core/BuildOrchestrator.cs` | BuildingManager, GroupOperationHandler... |
| **GroupOperationHandler** | `/Construction/GroupOps/GroupOperationHandler.cs` | GridSystem, BuildingManager, RoadManager |
| **MassBuildHandler** | `/Construction/GroupOps/MassBuildHandler.cs` | GridSystem, RoadManager |
| **RoadOperationHandler** | `/Construction/GroupOps/RoadOperationHandler.cs` | GridSystem, RoadManager, PlayerInputController |
| **TimeManager** | `/Infrastructure/TimeManager.cs` | - |

### ⚠️ Проблемы Singleton Pattern:

**Всего 22 Singletons** - это СЛИШКОМ МНОГО!

**Исходящий граф зависимостей:**
```
PlayerInputController (13)
    ↓
BuildingManager (8) ↓ SelectionManager (3) ↓
    ↓                   ↓
ResourceManager ← MoneyManager ← EconomyManager ← TaxManager ← HappinessManager
     ↓                                                ↓
PopulationManager ← WorkforceManager           EventManager
```

**Проблемы:**
- ❌ **Star-shaped coupling:** Много классов зависит от PlayerInputController
- ❌ **Circular chains:** EventManager → HappinessManager → TaxManager → MoneyManager → (back to EconomyManager)
- ❌ **Hard to test:** Невозможно тестировать в изоляции
- ❌ **Global state:** Изменение одного синглтона может сломать 5+ других

**Рекомендация:**
```
Цель: Снизить с 22 до 5-7 синглтонов

Оставить только:
  1. ResourceManager      - глобальный ресурсный пул
  2. MoneyManager         - глобальный банк
  3. RoadManager          - дорожная сеть (один источник истины)
  4. PlayerInputController - входная точка
  5. EventManager         - глобальные события
  
Убрать:
  - PopulationManager → ResourceManager.GetPopulationData()
  - WorkforceManager → встроить в PopulationManager
  - EconomyManager → встроить в MoneyManager
  - TaxManager → встроить в MoneyManager
  - HappinessManager → встроить в EventManager
  - BuildingRegistry → встроить в BuildingManager
  - LogisticsManager → встроить в RoadManager
  - SelectionManager → встроить в PlayerInputController
  - BlueprintManager → встроить в BuildingManager
```

---

## 3. ЦИКЛИЧЕСКИЕ ЗАВИСИМОСТИ

### Обнаруженные циклы:

#### 🔴 Цикл 1: Economy Loop

```
MoneyManager
    ↓ depends on EconomyManager.IsInDebt
EconomyManager
    ↓ depends on TaxManager.GetTaxIncome()
TaxManager
    ↓ depends on HappinessManager.CurrentHappiness
HappinessManager
    ↓ depends on EventManager
EventManager
    ↓ depends on PopulationManager
PopulationManager
    ↓ depends on WorkforceManager
WorkforceManager
    ↓ depends on ResourceManager
ResourceManager
    ↓ (indirectly used by MoneyManager for building costs)
MoneyManager ← ЦИКЛ ЗАМКНУТ!
```

**Риск:** Если изменить MoneyManager, нужно проверить всю цепочку (8 файлов)

---

#### 🔴 Цикл 2: Building Operations Loop

```
BuildingManager
    ↓ uses ResourceManager.SpendResources()
ResourceManager
    ↓ broadcasts OnResourceChanged event
Event subscribers:
    UIManager, PopulationManager, Residence...
    ↓
Residence
    ↓ depends on PopulationManager
PopulationManager
    ↓ has workforce requests back to BuildingManager
    (via WorkforceManager assignment)
BuildingManager ← ЦИКЛ ЗАМКНУТ!
```

**Риск:** Удаление/переименование метода BuildingManager может сломать Residence.cs

---

#### 🟡 Цикл 3: Road & Building Coupling

```
RoadManager
    ↓ broadcasts OnRoadAdded/OnRoadRemoved
RoadCoverageVisualizer, LogisticsPathfinder...
    ↓
CartAgent uses RoadManager.FindPath()
    ↓ CartAgent is placed by BuildingManager
BuildingManager
    ↓ uses RoadManager for pathfinding validation
RoadManager ← ЦИКЛ ЗАМКНУТ!
```

---

## 4. TIGHT COUPLING (Плотная связанность)

### 4.1 Star-Shaped Coupling (звездообразное)

**PlayerInputController** зависит от 13+ классов:

```csharp
// In PlayerInputController.cs
public class PlayerInputController : MonoBehaviour
{
    MassBuildHandler        → controls batch operations
    SelectionManager        → manages selection
    ResourceManager         → checks resources
    RoadManager             → validates road placement
    BlueprintManager        → blueprint mode
    BuildOrchestrator       → orchestrates building
    GroupOperationHandler   → group operations
    PlayerInputController itself → state machine (13 states)
    ... и еще 5+ зависимостей в states/
}
```

**Проблема:** Изменение любого из 13 классов может повлиять на InputController

**Решение:** Использовать Event Aggregator pattern вместо прямых ссылок

---

### 4.2 Direct Component Access (прямое обращение к компонентам)

**Проблема в CartAgent.cs:**

```csharp
// ❌ ПЛОХО: Прямое обращение к компонентам соседа
var homeOutput = _homeBase.GetComponent<BuildingOutputInventory>();
var homeInput = _homeBase.GetComponent<BuildingInputInventory>();
var routing = _homeBase.GetComponent<BuildingResourceRouting>();

// ✅ ХОРОШО: Интерфейсы
IResourceProvider outputSource = _homeBase.GetComponent<IResourceProvider>();
IResourceReceiver inputTarget = _homeBase.GetComponent<IResourceReceiver>();
```

**Файлы с этой проблемой:**
- CartAgent.cs (3+ GetComponent calls)
- BuildingResourceRouting.cs (5+ GetComponent calls)
- ResourceProducer.cs (4+ GetComponent calls)
- GroupOperationHandler.cs (в ghost preview логике)

---

### 4.3 Cross-System References (кросс-системные ссылки)

**Residence.cs зависит от 5+ систем:**

```csharp
private AuraManager _auraManager;           // Economy ← Aura
private HappinessManager _happinessManager; // Economy ← Taxation
private PopulationManager _populationManager; // Economy ← Systems
private ResourceManager _resourceManager;   // Economy ← Systems
private TaxManager _taxManager;             // Economy ← Taxation

// Result: Удаление HappinessManager может сломать Residence!
```

---

## 5. НАРУШЕНИЕ SINGLE RESPONSIBILITY PRINCIPLE (SRP)

### Примеры классов с множественной ответственностью:

| Класс | Ответственности | SRP Score |
|-------|-----------------|-----------|
| **BuildingResourceRouting** | Input routing, Output routing, Auto-discovery, Round-robin, Coordination, State management | ★★★☆☆ (6 причин для изменения) |
| **BuildingManager** | Placement, Deletion, Movement, Copying, Rotation, Upgrade, Blueprint mode, Validation | ★★★☆☆ (8 причин) |
| **CartAgent** | State machine, Movement, Cargo management, Pathfinding, Inventory sync | ★★☆☆☆ (5 причин) |
| **Residence** | Population housing, Need satisfaction, Tax collection, Happiness tracking | ★★★☆☆ (4 причин) |
| **GroupOperationHandler** | Selection, Validation, Preview, Execution, Ghost management | ★★☆☆☆ (5 причин) |
| **RoadCoverageVisualizer** | Visualization, Rendering, Animation, Material management | ★★★★☆ (4 причин) |
| **PlayerInputController** | State management, Event routing, Mode transitions, Dependency injection | ★★★☆☆ (4 причин) |

---

## 6. ДЕТАЛЬНЫЕ РЕКОМЕНДАЦИИ ПО РЕФАКТОРИНГУ

### ФАЗА 1: Декомпозиция God Classes (2-3 недели)

#### 6.1 BuildingResourceRouting → 3 класса

**Текущее:**
```csharp
public class BuildingResourceRouting : MonoBehaviour  // 1375 строк
{
    // Маршрутизация Output (найти получателя)
    public void RefreshRoutes() { ... }
    
    // Маршрутизация Input (найти источник)
    private void AutoDiscoverInputSource() { ... }
    
    // Round-robin распределение
    private IResourceReceiver SelectConsumer() { ... }
    
    // Координация с другими производителями
    private void CoordinateWithProducers() { ... }
}
```

**Целевое состояние:**

```csharp
// 1️⃣ RoutingResolver - выбор маршрутов (~400 строк)
public class RoutingResolver : MonoBehaviour
{
    public IResourceReceiver ResolveOutputDestination()
    public IResourceProvider ResolveInputSource()
    private void AutoDiscoverWarehouse() { }
}

// 2️⃣ ConsumerSelector - выбор потребителей (~200 строк)
public class ConsumerSelector : MonoBehaviour
{
    public IResourceReceiver SelectNextConsumer()
    private void RotateConsumerIndex() { }
}

// 3️⃣ ProducerCoordinator - координация (~300 строк)
public class ProducerCoordinator : MonoBehaviour
{
    public static ProducerCoordinator Instance { get; }
    public bool IsConsumerAllocated(IResourceReceiver consumer)
    public void AllocateConsumer(IResourceReceiver consumer, IResourceProvider producer)
}

// BuildingResourceRouting остается как Facade (~150 строк)
public class BuildingResourceRouting : MonoBehaviour
{
    private RoutingResolver _routingResolver;
    private ConsumerSelector _consumerSelector;
    
    public void RefreshRoutes() { /* delegate */ }
}
```

**Файлы для изменения:**
- BuildingResourceRouting.cs (декомпозиция)
- ResourceCoordinator.cs (использование ProducerCoordinator)
- CartAgent.cs (использование RoutingResolver)

---

#### 6.2 BuildingManager → 4 класса

**Целевое состояние:**

```
BuildingManager (Facade, ~300 строк)
├── BuildingPlacer (~250 строк)
├── BuildingRemover (~200 строк)
├── BuildingTransformer (~300 строк)
└── BuildingValidator (~200 строк)
```

**Реализация:**

```csharp
// 1️⃣ BuildingValidator (~200 строк)
public class BuildingValidator : MonoBehaviour
{
    public bool CanPlaceBuilding(Vector2Int gridPos, Vector2Int size)
    public bool CanAffordBuilding(BuildingData data)
    public bool IsGridCellFree(Vector2Int gridPos)
    public (bool canPlace, string reason) ValidatePlacement()
}

// 2️⃣ BuildingPlacer (~250 строк)
public class BuildingPlacer : MonoBehaviour
{
    private BuildingValidator _validator;
    
    public GameObject PlaceBuilding(BuildingData data, Vector2Int gridPos)
    private void CreateGhostPreview(BuildingData data)
    public bool ConfirmPlacement()
}

// 3️⃣ BuildingRemover (~200 строк)
public class BuildingRemover : MonoBehaviour
{
    private const float REFUND_PERCENTAGE = 0.5f;
    
    public void DeleteBuilding(BuildingIdentity identity)
    private float CalculateRefund(BuildingData data)
    private void ReturnResourcesToPlayer(BuildingData data)
}

// 4️⃣ BuildingTransformer (~300 строк)
public class BuildingTransformer : MonoBehaviour
{
    public void MoveBuilding(BuildingIdentity identity, Vector2Int newPos)
    public void RotateBuilding(float angle)
    public void CopyBuilding(BuildingIdentity source, Vector2Int targetPos)
    public void UpgradeBuilding(BuildingIdentity identity)
}

// BuildingManager (Facade)
public class BuildingManager : MonoBehaviour
{
    private BuildingPlacer _placer;
    private BuildingRemover _remover;
    private BuildingTransformer _transformer;
    
    public void EnterBuildMode(BuildingData data)
        => _placer.CreateGhostPreview(data);
    
    public void ConfirmBuilding()
        => _placer.ConfirmPlacement();
}
```

**Преимущества:**
- Каждый класс имеет одну причину для изменения
- Легче тестировать (каждый компонент в отдельности)
- Легче переиспользовать (BuildingValidator можно использовать в группировке)

---

#### 6.3 CartAgent → 3 класса

**Целевое состояние:**

```
CartAgent (State Machine, ~200 строк)
├── CartMovement (~250 строк)
├── CartInventory (~150 строк)
└── CartPathfinder (~150 строк)
```

**Реализация:**

```csharp
// 1️⃣ CartInventory (~150 строк)
[System.Serializable]
public class CartInventory : MonoBehaviour
{
    private CargoSlot[] _cargoSlots = new CargoSlot[3];
    
    public bool TryAddResource(ResourceType type, float amount)
    public bool TryRemoveResource(ResourceType type, float amount)
    public float GetTotalWeight()
    public bool HasSpace(ResourceType type)
}

// 2️⃣ CartPathfinder (~150 строк)
public class CartPathfinder : MonoBehaviour
{
    public bool TryFindPath(Vector2Int from, Vector2Int to, out List<Vector2Int> path)
    private void HandlePathfindingFailure()
}

// 3️⃣ CartMovement (~250 строк)
public class CartMovement : MonoBehaviour
{
    private CartPathfinder _pathfinder;
    
    public void MoveToTarget(Vector2Int target)
    public void FollowPath(List<Vector2Int> path)
    private void UpdatePosition()
}

// CartAgent (State Machine & Controller)
public class CartAgent : MonoBehaviour
{
    private CartMovement _movement;
    private CartInventory _inventory;
    private CartPathfinder _pathfinder;
    
    private enum State { Idle, Loading, Delivering, ... }
    
    public void Update()
    {
        // State machine - только логика переходов
        switch(_state)
        {
            case State.Loading:
                if (_inventory.IsFull) TransitionTo(State.Delivering);
                break;
            // ...
        }
    }
}
```

---

### ФАЗА 2: Снижение количества Singletons (1-2 недели)

#### Целевое состояние: 22 → 7 синглтонов

**Оставляем (true singletons):**
```csharp
1. ResourceManager       // Глобальный пул ресурсов
2. MoneyManager          // Глобальная валюта (тесно связана с ResourceManager)
3. RoadManager           // Единственный источник истины для дорожной сети
4. PlayerInputController // Входная точка для игрока
5. EventManager          // Глобальные события (панdemics, riots)
6. TimeManager           // Глобальное управление временем
7. BuildingRegistry      // Кэш всех зданий (нужен для быстрого lookup)
```

**Убираем (преобразуем в обычные компоненты):**

```
PopulationManager → Вложить в ResourceManager
    ResourceManager.PopulationData populationData;
    
WorkforceManager → Вложить в PopulationManager
    PopulationData.AssignWorkforce(BuildingIdentity, int count)
    
EconomyManager → Вложить в MoneyManager
    MoneyManager.IsInDebt { get; }
    MoneyManager.CalculateDebtFromUpkeep()
    
TaxManager → Вложить в MoneyManager
    MoneyManager.TaxSystem
    
HappinessManager → Вложить в EventManager
    EventManager.CurrentHappiness
    EventManager.ModifyHappiness(float delta)
    
SelectionManager → Вложить в PlayerInputController
    PlayerInputController.SelectedBuildings
    
BlueprintManager → Вложить в BuildingManager
    BuildingManager.IsBlueprintMode { get; set; }
    
LogisticsManager → Вложить в RoadManager
    RoadManager.LogisticsPathfinder
```

**Реструктуризация зависимостей:**

```
ДО:                          ПОСЛЕ:
MoneyManager                 MoneyManager
├── EconomyManager           ├── economy data
├── TaxManager               ├── tax system
└── HappinessManager         └── happiness tracking

PopulationManager            ResourceManager
├── WorkforceManager         ├── PopulationData
└── TaxManager               └── PopulationData.workforce
```

---

### ФАЗА 3: Разрешение циклических зависимостей (1 неделя)

#### 3.1 Economy Loop → Event-Based Communication

**Текущая проблема:**
```csharp
// MoneyManager directly checks EconomyManager
if (EconomyManager.Instance.IsInDebt) { BlockBuilding(); }

// EconomyManager directly accesses TaxManager
float income = TaxManager.Instance.CalculateTax();

// TaxManager checks HappinessManager
float modifier = HappinessManager.Instance.CurrentHappiness;
```

**Решение:** Event-driven вместо polling

```csharp
// 1️⃣ MoneyManager просто отправляет события
public class MoneyManager : MonoBehaviour
{
    public event System.Action<float> OnMoneyChanged;
    public event System.Action<bool> OnDebtStatusChanged;
    
    private void Update()
    {
        if (_currentMoney < 0)
            OnDebtStatusChanged?.Invoke(true);
    }
}

// 2️⃣ Другие системы подписываются
public class BuildingManager : MonoBehaviour
{
    void OnEnable()
    {
        MoneyManager.Instance.OnDebtStatusChanged += HandleDebtChanged;
    }
    
    void OnDisable()
    {
        MoneyManager.Instance.OnDebtStatusChanged -= HandleDebtChanged;
    }
    
    private void HandleDebtChanged(bool isInDebt)
    {
        if (isInDebt) BlockBuildingPlacement();
    }
}

// 3️⃣ TaxManager отправляет события о налогах
public class TaxManager : MonoBehaviour
{
    private MoneyManager _moneyManager;
    
    public void CollectTaxes()
    {
        float totalTax = CalculateTax();
        _moneyManager.AddMoney(totalTax);
        // Все слушатели OnMoneyChanged получат уведомление
    }
}
```

**Преимущества:**
- ✓ Разрывает циклические зависимости
- ✓ Позволяет тестировать без инициализации всей цепочки
- ✓ Легче добавлять новых слушателей

---

#### 3.2 Building-Residence Loop → Interface Injection

**Текущая проблема:**
```csharp
// Residence.cs directly depends on PopulationManager
public class Residence : MonoBehaviour
{
    private PopulationManager _populationManager;
    
    void Start()
    {
        _populationManager = FindFirstObjectByType<PopulationManager>();
    }
}

// Но PopulationManager зависит от BuildingManager для назначения работников
```

**Решение:** Interface-based injection

```csharp
// 1️⃣ Создать интерфейс
public interface IPopulationService
{
    void AddPopulation(PopulationTier tier, int count);
    int GetAvailableWorkforce(PopulationTier tier);
    event System.Action<int> OnPopulationChanged;
}

// 2️⃣ Реализация
public class PopulationService : MonoBehaviour, IPopulationService
{
    public void AddPopulation(PopulationTier tier, int count) { ... }
    public int GetAvailableWorkforce(PopulationTier tier) { ... }
    public event System.Action<int> OnPopulationChanged;
}

// 3️⃣ Residence принимает интерфейс
public class Residence : MonoBehaviour
{
    private IPopulationService _populationService;
    
    void SetPopulationService(IPopulationService service)
    {
        _populationService = service;
    }
}

// 4️⃣ BuildingManager инжектирует сервис
public class BuildingManager : MonoBehaviour
{
    private IPopulationService _populationService;
    
    GameObject PlaceBuilding(BuildingData data, Vector2Int pos)
    {
        var residence = newBuilding.GetComponent<Residence>();
        if (residence != null)
            residence.SetPopulationService(_populationService);
    }
}
```

---

### ФАЗА 4: Замена прямых ссылок на интерфейсы (1-2 недели)

#### 4.1 CartAgent: Component Access → Interfaces

```csharp
// ДО: Прямое обращение к компонентам
private void LoadOutputFromHome()
{
    var output = _homeBase.GetComponent<BuildingOutputInventory>();
    output.TakeResources(...);
}

// ПОСЛЕ: Через интерфейсы
private void LoadOutputFromHome()
{
    var provider = _homeBase.GetComponent<IResourceProvider>();
    provider.TakeResources(...);
}
```

**Преимущества:**
- Не зависит от конкретной реализации (может быть BuildingOutputInventory или Warehouse)
- Легче тестировать (можно создать mock)
- Менее подвержено изменениям

---

## 7. ПРИОРИТИЗИРОВАННЫЙ ПЛАН РЕФАКТОРИНГА

### СПРИНТ 1 (Неделя 1-2): Критические God Classes

**Priority: 🔴 ВЫСОКИЙ**

1. **BuildingManager → 4 класса** (~30 часов)
   - BuildingValidator
   - BuildingPlacer
   - BuildingRemover
   - BuildingTransformer
   - Файлы: BuildingManager.cs, State_Building.cs, State_*

2. **CartAgent → 3 класса** (~ 20 часов)
   - CartMovement
   - CartInventory
   - CartPathfinder
   - Файлы: CartAgent.cs, WarehouseManager.cs

---

### СПРИНТ 2 (Неделя 3): Средние God Classes

**Priority: 🟠 СРЕДНИЙ**

3. **BuildingResourceRouting → 3 класса** (~25 часов)
   - RoutingResolver
   - ConsumerSelector
   - ProducerCoordinator

4. **GroupOperationHandler → 2 класса** (~15 часов)
   - GroupSelector
   - GroupExecutor

---

### СПРИНТ 3 (Неделя 4-5): Singleton Reduction

**Priority: 🔴 ВЫСОКИЙ** (много циклических зависимостей)

5. **Merge PopulationManager into ResourceManager** (~20 часов)
   - Update all references
   - Test thoroughly

6. **Merge EconomyManager into MoneyManager** (~15 часов)

---

### СПРИНТ 4 (Неделя 6): Breaking Circular Dependencies

**Priority: 🟠 СРЕДНИЙ**

7. **Economy Loop → Event-Based** (~20 часов)
   - Replace MoneyManager.Instance checks with events
   - Update 8+ dependent classes

8. **Building-Residence Loop → Interface Injection** (~15 часов)

---

### СПРИНТ 5 (Неделя 7): Component Access Refactor

**Priority: 🟡 НИЗКИЙ** (улучшение качества, не критично)

9. **Replace GetComponent with Interfaces** (~25 часов)
   - CartAgent.cs
   - BuildingResourceRouting.cs
   - ResourceProducer.cs
   - Residence.cs

10. **RoadCoverageVisualizer → 2 класса** (~15 часов)

---

## 8. МЕТРИКИ УЛУЧШЕНИЯ

### ЦЕЛЕВЫЕ ПОКАЗАТЕЛИ:

| Метрика | Текущее | Целевое | Критерий |
|---------|---------|---------|----------|
| **Max Class Size** | 1,375 строк | < 300 строк | Никакой класс не > 300 |
| **Avg Public Methods** | 18 методов | < 10 методов | В среднем 5-8 методов |
| **Singleton Count** | 22 | 7 | Только истинные глобали |
| **Circular Dependencies** | 3 detected | 0 | Ацикличный граф зависимостей |
| **Tight Coupling Index** | HIGH | MEDIUM | Max 3 зависимости на класс |
| **Test Coverage** | ~20% | > 60% | Unit tests для логики |
| **Code Duplication** | ~15% | < 5% | Extract common methods |

### ОЦЕНКА УСПЕХА:

✅ **После рефакторинга:**
- Новый разработчик сможет найти нужную функцию за < 5 минут
- Изменение одного компонента не сломает > 1 других
- Unit tests выполняются за < 2 секунды
- Нет циклических зависимостей
- Каждый класс имеет одну четко определенную ответственность

---

## 9. РИСКИ И СМЯГЧЕНИЕ

### РИСК 1: Регрессия функциональности

**Смягчение:**
- Создать E2E тесты перед рефакторингом
- Рефакторить по одному модулю за раз
- Каждое изменение → тестирование в Unity

### РИСК 2: Много конфликтов слияния

**Смягчение:**
- Рефакторить по порядку приоритета
- Частые pull requests (каждые 2-3 дня)
- Code review перед слиянием

### РИСК 3: Производительность деградирует

**Смягчение:**
- Профилировать после каждого спринта
- Кэшировать результаты interface lookups
- Использовать object pooling

---

## ЗАКЛЮЧЕНИЕ

Текущая кодовая база страдает от классических проблем монолитной архитектуры:

- ❌ **5 God Classes** с 6-8+ ответственностями каждый
- ❌ **22 Singleton'а** с циклическими зависимостями
- ❌ **Tight Coupling** (особенно PlayerInputController)
- ❌ **Component Access** вместо interface-based design

**Реекомендуемый путь:**
1. Разделить God Classes на специализированные компоненты
2. Снизить количество синглтонов с 22 до 7
3. Использовать Event Aggregator и Interface Injection
4. Написать Unit tests для каждого компонента

**Предполагаемое время:** 6-8 недель при 1 разработчике  
**Ожидаемые результаты:** Код со здоровой архитектурой, готовый к масштабированию

