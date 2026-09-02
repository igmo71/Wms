using Wms.Contracts.Mobile.V1;

namespace Wms.Mobile;

internal static class OrderSynchronizationPresentation
{
    public static MobileOrderSynchronizationResponse MergeOpeningAssessment(
        MobileOrderSynchronizationResponse? current,
        MobileOrderSynchronizationResponse incoming) =>
        !incoming.IsFresh
            && string.IsNullOrWhiteSpace(incoming.VerificationError)
            && incoming.Level == MobileOrderSynchronizationLevel.Synchronized
            && current is not null
                ? current
                : incoming;

    public static bool IsSynchronized(MobileOrderSynchronizationResponse synchronization) =>
        synchronization.Level == MobileOrderSynchronizationLevel.Synchronized;

    public static bool HasIssue(MobileOrderSynchronizationResponse synchronization) =>
        !IsSynchronized(synchronization)
        || !string.IsNullOrWhiteSpace(synchronization.VerificationError);

    public static bool CanPerformCriticalTransition(
        MobileOrderSynchronizationResponse synchronization) =>
        synchronization.IsFresh
        && string.IsNullOrWhiteSpace(synchronization.VerificationError)
        && IsSynchronized(synchronization);

    public static string BuildTitle(MobileOrderSynchronizationResponse synchronization) =>
        !string.IsNullOrWhiteSpace(synchronization.VerificationError)
            ? "Не удалось проверить синхронизацию"
            : BuildLevelTitle(synchronization.Level);

    private static string BuildLevelTitle(MobileOrderSynchronizationLevel level) =>
        level switch
        {
            MobileOrderSynchronizationLevel.RequiresOperatorDecision =>
                "Требует решения оператора",
            MobileOrderSynchronizationLevel.Blocking =>
                "Работа заблокирована",
            _ => string.Empty
        };

    public static string BuildDetails(MobileOrderSynchronizationResponse synchronization)
    {
        if (IsSynchronized(synchronization)
            && string.IsNullOrWhiteSpace(synchronization.VerificationError))
        {
            return string.Empty;
        }

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(synchronization.VerificationError))
        {
            parts.Add(synchronization.VerificationError);
            parts.Add(IsSynchronized(synchronization)
                ? "Последнее известное состояние: синхронизирован."
                : $"Последнее известное состояние: {BuildLevelTitle(synchronization.Level).ToLowerInvariant()}.");
        }

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

        if (!string.IsNullOrWhiteSpace(synchronization.VerificationError))
        {
            parts.Add("Начало и завершение недоступны до успешной проверки.");
        }
        else
        {
            parts.Add(synchronization.Level == MobileOrderSynchronizationLevel.RequiresOperatorDecision
                ? "Просмотрите и подтвердите изменения в веб-приложении."
                : "Устраните расхождение в 1С или обратитесь к ответственному.");
        }

        return string.Join(Environment.NewLine, parts);
    }
}
