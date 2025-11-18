using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Слот для груза в тележке
/// Каждый слот может содержать до 5 единиц ОДНОГО типа ресурса
/// </summary>
[System.Serializable]
public class CargoSlot
{
    public ResourceType resourceType = ResourceType.None;
    public float amount = 0f;
    public const float MAX_CAPACITY = 5f;

    public bool IsEmpty => amount <= 0 || resourceType == ResourceType.None;
    public bool IsFull => amount >= MAX_CAPACITY;
    public float AvailableSpace => MAX_CAPACITY - amount;

    public void Clear()
    {
        resourceType = ResourceType.None;
        amount = 0f;
    }

    public bool CanAccept(ResourceType type)
    {
        return IsEmpty || (resourceType == type && !IsFull);
    }
}

/// <summary>
/// Улучшенная тележка с поддержкой множественных грузов
///
/// НОВЫЕ ВОЗМОЖНОСТИ:
/// - 3 слота для грузов (вместо 1)
/// - Каждый слот вмещает до 5 единиц ресурса
/// - Один слот = один тип ресурса
///
/// ЦИКЛ РАБОТЫ:
/// 1. Загрузить до 3 типов Output (продукции) из дома
/// 2. Отвезти Output к получателю (склад/другое здание)
/// 3. Разгрузить все типы Output
/// 4. Загрузить до 3 типов Input (сырья) на месте разгрузки
/// 5. Вернуться домой с Input
/// 6. Разгрузить все типы Input
/// 7. Повторить
/// </summary>
[RequireComponent(typeof(BuildingIdentity))]
public class CartAgent : MonoBehaviour
{
    // ════════════════════════════════════════════════════════════════
    //                      СОСТОЯНИЯ (6 вместо 11!)
    // ════════════════════════════════════════════════════════════════
    
    private enum State
    {
        Idle,               // Ждёт, пока накопится продукция
        LoadingOutput,      // Грузит продукцию из дома (корутина)
        DeliveringOutput,   // Везёт продукцию к получателю
        UnloadingOutput,    // Разгружает продукцию (корутина)
        LoadingInput,       // Грузит сырьё (корутина)
        ReturningWithInput  // Везёт сырьё домой
        // UnloadingInput будет внутри ReturningWithInput при прибытии
    }
    
    private State _state = State.Idle;
    private Coroutine _activeCoroutine;
    
    // ════════════════════════════════════════════════════════════════
    //                          НАСТРОЙКИ
    // ════════════════════════════════════════════════════════════════
    
    [Header("Настройки Движения")]
    [Tooltip("Скорость движения (юнитов/сек)")]
    public float moveSpeed = 5f;

    [Tooltip("Время (сек) на погрузку и разгрузку")]
    public float loadingTime = 2.0f;

    // ════════════════════════════════════════════════════════════════
    //                    ССЫЛКИ НА "ДОМ"
    // ════════════════════════════════════════════════════════════════

    private Transform _homeBase;
    private Vector2Int _homePosition;
    private BuildingOutputInventory _homeOutput;
    private BuildingInputInventory _homeInput;
    private BuildingResourceRouting _routing;

    // ════════════════════════════════════════════════════════════════
    //                ГРУЗОВЫЕ СЛОТЫ (3 слота по 5 единиц)
    // ════════════════════════════════════════════════════════════════

    [Header("Грузовые Слоты (для отладки)")]
    [SerializeField] private CargoSlot[] _cargoSlots = new CargoSlot[3];

    private const int CARGO_SLOTS_COUNT = 3;
    
    // ════════════════════════════════════════════════════════════════
    //                    СИСТЕМЫ (НЕ МЕНЯЕМ)
    // ════════════════════════════════════════════════════════════════
    
    private GridSystem _gridSystem;
    private RoadManager _roadManager;
    
    // Навигация
    private List<Vector2Int> _currentPath;
    private int _pathIndex;
    private Vector3 _targetPosition;
    
    // ════════════════════════════════════════════════════════════════
    //                      ИНИЦИАЛИЗАЦИЯ
    // ════════════════════════════════════════════════════════════════
    
    void Start()
    {
        // 0. Инициализируем грузовые слоты
        InitializeCargoSlots();

        // 1. Находим "дом" (родительский объект)
        _homeBase = transform.parent;
        if (_homeBase == null)
        {
            Debug.LogError($"[CartAgent] {name} должен быть дочерним объектом здания!", this);
            enabled = false;
            return;
        }
        
        // 2. Находим компоненты на "доме"
        _homeOutput = _homeBase.GetComponent<BuildingOutputInventory>();
        _homeInput = _homeBase.GetComponent<BuildingInputInventory>();
        _routing = _homeBase.GetComponent<BuildingResourceRouting>();

        // FIX #2: Проверяем ВСЕ обязательные компоненты
        if (_homeOutput == null)
        {
            Debug.LogError($"[CartAgent] {name}: На базе {_homeBase.name} нет BuildingOutputInventory!", this);
            enabled = false;
            return;
        }

        if (_homeInput == null)
        {
            Debug.LogError($"[CartAgent] {name}: На базе {_homeBase.name} нет BuildingInputInventory!", this);
            enabled = false;
            return;
        }

        if (_routing == null)
        {
            Debug.LogError($"[CartAgent] {name}: На базе {_homeBase.name} нет BuildingResourceRouting!", this);
            enabled = false;
            return;
        }
        
        // 3. Находим глобальные системы
        _gridSystem = FindFirstObjectByType<GridSystem>();
        _roadManager = RoadManager.Instance;
        
        if (_gridSystem == null)
        {
            Debug.LogError($"[CartAgent] {name}: Не найден GridSystem!", this);
            enabled = false;
            return;
        }
        
        if (_roadManager == null)
        {
            Debug.LogError($"[CartAgent] {name}: Не найден RoadManager!", this);
            enabled = false;
            return;
        }
        
        // 4. Запоминаем "адрес" дома
        var identity = _homeBase.GetComponent<BuildingIdentity>();
        if (identity != null)
        {
            _homePosition = identity.rootGridPosition;
        }
        else
        {
            _gridSystem.GetXZ(_homeBase.position, out int hx, out int hz);
            _homePosition = new Vector2Int(hx, hz);
        }
        
        // 5. Ставим тележку на позицию дома
        transform.position = _homeBase.position;
        
        Debug.Log($"[CartAgent] {name} инициализирован для {_homeBase.name}");
    }

    // 🔥 FIX: Memory leak - останавливаем активную корутину при уничтожении
    private void OnDestroy()
    {
        if (_activeCoroutine != null)
        {
            StopCoroutine(_activeCoroutine);
            _activeCoroutine = null;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //                      ГЛАВНЫЙ ЦИКЛ
    // ════════════════════════════════════════════════════════════════
    
    void Update()
    {
        switch (_state)
        {
            case State.Idle:
                // Проверяем два варианта выхода из Idle:

                // 1. Если есть продукция для отправки
                if (_homeOutput != null && _homeOutput.HasAtLeastOneUnit())
                {
                    SetState(State.LoadingOutput);
                    break;
                }

                // 2. ✅ НОВОЕ: Если нужно сырье, но нет продукции - едем за сырьем напрямую
                if (ShouldFetchInputDirectly())
                {
                    Debug.Log($"[CartAgent] {name}: Нет продукции, но нужно сырье. Еду за Input напрямую!");
                    StartDirectInputFetch();
                }
                break;

            case State.DeliveringOutput:
            case State.ReturningWithInput:
                // В пути - просто едем
                FollowPath();
                break;

            // Остальные состояния управляются корутинами
            // (LoadingOutput, UnloadingOutput, LoadingInput)
        }
    }
    
    // ════════════════════════════════════════════════════════════════
    //                   УПРАВЛЕНИЕ СОСТОЯНИЯМИ
    // ════════════════════════════════════════════════════════════════
    
    private void SetState(State newState)
    {
        if (_state == newState) return;
        
        // Останавливаем активную корутину
        if (_activeCoroutine != null)
        {
            StopCoroutine(_activeCoroutine);
            _activeCoroutine = null;
        }
        
        _state = newState;
        
        // Запускаем корутину для нового состояния
        switch (_state)
        {
            case State.LoadingOutput:
                _activeCoroutine = StartCoroutine(LoadOutputCoroutine());
                break;
                
            case State.UnloadingOutput:
                _activeCoroutine = StartCoroutine(UnloadOutputCoroutine());
                break;
                
            case State.LoadingInput:
                _activeCoroutine = StartCoroutine(LoadInputCoroutine());
                break;
        }
    }
    
    // ════════════════════════════════════════════════════════════════
    //                         КОРУТИНЫ
    // ════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Шаг 1: Загружаем Output (продукцию) из дома (до 5 единиц в первый слот)
    /// </summary>
    private IEnumerator LoadOutputCoroutine()
    {
        Debug.Log($"[CartAgent] {name}: LoadOutputCoroutine начата");
        yield return new WaitForSeconds(loadingTime);

        if (_homeOutput == null)
        {
            Debug.LogWarning($"[CartAgent] {name}: _homeOutput == null при загрузке!");
            SetState(State.Idle);
            yield break;
        }

        // Очищаем все слоты перед загрузкой
        ClearAllCargoSlots();

        // Забираем продукцию из дома (Output здания = один тип ресурса)
        ResourceType outputType = _homeOutput.GetProvidedResourceType();
        float amountTaken = _homeOutput.TryTakeResource(outputType, CargoSlot.MAX_CAPACITY);

        if (amountTaken <= 0)
        {
            Debug.LogWarning($"[CartAgent] {name}: Не удалось загрузить продукцию из {_homeBase.name}");
            SetState(State.Idle);
            yield break;
        }

        // Загружаем в первый слот
        _cargoSlots[0].resourceType = outputType;
        _cargoSlots[0].amount = amountTaken;

        Debug.Log($"[CartAgent] {name} загрузил {amountTaken} {outputType} из {_homeBase.name} в слот [0]");

        // Едем к получателю
        IResourceReceiver destination = _routing.outputDestination;

        if (destination == null)
        {
            Debug.LogWarning($"[CartAgent] {name}: Output destination не настроен!");
            ReturnOutputToHome();
            SetState(State.Idle);
            yield break;
        }

        Debug.Log($"[CartAgent] {name}: Ищу путь к получателю {destination.GetGridPosition()}...");
        if (FindPathTo(destination.GetGridPosition()))
        {
            Debug.Log($"[CartAgent] {name}: Путь найден, везу груз к {destination.GetGridPosition()}");
            SetState(State.DeliveringOutput);
        }
        else
        {
            Debug.LogWarning($"[CartAgent] {name}: Не найден путь к {destination.GetGridPosition()}");
            ReturnOutputToHome();
            SetState(State.Idle);
        }
    }
    
    /// <summary>
    /// Шаг 2: Разгружаем все Output слоты в пункт назначения
    /// </summary>
    private IEnumerator UnloadOutputCoroutine()
    {
        Debug.Log($"[CartAgent] {name}: UnloadOutputCoroutine начата");
        yield return new WaitForSeconds(loadingTime);

        IResourceReceiver destination = _routing.outputDestination;

        if (destination == null)
        {
            Debug.LogWarning($"[CartAgent] {name}: Output destination исчез!");
            ReturnOutputToHome();
            SetState(State.Idle);
            yield break;
        }

        // Разгружаем все непустые слоты
        bool anySlotFailed = false;

        for (int i = 0; i < CARGO_SLOTS_COUNT; i++)
        {
            CargoSlot slot = _cargoSlots[i];
            if (slot.IsEmpty) continue;

            float delivered = destination.TryAddResource(slot.resourceType, slot.amount);
            slot.amount -= delivered;

            Debug.Log($"[CartAgent] {name} разгрузил {delivered} {slot.resourceType} из слота [{i}] в {destination.GetGridPosition()}");

            // Если не смогли разгрузить всё - склад полон
            if (slot.amount > 0.01f)
            {
                anySlotFailed = true;
                Debug.LogWarning($"[CartAgent] {name}: Склад полон! В слоте [{i}] осталось {slot.amount} {slot.resourceType}");
            }
        }

        // Если хоть один слот не разгрузился полностью - ждём и повторяем
        if (anySlotFailed)
        {
            Debug.LogWarning($"[CartAgent] {name}: Не удалось полностью разгрузить. Жду 2 сек...");
            yield return new WaitForSeconds(2f);
            _activeCoroutine = StartCoroutine(UnloadOutputCoroutine());
            yield break;
        }

        // Все слоты успешно разгружены
        ClearAllCargoSlots();

        // ✅ Уведомляем BuildingResourceRouting о завершении доставки для round-robin
        if (_routing != null)
        {
            _routing.NotifyDeliveryCompleted();
        }

        // ✅ КЛЮЧЕВОЙ МОМЕНТ: Сразу пытаемся загрузить Input!
        Debug.Log($"[CartAgent] {name}: Output разгружен, пытаюсь загрузить Input...");
        TryLoadInput();
    }
    
    /// <summary>
    /// Шаг 3: Пытаемся загрузить до 3 типов Input на текущей позиции
    /// </summary>
    private void TryLoadInput()
    {
        // ✅ Проверяем, требует ли здание Input ВООБЩЕ
        if (_homeInput == null || _homeInput.requiredResources == null || _homeInput.requiredResources.Count == 0)
        {
            Debug.Log($"[CartAgent] {name}: Дом не требует сырья, возвращаюсь пустым");
            ReturnHomeEmpty();
            return;
        }

        // Получаем список нужных ресурсов (до 3 типов)
        List<ResourceType> neededTypes = GetNeededInputTypes(CARGO_SLOTS_COUNT);

        if (neededTypes.Count == 0)
        {
            Debug.Log($"[CartAgent] {name}: Все слоты Input заполнены (≥90%), возвращаюсь пустым");
            ReturnHomeEmpty();
            return;
        }

        Debug.Log($"[CartAgent] {name}: Нужны Input ресурсы: {string.Join(", ", neededTypes)}");

        // Есть ли источник?
        IResourceProvider source = _routing.inputSource;
        if (source == null)
        {
            Debug.LogWarning($"[CartAgent] {name}: Input source не настроен!");
            ReturnHomeEmpty();
            return;
        }

        // Проверяем доступность каждого ресурса
        bool anyResourceAvailable = false;
        foreach (var resType in neededTypes)
        {
            // ✅ Если источник - склад, но есть производитель, НЕ БРАТЬ со склада
            bool isWarehouse = source is Warehouse;
            if (isWarehouse && HasProducerForResource(resType))
            {
                Debug.Log($"[CartAgent] {name}: Пропускаю {resType} - найден производитель, жду производства");
                continue;
            }

            // Проверяем доступность
            float availableAmount = source.GetAvailableAmount(resType);
            if (availableAmount >= 1f)
            {
                anyResourceAvailable = true;
                Debug.Log($"[CartAgent] {name}: В источнике доступно {availableAmount} {resType}");
            }
            else
            {
                Debug.Log($"[CartAgent] {name}: В источнике недостаточно {resType} ({availableAmount})");
            }
        }

        if (!anyResourceAvailable)
        {
            Debug.LogWarning($"[CartAgent] {name}: В источнике нет нужных ресурсов, возвращаюсь пустым");
            ReturnHomeEmpty();
            return;
        }

        // Всё ОК - грузим!
        Debug.Log($"[CartAgent] {name}: Начинаю загрузку Input ресурсов из {source.GetGridPosition()}");
        SetState(State.LoadingInput);
    }
    
    /// <summary>
    /// Шаг 4: Загружаем до 3 типов Input с текущей позиции
    /// </summary>
    private IEnumerator LoadInputCoroutine()
    {
        Debug.Log($"[CartAgent] {name}: LoadInputCoroutine начата, ждем {loadingTime} сек...");
        yield return new WaitForSeconds(loadingTime);

        IResourceProvider source = _routing.inputSource;
        if (source == null)
        {
            Debug.LogWarning($"[CartAgent] {name}: LoadInputCoroutine - inputSource == null!");
            ReturnHomeEmpty();
            yield break;
        }

        // Получаем список нужных ресурсов
        List<ResourceType> neededTypes = GetNeededInputTypes(CARGO_SLOTS_COUNT);

        if (neededTypes.Count == 0)
        {
            Debug.LogWarning($"[CartAgent] {name}: LoadInputCoroutine - список neededTypes пуст!");
            ReturnHomeEmpty();
            yield break;
        }

        Debug.Log($"[CartAgent] {name}: Пытаюсь загрузить {neededTypes.Count} типов ресурсов: {string.Join(", ", neededTypes)}");

        // Очищаем слоты перед загрузкой
        ClearAllCargoSlots();

        int loadedCount = 0;
        int slotIndex = 0;

        // Загружаем каждый тип ресурса в отдельный слот
        foreach (var resType in neededTypes)
        {
            if (slotIndex >= CARGO_SLOTS_COUNT)
                break;

            // Проверяем, доступен ли этот ресурс
            float availableAtSource = source.GetAvailableAmount(resType);

            if (availableAtSource < 1f)
            {
                Debug.Log($"[CartAgent] {name}: Пропускаю {resType} - недостаточно в источнике ({availableAtSource})");
                continue;
            }

            // Проверяем место в доме
            float spaceInHome = _homeInput.GetAvailableSpace(resType);
            if (spaceInHome < 0.1f)
            {
                Debug.Log($"[CartAgent] {name}: Пропускаю {resType} - нет места в доме");
                continue;
            }

            // Берём ресурс (до 5 единиц или сколько поместится в доме)
            float amountToTake = Mathf.Min(CargoSlot.MAX_CAPACITY, spaceInHome);
            float amountTaken = source.TryTakeResource(resType, amountToTake);

            if (amountTaken > 0)
            {
                // Загружаем в слот
                _cargoSlots[slotIndex].resourceType = resType;
                _cargoSlots[slotIndex].amount = amountTaken;

                Debug.Log($"[CartAgent] {name} загрузил {amountTaken} {resType} из {source.GetGridPosition()} в слот [{slotIndex}]");

                loadedCount++;
                slotIndex++;
            }
            else
            {
                Debug.LogWarning($"[CartAgent] {name}: Не удалось загрузить {resType} - TryTakeResource вернул 0");
            }
        }

        // Проверяем, загрузили ли хоть что-то
        if (loadedCount == 0)
        {
            Debug.LogWarning($"[CartAgent] {name}: Не удалось загрузить ни один ресурс!");
            ReturnHomeEmpty();
            yield break;
        }

        Debug.Log($"[CartAgent] {name}: Успешно загружено {loadedCount} типов ресурсов, еду домой");

        // Едем домой
        if (FindPathTo(_homePosition))
        {
            Debug.Log($"[CartAgent] {name}: Путь домой найден, начинаю движение");
            SetState(State.ReturningWithInput);
        }
        else
        {
            Debug.LogError($"[CartAgent] {name}: НЕ МОГУ НАЙТИ ПУТЬ ДОМОЙ к {_homePosition}!");
            ReturnAllInputToSource(source);
            GoHomeAndIdle();
        }
    }
    
    // ════════════════════════════════════════════════════════════════
    //                  ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ
    // ════════════════════════════════════════════════════════════════

    /// <summary>
    /// Инициализирует грузовые слоты
    /// </summary>
    private void InitializeCargoSlots()
    {
        for (int i = 0; i < CARGO_SLOTS_COUNT; i++)
        {
            _cargoSlots[i] = new CargoSlot();
        }
    }

    /// <summary>
    /// Очищает все грузовые слоты
    /// </summary>
    private void ClearAllCargoSlots()
    {
        foreach (var slot in _cargoSlots)
        {
            slot.Clear();
        }
    }

    /// <summary>
    /// Проверяет, есть ли хоть один груз в тележке
    /// </summary>
    private bool HasAnyCargo()
    {
        // 🚀 PERFORMANCE FIX: Заменил LINQ .Any() на простой цикл (убираем GC аллокации)
        for (int i = 0; i < _cargoSlots.Count; i++)
        {
            if (!_cargoSlots[i].IsEmpty)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Проверяет, все ли слоты пусты
    /// </summary>
    private bool IsAllSlotsEmpty()
    {
        // 🚀 PERFORMANCE FIX: Заменил LINQ .All() на простой цикл (убираем GC аллокации)
        for (int i = 0; i < _cargoSlots.Count; i++)
        {
            if (!_cargoSlots[i].IsEmpty)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Возвращает свободный слот или слот с указанным типом ресурса (если есть место)
    /// </summary>
    private CargoSlot GetAvailableSlot(ResourceType resourceType)
    {
        // Сначала ищем слот с тем же типом ресурса (если не заполнен)
        foreach (var slot in _cargoSlots)
        {
            if (slot.resourceType == resourceType && !slot.IsFull)
            {
                return slot;
            }
        }

        // Если не нашли, ищем пустой слот
        foreach (var slot in _cargoSlots)
        {
            if (slot.IsEmpty)
            {
                return slot;
            }
        }

        // Нет свободных слотов
        return null;
    }

    /// <summary>
    /// Определяет, какой Input нужен дому (первый незаполненный слот)
    /// УСТАРЕВШИЙ - использовать GetNeededInputTypes()
    /// </summary>
    private ResourceType GetNeededInputType()
    {
        if (_homeInput == null || _homeInput.requiredResources == null)
            return ResourceType.None;

        if (_homeInput.requiredResources.Count == 0)
            return ResourceType.None;

        foreach (var slot in _homeInput.requiredResources)
        {
            if (slot.maxAmount <= 0) continue;

            float fillRatio = slot.currentAmount / slot.maxAmount;
            if (fillRatio < 0.9f)
            {
                return slot.resourceType;
            }
        }

        return ResourceType.None;
    }

    /// <summary>
    /// Возвращает список нужных Input ресурсов (до maxCount типов)
    /// Приоритет отдается наиболее пустым слотам
    /// 🚀 PERF FIX: Заменен LINQ на обычный цикл для устранения GC аллокаций
    /// </summary>
    private List<ResourceType> GetNeededInputTypes(int maxCount)
    {
        List<ResourceType> result = new List<ResourceType>();

        if (_homeInput == null || _homeInput.requiredResources == null || _homeInput.requiredResources.Count == 0)
            return result;

        // 🚀 ОПТИМИЗАЦИЯ: Используем массивы вместо LINQ для избежания аллокаций
        int slotCount = _homeInput.requiredResources.Count;

        // Временные массивы для сортировки (стек-аллокация, не GC)
        int[] validIndices = new int[slotCount];
        float[] fillRatios = new float[slotCount];
        int validCount = 0;

        // Собираем индексы незаполненных слотов
        for (int i = 0; i < slotCount; i++)
        {
            var slot = _homeInput.requiredResources[i];
            if (slot.maxAmount > 0)
            {
                float ratio = slot.currentAmount / slot.maxAmount;
                if (ratio < 0.9f)
                {
                    validIndices[validCount] = i;
                    fillRatios[validCount] = ratio;
                    validCount++;
                }
            }
        }

        // Если нет незаполненных слотов - возвращаем пустой список
        if (validCount == 0)
            return result;

        // Сортируем по fill ratio (insertion sort - эффективен для малых массивов)
        for (int i = 1; i < validCount; i++)
        {
            float currentRatio = fillRatios[i];
            int currentIndex = validIndices[i];
            int j = i - 1;

            while (j >= 0 && fillRatios[j] > currentRatio)
            {
                fillRatios[j + 1] = fillRatios[j];
                validIndices[j + 1] = validIndices[j];
                j--;
            }

            fillRatios[j + 1] = currentRatio;
            validIndices[j + 1] = currentIndex;
        }

        // Берем первые maxCount элементов
        int takeCount = Mathf.Min(validCount, maxCount);
        for (int i = 0; i < takeCount; i++)
        {
            result.Add(_homeInput.requiredResources[validIndices[i]].resourceType);
        }

        return result;
    }

    /// <summary>
    /// ✅ НОВОЕ: Проверяет, нужно ли ехать за Input напрямую (без отправки Output)
    /// </summary>
    private bool ShouldFetchInputDirectly()
    {
        // Не нужно, если нет компонента Input
        if (_homeInput == null || _homeInput.requiredResources == null || _homeInput.requiredResources.Count == 0)
            return false;

        // Не нужно, если маршрут Input не настроен
        if (_routing == null || _routing.inputSource == null)
            return false;

        // Проверяем, есть ли хотя бы один слот, который нуждается в пополнении
        foreach (var slot in _homeInput.requiredResources)
        {
            if (slot.maxAmount <= 0) continue;

            float fillRatio = slot.currentAmount / slot.maxAmount;

            // Если слот заполнен меньше чем на 25% - срочно нужен Input!
            if (fillRatio < 0.25f)
            {
                // ✅ НОВОЕ: Проверяем, является ли inputSource складом
                bool isWarehouse = _routing.inputSource is Warehouse;

                // ✅ ЕСЛИ ИСТОЧНИК - СКЛАД, проверяем наличие производителя
                if (isWarehouse)
                {
                    // Проверяем, есть ли производитель для этого ресурса
                    if (HasProducerForResource(slot.resourceType))
                    {
                        Debug.Log($"[CartAgent] {name}: НЕ еду на склад! Найден производитель {slot.resourceType}. Жду производства.");
                        return false; // НЕ ЕХАТЬ - ЖДАТЬ производителя!
                    }
                }

                // Проверяем, есть ли этот ресурс в источнике
                float availableAtSource = _routing.inputSource.GetAvailableAmount(slot.resourceType);
                if (availableAtSource >= 1f)
                {
                    Debug.Log($"[CartAgent] {name}: ShouldFetchInputDirectly = TRUE. Слот {slot.resourceType} заполнен на {fillRatio*100:F0}%, в источнике доступно {availableAtSource}");
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// ✅ НОВОЕ: Проверяет, существует ли производитель для указанного ресурса
    /// </summary>
    private bool HasProducerForResource(ResourceType resourceType)
    {
        // 🚀 PERFORMANCE FIX: Используем BuildingRegistry вместо FindObjectsByType
        var allOutputs = BuildingRegistry.Instance?.GetAllOutputs();

        if (allOutputs == null) return false;

        foreach (var output in allOutputs)
        {
            // Пропускаем себя
            if (output.gameObject == _homeBase.gameObject)
                continue;

            // Проверяем тип производимого ресурса
            if (output.outputResource.resourceType == resourceType)
            {
                Debug.Log($"[CartAgent] {name}: Найден производитель {resourceType}: {output.gameObject.name}");
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// ✅ НОВОЕ: Начинает поездку за Input напрямую (без отправки Output)
    /// </summary>
    private void StartDirectInputFetch()
    {
        IResourceProvider source = _routing.inputSource;

        if (source == null)
        {
            Debug.LogWarning($"[CartAgent] {name}: StartDirectInputFetch - inputSource == null!");
            return;
        }

        Vector2Int sourcePosition = source.GetGridPosition();

        Debug.Log($"[CartAgent] {name}: StartDirectInputFetch - ищу путь к источнику {sourcePosition}");

        if (FindPathTo(sourcePosition))
        {
            Debug.Log($"[CartAgent] {name}: Путь к источнику Input найден, начинаю движение");
            // Переходим в состояние "едем за Input"
            // Используем ReturningWithInput, но без груза (слоты уже пусты)
            _state = State.ReturningWithInput;
        }
        else
        {
            Debug.LogWarning($"[CartAgent] {name}: Не найден путь к источнику Input {sourcePosition}");
        }
    }

    /// <summary>
    /// ✅ НОВОЕ: Загружает Input при прямом прибытии к источнику (без разгрузки Output)
    /// </summary>
    private void TryLoadInputDirectly()
    {
        ResourceType neededType = GetNeededInputType();
        if (neededType == ResourceType.None)
        {
            Debug.Log($"[CartAgent] {name}: TryLoadInputDirectly - не нужен Input (слоты заполнены), возвращаюсь домой пустым");
            ReturnHomeEmpty();
            return;
        }

        IResourceProvider source = _routing.inputSource;
        if (source == null)
        {
            Debug.LogWarning($"[CartAgent] {name}: TryLoadInputDirectly - inputSource == null!");
            ReturnHomeEmpty();
            return;
        }

        // Проверяем доступное количество
        float availableAtSource = source.GetAvailableAmount(neededType);
        Debug.Log($"[CartAgent] {name}: TryLoadInputDirectly - в источнике доступно {availableAtSource} {neededType}");

        if (availableAtSource < 1f)
        {
            Debug.LogWarning($"[CartAgent] {name}: TryLoadInputDirectly - в источнике недостаточно {neededType}, возвращаюсь домой пустым");
            ReturnHomeEmpty();
            return;
        }

        // Запускаем корутину загрузки Input
        Debug.Log($"[CartAgent] {name}: TryLoadInputDirectly - начинаю загрузку {neededType}");
        SetState(State.LoadingInput);
    }
    
    /// <summary>
    /// Возвращает Output обратно в дом (если не смогли отвезти)
    /// </summary>
    private void ReturnOutputToHome()
    {
        if (_homeOutput == null) return;

        for (int i = 0; i < CARGO_SLOTS_COUNT; i++)
        {
            CargoSlot slot = _cargoSlots[i];
            if (slot.IsEmpty) continue;

            int amountToReturn = Mathf.FloorToInt(slot.amount);
            bool success = _homeOutput.TryAddResource(amountToReturn);

            if (success)
            {
                Debug.Log($"[CartAgent] {name} вернул {amountToReturn} {slot.resourceType} из слота [{i}] обратно в дом");
            }
            else
            {
                Debug.LogWarning($"[CartAgent] {name}: Не удалось вернуть {amountToReturn} {slot.resourceType} в дом (переполнен!)");
            }
        }

        ClearAllCargoSlots();
    }

    /// <summary>
    /// Возвращает все Input слоты обратно источнику (если не смогли довезти домой)
    /// </summary>
    private void ReturnAllInputToSource(IResourceProvider source)
    {
        if (source == null || !(source is IResourceReceiver receiver))
        {
            Debug.LogWarning($"[CartAgent] {name}: Источник не является IResourceReceiver, не могу вернуть груз!");
            ClearAllCargoSlots();
            return;
        }

        for (int i = 0; i < CARGO_SLOTS_COUNT; i++)
        {
            CargoSlot slot = _cargoSlots[i];
            if (slot.IsEmpty) continue;

            receiver.TryAddResource(slot.resourceType, slot.amount);
            Debug.Log($"[CartAgent] {name} вернул {slot.amount} {slot.resourceType} из слота [{i}] обратно в источник");
        }

        ClearAllCargoSlots();
    }

    /// <summary>
    /// УСТАРЕВШИЙ - Возвращает Input обратно источнику (старый метод для совместимости)
    /// </summary>
    private void ReturnInputToSource(IResourceProvider source)
    {
        ReturnAllInputToSource(source);
    }
    
    /// <summary>
    /// Возвращается домой пустой
    /// </summary>
    private void ReturnHomeEmpty()
    {
        Debug.Log($"[CartAgent] {name}: Возвращаюсь домой пустым к {_homePosition}");

        if (FindPathTo(_homePosition))
        {
            Debug.Log($"[CartAgent] {name}: Путь домой найден, начинаю движение");
            SetState(State.ReturningWithInput); // ✅ ИСПРАВЛЕНИЕ: используем SetState() вместо прямого _state =
        }
        else
        {
            // Аварийная телепортация
            Debug.LogWarning($"[CartAgent] {name}: Не могу найти путь домой! Телепортируюсь");
            GoHomeAndIdle();
        }
    }
    
    /// <summary>
    /// Телепортация домой + сброс в Idle
    /// </summary>
    private void GoHomeAndIdle()
    {
        transform.position = _homeBase.position;
        _currentPath = null;
        _pathIndex = 0;
        ClearAllCargoSlots();
        SetState(State.Idle);
    }
    
    // ════════════════════════════════════════════════════════════════
    //                  ЛОГИКА ДОСТИЖЕНИЯ ЦЕЛИ
    // ════════════════════════════════════════════════════════════════
    
    /// <summary>
    /// Вызывается, когда достигли конца пути
    /// </summary>
    private void OnPathFinished()
    {
        if (_state == State.DeliveringOutput)
        {
            // Приехали к получателю Output
            IResourceReceiver destination = _routing.outputDestination;

            if (destination != null && destination.CanAcceptCart())
            {
                SetState(State.UnloadingOutput);
            }
            else
            {
                // Получатель не может принять - ждём
                Debug.Log($"[CartAgent] {name} ждёт у получателя Output");
                // Остаёмся в DeliveringOutput, попробуем в следующем кадре
            }
        }
        else if (_state == State.ReturningWithInput)
        {
            // ✅ НОВАЯ ЛОГИКА: Проверяем, где мы находимся
            _gridSystem.GetXZ(transform.position, out int currentX, out int currentZ);
            Vector2Int currentPos = new Vector2Int(currentX, currentZ);

            // Проверяем, находимся ли мы дома (с некоторой погрешностью)
            bool isAtHome = Vector2Int.Distance(currentPos, _homePosition) < 2f;

            if (isAtHome)
            {
                // Вернулись домой
                if (HasAnyCargo())
                {
                    // С грузом - разгружаем
                    Debug.Log($"[CartAgent] {name}: Приехал домой с грузом, разгружаю");

                    // FIX #4: Сохраняем корутину в _activeCoroutine для правильной очистки
                    if (_activeCoroutine != null)
                        StopCoroutine(_activeCoroutine);
                    _activeCoroutine = StartCoroutine(UnloadInputAtHomeCoroutine());
                }
                else
                {
                    // Пустые - в Idle
                    Debug.Log($"[CartAgent] {name}: Приехал домой пустым, возвращаюсь в Idle");
                    SetState(State.Idle);
                }
            }
            else
            {
                // ✅ Приехали к источнику Input (не домой)
                if (IsAllSlotsEmpty())
                {
                    Debug.Log($"[CartAgent] {name}: Приехал к источнику Input, начинаю загрузку");
                    // Пытаемся загрузить Input прямо здесь
                    TryLoadInputDirectly();
                }
                else
                {
                    // Странная ситуация - груз есть, но мы не дома
                    Debug.LogWarning($"[CartAgent] {name}: Приехал не домой с грузом! Возвращаюсь домой");
                    ReturnHomeEmpty();
                }
            }
        }
    }
    
    /// <summary>
    /// Разгружаем все Input слоты в дом (последний шаг цикла)
    /// </summary>
    private IEnumerator UnloadInputAtHomeCoroutine()
    {
        yield return new WaitForSeconds(loadingTime);

        if (_homeInput != null)
        {
            // Разгружаем все непустые слоты
            for (int i = 0; i < CARGO_SLOTS_COUNT; i++)
            {
                CargoSlot slot = _cargoSlots[i];
                if (slot.IsEmpty) continue;

                float delivered = _homeInput.TryAddResource(slot.resourceType, slot.amount);
                Debug.Log($"[CartAgent] {name} разгрузил {delivered} {slot.resourceType} из слота [{i}] в дом");

                slot.amount -= delivered;

                if (slot.amount > 0.01f)
                {
                    Debug.LogWarning($"[CartAgent] {name}: Не удалось полностью разгрузить слот [{i}] - осталось {slot.amount} {slot.resourceType}");
                }
            }
        }

        // Очищаем все слоты
        ClearAllCargoSlots();

        // Цикл завершён - возвращаемся в Idle
        SetState(State.Idle);
    }
    
    // ════════════════════════════════════════════════════════════════
    //            НАВИГАЦИЯ (СТАРЫЙ КОД - НЕ МЕНЯЕМ!)
    // ════════════════════════════════════════════════════════════════
    
    private void FollowPath()
    {
        if (_currentPath == null)
        {
            // Путь потерялся
            Debug.LogWarning($"[CartAgent] {name}: Путь потерялся!");

            if (HasAnyCargo())
            {
                if (_state == State.DeliveringOutput)
                    ReturnOutputToHome();
                else if (_state == State.ReturningWithInput)
                    ReturnAllInputToSource(_routing.inputSource);
            }

            GoHomeAndIdle();
            return;
        }
        
        // === СТАРЫЙ КОД FollowPath() ===
        Vector2Int currentCell;
        if (_pathIndex > 0 && _pathIndex <= _currentPath.Count)
            currentCell = _currentPath[_pathIndex - 1];
        else
            currentCell = _currentPath[0];
        
        RoadTile currentTile = _gridSystem.GetRoadTileAt(currentCell.x, currentCell.y);
        float currentMultiplier = 1.0f;
        if (currentTile != null && currentTile.roadData != null)
            currentMultiplier = currentTile.roadData.speedMultiplier;
        
        Vector3 newPos = Vector3.MoveTowards(
            transform.position, 
            _targetPosition, 
            moveSpeed * currentMultiplier * Time.deltaTime
        );
        
        transform.position = newPos;
        
        Vector3 direction = (_targetPosition - transform.position).normalized;
        if (direction != Vector3.zero)
            transform.rotation = Quaternion.LookRotation(direction);
        
        if (Vector3.Distance(transform.position, _targetPosition) < 0.1f)
        {
            _pathIndex++;
            if (_pathIndex >= _currentPath.Count)
            {
                OnPathFinished();
            }
            else
            {
                SetNewTargetNode();
            }
        }
    }
    
    private bool FindPathTo(Vector2Int destinationCell)
    {
        var roadGraph = _roadManager.GetRoadGraph();
        if (roadGraph == null || roadGraph.Count == 0)
        {
            Debug.LogWarning($"[CartAgent] {name}: RoadGraph пуст или null!");
            return false;
        }

        Vector2Int startBuildingCell;
        if (Vector3.Distance(transform.position, _homeBase.position) < 1f)
        {
            startBuildingCell = GetCurrentHomeCell();
            if (startBuildingCell.x == -1)
            {
                Debug.LogWarning($"[CartAgent] {name}: Не удалось получить координаты дома!");
                return false;
            }
        }
        else
        {
            _gridSystem.GetXZ(transform.position, out int sx, out int sz);
            startBuildingCell = new Vector2Int(sx, sz);
        }

        Debug.Log($"[CartAgent] {name}: Ищу путь от {startBuildingCell} к {destinationCell}");

        List<Vector2Int> startAccessPoints = LogisticsPathfinder.FindAllRoadAccess(
            startBuildingCell, _gridSystem, roadGraph);

        Debug.Log($"[CartAgent] {name}: Найдено {startAccessPoints.Count} точек доступа к дороге у отправителя: [{string.Join(", ", startAccessPoints)}]");

        if (startAccessPoints.Count == 0)
        {
            Debug.LogWarning($"[CartAgent] {name}: Нет точек доступа к дороге у отправителя {startBuildingCell}!");
            return false;
        }

        List<Vector2Int> endAccessPoints = LogisticsPathfinder.FindAllRoadAccess(
            destinationCell, _gridSystem, roadGraph);

        Debug.Log($"[CartAgent] {name}: Найдено {endAccessPoints.Count} точек доступа к дороге у получателя: [{string.Join(", ", endAccessPoints)}]");

        if (endAccessPoints.Count == 0)
        {
            Debug.LogWarning($"[CartAgent] {name}: ❌ ПРОБЛЕМА: Нет точек доступа к дороге у получателя {destinationCell}! Здание не подключено к дорожной сети.");
            return false;
        }

        var distances = LogisticsPathfinder.Distances_BFS_Multi(
            startAccessPoints, 1000, roadGraph);

        Vector2Int bestEndCell = new Vector2Int(-1, -1);
        int minDistance = int.MaxValue;
        int reachableCount = 0;

        foreach (var endCell in endAccessPoints)
        {
            if (distances.TryGetValue(endCell, out int dist))
            {
                reachableCount++;
                if (dist < minDistance)
                {
                    minDistance = dist;
                    bestEndCell = endCell;
                }
            }
        }

        if (bestEndCell.x == -1)
        {
            Debug.LogWarning($"[CartAgent] {name}: ❌ ПРОБЛЕМА: Ни одна из {endAccessPoints.Count} точек доступа получателя {destinationCell} не достижима! Дороги отправителя и получателя НЕ соединены. Проверьте дорожную сеть.");
            return false;
        }

        Debug.Log($"[CartAgent] {name}: ✅ Из {endAccessPoints.Count} точек доступа получателя {reachableCount} достижимы. Лучшая: {bestEndCell} (расстояние: {minDistance})");

        _currentPath = null;
        foreach(var startCell in startAccessPoints)
        {
            var path = LogisticsPathfinder.FindActualPath(startCell, bestEndCell, roadGraph);
            if (path != null)
            {
                _currentPath = path;
                Debug.Log($"[CartAgent] {name}: Найден путь от {startCell} к {bestEndCell}, длина: {path.Count}");
                break;
            }
        }

        if (_currentPath != null && _currentPath.Count > 0)
        {
            if (startBuildingCell != _currentPath[0])
                _currentPath.Insert(0, startBuildingCell);

            if (destinationCell != _currentPath[_currentPath.Count - 1])
                _currentPath.Add(destinationCell);

            _pathIndex = 0;
            SetNewTargetNode();
            Debug.Log($"[CartAgent] {name}: Полный путь построен, длина: {_currentPath.Count}");
            return true;
        }

        Debug.LogWarning($"[CartAgent] {name}: Не удалось построить путь от {startBuildingCell} к {destinationCell}!");
        return false;
    }
    
    private void SetNewTargetNode()
    {
        Vector2Int targetCell = _currentPath[_pathIndex];
        _targetPosition = _gridSystem.GetWorldPosition(targetCell.x, targetCell.y);
        
        float offset = _gridSystem.GetCellSize() / 2f;
        _targetPosition.x += offset;
        _targetPosition.z += offset;
        _targetPosition.y += 0.1f;
    }
    
    private Vector2Int GetCurrentHomeCell()
    {
        if (_homeBase == null) return new Vector2Int(-1, -1);
        
        var identity = _homeBase.GetComponent<BuildingIdentity>();
        if (identity != null)
            return identity.rootGridPosition;
        
        _gridSystem.GetXZ(_homeBase.position, out int hx, out int hz);
        return new Vector2Int(hx, hz);
    }
    
    // ════════════════════════════════════════════════════════════════
    //              ДЛЯ ВИЗУАЛИЗАЦИИ (НЕ МЕНЯЕМ)
    // ════════════════════════════════════════════════════════════════
    
    public bool IsBusy()
    {
        return _state != State.Idle;
    }
    
    public List<Vector3> GetRemainingPathWorld()
    {
        var pathPoints = new List<Vector3>();
        
        if (_currentPath == null || _currentPath.Count == 0) 
            return pathPoints;
        
        pathPoints.Add(transform.position);
        pathPoints.Add(_targetPosition);
        
        for (int i = _pathIndex + 1; i < _currentPath.Count; i++)
        {
            var cell = _currentPath[i];
            var pos = _gridSystem.GetWorldPosition(cell.x, cell.y);
            
            float offset = _gridSystem.GetCellSize() / 2f;
            pos.x += offset;
            pos.z += offset;
            pos.y += 0.1f;
            
            pathPoints.Add(pos);
        }
        
        return pathPoints;
    }
}