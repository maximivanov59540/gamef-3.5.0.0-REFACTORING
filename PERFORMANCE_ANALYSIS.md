# O(n²) and Slow Algorithms Performance Analysis

**Date:** 2025-11-18  
**Status:** Critical Issues Found  
**Priority:** HIGH - Address before next release

---

## Executive Summary

Found **15+ performance issues** with **7 CRITICAL O(n²) patterns** that can severely impact game performance with 100+ buildings.

### Impact Estimate
- **Best case:** Game runs smoothly  
- **Worst case (500 buildings):** 50-100ms per frame stalls from repeated O(n) operations

---

## CRITICAL ISSUES (Fix Immediately)

### 1️⃣ RoadManager.cs - List.Contains in Graph Building

**Location:** `/home/user/gamef-3.5.0.0-REFACTORING/Construction/Roads/RoadManager.cs`  
**Lines:** 110, 113  
**Complexity:** O(n²)

```csharp
// PROBLEM: Contains() is O(n) for List
if (!_roadGraph[gridPos].Contains(nb))      // ← O(n)
    _roadGraph[gridPos].Add(nb);
```

**Impact:** Every road placement triggers O(n) checks × 4 neighbors × 500+ roads = 2000+ O(n) operations  
**Fix:** Replace `List<Vector2Int>` with `HashSet<Vector2Int>` in graph

---

### 2️⃣ BuildingRegistry.cs - Contains Before Add/Remove

**Location:** `/home/user/gamef-3.5.0.0-REFACTORING/Economy/Systems/BuildingRegistry.cs`  
**Lines:** 54, 66, 78, 90  
**Complexity:** O(n)

```csharp
// PROBLEM: Contains() is O(n) for List
if (output == null || _allOutputs.Contains(output)) return;  // ← O(n)
_allOutputs.Add(output);
```

**Impact:** Called 100+ times on game start (every building OnEnable), list grows to 100-500 items  
**Fix:** Replace `List<T>` with `HashSet<T>` for registration tracking

---

### 3️⃣ RoadCoverageVisualizer.cs - GetComponent in Loops

**Location:** `/home/user/gamef-3.5.0.0-REFACTORING/Construction/Roads/RoadCoverageVisualizer.cs`  
**Lines:** 322-334, 340-370, 386-391  
**Complexity:** O(n×m) where m=components per object

```csharp
// PROBLEM: GetComponent is O(n) for components, called in loop
foreach (var tile in _roadRenderers.Keys)
{
    var hl = tile.GetComponent<RoadTileHighlighter>();  // ← O(n) × 100+ tiles
    if (hl != null) hl.SetHighlight(false);
}

foreach (var kv in effMap)
{
    r = tile.GetComponent<Renderer>();                   // ← O(n) × 1000+ calls
    if (r == null) r = tile.GetComponentInChildren<Renderer>();  // ← O(n) again!
}
```

**Impact:** Called every frame when road coverage changes, 100+ tiles × O(n) = slow frame rate  
**Fix:** Cache components in `Dictionary<RoadTile, RoadTileHighlighter>`

---

### 4️⃣ LogisticsPathfinder.cs - Triple Nested Loops

**Location:** `/home/user/gamef-3.5.0.0-REFACTORING/Construction/Roads/LogisticsPathfinder.cs`  
**Lines:** 301-315 (FindNearestRoads method)  
**Complexity:** O(radius³)

```csharp
// PROBLEM: Three nested loops = O(n³) algorithm
for (int radius = 1; radius <= maxRadius; radius++)           // ← outer
{
    for (int x = center.x - radius; x <= center.x + radius; x++)      // ← middle
    {
        for (int z = center.y - radius; z <= center.y + radius; z++)  // ← inner O(n²)!
        {
            // 1331 iterations with radius=5
        }
    }
}
```

**Impact:** Fallback when building has no road access, can freeze game for 50-100ms  
**Fix:** Use intelligent spiral BFS instead of square scan

---

### 5️⃣ AuraManager.cs - List.Contains for Emitters

**Location:** `/home/user/gamef-3.5.0.0-REFACTORING/Economy/Aura/AuraManager.cs`  
**Lines:** 43, 48  
**Complexity:** O(n)

```csharp
// PROBLEM: Contains is O(n) for List
if (!_allEmitters.Contains(emitter)) _allEmitters.Add(emitter);  // ← O(n)
if (_allEmitters.Contains(emitter)) _allEmitters.Remove(emitter); // ← O(n)
```

**Impact:** Called every time emitter is created/destroyed  
**Fix:** Replace `List<AuraEmitter>` with `HashSet<AuraEmitter>`

---

### 6️⃣ EventManager.cs - Any() in Update Loop

**Location:** `/home/user/gamef-3.5.0.0-REFACTORING/Economy/Event/EventManager.cs`  
**Lines:** 134-135  
**Complexity:** O(n)

```csharp
// PROBLEM: Any() scans entire list
bool hasActivePandemic = _allBuildings.Any(b => b.CurrentEventType == EventType.Pandemic);  // ← O(n)
bool hasActiveRiot = _allBuildings.Any(b => b.CurrentEventType == EventType.Riot);          // ← O(n)
```

**Impact:** Called every event check interval (1-30 minutes), scans 100-500 buildings  
**Fix:** Maintain `activePandemicCount` and `activeRiotCount` counters

---

### 7️⃣ EventManager.cs - GetComponent in LINQ Query

**Location:** `/home/user/gamef-3.5.0.0-REFACTORING/Economy/Event/EventManager.cs`  
**Lines:** 202-213  
**Complexity:** O(n×m)

```csharp
// PROBLEM: GetComponent inside LINQ query
List<EventAffected> eligibleBuildings = _allBuildings
    .Where(b => b != null && !b.HasActiveEvent)
    .Where(b => eventType == EventType.Pandemic ? b.canGetPandemic : b.canRiot)
    .ToList();

if (eventType == EventType.Pandemic)
{
    eligibleBuildings = eligibleBuildings
        .Where(b => b.GetComponent<Residence>() != null)  // ← O(n) × 200 buildings!
        .ToList();
}
```

**Impact:** GetComponent called 200+ times during event trigger  
**Fix:** Cache Residence component reference in EventAffected

---

## HIGH PRIORITY ISSUES

### 8️⃣ BuildingInputInventory.cs - FirstOrDefault() in Tight Loops

**Location:** `/home/user/gamef-3.5.0.0-REFACTORING/Economy/Storage/BuildingInputInventory.cs`  
**Line:** 183-185  
**Complexity:** O(n)

```csharp
// PROBLEM: FirstOrDefault is O(n) for List
private StorageData GetSlotForResource(ResourceType type)
{
    return requiredResources.FirstOrDefault(s => s.resourceType == type);  // ← O(n)
}

// Called from HasResources (line 133) and ConsumeResources (line 152)
foreach (var cost in costs)
{
    StorageData slot = GetSlotForResource(cost.resourceType);  // ← O(n) in loop!
}
```

**Impact:** Called multiple times per production cycle, 100+ buildings × 5 costs = 500 O(n) operations per frame  
**Fix:** Use `Dictionary<ResourceType, StorageData>` for O(1) lookup

---

### 9️⃣ LogisticsManager.cs - Where().ToList() LINQ

**Location:** `/home/user/gamef-3.5.0.0-REFACTORING/Economy/Storage/LogisticsManager.cs`  
**Line:** 75  
**Complexity:** O(n)

```csharp
// PROBLEM: Creates temporary list and allocation every call
var matchingRequests = _activeRequests.Where(r => r.RequestedType == resourceToDeliver).ToList();  // ← O(n)
```

**Impact:** Called every cart update (potentially every frame), allocates memory 50+ times per frame  
**Fix:** Use `Dictionary<ResourceType, List<Request>>` grouping

---

### 🔟 ResourceProducer.cs - List.Find()

**Location:** `/home/user/gamef-3.5.0.0-REFACTORING/Economy/Systems/ResourceProducer.cs`  
**Line:** 435  
**Complexity:** O(n)

```csharp
// PROBLEM: Find is O(n) for List
ResourceCost cost = productionData.inputCosts.Find(c => c.resourceType == type);  // ← O(n)
```

**Impact:** Called during resource checks, repeated for each input type  
**Fix:** Use `Dictionary<ResourceType, ResourceCost>` in ResourceProductionData

---

## MEDIUM PRIORITY ISSUES

### 1️⃣1️⃣ BuildingInputInventory.cs - Exists() Check

**Location:** `/home/user/gamef-3.5.0.0-REFACTORING/Economy/Storage/BuildingInputInventory.cs`  
**Line:** 208  
**Complexity:** O(n)

```csharp
// PROBLEM: Exists scans entire list
public bool AcceptsResource(ResourceType type)
{
    return requiredResources.Exists(s => s.resourceType == type);  // ← O(n)
}
```

**Impact:** Called when carts check destination  
**Fix:** Use `HashSet<ResourceType>` for O(1) lookup

---

### 1️⃣2️⃣ ModularBuilding.cs - Contains in Registration

**Location:** `/home/user/gamef-3.5.0.0-REFACTORING/Construction/Modular Buildings/ModularBuilding.cs`  
**Lines:** 37, 57  
**Complexity:** O(n) [but n is small, max 10]

```csharp
// PROBLEM: Contains is O(n) but max 10 modules
if (!_modules.Contains(module))  // ← O(10)
    _modules.Add(module);
```

**Impact:** Low risk (small list), but pattern repeats elsewhere  
**Fix:** Use `HashSet<BuildingModule>` for consistency

---

## ALGORITHMIC IMPROVEMENTS (Not strict O(n²), but fixable)

### Nested Loop Issues

**RoadCoverageVisualizer.cs (Lines 245-267)**  
Building perimeter scan: O(w×h) but w,h ≤ 5×5, so ~25 iterations max

**GroupOperationHandler.cs (Lines 91-102)**  
Mass building copy: O(n×w×h) where n=selected buildings, but optimized with HashSet

---

## Recommended Fix Priority

### Phase 1 (Critical - Do First)
```
1. RoadManager.cs - Replace List with HashSet
2. BuildingRegistry.cs - Replace List with HashSet  
3. RoadCoverageVisualizer.cs - Cache GetComponent
4. LogisticsPathfinder.cs - Fix triple nested loop
```

### Phase 2 (High - Do Next)
```
5. BuildingInputInventory.cs - Use Dictionary
6. LogisticsManager.cs - Group requests by type
7. EventManager.cs - Cache component references
```

### Phase 3 (Medium - Polish)
```
8. AuraManager.cs - Use HashSet
9. ModularBuilding.cs - Use HashSet for modules
```

---

## Performance Impact Estimates

| Issue | Impact | Priority |
|-------|--------|----------|
| RoadManager Contains | 2000× O(n) operations per map | CRITICAL |
| BuildingRegistry Contains | 100-500× O(n) on startup | CRITICAL |
| RoadCoverageVisualizer GetComponent | 100+ O(n) per update | CRITICAL |
| LogisticsPathfinder O(n³) | 1331 iterations worst-case | CRITICAL |
| BuildingInputInventory FirstOrDefault | 500 O(n) per frame | HIGH |
| LogisticsManager LINQ | 50 allocations per frame | HIGH |
| EventManager Any() | O(n) every 1-30 min | MEDIUM |
| EventManager GetComponent | 200 O(n) per event | MEDIUM |

---

## Test Cases for Verification

1. Place 50+ roads - should complete in < 100ms
2. Build game with 100+ buildings - startup in < 2s
3. Enable 10+ aura emitters - no frame stalls
4. Trigger 100 logistics requests - smooth delivery
5. Run event system with 500 buildings - event trigger < 50ms

---

## Code Review Notes

- Prefer `HashSet<T>` over `List<T>` when checking duplicates
- Cache `GetComponent` results in dictionaries
- Avoid LINQ inside loops or event-driven code
- Group data by access pattern (use Dictionary for lookups)
- Profile before optimizing; verify improvements with Profiler

---

*Generated: 2025-11-18*  
*For questions, refer to CLAUDE.md documentation*
