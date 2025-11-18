using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Склад - узел логистической сети.
/// Может ОТДАВАТЬ и ПРИНИМАТЬ ресурсы (оба интерфейса).
/// Ресурсы хранятся в глобальном ResourceManager.
/// </summary>
[RequireComponent(typeof(BuildingIdentity))]
[RequireComponent(typeof(AuraEmitter))]
public class Warehouse : MonoBehaviour, IResourceProvider, IResourceReceiver
{
    [Header("Логистика")]
    [Tooltip("Макс. кол-во тележек, разгружаемых ОДНОВРЕМЕННО (Уровень склада)")]
    public int maxCartQueue = 1;

    [Tooltip("Время (сек) на полную разгрузку ОДНОЙ тележки")]
    public float unloadTime = 15.0f;

    // Список тех, кто СЕЙЧАС разгружается
    private List<CartAgent> _cartQueue = new List<CartAgent>();

    // Ссылка на визуализатор радиуса
    private AuraEmitter _auraEmitter;
    
    // === НОВЫЕ ПОЛЯ ДЛЯ ИНТЕРФЕЙСОВ ===
    private BuildingIdentity _identity;
    private ResourceManager _resourceManager;

    /// <summary>
    /// Возвращает актуальный радиус действия склада из AuraEmitter.
    /// </summary>
    public float roadRadius
    {
        get
        {
            if (_auraEmitter == null)
                _auraEmitter = GetComponent<AuraEmitter>();
            return _auraEmitter != null ? _auraEmitter.radius : 20f; // 20f - значение по умолчанию
        }
    }

    void Awake()
    {
        // === НОВЫЙ КОД ===
        _identity = GetComponent<BuildingIdentity>();
        _resourceManager = ResourceManager.Instance;

        // === СТАРЫЙ КОД ===
        // Находим AuraEmitter на этом же объекте
        _auraEmitter = GetComponent<AuraEmitter>();

        if (_auraEmitter == null)
        {
            Debug.LogWarning($"[Warehouse] На {gameObject.name} не найден компонент AuraEmitter. Добавляем автоматически.");
            _auraEmitter = gameObject.AddComponent<AuraEmitter>();
            _auraEmitter.type = AuraType.Warehouse;
            _auraEmitter.radius = 20f;
        }
        else if (_auraEmitter.type != AuraType.Warehouse)
        {
            Debug.LogWarning($"[Warehouse] AuraEmitter на {gameObject.name} имеет неправильный тип. Исправляем на Warehouse.");
            _auraEmitter.type = AuraType.Warehouse;
        }
    }

    // 🚀 PERFORMANCE FIX: Автоматическая регистрация в BuildingRegistry
    private void OnEnable()
    {
        BuildingRegistry.Instance?.RegisterWarehouse(this);
    }

    private void OnDisable()
    {
        BuildingRegistry.Instance?.UnregisterWarehouse(this);
    }

    void Start()
    {
        Debug.Log($"[Warehouse] {gameObject.name} инициализирован с радиусом: {roadRadius}");
    }

    void OnValidate()
    {
        // Когда радиус изменяется в Inspector, пересчитываем доступ для всех производств
        if (Application.isPlaying && _auraEmitter != null)
        {
            RefreshAllProducers();
        }
    }

    /// <summary>
    /// Пересчитывает доступ к складу для всех производств на карте.
    /// </summary>
    private void RefreshAllProducers()
    {
        // FIX #13: Используем BuildingRegistry вместо FindObjectsByType
        if (BuildingRegistry.Instance != null)
        {
            var allProducers = BuildingRegistry.Instance.GetAllProducers();
            int count = 0;
            foreach (var producer in allProducers)
            {
                if (producer != null) // Проверяем на null (объект мог быть удален)
                {
                    producer.RefreshWarehouseAccess();
                    count++;
                }
            }
            Debug.Log($"[Warehouse] {gameObject.name}: пересчитан доступ для {count} производств (новый радиус: {roadRadius})");
        }
        else
        {
            Debug.LogWarning($"[Warehouse] {gameObject.name}: BuildingRegistry.Instance == null! Не могу обновить производителей.");
        }
    }

    // ════════════════════════════════════════════════════════════════
    //                   СТАРЫЕ МЕТОДЫ (НЕ МЕНЯЕМ)
    // ════════════════════════════════════════════════════════════════

    public bool RequestUnload(CartAgent cart)
    {
        if (_cartQueue.Count < maxCartQueue)
        {
            _cartQueue.Add(cart);
            Debug.Log($"[Warehouse] {cart.name} начал разгрузку. В очереди: {_cartQueue.Count}/{maxCartQueue}");
            return true; // "Добро пожаловать, проезжай"
        }
        return false; // "Мест нет, стой в очереди"
    }

    public void FinishUnload(CartAgent cart)
    {
        _cartQueue.Remove(cart);
        Debug.Log($"[Warehouse] {cart.name} закончил разгрузку. В очереди: {_cartQueue.Count}/{maxCartQueue}");
    }
    
    public int GetQueueCount() 
    { 
        return _cartQueue.Count; 
    }

    // ════════════════════════════════════════════════════════════════
    //              РЕАЛИЗАЦИЯ IResourceProvider (ОТДАТЬ)
    // ════════════════════════════════════════════════════════════════

    public Vector2Int GetGridPosition()
    {
        if (_identity == null)
            _identity = GetComponent<BuildingIdentity>();
        
        return _identity != null ? _identity.rootGridPosition : Vector2Int.zero;
    }

    public ResourceType GetProvidedResourceType()
    {
        // Склад может отдать любой ресурс, который есть в наличии
        // Возвращаем первый доступный
        if (_resourceManager == null)
            _resourceManager = ResourceManager.Instance;
        
        if (_resourceManager == null)
            return ResourceType.Wood; // Дефолт на случай ошибки
        
        foreach (ResourceType type in System.Enum.GetValues(typeof(ResourceType)))
        {
            if (_resourceManager.GetResourceAmount(type) >= 1f)
                return type;
        }
        
        return ResourceType.Wood; // Дефолт
    }

    public float GetAvailableAmount(ResourceType type)
    {
        if (_resourceManager == null)
            _resourceManager = ResourceManager.Instance;
        
        return _resourceManager != null ? _resourceManager.GetResourceAmount(type) : 0f;
    }

    public float TryTakeResource(ResourceType type, float amount)
    {
        if (_resourceManager == null)
            _resourceManager = ResourceManager.Instance;
        
        if (_resourceManager == null)
            return 0f;
        
        return _resourceManager.TakeFromStorage(type, amount);
    }

    public bool CanAcceptCart()
    {
        return _cartQueue.Count < maxCartQueue;
    }

    // ════════════════════════════════════════════════════════════════
    //             РЕАЛИЗАЦИЯ IResourceReceiver (ПРИНЯТЬ)
    // ════════════════════════════════════════════════════════════════

    public bool AcceptsResource(ResourceType type)
    {
        // Склад принимает ВСЕ типы ресурсов
        return true;
    }

    public float GetAvailableSpace(ResourceType type)
    {
        if (_resourceManager == null)
            _resourceManager = ResourceManager.Instance;
        
        if (_resourceManager == null)
            return 0f;
        
        float limit = _resourceManager.GetResourceLimit(type);
        float current = _resourceManager.GetResourceAmount(type);
        return Mathf.Max(0, limit - current);
    }

    public float TryAddResource(ResourceType type, float amount)
    {
        if (_resourceManager == null)
            _resourceManager = ResourceManager.Instance;
        
        if (_resourceManager == null)
            return 0f;
        
        return _resourceManager.AddToStorage(type, amount);
    }

    // Примечание: CanAcceptCart() уже реализован выше для IResourceProvider
    // (один метод для обоих интерфейсов)
}