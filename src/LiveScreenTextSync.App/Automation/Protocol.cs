namespace LiveScreenTextSync.App.Automation;

public sealed record TargetInfo(int ProcessId, long ProcessStarted, string ProcessName, string Name,
    string AutomationId, string ClassName, string Mechanism, int[] RuntimeId);
public sealed record WorkerRequest(string Operation, int X = 0, int Y = 0, string? Text = null);
public sealed record WorkerResponse(bool Success, string Message, TargetInfo? Target = null, bool Verified = false);
