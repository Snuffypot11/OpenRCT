using System;

namespace OpenRCT.Core.Models
{
    public enum ActionExecutionStatus
    {
        Pending = 0,
        Running = 1,
        Succeeded = 2,
        Warning = 3,
        Failed = 4,
        Cancelled = 5
    }

    public sealed class ActionExecutionResult
    {
        public string DeviceName { get; set; } = string.Empty;
        public string ActionName { get; set; } = string.Empty;
        public ActionExecutionStatus Status { get; set; } = ActionExecutionStatus.Pending;
        public string Message { get; set; } = string.Empty;
        public string? ErrorDetails { get; set; }
        public TimeSpan Duration { get; set; }
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

        public bool IsSuccess => Status == ActionExecutionStatus.Succeeded || Status == ActionExecutionStatus.Warning;

        public static ActionExecutionResult Success(string deviceName, string actionName, string message, TimeSpan duration) =>
            new ActionExecutionResult
            {
                DeviceName = deviceName,
                ActionName = actionName,
                Status = ActionExecutionStatus.Succeeded,
                Message = message,
                Duration = duration,
                TimestampUtc = DateTime.UtcNow
            };

        public static ActionExecutionResult Warning(string deviceName, string actionName, string message, TimeSpan duration) =>
            new ActionExecutionResult
            {
                DeviceName = deviceName,
                ActionName = actionName,
                Status = ActionExecutionStatus.Warning,
                Message = message,
                Duration = duration,
                TimestampUtc = DateTime.UtcNow
            };

        public static ActionExecutionResult Failure(string deviceName, string actionName, string message, string? errorDetails, TimeSpan duration) =>
            new ActionExecutionResult
            {
                DeviceName = deviceName,
                ActionName = actionName,
                Status = ActionExecutionStatus.Failed,
                Message = message,
                ErrorDetails = errorDetails,
                Duration = duration,
                TimestampUtc = DateTime.UtcNow
            };

        public static ActionExecutionResult Cancelled(string deviceName, string actionName, TimeSpan duration) =>
            new ActionExecutionResult
            {
                DeviceName = deviceName,
                ActionName = actionName,
                Status = ActionExecutionStatus.Cancelled,
                Message = "Cancelled by user.",
                Duration = duration,
                TimestampUtc = DateTime.UtcNow
            };
    }

    public sealed class DeviceErrorEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string Source { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? Details { get; set; }
    }
}
