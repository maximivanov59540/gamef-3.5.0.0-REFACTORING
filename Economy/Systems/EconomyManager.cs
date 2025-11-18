using System.Collections;
using UnityEngine;

/// <summary>
/// "Мозг" экономики (Синглтон).
/// Управляет "Содержанием" (Upkeep) зданий и состоянием "Долга" (Debt).
/// </summary>
public class EconomyManager : MonoBehaviour
{
    public static EconomyManager Instance { get; private set; }

    [Tooltip("Мы в долгах? (Не можем строить)")]
    public bool IsInDebt { get; private set; } = false;

    private NotificationManager _notificationManager;
    private Coroutine _minuteTickCoroutine; // 🔥 FIX: Храним ссылку на корутину

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
        }
    }

    private void Start()
    {
        _notificationManager = FindFirstObjectByType<NotificationManager>();
        _minuteTickCoroutine = StartCoroutine(MinuteTick());
    }

    // 🔥 FIX: Memory leak - останавливаем корутину при уничтожении
    private void OnDestroy()
    {
        if (_minuteTickCoroutine != null)
        {
            StopCoroutine(_minuteTickCoroutine);
            _minuteTickCoroutine = null;
        }
    }

    /// <summary>
    /// Корутина, которая срабатывает раз в 60 секунд.
    /// </summary>
    private IEnumerator MinuteTick()
    {
        while (true)
        {
            // Ждем 1 минуту
            yield return new WaitForSeconds(60f);

            float totalUpkeep = 0;

            // 1. Ищем ВСЕ построенные здания в игре
            // (В будущем это можно оптимизировать, если BuildingManager
            // будет "регистрировать" здания при постройке)
            var allBuildings = FindObjectsByType<BuildingIdentity>(FindObjectsSortMode.None);
            
            foreach (var building in allBuildings)
            {
                // "Проекты" (чертежи) не тратят деньги на содержание
                if (!building.isBlueprint && building.buildingData != null)
                {
                    totalUpkeep += building.buildingData.upkeepCostPerMinute;
                }
            }

            if (totalUpkeep > 0)
            {
                // 2. Пытаемся списать деньги из "Казны"
                bool success = MoneyManager.Instance.SpendMoney(totalUpkeep);
                
                // 3. Обновляем статус "в долгах"
                IsInDebt = !success; 
                
                if (!success)
                {
                    Debug.LogWarning($"[EconomyManager] Не удалось оплатить содержание! Upkeep: {totalUpkeep}. Мы в долгах!");
                    _notificationManager?.ShowNotification("Внимание: Казна пуста! Содержание не оплачено.");
                }
                else
                {
                    IsInDebt = false; // (Явно снимаем "долг", если оплата прошла)
                    Debug.Log($"[EconomyManager] Содержание (Upkeep) оплачено: {totalUpkeep}");
                }
            }
            else
            {
                IsInDebt = false; // (Если платить не за что, мы не в долгах)
            }
        }
    }
}