using TMPro;
using UnityEngine;

public class UIResourceDisplay : MonoBehaviour
{
    // Твои старые ссылки
    public ResourceManager resourceManager;
    public PopulationManager populationManager;

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
        if (populationManager == null)
            populationManager = FindFirstObjectByType<PopulationManager>();
        if (moneyManager == null)
            moneyManager = MoneyManager.Instance;

        // Инициализируем отображение
        UpdateAllDisplays();

        // Подписываемся на события изменений (вместо Update каждый кадр)
        if (resourceManager != null)
            resourceManager.OnResourceChanged += OnResourceChanged;

        if (moneyManager != null)
            moneyManager.OnMoneyChanged += OnMoneyChanged;

        // TODO: Добавить события для PopulationManager если они есть
    }

    void OnDestroy()
    {
        // Отписываемся от событий при уничтожении
        if (resourceManager != null)
            resourceManager.OnResourceChanged -= OnResourceChanged;

        if (moneyManager != null)
            moneyManager.OnMoneyChanged -= OnMoneyChanged;
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

    // Вспомогательный метод для обновления всех дисплеев
    private void UpdateAllDisplays()
    {
        if (resourceManager != null)
        {
            OnResourceChanged(ResourceType.Wood);
            OnResourceChanged(ResourceType.Stone);
            OnResourceChanged(ResourceType.Planks);
        }

        if (populationManager != null && populationText != null)
        {
            // Население пока оставляем в Update (если нет событий)
            populationText.text = string.Format("Население: {0} / {1}", populationManager.currentPopulation, populationManager.maxPopulation);
        }

        if (moneyManager != null)
        {
            OnMoneyChanged(moneyManager.GetCurrentMoney());
        }
    }

    // FIX #14: Update теперь обновляет только население (если нет событий для него)
    void Update()
    {
        // Обновляем только население (т.к. у PopulationManager может не быть событий)
        if (populationManager != null && populationText != null)
        {
            populationText.text = string.Format("Население: {0} / {1}", populationManager.currentPopulation, populationManager.maxPopulation);
        }
    }
}