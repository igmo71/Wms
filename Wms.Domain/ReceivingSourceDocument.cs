using Wms.Common;

namespace Wms.Domain;

public sealed class ReceivingSourceDocument
{
    private ReceivingSourceDocument() { }

    public Guid Id { get; private set; }
    public Guid RefKey { get; private set; }
    public Guid? LastSuccessfulVersionId { get; private set; }
    public DateTimeOffset? LastSuccessfulCheckAtUtc { get; private set; }
    public DateTimeOffset? LastAttemptAtUtc { get; private set; }
    public string? LastAttemptError { get; private set; }

    public static OperationResult<ReceivingSourceDocument> Create(Guid refKey)
    {
        if (refKey == Guid.Empty)
            return OperationError.Invalid("Ref_Key документа 1С обязателен.");

        return new ReceivingSourceDocument { Id = Guid.NewGuid(), RefKey = refKey };
    }

    public OperationResult RecordSuccessfulCheck(ReceivingSourceVersion version, DateTimeOffset checkedAtUtc)
    {
        if (version is null || version.Id == Guid.Empty
            || version.ReceivingSourceDocumentId != Id || checkedAtUtc == default)
            return OperationError.Invalid("Версия и время успешной сверки обязательны.");

        LastSuccessfulVersionId = version.Id;
        LastSuccessfulCheckAtUtc = checkedAtUtc;
        LastAttemptAtUtc = checkedAtUtc;
        LastAttemptError = null;
        return OperationResult.Success();
    }

    public OperationResult RecordFailedCheck(DateTimeOffset attemptedAtUtc, string error)
    {
        if (attemptedAtUtc == default || string.IsNullOrWhiteSpace(error))
            return OperationError.Invalid("Время и причина ошибки сверки обязательны.");

        LastAttemptAtUtc = attemptedAtUtc;
        LastAttemptError = error.Trim();
        return OperationResult.Success();
    }
}
