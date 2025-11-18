using System.Collections.Generic;
using UnityEngine;
using System.Linq; // <-- Добавили using

public static class LogisticsPathfinder
{
    /// Быстрый ответ «есть ли путь» (как было).
    public static bool HasPath_BFS(Vector2Int start, Vector2Int end,
        Dictionary<Vector2Int, List<Vector2Int>> graph)
    {
        if (start == end) return true;
        if (graph == null) return false;
        if (!graph.ContainsKey(start) || !graph.ContainsKey(end)) return false;

        var q = new Queue<Vector2Int>();
        var visited = new HashSet<Vector2Int>();

        visited.Add(start);
        q.Enqueue(start);

        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            if (cur == end) return true;

            var neigh = graph[cur];
            for (int i = 0; i < neigh.Count; i++)
            {
                var nb = neigh[i];
                if (visited.Add(nb))
                    q.Enqueue(nb);
            }
        }
        return false;
    }

    /// КАРТА расстояний (в шагах по дорогам) от start до всех достижимых узлов, с отсечкой по maxSteps.
    public static Dictionary<Vector2Int, int> Distances_BFS(
        Vector2Int start,
        int maxSteps,
        Dictionary<Vector2Int, List<Vector2Int>> graph)
    {
        var dist = new Dictionary<Vector2Int, int>(256);
        if (graph == null || !graph.ContainsKey(start)) return dist;

        var q = new Queue<Vector2Int>();
        dist[start] = 0;
        q.Enqueue(start);

        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            int d = dist[cur];
            if (d >= maxSteps) continue;

            var neigh = graph[cur];
            for (int i = 0; i < neigh.Count; i++)
            {
                var nb = neigh[i];
                if (!dist.ContainsKey(nb))
                {
                    dist[nb] = d + 1;
                    q.Enqueue(nb);
                }
            }
        }
        return dist;
    }
    
    // --- (Distances_BFS_Multi остается без изменений) ---
    public static Dictionary<Vector2Int, int> Distances_BFS_Multi(
    IEnumerable<Vector2Int> starts,
    int maxSteps,
    Dictionary<Vector2Int, List<Vector2Int>> graph)
    {
        var dist = new Dictionary<Vector2Int, int>(256);
        if (graph == null) return dist;

        var q = new Queue<Vector2Int>();

        // добавить все валидные старты
        foreach (var s in starts)
        {
            if (!graph.ContainsKey(s)) continue;
            if (dist.ContainsKey(s)) continue;
            dist[s] = 0;
            q.Enqueue(s);
        }

        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            int d = dist[cur];
            if (d >= maxSteps) continue;

            var neigh = graph[cur];
            for (int i = 0; i < neigh.Count; i++)
            {
                var nb = neigh[i];
                if (!dist.ContainsKey(nb))
                {
                    dist[nb] = d + 1;
                    q.Enqueue(nb);
                }
            }
        }
        return dist;
    }
    
    // --- (FindActualPath остается без изменений) ---
    public static List<Vector2Int> FindActualPath(
        Vector2Int start, 
        Vector2Int end, 
        Dictionary<Vector2Int, List<Vector2Int>> graph)
    {
        if (start == end) return new List<Vector2Int> { start };
        if (graph == null) return null;
        if (!graph.ContainsKey(start) || !graph.ContainsKey(end)) return null;

        var q = new Queue<Vector2Int>();
        var cameFrom = new Dictionary<Vector2Int, Vector2Int>(); 

        q.Enqueue(start);
        cameFrom[start] = start; 

        bool found = false;

        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            if (cur == end)
            {
                found = true;
                break; 
            }

            var neigh = graph[cur];
            for (int i = 0; i < neigh.Count; i++)
            {
                var nb = neigh[i];
                if (!cameFrom.ContainsKey(nb)) 
                {
                    cameFrom[nb] = cur; 
                    q.Enqueue(nb);
                }
            }
        }

        if (!found)
        {
            return null;
        }

        var path = new List<Vector2Int>();
        var current = end;

        // 🔥 FIX: Защита от циклических ссылок и бесконечного цикла
        var visited = new HashSet<Vector2Int>();
        const int MAX_PATH_LENGTH = 10000; // Максимальная длина пути
        int steps = 0;

        while (current != start && ++steps <= MAX_PATH_LENGTH)
        {
            // Проверка на циклическую ссылку
            if (!visited.Add(current))
            {
                Debug.LogError($"[LogisticsPathfinder] FindActualPath: Circular reference detected at {current}!");
                return null; // Возвращаем null при ошибке
            }

            // Проверка наличия ключа в словаре
            if (!cameFrom.ContainsKey(current))
            {
                Debug.LogError($"[LogisticsPathfinder] FindActualPath: Missing key {current} in cameFrom dictionary!");
                return null;
            }

            path.Add(current);
            current = cameFrom[current];
        }

        if (steps > MAX_PATH_LENGTH)
        {
            Debug.LogWarning($"[LogisticsPathfinder] FindActualPath: Max path length {MAX_PATH_LENGTH} exceeded!");
            return null;
        }

        path.Add(start);
        path.Reverse();

        return path;
    }

    // --- ⬇️ ⬇️ ⬇️ НАШ НОВЫЙ МЕТОД ⬇️ ⬇️ ⬇️ ---
    /// <summary>
    /// "УМНЫЙ" ПОИСК (Множественный): Находит ВСЕ клетки дорог у периметра здания.
    /// Если не найдено дорог рядом, ищет в расширенном радиусе.
    /// </summary>
    public static List<Vector2Int> FindAllRoadAccess(Vector2Int buildingCell, GridSystem gridSystem, Dictionary<Vector2Int, List<Vector2Int>> graph)
    {
        var results = new List<Vector2Int>();
        var seen = new HashSet<Vector2Int>(); // Для защиты от дубликатов

        // 1. Проверяем саму клетку (если здание 1х1 стоит прямо на дороге)
        if (graph.ContainsKey(buildingCell) && seen.Add(buildingCell))
        {
            results.Add(buildingCell);
        }

        BuildingIdentity identity = gridSystem.GetBuildingIdentityAt(buildingCell.x, buildingCell.y);

        // 2. Если это не здание (а просто точка), ищем в 4-х соседях
        if (identity == null)
        {
            Vector2Int[] neighbors = {
                buildingCell + Vector2Int.up,
                buildingCell + Vector2Int.down,
                buildingCell + Vector2Int.left,
                buildingCell + Vector2Int.right
            };
            foreach (var cell in neighbors)
            {
                if (graph.ContainsKey(cell) && seen.Add(cell))
                {
                    results.Add(cell);
                }
            }

            // ✅ НОВОЕ: Если не найдено дорог рядом, ищем в расширенном радиусе
            if (results.Count == 0)
            {
                Debug.LogWarning($"[LogisticsPathfinder] Точка {buildingCell}: нет дорог рядом, запускаю расширенный поиск...");
                results = FindNearestRoads(buildingCell, graph, 5);
            }

            return results; // Возвращаем то, что нашли (может быть пусто)
        }

        // 3. Если это здание, сканируем ВЕСЬ периметр
        Vector2Int root = identity.rootGridPosition;
        Vector2Int size = identity.buildingData.size;
        float yRotation = identity.yRotation;

        if (Mathf.Abs(yRotation - 90f) < 1f || Mathf.Abs(yRotation - 270f) < 1f)
        {
            size = new Vector2Int(size.y, size.x);
        }

        int minX = root.x - 1; int maxX = root.x + size.x;
        int minZ = root.y - 1; int maxZ = root.y + size.y;

        for (int x = minX; x <= maxX; x++)
        {
            Vector2Int topCell = new Vector2Int(x, maxZ);
            if (graph.ContainsKey(topCell) && seen.Add(topCell))
            {
                results.Add(topCell);
            }

            Vector2Int bottomCell = new Vector2Int(x, minZ);
            if (graph.ContainsKey(bottomCell) && seen.Add(bottomCell))
            {
                results.Add(bottomCell);
            }
        }
        for (int z = minZ + 1; z < maxZ; z++)
        {
            Vector2Int leftCell = new Vector2Int(minX, z);
            if (graph.ContainsKey(leftCell) && seen.Add(leftCell))
            {
                results.Add(leftCell);
            }

            Vector2Int rightCell = new Vector2Int(maxX, z);
            if (graph.ContainsKey(rightCell) && seen.Add(rightCell))
            {
                results.Add(rightCell);
            }
        }

        // ✅ НОВОЕ: Если не найдено дорог у периметра здания, ищем в расширенном радиусе
        if (results.Count == 0)
        {
            Debug.LogWarning($"[LogisticsPathfinder] Здание {identity.name} ({buildingCell}): нет дорог у периметра! Запускаю расширенный поиск...");
            results = FindNearestRoads(buildingCell, graph, 5);
        }

        return results;
    }

    /// <summary>
    /// ✅ НОВОЕ: Находит ближайшие дороги в указанном радиусе от точки
    /// </summary>
    private static List<Vector2Int> FindNearestRoads(Vector2Int center, Dictionary<Vector2Int, List<Vector2Int>> graph, int maxRadius)
    {
        var results = new List<Vector2Int>();

        // Ищем по спирали от центра наружу
        for (int radius = 1; radius <= maxRadius; radius++)
        {
            for (int x = center.x - radius; x <= center.x + radius; x++)
            {
                for (int z = center.y - radius; z <= center.y + radius; z++)
                {
                    // Проверяем только клетки на текущем радиусе (периметр квадрата)
                    if (Mathf.Abs(x - center.x) == radius || Mathf.Abs(z - center.y) == radius)
                    {
                        Vector2Int cell = new Vector2Int(x, z);
                        if (graph.ContainsKey(cell))
                        {
                            results.Add(cell);
                        }
                    }
                }
            }

            // Если нашли хотя бы одну дорогу на этом радиусе, останавливаемся
            if (results.Count > 0)
            {
                Debug.Log($"[LogisticsPathfinder] Расширенный поиск: найдено {results.Count} дорог на расстоянии {radius} клеток от {center}");
                return results;
            }
        }

        Debug.LogWarning($"[LogisticsPathfinder] Расширенный поиск: НЕ найдено дорог в радиусе {maxRadius} от {center}!");
        return results;
    }
}