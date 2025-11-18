# Performance Issues - Quick Fix Reference

## At a Glance

**Total Issues Found:** 15  
**Critical (Must Fix):** 7  
**High Priority:** 3  
**Medium Priority:** 5

---

## The 7 Critical Fixes (Do These First)

### 1. RoadManager.cs - Line 110, 113
```csharp
// BEFORE: O(n) Contains check
if (!_roadGraph[gridPos].Contains(nb))
    _roadGraph[gridPos].Add(nb);

// AFTER: O(1) HashSet
// Change: List<Vector2Int> → HashSet<Vector2Int>
if (!_roadGraph[gridPos].Add(nb))  // Add returns false if already exists
    return;
```

### 2. BuildingRegistry.cs - Lines 54-90
```csharp
// BEFORE: 4× List.Contains check
private readonly List<BuildingOutputInventory> _allOutputs;

// AFTER: Use HashSet for all 4 lists
private readonly HashSet<BuildingOutputInventory> _allOutputs;

// Then simply: _allOutputs.Add(output);
// No need for Contains check - Add returns false if duplicate
```

### 3. RoadCoverageVisualizer.cs - Lines 322-391
```csharp
// BEFORE: GetComponent in loop
foreach (var tile in _roadRenderers.Keys)
{
    var hl = tile.GetComponent<RoadTileHighlighter>();  // ← SLOW
}

// AFTER: Cache components
private Dictionary<RoadTile, RoadTileHighlighter> _highlighterCache;

// In registration:
_highlighterCache[tile] = tile.GetComponent<RoadTileHighlighter>();

// In loops:
if (_highlighterCache.TryGetValue(tile, out var hl))
    hl.SetHighlight(false);
```

### 4. LogisticsPathfinder.cs - Lines 301-315
```csharp
// BEFORE: O(radius³) triple nested loop
for (int radius = 1; radius <= maxRadius; radius++)
    for (int x = center.x - radius; x <= center.x + radius; x++)
        for (int z = center.y - radius; z <= center.y + radius; z++)
            // 1331 iterations with radius=5

// AFTER: Use BFS or smarter spiral
private static List<Vector2Int> FindNearestRoads(Vector2Int center, ...)
{
    // Use BFS starting from center, stops when finds road
    // Much faster fallback
}
```

### 5. AuraManager.cs - Lines 43, 48
```csharp
// BEFORE: List.Contains
private List<AuraEmitter> _allEmitters;

// AFTER: HashSet
private HashSet<AuraEmitter> _allEmitters;
```

### 6. EventManager.cs - Lines 134-135
```csharp
// BEFORE: O(n) Any() scan
bool hasActivePandemic = _allBuildings.Any(b => b.CurrentEventType == EventType.Pandemic);

// AFTER: Counter-based
private int _activePandemicCount = 0;

// Update on StartEvent/EndEvent:
// _activePandemicCount++;  / --
// Or check: bool hasActivePandemic = _activePandemicCount > 0;
```

### 7. EventManager.cs - Lines 202-213
```csharp
// BEFORE: GetComponent in LINQ
.Where(b => b.GetComponent<Residence>() != null)

// AFTER: Cache reference
// In EventAffected.cs:
public Residence CachedResidence { get; set; }

// In EventManager:
.Where(b => b.CachedResidence != null)
```

---

## High Priority Fixes (Do Next)

### 8. BuildingInputInventory.cs - Line 183
```csharp
// BEFORE: O(n) FirstOrDefault
return requiredResources.FirstOrDefault(s => s.resourceType == type);

// AFTER: Dictionary
private Dictionary<ResourceType, StorageData> _resourceSlots;
public StorageData GetSlotForResource(ResourceType type)
{
    return _resourceSlots.TryGetValue(type, out var slot) ? slot : null;
}
```

### 9. LogisticsManager.cs - Line 75
```csharp
// BEFORE: Where().ToList() allocation
var matchingRequests = _activeRequests.Where(r => r.RequestedType == resourceToDeliver).ToList();

// AFTER: Dictionary grouping
private Dictionary<ResourceType, List<ResourceRequest>> _requestsByType;

var matchingRequests = _requestsByType.TryGetValue(resourceToDeliver, out var reqs) ? reqs : new List<ResourceRequest>();
```

### 10. ResourceProducer.cs - Line 435
```csharp
// BEFORE: O(n) Find
ResourceCost cost = productionData.inputCosts.Find(c => c.resourceType == type);

// AFTER: Dictionary
private Dictionary<ResourceType, ResourceCost> _costLookup;
productionData.inputCosts.ForEach(c => _costLookup[c.resourceType] = c);
var cost = _costLookup.TryGetValue(type, out var c) ? c : null;
```

---

## Medium Priority (Polish)

### 11. BuildingInputInventory.cs - Line 208
```csharp
// Use HashSet<ResourceType> for O(1) lookup
private HashSet<ResourceType> _acceptedResources;
return _acceptedResources.Contains(type);
```

### 12. ModularBuilding.cs - Lines 37, 57
```csharp
// Replace List with HashSet
private HashSet<BuildingModule> _modules;
if (_modules.Add(module))  // Returns false if already exists
    return;
```

---

## One-Liner Checklist

- [ ] RoadManager: List → HashSet (2 locations)
- [ ] BuildingRegistry: List → HashSet (4 methods)
- [ ] RoadCoverageVisualizer: Cache GetComponent (1 dict + 3 methods)
- [ ] LogisticsPathfinder: Optimize FindNearestRoads (1 method)
- [ ] AuraManager: List → HashSet (1 field + 2 methods)
- [ ] EventManager: Add counters (1 field + 2 locations)
- [ ] EventManager: Cache components (1 field + 1 location)
- [ ] BuildingInputInventory: List → Dictionary (1 method)
- [ ] LogisticsManager: Add grouping dict (1 method)
- [ ] ResourceProducer: List → Dictionary (1 method)
- [ ] BuildingInputInventory: List → HashSet (1 location)
- [ ] ModularBuilding: List → HashSet (1 field + 2 methods)

**Total Changes:** ~30 code modifications, most are straightforward replacements

---

## Testing Before/After

### Test 1: Road Placement
```
Before: Place 50 roads
After: Should be 5-10x faster
```

### Test 2: Game Startup
```
Before: Load 100 buildings
After: Should startup 5-10ms faster
```

### Test 3: Road Coverage
```
Before: Enable coverage visualization
After: No frame stalls, smooth updates
```

### Test 4: Pathfinding
```
Before: Building with no road access
After: Fallback completes quickly
```

### Test 5: Events
```
Before: Trigger event with 500 buildings
After: Event selection < 50ms
```

---

## Pattern Recognition for Future Code

**Bad Patterns to Avoid:**
```csharp
// AVOID: List.Contains before Add/Remove
if (!list.Contains(item)) list.Add(item);  // ← O(n)

// AVOID: GetComponent in loops
foreach (var obj in objects)
    var comp = obj.GetComponent<MyComponent>();  // ← O(n) per iteration

// AVOID: LINQ chains creating lists
list.Where(...).Where(...).ToList()  // ← Multiple scans + allocation

// AVOID: Scanning large lists repeatedly
for (int i = 0; i < 1000; i++)
    if (bigList.Any(x => x.id == i))  // ← O(n²)
```

**Good Patterns to Use:**
```csharp
// USE: HashSet for existence checks
if (hashset.Add(item))  // ← O(1)

// USE: Cache GetComponent
_cache[obj] = obj.GetComponent<MyComponent>();  // ← Do once
foreach (var obj in objects)
    var comp = _cache[obj];  // ← O(1) lookup

// USE: Single-pass filtering
var result = list.Where(x => conditions...).ToList()  // ← One pass

// USE: Dictionary for lookups
var lookup = dict[id];  // ← O(1) instead of Any()
```

---

## Files to Modify (In Order)

1. RoadManager.cs
2. BuildingRegistry.cs
3. RoadCoverageVisualizer.cs
4. LogisticsPathfinder.cs
5. AuraManager.cs
6. EventManager.cs
7. BuildingInputInventory.cs
8. LogisticsManager.cs
9. ResourceProducer.cs
10. ModularBuilding.cs

---

## Estimated Time Commitment

- **Phase 1 (Critical):** 2-3 hours
  - 7 files with core changes
  - High impact, straightforward fixes

- **Phase 2 (High):** 1-2 hours
  - 3 files, medium complexity
  - Good performance gains

- **Phase 3 (Medium):** 30 minutes
  - 2 files, low complexity
  - Polish and consistency

- **Testing:** 1 hour
  - Verify no regressions
  - Profile improvements

**Total:** ~4-6 hours for complete optimization

---

**Generated:** 2025-11-18  
**Status:** Ready for implementation
