using Wms.Contracts.Mobile.V1;

namespace Wms.Mobile;

internal static class OrderSynchronizationPresentation
{
    public static bool IsSynchronized(MobileOrderSynchronizationResponse synchronization) =>
        synchronization.Level == MobileOrderSynchronizationLevel.Synchronized;

    public static string BuildTitle(MobileOrderSynchronizationResponse synchronization) =>
        synchronization.Level switch
        {
            MobileOrderSynchronizationLevel.RequiresOperatorDecision =>
                "Требует решения оператора",
            MobileOrderSynchronizationLevel.Blocking =>
                "Работа заблокирована",
            _ => string.Empty
        };

    public static string BuildDetails(MobileOrderSynchronizationResponse synchronization)
    {
        if (IsSynchronized(synchronization))
        {
            return string.Empty;
        }

        var parts = new List<string>();
        if (synchronization.ChangedFields.Count > 0)
        {
            parts.Add($"Изменено: {string.Join(", ", synchronization.ChangedFields)}.");
        }

        if (synchronization.CommentChanged)
        {
            parts.Add(string.IsNullOrWhiteSpace(synchronization.OneCComment)
                ? "Комментарий в 1С очищен."
                : $"Комментарий 1С: {synchronization.OneCComment}");
        }

        parts.Add(synchronization.Level == MobileOrderSynchronizationLevel.RequiresOperatorDecision
            ? "Просмотрите и подтвердите изменения в веб-приложении."
            : "Устраните расхождение в 1С или обратитесь к ответственному.");
        return string.Join(Environment.NewLine, parts);
    }
}
