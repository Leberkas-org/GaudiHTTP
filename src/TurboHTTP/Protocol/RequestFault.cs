namespace TurboHTTP.Protocol;

internal static class RequestFault
{
    public static void Fail(HttpRequestMessage request, Exception exception)
    {
        if (request.Options.TryGetValue(TcsCorrelation.Key, out var pending))
        {
            pending.TrySetException(exception);
        }
    }

    public static void FailAll(IEnumerable<HttpRequestMessage> requests, Exception exception)
    {
        foreach (var request in requests)
        {
            Fail(request, exception);
        }
    }

    public static void FailAll(Queue<HttpRequestMessage> queue, Exception exception)
    {
        while (queue.Count > 0)
        {
            Fail(queue.Dequeue(), exception);
        }
    }
}
