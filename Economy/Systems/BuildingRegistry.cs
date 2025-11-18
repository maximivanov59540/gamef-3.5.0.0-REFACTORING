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
    private readonly List<BuildingOutputInventory> _allOutputs = new List<BuildingOutputInventory>(256);
    private readonly List<BuildingInputInventory> _allInputs = new List<BuildingInputInventory>(256);
    private readonly List<Warehouse> _allWarehouses = new List<Warehouse>(16);
    private readonly List<BuildingResourceRouting> _allRoutings = new List<BuildingResourceRouting>(256); // 🚀 O(n²) FIX

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

    public void RegisterOutput(BuildingOutputInventory output)
    {
        if (output == null || _allOutputs.Contains(output)) return;
        _allOutputs.Add(output);
    }

    public void UnregisterOutput(BuildingOutputInventory output)
    {
        if (output == null) return;
        _allOutputs.Remove(output);
    }

    public void RegisterInput(BuildingInputInventory input)
    {
        if (input == null || _allInputs.Contains(input)) return;
        _allInputs.Add(input);
    }

    public void UnregisterInput(BuildingInputInventory input)
    {
        if (input == null) return;
        _allInputs.Remove(input);
    }

    public void RegisterWarehouse(Warehouse warehouse)
    {
        if (warehouse == null || _allWarehouses.Contains(warehouse)) return;
        _allWarehouses.Add(warehouse);
    }

    public void UnregisterWarehouse(Warehouse warehouse)
    {
        if (warehouse == null) return;
        _allWarehouses.Remove(warehouse);
    }

    public void RegisterRouting(BuildingResourceRouting routing)
    {
        if (routing == null || _allRoutings.Contains(routing)) return;
        _allRoutings.Add(routing);
    }

    public void UnregisterRouting(BuildingResourceRouting routing)
    {
        if (routing == null) return;
        _allRoutings.Remove(routing);
    }

    // === ПОЛУЧЕНИЕ СПИСКОВ (O(1) вместо O(N) с FindObjectsByType) ===

    /// <summary>
    /// Получить все BuildingOutputInventory (производители).
    /// ВАЖНО: Возвращает READ-ONLY список! Не модифицировать!
    /// </summary>
    public IReadOnlyList<BuildingOutputInventory> GetAllOutputs()
    {
        return _allOutputs;
    }

    /// <summary>
    /// Получить все BuildingInputInventory (потребители).
    /// ВАЖНО: Возвращает READ-ONLY список! Не модифицировать!
    /// </summary>
    public IReadOnlyList<BuildingInputInventory> GetAllInputs()
    {
        return _allInputs;
    }

    /// <summary>
    /// Получить все Warehouse (склады).
    /// ВАЖНО: Возвращает READ-ONLY список! Не модифицировать!
    /// </summary>
    public IReadOnlyList<Warehouse> GetAllWarehouses()
    {
        return _allWarehouses;
    }

    /// <summary>
    /// Получить все BuildingResourceRouting (маршрутизация).
    /// ВАЖНО: Возвращает READ-ONLY список! Не модифицировать!
    /// 🚀 O(n²) FIX: Используется вместо FindObjectsByType в балансировке нагрузки
    /// </summary>
    public IReadOnlyList<BuildingResourceRouting> GetAllRoutings()
    {
        return _allRoutings;
    }

    // === ОТЛАДКА ===

    public int GetOutputCount() => _allOutputs.Count;
    public int GetInputCount() => _allInputs.Count;
    public int GetWarehouseCount() => _allWarehouses.Count;
    public int GetRoutingCount() => _allRoutings.Count;

    /// <summary>
    /// Принудительное пересканирование сцены (только для отладки!).
    /// Используется если что-то пошло не так с регистрацией.
    /// </summary>
    [ContextMenu("DEBUG: Force Rescan Scene")]
    public void ForceRescanScene()
    {
        _allOutputs.Clear();
        _allInputs.Clear();
        _allWarehouses.Clear();
        _allRoutings.Clear();

        var outputs = FindObjectsByType<BuildingOutputInventory>(FindObjectsSortMode.None);
        var inputs = FindObjectsByType<BuildingInputInventory>(FindObjectsSortMode.None);
        var warehouses = FindObjectsByType<Warehouse>(FindObjectsSortMode.None);
        var routings = FindObjectsByType<BuildingResourceRouting>(FindObjectsSortMode.None);

        _allOutputs.AddRange(outputs);
        _allInputs.AddRange(inputs);
        _allWarehouses.AddRange(warehouses);
        _allRoutings.AddRange(routings);

        Debug.LogWarning($"[BuildingRegistry] Force rescan: {_allOutputs.Count} outputs, {_allInputs.Count} inputs, {_allWarehouses.Count} warehouses, {_allRoutings.Count} routings");
    }

    // === СТАТИСТИКА (для UI/отладки) ===

    private void Update()
    {
        // Периодически логируем статистику (каждые 60 секунд)
        if (Time.frameCount % 3600 == 0)
        {
            Debug.Log($"[BuildingRegistry] Статистика: {_allOutputs.Count} производителей, {_allInputs.Count} потребителей, {_allWarehouses.Count} складов, {_allRoutings.Count} маршрутов");
        }
    }
}
