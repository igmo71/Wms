namespace Wms.WebApp.Common;

public record ServiceError(ServiceErrorType Type, string? Message);
