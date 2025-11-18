using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Объединенный менеджер дорог и логистики (PHASE 4/4 - Singleton Reduction)
/// Объединяет функциональность:
/// - RoadManager (дороги и граф)
/// - LogisticsManager (запросы на доставку ресурсов)
/// </summary>
public class RoadManager : MonoBehaviour
{
    public static RoadManager Instance { get; private set; }
    public event System.Action<Vector2Int> OnRoadAdded;
    public event System.Action<Vector2Int> OnRoadRemoved;

    [Header("Ссылки на 'Инструменты'")]
    [SerializeField] private GridSystem gridSystem;

    // (Это поле [SerializeField] private GameObject roadPrefab; - БОЛЬШЕ НЕ НУЖНО!)
    // (Можешь его удалить, так как префаб теперь берется из RoadData)

    [SerializeField] private Transform roadsRoot;

    // === СИСТЕМА ДОРОГ (ранее RoadManager) ===

    // FIX ISSUE #1: Замена List на HashSet для O(1) Contains/Add вместо O(n)
    private readonly Dictionary<Vector2Int, HashSet<Vector2Int>> _roadGraph = new Dictionary<Vector2Int, HashSet<Vector2Int>>();
    private static readonly Vector2Int[] DIRS = new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

    // === СИСТЕМА ЛОГИСТИКИ (ранее LogisticsManager) ===

    // "Доска Заказов" - список активных запросов на доставку ресурсов
    private readonly List<ResourceRequest> _activeRequests = new List<ResourceRequest>();

    // ISSUE #9 FIX: Группировка запросов по типу для O(1) доступа вместо O(n) Where().ToList()
    private readonly Dictionary<ResourceType, List<ResourceRequest>> _requestsByType = new Dictionary<ResourceType, List<ResourceRequest>>();

    // --- (Awake, RebuildGraphFromScene - остаются БЕЗ ИЗМЕНЕНИЙ) ---
    void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        else Instance = this;
        if (gridSystem == null) gridSystem = FindFirstObjectByType<GridSystem>();
        
        // (Проверка roadPrefab больше не нужна)

        if (roadsRoot == null)
        {
            var go = GameObject.Find("RoadsRoot");
            if (go == null) go = new GameObject("RoadsRoot");
            roadsRoot = go.transform;
        }
        roadsRoot.SetParent(null);
        roadsRoot.position = Vector3.zero;
        RebuildGraphFromScene();
    }


    /// <summary>
    /// ИЗМЕНЕННЫЙ МЕТОД: Теперь принимает 'RoadData'
    /// </summary>
    public void PlaceRoad(Vector2Int gridPos, RoadData data)
    {
        if (gridPos.x == -1 || data == null || data.roadPrefab == null) return;

        if (gridPos.x < 0 || gridPos.y < 0 || 
            gridPos.x >= gridSystem.GetGridWidth() || 
            gridPos.y >= gridSystem.GetGridHeight())
        {
            return; // "Попытка" "строить" "за" "пределами" "мира"
        }

        // ЖЁСТКАЯ защита
        if (gridSystem.GetBuildingIdentityAt(gridPos.x, gridPos.y) != null) return;
        if (gridSystem.GetRoadTileAt(gridPos.x, gridPos.y) != null) return;
        if (gridSystem.IsCellOccupied(gridPos.x, gridPos.y)) return;

        // Создаем физику
        Vector3 worldPos = gridSystem.GetWorldPosition(gridPos.x, gridPos.y);
        float offset = gridSystem.GetCellSize() / 2f;
        worldPos.x += offset;
        worldPos.z += offset;
        worldPos.y += 0.01f;

        // --- ИЗМЕНЕНИЕ: Используем data.roadPrefab ---
        GameObject roadGO = Instantiate(data.roadPrefab, worldPos, Quaternion.Euler(90, 0, 0), roadsRoot);
        roadGO.name = $"Road_{data.roadName}_{gridPos.x}_{gridPos.y}";

        // (Код выравнивания по 'Ground' - без изменений)
        if (Physics.Raycast(worldPos + Vector3.up * 50f, Vector3.down, out var hit, 200f, 1 << LayerMask.NameToLayer("Ground")))
        {
            worldPos.y = hit.point.y + 0.01f;
            roadGO.transform.position = worldPos;
        }
        else
        {
            worldPos.y = gridSystem.GetWorldPosition(0, 0).y + 0.01f;
            roadGO.transform.position = worldPos;
        }

        RoadTile roadTileComponent = roadGO.GetComponent<RoadTile>();
        if (roadTileComponent == null)
        {
            Debug.LogError($"Префаб дороги ({data.roadPrefab.name}) не содержит 'RoadTile'!", roadGO);
            Destroy(roadGO);
            return;
        }

        // --- НОВОЕ: Сохраняем 'чертеж' в 'тайл' ---
        roadTileComponent.roadData = data;

        // Регистрируем в GridSystem
        gridSystem.SetRoadTile(gridPos, roadTileComponent);

        // --- Обновление графа (FIX ISSUE #1: HashSet.Add автоматически проверяет дубли) ---
        if (!_roadGraph.ContainsKey(gridPos))
            _roadGraph[gridPos] = new HashSet<Vector2Int>();
        foreach (var d in DIRS)
        {
            Vector2Int nb = gridPos + d;
            var nbTile = gridSystem.GetRoadTileAt(nb.x, nb.y);
            if (nbTile == null) continue;

            if (!_roadGraph.ContainsKey(nb))
                _roadGraph[nb] = new HashSet<Vector2Int>();

            // HashSet.Add возвращает false если элемент уже существует, дополнительная проверка не нужна
            _roadGraph[gridPos].Add(nb);
            _roadGraph[nb].Add(gridPos);
        }
        OnRoadAdded?.Invoke(gridPos);
    }

    // --- (RemoveRoad, GetRoadGraph - остаются БЕЗ ИЗМЕНЕНИЙ) ---
    public void RemoveRoad(Vector2Int gridPos)
    {
        if (gridPos.x == -1) return;
        RoadTile roadTileComponent = gridSystem.GetRoadTileAt(gridPos.x, gridPos.y);
        if (roadTileComponent == null) return;
        OnRoadRemoved?.Invoke(gridPos);
        if (_roadGraph.TryGetValue(gridPos, out var neighbours))
        {
            // FIX ISSUE #1: Используем HashSet, копируем чтобы избежать модификации во время итерации
            var copy = new HashSet<Vector2Int>(neighbours);
            foreach (var nb in copy)
                if (_roadGraph.TryGetValue(nb, out var set))
                    set.Remove(gridPos);
            _roadGraph.Remove(gridPos);
        }
        gridSystem.SetRoadTile(gridPos, null);
        Destroy(roadTileComponent.gameObject);
    }
    
    /// <summary>
    /// НОВЫЙ МЕТОД: Заменяет дорогу (для апгрейда)
    /// </summary>
    public void UpgradeRoad(Vector2Int gridPos, RoadData newData)
    {
        // (Мы не проверяем ресурсы здесь, это делает 'State_Upgrading')
        
        // 1. Запоминаем, кто был соседом (чтобы не сломать граф)
        RoadTile oldTile = gridSystem.GetRoadTileAt(gridPos.x, gridPos.y);
        if (oldTile == null) return;
        
        // 2. Сносим старую
        RemoveRoad(gridPos);
        
        // 3. Ставим новую
        PlaceRoad(gridPos, newData);
    }

    // ── НОВОЕ: публичный доступ к графу ───────────────────────
    // FIX ISSUE #1: Обновлен return type на HashSet
    public Dictionary<Vector2Int, HashSet<Vector2Int>> GetRoadGraph() => _roadGraph;
    private void RebuildGraphFromScene()
    {
        _roadGraph.Clear();

        IEnumerable<RoadTile> tiles;
        if (roadsRoot != null)
        {
            // true — берём даже неактивные (вдруг у тебя есть скрытые сегменты дорог)
            tiles = roadsRoot.GetComponentsInChildren<RoadTile>(true);
        }
        else
        {
            // Фолбэк на глобальный поиск (см. Вариант 2)
#if UNITY_2023_1_OR_NEWER || UNITY_6000_0_OR_NEWER
            tiles = UnityEngine.Object.FindObjectsByType<RoadTile>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
tiles = UnityEngine.Object.FindObjectsOfType<RoadTile>(includeInactive: true);
#endif

        }

        foreach (var tile in tiles)
        {
            // Определим клетку по позиции мира
            int gx, gz;
            gridSystem.GetXZ(tile.transform.position, out gx, out gz);
            var pos = new Vector2Int(gx, gz);

            // Убедимся, что GridSystem тоже знает про этот тайл
            if (gridSystem.GetRoadTileAt(pos.x, pos.y) != tile)
                gridSystem.SetRoadTile(pos, tile);

            if (!_roadGraph.ContainsKey(pos))
                _roadGraph[pos] = new HashSet<Vector2Int>();
        }

        // Подружим соседей (4-направления), как это делается в PlaceRoad(...)
        // FIX ISSUE #1: HashSet.Add автоматически игнорирует дубликаты
        foreach (var kv in _roadGraph)
        {
            var pos = kv.Key;
            foreach (var d in new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right })
            {
                var nb = pos + d;
                var nbTile = gridSystem.GetRoadTileAt(nb.x, nb.y);
                if (nbTile == null) continue;

                if (!_roadGraph.ContainsKey(nb))
                    _roadGraph[nb] = new HashSet<Vector2Int>();

                _roadGraph[pos].Add(nb);
                _roadGraph[nb].Add(pos);
            }
        }

        // Дадим знать слушателям, что граф появился
        foreach (var pos in _roadGraph.Keys)
            OnRoadAdded?.Invoke(pos);
    }

    // === ПУБЛИЧНЫЕ МЕТОДЫ: ЛОГИСТИКА (ранее LogisticsManager) ===

    /// <summary>
    /// Здание-потребитель (InputInventory) "вешает" свой заказ на доску.
    /// </summary>
    public void CreateRequest(ResourceRequest request)
    {
        if (!_activeRequests.Contains(request))
        {
            _activeRequests.Add(request);

            // ISSUE #9 FIX: Добавляем в словарь группировки
            if (!_requestsByType.ContainsKey(request.RequestedType))
            {
                _requestsByType[request.RequestedType] = new List<ResourceRequest>();
            }
            _requestsByType[request.RequestedType].Add(request);

            Debug.Log($"[RoadManager/Logistics] Новый запрос на {request.RequestedType} от {request.Requester.name} (Приоритет: {request.Priority})");
        }
    }

    /// <summary>
    /// Здание-потребитель (InputInventory) "снимает" свой заказ (т.к. склад полон).
    /// </summary>
    public void FulfillRequest(ResourceRequest request)
    {
        if (_activeRequests.Contains(request))
        {
            _activeRequests.Remove(request);

            // ISSUE #9 FIX: Удаляем из словаря группировки
            if (_requestsByType.TryGetValue(request.RequestedType, out var typeRequests))
            {
                typeRequests.Remove(request);

                // Если список пуст, удаляем ключ из словаря
                if (typeRequests.Count == 0)
                {
                    _requestsByType.Remove(request.RequestedType);
                }
            }

            Debug.Log($"[RoadManager/Logistics] Запрос на {request.RequestedType} от {request.Requester.name} выполнен/отменен.");
        }
    }

    /// <summary>
    /// Находит лучший запрос для доставки с учетом расстояния и приоритета
    /// </summary>
    public ResourceRequest GetBestRequest(Vector2Int cartGridPos, ResourceType resourceToDeliver, float roadRadius)
    {
        if (_activeRequests.Count == 0 || gridSystem == null)
            return null;

        if (_roadGraph == null || _roadGraph.Count == 0) return null;

        // 1. Находим ВСЕ "выходы" тележки
        List<Vector2Int> cartRoadCells = LogisticsPathfinder.FindAllRoadAccess(cartGridPos, gridSystem, _roadGraph);
        if (cartRoadCells.Count == 0)
        {
            return null; // Тележка сама не у дороги?
        }

        // 2. ISSUE #9 FIX: Используем словарь вместо Where().ToList() для O(1) доступа
        if (!_requestsByType.TryGetValue(resourceToDeliver, out var matchingRequests) || matchingRequests.Count == 0)
            return null;

        // 3. Считаем расстояния от ВСЕХ "выходов" тележки
        int maxSteps = Mathf.FloorToInt(roadRadius);
        var distancesFromCart = LogisticsPathfinder.Distances_BFS_Multi(cartRoadCells, maxSteps, _roadGraph);

        // 4. Собираем список "валидных" запросов
        var validRequests = new List<(ResourceRequest request, int distance)>();

        foreach (var req in matchingRequests)
        {
            // Находим ВСЕ "входы" для "заказчика"
            List<Vector2Int> destRoadCells = LogisticsPathfinder.FindAllRoadAccess(req.DestinationCell, gridSystem, _roadGraph);
            if (destRoadCells.Count == 0) continue; // Заказчик не у дороги

            // Ищем ЛУЧШИЙ "вход" (ближайший к тележке)
            int minDistance = int.MaxValue;
            bool foundAccess = false;

            foreach (var destCell in destRoadCells)
            {
                if (distancesFromCart.TryGetValue(destCell, out int dist))
                {
                    if (dist < minDistance)
                    {
                        minDistance = dist;
                        foundAccess = true;
                    }
                }
            }

            // Если хотя бы один "вход" достижим
            if (foundAccess)
            {
                validRequests.Add((req, minDistance));
            }
        }

        // 5. Сортируем по приоритету, затем по расстоянию
        var sortedRequests = validRequests
            .OrderByDescending(r => r.request.Priority)
            .ThenBy(r => r.distance);

        // 6. Возвращаем лучший запрос
        return sortedRequests.FirstOrDefault().request;
    }
}

/// Очень простой пул для временных списков, чтобы не аллоцировать лишнее.
static class ListPool<T>
{
    private static readonly Stack<List<T>> Pool = new Stack<List<T>>();

    public static List<T> Get() => Pool.Count > 0 ? Pool.Pop() : new List<T>();
    public static void Release(List<T> list)
    {
        list.Clear();
        Pool.Push(list);
    }
    // ВОССТАНОВЛЕНИЕ ГРАФА ДЛЯ ПРЕДРАЗМЕЩЕННЫХ ДОРОГ
}
