using UnityEngine;

public class AuraEmitter : MonoBehaviour
{
    [Header("Настройки Ауры")]
    public AuraType type;
    public AuraDistributionType distributionType = AuraDistributionType.Radial;
    public float radius = 20f;

    [Header("Влияние на События")]
    [Tooltip("Снижение шанса события в радиусе ауры (0-1)\\n\\n" +
             "ПРИМЕРЫ:\\n" +
             "  • Больница: 0.3 = снижает шанс пандемии на 30%\\n" +
             "  • Полицейский участок: 0.3 = снижает шанс бунта на 30%\\n\\n" +
             "Используется только для Hospital и Police аур")]
    [Range(0f, 1f)]
    public float eventChanceReduction = 0.3f;

    // ── НОВОЕ: позиция эмиттера в сетке ───────────────────────
    private Vector2Int _rootGridPosition;
    private GridSystem _gridSystem;
    
    // --- ⬇️ ВОТ ЭТИ СТРОКИ НУЖНО ДОБАВИТЬ СЮДА ⬇️ ---
    private BuildingIdentity _identity; // <-- (1/3) НОВАЯ СТРОКА

    private void Awake()
    {
        _gridSystem = FindFirstObjectByType<GridSystem>();
        _identity = GetComponent<BuildingIdentity>(); // <-- (2/3) НОВАЯ СТРОКА
    }
    // --- ⬆️ КОНЕЦ НОВЫХ СТРОК В AWAKE ⬆️ ---


    private void Start()
    {
        CacheRootCell();
    }

    private void OnValidate()
    {
        // при редактировании в инспекторе обновим клетку (не критично)
        if (Application.isPlaying) return;
        _gridSystem = FindFirstObjectByType<GridSystem>();
        CacheRootCell();
    }

    private void CacheRootCell()
    {
        if (_gridSystem == null) return;
        int gx, gz;
        _gridSystem.GetXZ(transform.position, out gx, out gz);
        _rootGridPosition = new Vector2Int(gx, gz);
    }
    public void RefreshRootCell()
    {
        if (_gridSystem == null)
            _gridSystem = FindFirstObjectByType<GridSystem>();
        CacheRootCell();
    }

    public Vector2Int GetRootPosition() => _rootGridPosition;

    private void OnEnable()
    {
        AuraManager.Instance?.RegisterEmitter(this);
    }

    private void OnDisable()
    {
        AuraManager.Instance?.UnregisterEmitter(this);
    }
    
    // --- ⬇️ ВОТ ЭТОТ МЕТОД НУЖНО ДОБАВИТЬ СЮДА ⬇️ ---
    
    /// <summary>
    /// Возвращает 'BuildingIdentity' этого эмиттера.
    /// </summary>
    public BuildingIdentity GetIdentity() // <-- (3/3) НОВЫЙ МЕТОД
    {
        return _identity;
    }

    /// <summary>
    /// Проверяет, активна ли аура (здание построено и работает)
    /// </summary>
    public bool IsActive()
    {
        if (_identity == null) return false;

        // Проверяем, что здание не в режиме блюпринта
        return !_identity.isBlueprint;
    }

    /// <summary>
    /// Проверяет, находится ли здание в радиусе ауры
    /// </summary>
    public bool IsBuildingInRange(Vector2Int buildingPos)
    {
        if (distributionType == AuraDistributionType.Radial)
        {
            // Простое радиальное расстояние
            float distance = Vector2Int.Distance(_rootGridPosition, buildingPos);
            return distance <= radius;
        }
        else if (distributionType == AuraDistributionType.RoadBased)
        {
            // TODO: Реализовать проверку через дорожную сеть
            // Пока используем радиальное расстояние как fallback
            float distance = Vector2Int.Distance(_rootGridPosition, buildingPos);
            return distance <= radius;
        }

        return false;
    }
}