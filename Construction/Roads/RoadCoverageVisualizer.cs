using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// Отвечает за подсветку дорог и контуров зданий от одного или нескольких RoadBased-источников.
/// Цвет: голубой → очень светло-голубой в последних 10% радиуса.
/// Яркость = эффективность; плавный fade-in/out ~0.12 c.
public class RoadCoverageVisualizer : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private GridSystem gridSystem;
    [SerializeField] private RoadManager roadManager;
    [SerializeField] private bool showDebugPreviewAtStart = false;
    [Header("Visual")]
    [Tooltip("Длительность плавного появления/исчезновения, сек")]
    [Range(0.05f, 0.2f)] public float fadeTime = 0.12f;
    [Tooltip("Толщина контура зданий (метры)")]
    [Range(0.01f, 0.1f)] public float buildingOutlineWidth = 0.035f;

    // Цвета (голубой → очень светло-голубой)
    private static readonly Color StrongBlue = new Color(0.35f, 0.75f, 1f, 1f);
    private static readonly Color LightBlue  = new Color(0.85f, 0.95f, 1f, 1f);

    private readonly Dictionary<RoadTile, Renderer> _roadRenderers = new Dictionary<RoadTile, Renderer>(512);
    private readonly Dictionary<RoadTile, MaterialPropertyBlock> _mpbCache = new Dictionary<RoadTile, MaterialPropertyBlock>(512);

    // Активные источники покрытия (может быть несколько рынков/служб)
    private readonly List<CoverageSource> _sources = new List<CoverageSource>(4);

    // Сводная карта: RoadTile -> maxEfficiency от всех источников
    private readonly Dictionary<RoadTile, float> _mergedEff = new Dictionary<RoadTile, float>(1024);

    // Построенные контуры зданий (пул)
    private readonly List<LineRenderer> _outlinePool = new List<LineRenderer>(128);
    private static readonly Vector2Int[] DIRS4 = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
    private int _outlineUsed = 0;

    private float _overlayAlpha = 0f;  // для fade
    private Coroutine _fadeCo;

    void Awake()
    {
        if (gridSystem == null) gridSystem = FindFirstObjectByType<GridSystem>();
        if (roadManager == null) roadManager = RoadManager.Instance ?? FindFirstObjectByType<RoadManager>();

        // Подписка на изменения графа — инвалидируем пересчёт
        if (roadManager != null)
        {
            roadManager.OnRoadAdded += OnRoadChanged;
            roadManager.OnRoadRemoved += OnRoadChanged;
        }
    }

    // 🔥 FIX: Именованный метод для отписки в OnDestroy
    private void OnRoadChanged(Vector2Int pos)
    {
        _dirty = true;
    }

    // 🔥 FIX: Memory leak - отписка от событий и остановка корутины
    private void OnDestroy()
    {
        // Отписываемся от событий RoadManager
        if (roadManager != null)
        {
            roadManager.OnRoadAdded -= OnRoadChanged;
            roadManager.OnRoadRemoved -= OnRoadChanged;
        }

        // Останавливаем активную корутину fade
        if (_fadeCo != null)
        {
            StopCoroutine(_fadeCo);
            _fadeCo = null;
        }
    }
    private void Start()
    {
        if (!showDebugPreviewAtStart) return;
        var anyTile = FindFirstObjectByType<RoadTile>();
        if (anyTile != null)
        {
            var cell = WorldToCell(anyTile.transform.position);
            Debug.Log($"[RCV] Forced preview from cell {cell}");
            ShowPreview(anyTile.transform.position, 12f);
        }
    }
    // -------- Публичный API --------

    /// Показ покрытия от конкретного эмиттера (рынка).
    public void ShowForEmitter(AuraEmitter emitter)
    {
        Debug.Log($"[RCV] ShowForEmitter from {emitter.name} root={emitter.GetRootPosition()} radius={emitter.radius}");
        if (emitter == null) return;
        if (emitter.distributionType != AuraDistributionType.RoadBased) return;

        var src = GetOrCreateSource(emitter);
        src.preview = false;
        src.worldPos = emitter.transform.position;
        src.rootCell = emitter.GetRootPosition();
        src.radius   = emitter.radius;

        _dirty = true;
        FadeTo(1f);
    }

    /// Превью (при планировании постройки эмиттера).
    public void ShowPreview(Vector3 worldPos, float radius)
    {
        var cell = WorldToCell(worldPos);
        var src = GetOrCreatePreviewSource();
        src.preview = true;
        src.worldPos = worldPos;
        src.rootCell = cell;
        src.radius   = radius;

        _dirty = true;
        FadeTo(1f);
    }

    /// Полностью скрыть визуал.
    public void HideAll()
    {
        _sources.Clear();
        _dirty = true;
        FadeTo(0f);
        foreach (var kv in _roadRenderers)
        {
            if (kv.Value != null) kv.Value.SetPropertyBlock(null);
            var hl = kv.Key.GetComponent<RoadTileHighlighter>();
            if (hl != null) hl.SetHighlight(false);
        }
    }

    /// Убрать только превью, оставить активные эмиттеры.
    public void HidePreview()
    {
        for (int i = _sources.Count - 1; i >= 0; i--)
            if (_sources[i].preview) _sources.RemoveAt(i);

        if (_sources.Count == 0) FadeTo(0f); else { _dirty = true; FadeTo(1f); }
        
        // --- ИЗМЕНЕНИЕ: Удален "грязный" цикл foreach, который был здесь ---
        // (Он вызывал мерцание и конфликтовал с Recompute)
    }

    // -------- Основа работы --------

    private bool _dirty = true;

    void Update()
    {
        if (_sources.Count == 0)
        {
            if (_overlayAlpha <= 0f) ClearVisualsHard();
            return;
        }

        if (_dirty)
        {
            Debug.Log($"[RCV] Recompute: sources={_sources.Count}, alpha={_overlayAlpha}");
            RecomputeAndApply();
            _dirty = false;
        }
        // мягкое обновление делает FadeTo()
    }

    private void RecomputeAndApply()
    {
        _mergedEff.Clear();
        // Для подсветки контуров зданий: BuildingIdentity -> maxEff рядом
        var buildingEff = new Dictionary<BuildingIdentity, float>(256);

        foreach (var src in _sources)
        {
            int stepsMax = Mathf.Max(0, Mathf.FloorToInt(src.radius / Mathf.Max(0.0001f, gridSystem.GetCellSize())));
            var graph = roadManager.GetRoadGraph();
            Debug.Log($"[RCV] Source root={src.rootCell} stepsMax={stepsMax} graphNodes={graph?.Count}");
            // Стартовые узлы BFS: если рынок стоит НЕ на дороге — берём всех соседей-дорог
            // стало:
            var seeds = ComputeSeedsForSource(src, graph);
            Debug.Log($"[RCV] Seeds={seeds.Count} (root is road? {graph.ContainsKey(src.rootCell)})");
            if (seeds.Count == 0) continue;


            var dist = LogisticsPathfinder.Distances_BFS_Multi(seeds, stepsMax, graph);
            Debug.Log($"[RCV] Dist computed: count={dist.Count}");


            foreach (var kv in dist)
            {
                var pos = kv.Key;
                int steps = kv.Value;
                float eff = EvaluateEfficiency(steps, stepsMax, out float tEdge);

                // Найдём реальный RoadTile и подмерджим эффективность
                var tile = gridSystem.GetRoadTileAt(pos.x, pos.y);
                if (tile == null) continue;

                if (!_mergedEff.TryGetValue(tile, out float cur) || eff > cur)
                    _mergedEff[tile] = eff;

                // Соседние здания — для контуров
                TryAccumulateBuildingEff(pos + Vector2Int.up, eff, buildingEff);
                TryAccumulateBuildingEff(pos + Vector2Int.down, eff, buildingEff);
                TryAccumulateBuildingEff(pos + Vector2Int.left, eff, buildingEff);
                TryAccumulateBuildingEff(pos + Vector2Int.right, eff, buildingEff);
            }
        }

        ApplyRoadColors(_mergedEff);
        ApplyBuildingOutlines(buildingEff);
        Debug.Log($"[RCV] Colored roads: {_mergedEff.Count}, overlayAlpha={_overlayAlpha}");
    }
    // Находим стартовые узлы BFS (семена) для эмиттера.
    // Учитываем реальный футапринт здания (rootGridPosition + size + поворот),
    // а не только одну rootCell. Если здание недоступно — используем старую логику (4 соседа).
    private List<Vector2Int> ComputeSeedsForSource(CoverageSource src, Dictionary<Vector2Int, List<Vector2Int>> graph)
    {
        var seeds = new List<Vector2Int>(16);
        var seen = new HashSet<Vector2Int>();

        // 0) Если сам root — дорога, этого достаточно
        if (graph.ContainsKey(src.rootCell))
        {
            seeds.Add(src.rootCell);
            return seeds;
        }

        // 1) Пытаемся взять BuildingIdentity (через emitterRef)
        BuildingIdentity b = (src.emitterRef != null) ? src.emitterRef.GetComponent<BuildingIdentity>() : null;

        if (b != null && b.buildingData != null)
        {
            // Футапринт здания от ЛН угла
            Vector2Int root = b.rootGridPosition;         // ВАЖНО: НЕ src.rootCell
            Vector2Int size = b.buildingData.size;

            // Учёт поворота так же, как в коде контура
            float rot = b.yRotation;
            if (Mathf.Abs(rot - 90f) < 1f || Mathf.Abs(rot - 270f) < 1f)
                size = new Vector2Int(size.y, size.x);

            // Обходим все клетки здания и смотрим их внешних соседей
            for (int x = 0; x < size.x; x++)
                for (int y = 0; y < size.y; y++)
                {
                    Vector2Int inside = new Vector2Int(root.x + x, root.y + y);

                    foreach (var d in DIRS4)
                    {
                        var nb = inside + d;

                        // Пропускаем клетки, которые тоже внутри прямоугольника здания
                        if (nb.x >= root.x && nb.x < (root.x + size.x) &&
                            nb.y >= root.y && nb.y < (root.y + size.y))
                            continue;

                        // Добавляем примыкающие дороги (без дублей)
                        if (gridSystem.GetRoadTileAt(nb.x, nb.y) != null &&
                            graph.ContainsKey(nb) &&
                            seen.Add(nb))
                        {
                            seeds.Add(nb);
                        }
                    }
                }

            // 3) Финальный фолбэк: ищем ближайшие дороги в таксометровом радиусе 1..3
            if (seeds.Count == 0)
            {
                for (int r = 1; r <= 3; r++)
                {
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int dy = r - Mathf.Abs(dx);

                        var p1 = src.rootCell + new Vector2Int(dx, dy);
                        var p2 = src.rootCell + new Vector2Int(dx, -dy);

                        if (gridSystem.GetRoadTileAt(p1.x, p1.y) != null && graph.ContainsKey(p1) && seen.Add(p1))
                            seeds.Add(p1);

                        if (gridSystem.GetRoadTileAt(p2.x, p2.y) != null && graph.ContainsKey(p2) && seen.Add(p2))
                            seeds.Add(p2);
                    }
                    if (seeds.Count > 0) break; // нашли — достаточно
                }
            }

            return seeds; // ← теперь единственный return внутри этой ветки
        }

        // 2) Фолбэк — старая логика: 4 соседа от rootCell эмиттера
        foreach (var d in DIRS4)
        {
            var nb = src.rootCell + d;
            if (gridSystem.GetRoadTileAt(nb.x, nb.y) != null &&
                graph.ContainsKey(nb) &&
                seen.Add(nb))
            {
                seeds.Add(nb);
            }
        }

        return seeds;
    }
    private void TryAccumulateBuildingEff(Vector2Int cell, float eff, Dictionary<BuildingIdentity, float> map)
    {
        var b = gridSystem.GetBuildingIdentityAt(cell.x, cell.y);
        if (b == null) return;
        if (!map.TryGetValue(b, out float cur) || eff > cur) map[b] = eff;
    }

    // -------- Рисование дорог через MPB --------

    private void ApplyRoadColors(Dictionary<RoadTile, float> effMap)
    {
        // --- ИЗМЕНЕНИЕ: "Clear Pass" (Проход Очистки) ---
        // Пробегаемся по ВСЕМ плиткам, которые мы КОГДА-ЛИБО красили
        // (Этот список хранится в _roadRenderers)
        foreach (var tile in _roadRenderers.Keys)
        {
            // Если этой плитки НЕТ в НОВОЙ карте эффективности (effMap)...
            if (tile != null && !effMap.ContainsKey(tile))
            {
                // ...значит, ее подсветку нужно СТЕРЕТЬ.
                var r = _roadRenderers[tile];
                if (r != null) r.SetPropertyBlock(null);

                var hl = tile.GetComponent<RoadTileHighlighter>();
                if (hl != null) hl.SetHighlight(false);
            }
        }
        // --- КОНЕЦ ИЗМЕНЕНИЯ ---


        // "Apply Pass" (Проход Применения)
        // (Этот код остался почти без изменений, он только красит)
        foreach (var kv in effMap)
        {
            var tile = kv.Key;
            float eff = Mathf.Clamp01(kv.Value);
            if (tile == null) continue;

            if (!_roadRenderers.TryGetValue(tile, out var r))
            {
                // Берём Renderer и у самого узла, и у детей (на случай, если меш ниже)
                r = tile.GetComponent<Renderer>();
                if (r == null) r = tile.GetComponentInChildren<Renderer>();
                if (r == null) continue;
                _roadRenderers[tile] = r; // Добавляем в кэш, чтобы "Clear Pass" о нем знал
            }

            if (!_mpbCache.TryGetValue(tile, out var mpb))
            {
                mpb = new MaterialPropertyBlock();
                _mpbCache[tile] = mpb;
            }
            else mpb.Clear();

            // Цвет: в последних 10% радиуса лёгкий переход к очень светло-голубому,
            // яркость = eff; глоу необязателен — добавим, если у материала есть эмиссия
            var hl = tile.GetComponent<RoadTileHighlighter>();
            if (hl != null)
            {
                // тот же цвет, что и в MPB
                hl.SetHighlight(true, ColorForEfficiency(eff));
            }
        }
        
        // --- ИЗМЕНЕНИЕ: Удален старый комментарий про "Очистку" ---
    }

    private void ClearVisualsHard()
    {
        // Сбросим MPB у всех, кого когда-либо трогали.
        foreach (var kv in _roadRenderers)
        {
            if (kv.Value != null) kv.Value.SetPropertyBlock(null);
        }
        // Спрячем все контуры
        for (int i = _outlineUsed; i < _outlinePool.Count; i++)
            if (_outlinePool[i] != null) _outlinePool[i].gameObject.SetActive(false);
        _outlineUsed = 0;
        foreach (var kv in _roadRenderers)
        {
            if (kv.Value != null) kv.Value.SetPropertyBlock(null);
            var hl = kv.Key.GetComponent<RoadTileHighlighter>();
            if (hl != null) hl.SetHighlight(false);
        }
    }

    // -------- Контуры зданий (LineRenderer + пул) --------

    private void ApplyBuildingOutlines(Dictionary<BuildingIdentity, float> buildingEff)
    {
        _outlineUsed = 0;
        if (buildingEff == null || buildingEff.Count == 0)
        {
            // спрячем неиспользованные
            for (int i = 0; i < _outlinePool.Count; i++)
                _outlinePool[i].gameObject.SetActive(false);
            return;
        }

        foreach (var kv in buildingEff)
        {
            var b = kv.Key;
            float eff = Mathf.Clamp01(kv.Value);
            if (b == null) continue;

            var lr = GetOutline();
            lr.gameObject.SetActive(true);

            // Цвет и альфа
            Color c = ColorForEfficiency(eff);
            c.a = eff * _overlayAlpha;
            lr.startColor = c;
            lr.endColor = c;
            lr.widthMultiplier = buildingOutlineWidth;

            // Прямоугольник по размеру здания с учётом поворота
            var size = b.buildingData.size;
            var rot  = b.yRotation;
            if (Mathf.Abs(rot - 90f) < 1f || Mathf.Abs(rot - 270f) < 1f)
                size = new Vector2Int(size.y, size.x);

            var root = b.rootGridPosition;
            // 4 угла в мировых координатах (по внешнему периметру клеток)
            Vector3 p0 = CellCorner(root.x,         root.y);
            Vector3 p1 = CellCorner(root.x+size.x,  root.y);
            Vector3 p2 = CellCorner(root.x+size.x,  root.y+size.y);
            Vector3 p3 = CellCorner(root.x,         root.y+size.y);

            lr.positionCount = 5;
            lr.SetPosition(0, p0);
            lr.SetPosition(1, p1);
            lr.SetPosition(2, p2);
            lr.SetPosition(3, p3);
            lr.SetPosition(4, p0);
        }

        // спрячем оставшиеся в пуле
        for (int i = _outlineUsed; i < _outlinePool.Count; i++)
            _outlinePool[i].gameObject.SetActive(false);
    }

    private LineRenderer GetOutline()
    {
        if (_outlineUsed < _outlinePool.Count)
            return _outlinePool[_outlineUsed++];

        var go = new GameObject("BuildingOutline");
        go.transform.SetParent(transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.useWorldSpace = true;
        lr.loop = false;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.textureMode = LineTextureMode.Stretch;
        lr.alignment = LineAlignment.View;
        lr.widthMultiplier = buildingOutlineWidth;
        _outlinePool.Add(lr);
        _outlineUsed++;
        return lr;
    }

    // -------- Утилиты --------

    private Vector2Int WorldToCell(Vector3 world)
    {
        gridSystem.GetXZ(world, out int x, out int z);
        return new Vector2Int(x, z);
    }

    private Vector3 CellCorner(int x, int z)
    {
        // угол клетки (левый-нижний по твоей системе координат), плюс небольшой подъём над землёй
        var p = gridSystem.GetWorldPosition(x, z);
        p.y += 0.02f;
        return p;
    }

    /// Эффективность: 100% в первых 90% радиуса, затем резкий спад до 0%.
    private float EvaluateEfficiency(int steps, int stepsMax, out float tEdge)
    {
        if (stepsMax <= 0) { tEdge = 0f; return 1f; }

        float d = steps;
        float R = stepsMax;

        if (d <= 0.9f * R) { tEdge = 0f; return 1f; }

        tEdge = Mathf.InverseLerp(0.9f * R, R, d); // 0..1 в последних 10%
        // быстрый спад: квадратичный
        float eff = 1f - (tEdge * tEdge);
        return Mathf.Clamp01(eff);
    }

    private Color ColorForEfficiency(float eff)
    {
        // Внутри 90% — насыщенный голубой, к краю (eff↓) — светлее.
        // Переход по (1-eff)^2, чтобы к краю быстрее осветлялся.
        float t = Mathf.Clamp01(1f - eff);
        t = t * t;
        return Color.Lerp(StrongBlue, LightBlue, t);
    }

    private void FadeTo(float target)
    {
        if (_fadeCo != null) StopCoroutine(_fadeCo);
        _fadeCo = StartCoroutine(FadeCR(target));
    }

    private IEnumerator FadeCR(float target)
    {
        float start = _overlayAlpha;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(0.001f, fadeTime);
            _overlayAlpha = Mathf.Lerp(start, target, t);
            // При исчезновении в ноль — сделаем жёсткую очистку один раз
            if (Mathf.Approximately(_overlayAlpha, 0f) && Mathf.Approximately(target, 0f))
                ClearVisualsHard();
            yield return null;
        }
        _overlayAlpha = target;
        if (Mathf.Approximately(_overlayAlpha, 0f))
            ClearVisualsHard();
    }

    // ------- Источники (несколько рынков / превью) -------

    private class CoverageSource
    {
        public bool preview;
        public Vector3 worldPos;
        public Vector2Int rootCell;
        public float radius;
        public AuraEmitter emitterRef; // null для превью
    }

    private CoverageSource GetOrCreateSource(AuraEmitter emitter)
    {
        for (int i = 0; i < _sources.Count; i++)
            if (_sources[i].emitterRef == emitter) return _sources[i];

        var src = new CoverageSource { emitterRef = emitter, preview = false };
        _sources.Add(src);
        return src;
    }

    private CoverageSource GetOrCreatePreviewSource()
    {
        for (int i = 0; i < _sources.Count; i++)
            if (_sources[i].preview) return _sources[i];

        var src = new CoverageSource { preview = true };
        _sources.Add(src);
        return src;
    }
}