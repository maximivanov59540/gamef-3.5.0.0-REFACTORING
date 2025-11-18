using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Централизованный реестр всех зданий в игре.
/// Решает проблему производительности с FindObjectsByType в Update.
///
/// ПРОБЛЕМА:
/// - FindObjectsByType вызывается 15+ раз в Update циклах
/// - O(N) сканирование всей сцены каждые 5 секунд
/// - При 500 зданиях = 7500 операций поиска в секунду!
///
/// РЕШЕНИЕ:
/// - Здания регистрируются при создании (OnEnable)
/// - Поиск = O(1) доступ к кешированным спискам
/// - При 500 зданиях = 0 операций поиска (только доступ к List)
/// </summary>
public class BuildingRegistry : MonoBehaviour
{
    public static BuildingRegistry Instance { get; private set; }

    // === КЕШИРОВАННЫЕ СПИСКИ ===
    // FIX ISSUE #2: Замена List на HashSet для O(1) Contains/Add вместо O(n)
    private readonly HashSet<BuildingOutputInventory> _allOutputs = new HashSet<BuildingOutputInventory>();
    private readonly HashSet<BuildingInputInventory> _allInputs = new HashSet<BuildingInputInventory>();
    private readonly HashSet<Warehouse> _allWarehouses = new HashSet<Warehouse>();
    private readonly HashSet<BuildingResourceRouting> _allRoutings = new HashSet<BuildingResourceRouting>(); // 🚀 O(n²) FIX
    private readonly HashSet<Residence> _allResidences = new HashSet<Residence>(); // FIX #11: Для TaxManager
    private readonly HashSet<BuildingIdentity> _allBuildings = new HashSet<BuildingIdentity>(); // FIX #12: Для EconomyManager
    private readonly HashSet<ResourceProducer> _allProducers = new HashSet<ResourceProducer>(); // FIX #13: Для Warehouse

    // === UNITY LIFECYCLE ===

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        Debug.Log("[BuildingRegistry] Система кеширования зданий инициализирована");
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    // === РЕГИСТРАЦИЯ BUILDINGS ===

    // FIX ISSUE #2: HashSet.Add автоматически проверяет дубли (O(1) вместо O(n))
    public void RegisterOutput(BuildingOutputInventory output)
    {
        if (output == null) return;
        _allOutputs.Add(output); // HashSet игнорирует дубликаты автоматически
    }

    public void UnregisterOutput(BuildingOutputInventory output)
    {
        if (output == null) return;
        _allOutputs.Remove(output);
    }

    public void RegisterInput(BuildingInputInventory input)
    {
        if (input == null) return;
        _allInputs.Add(input);
    }

    public void UnregisterInput(BuildingInputInventory input)
    {
        if (input == null) return;
        _allInputs.Remove(input);
    }

    public void RegisterWarehouse(Warehouse warehouse)
    {
        if (warehouse == null) return;
        _allWarehouses.Add(warehouse);
    }

    public void UnregisterWarehouse(Warehouse warehouse)
    {
        if (warehouse == null) return;
        _allWarehouses.Remove(warehouse);
    }

    public void RegisterRouting(BuildingResourceRouting routing)
    {
        if (routing == null) return;
        _allRoutings.Add(routing);
    }

    public void UnregisterRouting(BuildingResourceRouting routing)
    {
        if (routing == null) return;
        _allRoutings.Remove(routing);
    }

    // FIX #11: Регистрация Residence для TaxManager
    public void RegisterResidence(Residence residence)
    {
        if (residence == null) return;
        _allResidences.Add(residence);
    }

    public void UnregisterResidence(Residence residence)
    {
        if (residence == null) return;
        _allResidences.Remove(residence);
    }

    // FIX #12: Регистрация BuildingIdentity для EconomyManager
    public void RegisterBuilding(BuildingIdentity building)
    {
        if (building == null) return;
        _allBuildings.Add(building);
    }

    public void UnregisterBuilding(BuildingIdentity building)
    {
        if (building == null) return;
        _allBuildings.Remove(building);
    }

    // FIX #13: Регистрация ResourceProducer для Warehouse
    public void RegisterProducer(ResourceProducer producer)
    {
        if (producer == null) return;
        _allProducers.Add(producer);
    }

    public void UnregisterProducer(ResourceProducer producer)
    {
        if (producer == null) return;
        _allProducers.Remove(producer);
    }

    // === ПОЛУЧЕНИЕ СПИСКОВ (O(1) вместо O(N) с FindObjectsByType) ===
    // FIX ISSUE #2: HashSet возвращается как IReadOnlyCollection (поддерживает foreach, Count, Contains)

    /// <summary>
    /// Получить все BuildingOutputInventory (производители).
    /// ВАЖНО: Возвращает READ-ONLY коллекцию! Не модифицировать!
    /// </summary>
    public IReadOnlyCollection<BuildingOutputInventory> GetAllOutputs()
    {
        return _allOutputs;
    }

    /// <summary>
    /// Получить все BuildingInputInventory (потребители).
    /// ВАЖНО: Возвращает READ-ONLY коллекцию! Не модифицировать!
    /// </summary>
    public IReadOnlyCollection<BuildingInputInventory> GetAllInputs()
    {
        return _allInputs;
    }

    /// <summary>
    /// Получить все Warehouse (склады).
    /// ВАЖНО: Возвращает READ-ONLY коллекцию! Не модифицировать!
    /// </summary>
    public IReadOnlyCollection<Warehouse> GetAllWarehouses()
    {
        return _allWarehouses;
    }

    /// <summary>
    /// Получить все BuildingResourceRouting (маршрутизация).
    /// ВАЖНО: Возвращает READ-ONLY коллекцию! Не модифицировать!
    /// 🚀 O(n²) FIX: Используется вместо FindObjectsByType в балансировке нагрузки
    /// </summary>
    public IReadOnlyCollection<BuildingResourceRouting> GetAllRoutings()
    {
        return _allRoutings;
    }

    /// <summary>
    /// Получить все Residence (жилые дома).
    /// ВАЖНО: Возвращает READ-ONLY коллекцию! Не модифицировать!
    /// FIX #11: Используется в TaxManager вместо FindObjectsByType каждую минуту
    /// </summary>
    public IReadOnlyCollection<Residence> GetAllResidences()
    {
        return _allResidences;
    }

    /// <summary>
    /// Получить все BuildingIdentity (все здания).
    /// ВАЖНО: Возвращает READ-ONLY коллекцию! Не модифицировать!
    /// FIX #12: Используется в EconomyManager для подсчёта upkeep каждую минуту
    /// </summary>
    public IReadOnlyCollection<BuildingIdentity> GetAllBuildings()
    {
        return _allBuildings;
    }

    /// <summary>
    /// Получить все ResourceProducer (производители).
    /// ВАЖНО: Возвращает READ-ONLY коллекцию! Не модифицировать!
    /// FIX #13: Используется в Warehouse.RefreshAllProducers() вместо FindObjectsByType
    /// </summary>
    public IReadOnlyCollection<ResourceProducer> GetAllProducers()
    {
        return _allProducers;
    }

    // === ОТЛАДКА ===

    public int GetOutputCount() => _allOutputs.Count;
    public int GetInputCount() => _allInputs.Count;
    public int GetWarehouseCount() => _allWarehouses.Count;
    public int GetRoutingCount() => _allRoutings.Count;
    public int GetResidenceCount() => _allResidences.Count; // FIX #11
    public int GetBuildingCount() => _allBuildings.Count; // FIX #12
    public int GetProducerCount() => _allProducers.Count; // FIX #13

    /// <summary>
    /// Принудительное пересканирование сцены (только для отладки!).
    /// Используется если что-то пошло не так с регистрацией.
    ///
    /// ⚠️ ВНИМАНИЕ: МЕДЛЕННАЯ ОПЕРАЦИЯ!
    /// Вызывает FindObjectsByType 7 раз. При 500+ зданиях это может вызвать lag spike 100-300ms.
    /// НИКОГДА не вызывайте этот метод в Update() или других горячих путях!
    /// Используйте только через Inspector (ContextMenu) или при загрузке сцены.
    /// </summary>
    [ContextMenu("DEBUG: Force Rescan Scene")]
    public void ForceRescanScene()
    {
        Debug.LogWarning("[BuildingRegistry] ⚠️ Начинается полное пересканирование сцены. Это медленная операция!");
        float startTime = Time.realtimeSinceStartup;

        _allOutputs.Clear();
        _allInputs.Clear();
        _allWarehouses.Clear();
        _allRoutings.Clear();
        _allResidences.Clear(); // FIX #11
        _allBuildings.Clear(); // FIX #12
        _allProducers.Clear(); // FIX #13

        var outputs = FindObjectsByType<BuildingOutputInventory>(FindObjectsSortMode.None);
        var inputs = FindObjectsByType<BuildingInputInventory>(FindObjectsSortMode.None);
        var warehouses = FindObjectsByType<Warehouse>(FindObjectsSortMode.None);
        var routings = FindObjectsByType<BuildingResourceRouting>(FindObjectsSortMode.None);
        var residences = FindObjectsByType<Residence>(FindObjectsSortMode.None); // FIX #11
        var buildings = FindObjectsByType<BuildingIdentity>(FindObjectsSortMode.None); // FIX #12
        var producers = FindObjectsByType<ResourceProducer>(FindObjectsSortMode.None); // FIX #13

        // FIX ISSUE #2: HashSet использует UnionWith вместо AddRange
        _allOutputs.UnionWith(outputs);
        _allInputs.UnionWith(inputs);
        _allWarehouses.UnionWith(warehouses);
        _allRoutings.UnionWith(routings);
        _allResidences.UnionWith(residences); // FIX #11
        _allBuildings.UnionWith(buildings); // FIX #12
        _allProducers.UnionWith(producers); // FIX #13

        float elapsedMs = (Time.realtimeSinceStartup - startTime) * 1000f;
        Debug.LogWarning($"[BuildingRegistry] Пересканирование завершено за {elapsedMs:F1}ms: {_allOutputs.Count} outputs, {_allInputs.Count} inputs, {_allWarehouses.Count} warehouses, {_allRoutings.Count} routings, {_allResidences.Count} residences, {_allBuildings.Count} buildings, {_allProducers.Count} producers");
    }

    // === СТАТИСТИКА (для UI/отладки) ===

    private void Update()
    {
        // Периодически логируем статистику (каждые 60 секунд)
        if (Time.frameCount % 3600 == 0)
        {
            Debug.Log($"[BuildingRegistry] Статистика: {_allBuildings.Count} зданий, {_allProducers.Count} производителей, {_allOutputs.Count} выходов, {_allInputs.Count} входов, {_allWarehouses.Count} складов, {_allRoutings.Count} маршрутов, {_allResidences.Count} резиденций");
        }
    }
}
