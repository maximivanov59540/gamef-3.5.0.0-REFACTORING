using UnityEngine;

public class BuildingIdentity : MonoBehaviour
{
    public BuildingData buildingData;
    public Vector2Int rootGridPosition;

    // --- НОВЫЕ СТРОКИ ---
    public float yRotation = 0f;
    public bool isBlueprint = false;

    [Header("Tier System")]
    [Tooltip("Текущий уровень этого конкретного здания (1, 2, 3...)")]
    public int currentTier = 1;
    // --- КОНЕЦ ---

    // 🚀 PERF FIX: Кеширование GetComponentsInChildren для избежания аллокаций
    // Используется в BuildingManager для операций с зданиями
    [HideInInspector] public ResourceProducer[] cachedProducers;
    [HideInInspector] public Collider[] cachedColliders;

    /// <summary>
    /// Инициализирует tier на основе BuildingData при создании
    /// </summary>
    void Awake()
    {
        if (buildingData != null && currentTier == 1)
        {
            currentTier = buildingData.currentTier;
        }

        // 🚀 PERF FIX: Кешируем компоненты при создании
        CacheComponents();

        // FIX #12: Регистрируемся в BuildingRegistry для EconomyManager
        if (BuildingRegistry.Instance != null)
        {
            BuildingRegistry.Instance.RegisterBuilding(this);
        }
    }

    /// <summary>
    /// 🚀 PERF FIX: Кеширует дочерние компоненты для быстрого доступа
    /// </summary>
    public void CacheComponents()
    {
        if (cachedProducers == null)
            cachedProducers = GetComponentsInChildren<ResourceProducer>(true); // includeInactive = true

        if (cachedColliders == null)
            cachedColliders = GetComponentsInChildren<Collider>(true);
    }

    /// <summary>
    /// Разрегистрация при уничтожении
    /// </summary>
    void OnDestroy()
    {
        // FIX #12: Разрегистрируемся из BuildingRegistry
        if (BuildingRegistry.Instance != null)
        {
            BuildingRegistry.Instance.UnregisterBuilding(this);
        }
    }

    /// <summary>
    /// Проверяет, можно ли улучшить это здание
    /// </summary>
    public bool CanUpgradeToNextTier()
    {
        return buildingData != null && buildingData.CanUpgrade() && !isBlueprint;
    }

    /// <summary>
    /// Возвращает данные следующего уровня
    /// </summary>
    public BuildingData GetNextTierData()
    {
        return buildingData != null ? buildingData.nextTier : null;
    }
}