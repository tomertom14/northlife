namespace NorthLife.Api.Services;

public sealed class EventLifecycleException(string message) : InvalidOperationException(message);

public sealed class EventRevisionConflictException(int expectedRevision, int actualRevision)
    : InvalidOperationException(
        $"Event revision conflict. Expected {expectedRevision}, actual {actualRevision}.");
