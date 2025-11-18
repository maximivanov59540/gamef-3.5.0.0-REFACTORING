using UnityEngine;
using System.Collections.Generic;
public class ResourceProducer : MonoBehaviour
{
    [Tooltip("Данные о 'рецепте' (время, затраты, выход)")]
    public ResourceProductionData productionData;

    // ISSUE #10 FIX: Кэшированный словарь для O(1) доступа к inputCosts вместо O(n) Find
    private Dictionary<ResourceType, ResourceCost> _inputCostLookup = new Dictionary<ResourceType, ResourceCost>();

    [Header("Рабочая Сила")]
    [Tooltip("Тип работников, требуемых для этого здания (Farmers/Craftsmen/Artisans)")]
    public PopulationTier requiredWorkerType = PopulationTier.Farmers;

    [Tooltip("Сколько рабочих 'потребляет' это здание")]
    public int workforceRequired = 0;
    
    [Header("Разгон")]
    [Tooltip("Текущая 'разогретость' (0.0 - 1.0)")]
    [SerializeField] [Range(0f, 1f)] private float _rampUpEfficiency = 0.0f;
    [Tooltip("Время (сек) для 'разгона' от 0% до 100%")]
    public float rampUpTimeSeconds = 60.0f;
    [Tooltip("Время (сек) для 'остывания' от 100% до 0%")]
    public float rampDownTimeSeconds = 60.0f;
    
    [Header("Производительность от Модулей")]
    [Tooltip("Если здание модульное, производительность = (текущие модули / максимум модулей). Для НЕ модульных зданий всегда 100%")]
    private float _currentModuleBonus = 1.0f; // (Множитель по умолчанию 1.0 = 100% для НЕ модульных зданий)
    
    [Header("Эффективность")]
    private float _efficiencyModifier = 1.0f; // 100% по дефолту

    private float _currentWorkforceCap = 1.0f;
    
    [Header("Состояние цикла")]
    [SerializeField]
    [Tooltip("Внутренний таймер. Накапливается до 'cycleTimeSeconds'")]
    private float _cycleTimer = 0f;
    
    public bool IsPaused { get; private set; } = false;
    [Header("Логистика Склада")]
    [SerializeField] private Warehouse _assignedWarehouse; // Склад, к которому мы "приписаны"
    private bool _hasWarehouseAccess = false; // Наш "пропуск" к работе
    
    // 🔒 ARCH FIX: Используем интерфейсы вместо конкретных классов
    private IBuildingIdentifiable _identity;     // Было: BuildingIdentity
    private IBuildingRouting _routing;           // Было: BuildingResourceRouting

    private GridSystem _gridSystem;
    private RoadManager _roadManager;

    // 🔒 ARCH FIX: Используем интерфейсы для inventory компонентов
    private IResourceReceiver _inputInv;         // Было: BuildingInputInventory
    private IResourceProvider _outputInv;        // Было: BuildingOutputInventory

    private bool _initialized = false;

    void Awake()
    {
        // 🔒 ARCH FIX: Используем интерфейсы для уменьшения coupling
        _inputInv = GetComponent<IResourceReceiver>();
        _outputInv = GetComponent<IResourceProvider>();
        _identity = GetComponent<IBuildingIdentifiable>();
        _routing = GetComponent<IBuildingRouting>();
        if (_inputInv == null && productionData != null && productionData.inputCosts.Count > 0)
            Debug.LogError($"На здании {gameObject.name} нет 'IResourceReceiver', но рецепт требует сырье!", this);
        if (_outputInv == null && productionData != null && productionData.outputYield.amount > 0)
            Debug.LogError($"На здании {gameObject.name} нет 'IResourceProvider', но рецепт производит товар!", this);
        if (_outputInv != null)
        {
            _outputInv.OnFull += PauseProduction;
            _outputInv.OnSpaceAvailable += ResumeProduction;
        }

        // FIX #13: Регистрируемся в BuildingRegistry для Warehouse
        if (BuildingRegistry.Instance != null && _identity != null && !_identity.isBlueprint)
        {
            BuildingRegistry.Instance.RegisterProducer(this);
        }
    }
    
    void Start()
    {
        // 🔥 RACE CONDITION FIX: Инициализация с retry логикой через Coroutine
        // Вместо проверки в Update() каждый кадр, используем отложенную инициализацию
        StartCoroutine(InitializeWhenReady());
    }

    /// <summary>
    /// 🔥 RACE CONDITION FIX: Отложенная инициализация до готовности всех Singleton'ов
    /// </summary>
    private System.Collections.IEnumerator InitializeWhenReady()
    {
        // Ждем пока все необходимые системы будут готовы
        while (_gridSystem == null || RoadManager.Instance == null || ResourceManager.Instance == null || ResourceManager.Instance.Population == null)
        {
            if (_gridSystem == null)
            {
                _gridSystem = FindFirstObjectByType<GridSystem>();
            }

            // Ждем следующий кадр перед повторной проверкой
            yield return null;
        }

        // Все системы готовы - инициализируем
        _roadManager = RoadManager.Instance;

        // ✅ НОВАЯ ЛОГИКА: Если есть BuildingResourceRouting, используем новую систему
        if (_routing != null)
        {
            // НОВАЯ СИСТЕМА: Проверяем, что маршруты настроены
            if (_routing.HasOutputDestination())
            {
                _hasWarehouseAccess = true;
                Debug.Log($"[Producer] {gameObject.name}: Использую НОВУЮ систему (BuildingResourceRouting). Доступ к складу = true");
            }
            else
            {
                Debug.LogWarning($"[Producer] {gameObject.name}: BuildingResourceRouting есть, но маршруты не настроены!");
                _hasWarehouseAccess = false;
            }
        }
        else
        {
            // СТАРАЯ СИСТЕМА: Ищем склад по дорогам
            FindWarehouseAccess();
        }

        ResourceManager.Instance.Population.RegisterProducer(this);

        // ISSUE #10 FIX: Строим кэш inputCosts для быстрого доступа
        RebuildInputCostLookup();

        _initialized = true;

        Debug.Log($"[Producer] {gameObject.name}: Инициализация завершена успешно");
    }

    /// <summary>
    /// ISSUE #10 FIX: Строит словарь для O(1) доступа к inputCosts
    /// </summary>
    private void RebuildInputCostLookup()
    {
        _inputCostLookup.Clear();

        if (productionData != null && productionData.inputCosts != null)
        {
            foreach (var cost in productionData.inputCosts)
            {
                if (cost != null)
                {
                    _inputCostLookup[cost.resourceType] = cost;
                }
            }
        }
    }
    
    private void OnDestroy()
    {
        if (_outputInv != null)
        {
            // "Отписываемся" "от" "событий", "на" "которые" "подписались" "в" "Awake"
            _outputInv.OnFull -= PauseProduction;
            _outputInv.OnSpaceAvailable -= ResumeProduction;
        }
        ResourceManager.Instance?.Population?.UnregisterProducer(this);

        // FIX #13: Разрегистрируемся из BuildingRegistry
        if (BuildingRegistry.Instance != null)
        {
            BuildingRegistry.Instance.UnregisterProducer(this);
        }
    }

void Update()
{
    // 🔥 RACE CONDITION FIX: Инициализация теперь в Start() через Coroutine
    // Проверяем только готовность к работе
    if (!_initialized)
    {
        return; // Еще не инициализированы - ждем
    }


    // 1. Проверка Паузы (старая)
    if (IsPaused || productionData == null)
    {
        // ⬇️⬇️ ДОБАВЬ ЭТОТ ЛОГ ⬇️⬇️
        if (IsPaused) Debug.Log($"[Producer] {gameObject.name}: ПРОВЕРКА 1: Стою на паузе (IsPaused = true).");
        return;
    }
    // 2. ПРОВЕРКА: Есть ли "пропуск" от склада?
    if (!_hasWarehouseAccess)
    {
        // ✅ НОВАЯ ЛОГИКА: Если есть BuildingResourceRouting, не паузим производство
        if (_routing != null)
        {
            // НОВАЯ СИСТЕМА: Тележка сама найдет путь, производство не останавливаем
            _hasWarehouseAccess = true;
            Debug.Log($"[Producer] {gameObject.name}: Использую BuildingResourceRouting - доступ установлен");
        }
        else
        {
            // СТАРАЯ СИСТЕМА: Ищем склад
            Debug.LogWarning($"[Producer] {gameObject.name}: ПРОВЕРКА 2: НЕТ ДОСТУПА к складу. Ищу снова...");
            FindWarehouseAccess();
            if (!_hasWarehouseAccess)
            {
                PauseProduction();
                return;
            }
            else
            {
                ResumeProduction();
            }
        }
    }

    // --- ⬇️ НАЧАЛО НОВОГО БЛОКА ЛОГИКИ (ЗАДАЧА 10 и 11) ⬇️ ---

    // --- Шаг 1: Логика "Разгона" (Задача 10) ---
    bool hasInputs = (_inputInv != null) ? _inputInv.HasResources(productionData.inputCosts) : true;

    float targetRampUp = (hasInputs && _hasWarehouseAccess) ? 1.0f : 0.0f;

    float rampSpeed;
    if (targetRampUp > _rampUpEfficiency)
        rampSpeed = (Time.deltaTime / Mathf.Max(0.01f, rampUpTimeSeconds));
    else
        rampSpeed = (Time.deltaTime / Mathf.Max(0.01f, rampDownTimeSeconds));

    _rampUpEfficiency = Mathf.MoveTowards(_rampUpEfficiency, targetRampUp, rampSpeed);


    // --- Шаг 2: Логика "Рабочей Силы" (с типизированными работниками) ---
    // 🔥 FIX: Кешируем Instance локально для защиты от race condition
    var population = ResourceManager.Instance?.Population;
    _currentWorkforceCap = population != null
        ? population.GetWorkforceRatio(requiredWorkerType)
        : 1.0f;


    // --- Шаг 3: Финальный Расчет Эффективности (Задача 11) ---
    float finalEfficiency = _rampUpEfficiency * _currentWorkforceCap * _efficiencyModifier * _currentModuleBonus;

    if (finalEfficiency <= 0.001f)
    {
        _cycleTimer = 0f; 
        return; 
    }

    float currentCycleTime = productionData.cycleTimeSeconds / finalEfficiency;

    // --- ⬆️ КОНЕЦ НОВОГО БЛОКА ЛОГИКИ ⬆️ ---


    // 4. Накапливаем таймер
    _cycleTimer += Time.deltaTime;

    // 5. Ждем, пока таймер "дозреет"
    if (_cycleTimer < currentCycleTime)
    {
        return; // Еще не время
    }

    _cycleTimer -= currentCycleTime; 

    // 7. Проверяем "Желудок" (Input)
    if (_inputInv != null && !_inputInv.HasResources(productionData.inputCosts))
    {
        // ⬇️⬇️ ДОБАВЬ ЭТОТ ЛОГ ⬇️⬇️
        Debug.LogWarning($"[Producer] {gameObject.name}: ПРОВЕРКА 3: Не хватает сырья для цикла.");
        return; 
    }

    // 8. Проверяем "Кошелек" (Output)
    if (_outputInv != null && !_outputInv.HasSpace(productionData.outputYield.amount))
    {
        // ⬇️⬇️ ДОБАВЬ ЭТОТ ЛОГ ⬇️⬇️
        Debug.LogWarning($"[Producer] {gameObject.name}: ПРОВЕРКА 4: Выходной склад полон.");
        PauseProduction(); 
        return;
    }

    // 9. ВСЕ ПРОВЕРКИ ПРОЙДЕНЫ! ПРОИЗВОДИМ!
    Debug.Log($"[Producer] {gameObject.name}: ПРОИЗВОЖУ ПРОДУКТ!");
    if (_inputInv != null)
    {
        _inputInv.ConsumeResources(productionData.inputCosts);
    }

    if (_outputInv != null)
    {
        bool success = _outputInv.TryAddResource(productionData.outputYield.amount);
        if (!success)
        {
            return; 
        }
    }
}
    // --- Вставь это в ResourceProducer.cs ---
private void FindWarehouseAccess()
{
    Debug.Log($"[Producer] {gameObject.name}: Начинаю поиск склада..."); // <-- ЛОГ 1

    // 1. Проверка систем
    if (_identity == null)
    {
        Debug.LogError($"[Producer] {gameObject.name}: Ошибка! _identity == null. Поиск склада отменен.");
        _hasWarehouseAccess = false;
        return;
    }
    if (_gridSystem == null)
    {
        Debug.LogError($"[Producer] {gameObject.name}: Ошибка! _gridSystem == null. Поиск склада отменен.");
        _hasWarehouseAccess = false;
        return;
    }
    if (_roadManager == null)
    {
        Debug.LogError($"[Producer] {gameObject.name}: Ошибка! _roadManager == null. Поиск склада отменен.");
        _hasWarehouseAccess = false;
        return;
    }
    
    var roadGraph = _roadManager.GetRoadGraph();
    if (roadGraph == null)
    {
        Debug.LogWarning($"[Producer] {gameObject.name}: Граф дорог (roadGraph) == null. Поиск склада отменен.");
        _hasWarehouseAccess = false;
        return;
    }

    // 2. Найти наши "выходы" к дороге
    List<Vector2Int> myAccessPoints = LogisticsPathfinder.FindAllRoadAccess(_identity.rootGridPosition, _gridSystem, roadGraph);
    if (myAccessPoints.Count == 0)
    {
        Debug.LogWarning($"[Producer] {gameObject.name} в {_identity.rootGridPosition}: Не нашел доступа к дороге (myAccessPoints.Count == 0). Поиск склада отменен.");
        _hasWarehouseAccess = false;
        return; // <-- ВЫХОД 2
    }

    Debug.Log($"[Producer] {gameObject.name}: Нашел {myAccessPoints.Count} точек доступа к дороге."); // <-- ЛОГ 2

    // 3. 🚀 PERFORMANCE FIX: Используем BuildingRegistry вместо FindObjectsByType
    var allWarehouses = BuildingRegistry.Instance?.GetAllWarehouses();
    if (allWarehouses == null || allWarehouses.Count == 0)
    {
        Debug.LogWarning($"[Producer] {gameObject.name}: Не нашел НИ ОДНОГО склада (Warehouse) на карте. Поиск склада отменен.");
        _hasWarehouseAccess = false;
        return; // <-- ВЫХОД 3
    }

    Debug.Log($"[Producer] {gameObject.name}: Нашел {allWarehouses.Count} складов на карте."); // <-- ЛОГ 3

    // 4. Рассчитать ВСЕ дистанции от НАС
    var distancesFromMe = LogisticsPathfinder.Distances_BFS_Multi(myAccessPoints, 1000, roadGraph);

    // 5. Найти ближайший доступный склад
    Warehouse nearestWarehouse = null;
    int minDistance = int.MaxValue;

    foreach (var warehouse in allWarehouses)
    {
        var warehouseIdentity = warehouse.GetComponent<IBuildingIdentifiable>();
        if (warehouseIdentity == null) continue;
        
        List<Vector2Int> warehouseAccessPoints = LogisticsPathfinder.FindAllRoadAccess(warehouseIdentity.rootGridPosition, _gridSystem, roadGraph);
        
        foreach (var entryPoint in warehouseAccessPoints)
        {
            if (distancesFromMe.TryGetValue(entryPoint, out int dist) && dist < minDistance)
            {
                minDistance = dist;
                nearestWarehouse = warehouse;
            }
        }
    }

    // 8. ФИНАЛЬНАЯ ПРОВЕРКА: Мы ВООБЩЕ нашли склад? (Радиус не важен)
    if (nearestWarehouse != null)
    {
        // Успех! Путь до склада существует.
        _assignedWarehouse = nearestWarehouse;
        _hasWarehouseAccess = true;
        Debug.Log($"[Producer] {gameObject.name} приписан к {nearestWarehouse.name} (Дистанция: {minDistance})");
    }
    else
    {
        // Провал. Дороги нет, или складов нет.
        _hasWarehouseAccess = false;
        Debug.LogWarning($"[Producer] {gameObject.name} не нашел ни одного *доступного* склада (пути нет или все 'острова').");
    }

    // --- ВОТ ЛОГ, КОТОРЫЙ МЫ ЖДАЛИ ---
    Debug.Log($"[Producer] {gameObject.name} (FindWarehouseAccess): Проверка завершена. Доступ к складу = {_hasWarehouseAccess}");
}

    /// <summary>
    /// Обновляет производительность на основе количества модулей.
    /// НОВАЯ ЛОГИКА: здание без модулей не производит (производительность = 0%)
    /// Формула: производительность = (текущие модули / максимум модулей) * 100%
    /// </summary>
    /// <param name="currentModuleCount">Текущее количество установленных модулей</param>
    /// <param name="maxModuleCount">Максимальное количество модулей для здания</param>
    public void UpdateProductionRate(int currentModuleCount, int maxModuleCount)
    {
        // Если максимум модулей = 0, значит это здание НЕ модульное
        // В таком случае производительность = 100% (1.0)
        if (maxModuleCount == 0)
        {
            _currentModuleBonus = 1.0f;
            Debug.Log($"[Producer] {gameObject.name} - НЕ модульное здание. Производительность: 100%");
            return;
        }

        // Если здание модульное, рассчитываем процент
        // 0 модулей = 0%, все модули = 100%
        _currentModuleBonus = (float)currentModuleCount / (float)maxModuleCount;

        float percentage = _currentModuleBonus * 100f;
        Debug.Log($"[Producer] {gameObject.name} обновил производительность. Модулей: {currentModuleCount}/{maxModuleCount}, Производительность: {percentage:F1}%");
    }
    
    public void SetEfficiency(float normalizedValue)
    {
        _efficiencyModifier = normalizedValue;
    }
    public float GetEfficiency() => _efficiencyModifier;
    
    
    /// <summary>
    /// Останавливает производство (например, при событии "Бунт")
    /// </summary>
    public void PauseProduction()
    {
        if (IsPaused) return;
        IsPaused = true;
        // Debug.Log($"Производство {gameObject.name} на ПАУЗЕ (склад полон).");
    }

    /// <summary>
    /// Возобновляет производство
    /// </summary>
    public void ResumeProduction()
    {
        if (!IsPaused) return;
        IsPaused = false;
        // Debug.Log($"Производство {gameObject.name} ВОЗОБНОВЛЕНО (место появилось).");
    }
    public bool GetHasWarehouseAccess() 
    { 
        return _hasWarehouseAccess; 
    }

    public float GetWorkforceCap() 
    { 
        return _currentWorkforceCap; 
    }

    public float GetFinalEfficiency()
    {
        // Этот код дублирует логику из Update() - это нормально
        return _rampUpEfficiency * _currentWorkforceCap * _efficiencyModifier * _currentModuleBonus;
    }

    public float GetProductionPerMinute()
    {
        if (productionData == null || productionData.outputYield == null) return 0f;
        
        float eff = GetFinalEfficiency();
        if (eff == 0) return 0f;
        
        float cyclesPerMinute = 60f / (productionData.cycleTimeSeconds / eff);
        return cyclesPerMinute * productionData.outputYield.amount;
    }

    public float GetConsumptionPerMinute(ResourceType type)
    {
        if (productionData == null || productionData.inputCosts == null) return 0f;

        float eff = GetFinalEfficiency();
        if (eff == 0) return 0f;

        // ISSUE #10 FIX: Используем Dictionary для O(1) вместо O(n) Find
        if (!_inputCostLookup.TryGetValue(type, out var cost))
            return 0f;

        float cyclesPerMinute = 60f / (productionData.cycleTimeSeconds / eff);
        return cyclesPerMinute * cost.amount;
    }

    /// <summary>
    /// Публичный метод для пересчёта доступа к складу.
    /// Вызывается, когда радиус склада изменяется.
    /// </summary>
    public void RefreshWarehouseAccess()
    {
        // ✅ НОВАЯ ЛОГИКА: Если используем BuildingResourceRouting, обновляем маршруты
        if (_routing != null)
        {
            _routing.RefreshRoutes();
            _hasWarehouseAccess = _routing.HasOutputDestination();
            Debug.Log($"[Producer] {gameObject.name}: RefreshWarehouseAccess (НОВАЯ система). Доступ = {_hasWarehouseAccess}");
        }
        else
        {
            // СТАРАЯ СИСТЕМА
            FindWarehouseAccess();
        }
    }
}