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

    /// <summary>
    /// Инициализирует tier на основе BuildingData при создании
    /// </summary>
    void Awake()
    {
        if (buildingData != null && currentTier == 1)
        {
            currentTier = buildingData.currentTier;
        }

        // FIX #12: Регистрируемся в BuildingRegistry для EconomyManager
        if (BuildingRegistry.Instance != null)
        {
            BuildingRegistry.Instance.RegisterBuilding(this);
        }
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