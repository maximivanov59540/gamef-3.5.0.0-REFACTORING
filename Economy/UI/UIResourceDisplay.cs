using TMPro;
using UnityEngine;

public class UIResourceDisplay : MonoBehaviour
{
    // Твои старые ссылки
    public ResourceManager resourceManager;
    // УДАЛЕНО: PopulationManager больше не Singleton, теперь в ResourceManager.Population

    // +++ НАША НОВАЯ ССЫЛКА +++
    public MoneyManager moneyManager; // Ссылка на казну

    // Твои старые текстовые поля
    public TextMeshProUGUI woodText;
    public TextMeshProUGUI stoneText;
    public TextMeshProUGUI planksText;
    public TextMeshProUGUI populationText;

    // +++ НАШЕ НОВОЕ ТЕКСТОВОЕ ПОЛЕ +++
    public TextMeshProUGUI moneyText; // Поле для текста "Деньги"

    // FIX #14: Инициализация и подписка на события вместо Update
    void Start()
    {
        // Находим менеджеры если не назначены
        if (resourceManager == null)
            resourceManager = ResourceManager.Instance;
        if (moneyManager == null)
            moneyManager = MoneyManager.Instance;

        // Инициализируем отображение
        UpdateAllDisplays();

        // Подписываемся на события изменений (вместо Update каждый кадр)
        if (resourceManager != null)
            resourceManager.OnResourceChanged += OnResourceChanged;

        if (moneyManager != null)
            moneyManager.OnMoneyChanged += OnMoneyChanged;

        // 🔔 PERF FIX: Подписываемся на события Population (теперь в ResourceManager)
        if (resourceManager != null && resourceManager.Population != null)
            resourceManager.Population.OnAnyPopulationChanged += OnPopulationChanged;
    }

    void OnDestroy()
    {
        // Отписываемся от событий при уничтожении
        if (resourceManager != null)
            resourceManager.OnResourceChanged -= OnResourceChanged;

        if (moneyManager != null)
            moneyManager.OnMoneyChanged -= OnMoneyChanged;

        // 🔔 PERF FIX: Отписываемся от событий Population (теперь в ResourceManager)
        if (resourceManager != null && resourceManager.Population != null)
            resourceManager.Population.OnAnyPopulationChanged -= OnPopulationChanged;
    }

    // FIX #14: Обновляем только при изменении ресурсов
    private void OnResourceChanged(ResourceType type)
    {
        if (resourceManager == null) return;

        // Обновляем только нужный ресурс
        switch (type)
        {
            case ResourceType.Wood:
                if (woodText != null)
                    woodText.text = string.Format("Дерево: {0}", Mathf.FloorToInt(resourceManager.GetResourceAmount(ResourceType.Wood)));
                break;
            case ResourceType.Stone:
                if (stoneText != null)
                    stoneText.text = string.Format("Камень: {0}", Mathf.FloorToInt(resourceManager.GetResourceAmount(ResourceType.Stone)));
                break;
            case ResourceType.Planks:
                if (planksText != null)
                    planksText.text = string.Format("Доски: {0}", Mathf.FloorToInt(resourceManager.GetResourceAmount(ResourceType.Planks)));
                break;
        }
    }

    // FIX #14: Обновляем только при изменении денег
    private void OnMoneyChanged(float newAmount)
    {
        if (moneyText != null)
            moneyText.text = string.Format("Деньги: {0}", Mathf.FloorToInt(newAmount));
    }

    // 🔔 PERF FIX: Обновляем только при изменении населения
    private void OnPopulationChanged()
    {
        if (resourceManager != null && resourceManager.Population != null && populationText != null)
        {
            int current = resourceManager.Population.GetTotalCurrentPopulation();
            int max = resourceManager.Population.GetTotalMaxPopulation();
            populationText.text = string.Format("Население: {0} / {1}", current, max);
        }
    }

    // Вспомогательный метод для обновления всех дисплеев
    private void UpdateAllDisplays()
    {
        if (resourceManager != null)
        {
            OnResourceChanged(ResourceType.Wood);
            OnResourceChanged(ResourceType.Stone);
            OnResourceChanged(ResourceType.Planks);
        }

        // 🔔 PERF FIX: Используем event-driven обновление вместо Update()
        OnPopulationChanged();

        if (moneyManager != null)
        {
            OnMoneyChanged(moneyManager.GetCurrentMoney());
        }
    }

    // 🔔 PERF FIX: Update() больше не нужен - используем события!
    // Удалено для устранения ненужных обновлений каждый кадр
}