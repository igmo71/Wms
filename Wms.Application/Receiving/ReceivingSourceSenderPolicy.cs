using Wms.Common;

namespace Wms.Application.Receiving;

public static class ReceivingSourceSenderPolicy
{
    public const string MissingMessage = "У документа 1С не указан корректный идентификатор отправителя.";
    public const string MismatchMessage = "Прикрепить документ невозможно: другой отправитель.";
    public const string ChangedMessage = "Отправитель документа 1С изменился или не указан. Последняя принятая версия и план сохранены.";

    public static OperationResult<Guid> GetId(ParsedReceivingSource source)
    {
        if (!Guid.TryParse(source.Sender, out var id) || id == Guid.Empty)
            return OperationError.Invalid(MissingMessage);
        return id;
    }

    public static OperationResult RequireSame(Guid expected, ParsedReceivingSource source)
    {
        var actual = GetId(source);
        if (!actual.IsSuccess)
            return actual.Error!;
        if (actual.Value != expected)
            return OperationError.Conflict(MismatchMessage);
        return OperationResult.Success();
    }

    public static OperationResult<Guid> CheckAttachment(
        ReceivingSourceChangeState state, ParsedReceivingSource target,
        IReceivingSourceParser parser)
    {
        var order = state.Order ?? throw new InvalidOperationException("Ордер обязателен.");
        var sender = GetId(target);
        if (!sender.IsSuccess)
            return sender.Error!;
        if (order.SenderId is Guid fixedSender && fixedSender != sender.Value)
            return OperationError.Conflict(MismatchMessage);
        if (order.SenderId is null && state.HasPostedLpn)
            return OperationError.Conflict(
                "Отправитель ордера с проведённым LPN не установлен. Требуется проверка данных ордера.");
        foreach (var activeSource in state.ActiveSources)
        {
            if (activeSource.Version is null)
                return OperationError.Conflict("Версия связанного источника не найдена.");
            var parsed = parser.Parse(activeSource.Source.RefKey,
                activeSource.Version.SnapshotJson);
            if (!parsed.IsSuccess)
                return parsed.Error!;
            var same = RequireSame(sender.Value, parsed.Value!);
            if (!same.IsSuccess)
                return same.Error!;
        }
        return sender.Value;
    }
}
